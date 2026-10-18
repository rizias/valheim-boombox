using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;

public sealed class QaPerfMeters : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Io
    {
        public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Memory
    {
        public uint Size, Faults;
        public UIntPtr PeakWorking, Working, PeakPaged, Paged, PeakNonpaged, Nonpaged, Pagefile, PeakPagefile, Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Basic
    {
        public IntPtr Exit, Peb, Affinity, Priority, Pid, Parent;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadEntry
    {
        public uint Size, Usage, Id, Owner;
        public int Priority, Delta;
        public uint Flags;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        IntPtr handle,
        out long created,
        out long exited,
        out long kernel,
        out long user
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(
        IntPtr handle,
        out Io counters
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(
        IntPtr handle,
        out uint code
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessHandleCount(
        IntPtr handle,
        out uint count
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(
        IntPtr handle,
        IntPtr job,
        out bool inside
    );
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(
        IntPtr handle,
        ref Memory memory,
        uint bytes
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess,
        IntPtr source,
        IntPtr targetProcess,
        out IntPtr target,
        uint access,
        bool inherit,
        uint options
    );
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(
        uint flags,
        uint pid
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32First(
        IntPtr snapshot,
        ref ThreadEntry entry
    );
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32Next(
        IntPtr snapshot,
        ref ThreadEntry entry
    );
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process,
        int kind,
        ref Basic info,
        int bytes,
        out int returned
    );
    private sealed class Meter
    {
        public IntPtr Handle;
        public int Parent;
        public string Kind, Exe;
        public long Created, Cpu, Private, Stamp;
        public bool MemoryObserved;
        public int Threads;
        public Io Io;
    }
    private static QaPerfMeters current;
    private readonly object gate = new object();
    private readonly Dictionary<int, Meter> meters = new Dictionary<int, Meter>();
    private readonly Harmony harmony = new Harmony("local.boombox.qa.perf-processes");
    private readonly string ffmpeg, ffprobe;
    private bool disposed;
    public string Error;
    public QaPerfMeters(string project)
    {
        ffmpeg = Path.GetFullPath(BoomboxQa.QaEnvironment.Encoder);
        ffprobe = Path.GetFullPath(BoomboxQa.QaEnvironment.MediaProbe);
        using (var game = Process.GetCurrentProcess())
            Add(game, "game", game.MainModule.FileName);
        current = this;
        try
        {
            harmony.Patch(
                AccessTools.Method(
                    typeof(VikingBoombox.BoomboxPlugin).Assembly.GetType("VikingBoombox.MusicProcess"),
                    "Start"
                ),
                postfix: new HarmonyMethod(typeof(QaPerfMeters), nameof(Started))
            );
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    private static void Started(Process __result)
    {
        var owner = current;
        if (owner == null || __result == null)
            return;
        try
        {
            string exe = Path.GetFullPath(__result.StartInfo.FileName);
            if (!exe.Equals(owner.ffmpeg, StringComparison.OrdinalIgnoreCase)
            && !exe.Equals(owner.ffprobe, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected product audio executable: " + exe);
            owner.Add(
                __result,
                exe.Equals(owner.ffmpeg, StringComparison.OrdinalIgnoreCase)
                    ? "ffmpeg"
                    : "ffprobe",
                exe
            );
        }
        catch (Exception failure)
        {
            owner.Error = failure.ToString();
        }
    }
    private void Add(Process observer, string kind, string exe)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            if (!DuplicateHandle(new IntPtr(-1), observer.Handle, new IntPtr(-1), out handle, 0, false, 2))
                throw new InvalidOperationException("Owned observer handle duplication failed");
            var basic = new Basic();
            if (!GetProcessTimes(handle, out long created, out _, out _, out _)
                || !IsProcessInJob(handle, IntPtr.Zero, out bool inJob)
                || !inJob
                || NtQueryInformationProcess(handle, 0, ref basic, Marshal.SizeOf(typeof(Basic)), out _) != 0)
                throw new InvalidOperationException("Native process ownership unavailable");
            lock (gate)
            {
                if (disposed)
                {
                    CloseHandle(handle);
                    return;
                }
                if (meters.Count >= 32 || meters.ContainsKey(observer.Id))
                    throw new InvalidOperationException("Owned observer bound or PID reuse before terminal sample");
                meters.Add(
                    observer.Id,
                    new Meter
                    {
                        Handle = handle,
                        Parent = checked((int)basic.Parent.ToInt64()),
                        Kind = kind,
                        Exe = exe,
                        Created = created
                    }
                );
            }
        }
        catch
        {
            if (handle != IntPtr.Zero)
                CloseHandle(handle);
            throw;
        }
    }
    private static bool Alive(IntPtr handle)
    {
        if (!GetExitCodeProcess(handle, out uint code))
            throw new InvalidOperationException("Owned process exit state unavailable");
        return code == 259;
    }
    public JArray Snapshot()
    {
        var rows = new JArray();
        lock (gate)
        {
            var ended = new List<int>();
            var threads = NativeThreads();
            foreach (var entry in meters)
            {
                var value = entry.Value;
                IntPtr handle = value.Handle;
                if (!GetProcessTimes(handle, out long created, out _, out long kernel, out long user)
                    || created != value.Created
                    || !GetProcessIoCounters(handle, out var io))
                    throw new InvalidOperationException("Owned native counters unavailable");
                bool alive = Alive(handle);
                long stamp = Stopwatch.GetTimestamp(), cpu = checked(kernel + user);
                double seconds = value.Stamp == 0 ? 0 : (stamp - value.Stamp) / (double)Stopwatch.Frequency;
                var memory = new Memory { Size = (uint)Marshal.SizeOf(typeof(Memory)) };
                bool memoryRead = GetProcessMemoryInfo(handle, ref memory, memory.Size);
                if (alive && !memoryRead && Alive(handle))
                    throw new InvalidOperationException("Live private memory unavailable");
                if (memoryRead)
                {
                    value.Private = checked((long)memory.Private.ToUInt64());
                    value.MemoryObserved = true;
                }
                uint handles = 0;
                if (alive)
                {
                    try
                    {
                        value.Threads = threads[entry.Key];
                        if (!GetProcessHandleCount(handle, out handles))
                            throw new InvalidOperationException("Native handles unavailable");
                    }
                    catch (Exception)
                    {
                        if (Alive(handle))
                            throw;
                        alive = false;
                    }
                    alive &= Alive(handle);
                }
                if (!alive
                    && (!GetProcessTimes(
                    handle,
                    out created,
                    out _,
                    out kernel,
                    out user
                ) || !GetProcessIoCounters(
                    handle,
                    out io
                )))
                    throw new InvalidOperationException("Terminal CPU/IO unavailable");
                cpu = checked(kernel + user);
                rows.Add(new JObject
                {
                    ["pid"] = entry.Key,
                    ["parent"] = value.Parent,
                    ["created"] = DateTime.FromFileTimeUtc(created).ToString("O"),
                    ["kind"] = value.Kind,
                    ["exe"] = value.Exe,
                    ["alive"] = alive,
                    ["terminalCounters"] = !alive,
                    ["cpuTicks"] = cpu,
                    ["cpuOneCorePercent"] = seconds == 0
                        ? JValue.CreateNull()
                        : new JValue((cpu - value.Cpu) / 10000000.0 / seconds * 100),
                    ["private"] = value.MemoryObserved ? new JValue(value.Private) : JValue.CreateNull(),
                    ["memoryCurrent"] = memoryRead,
                    ["threads"] = value.Threads,
                    ["handles"] = alive ? new JValue(handles) : JValue.CreateNull(),
                    ["read"] = io.ReadBytes,
                    ["write"] = io.WriteBytes,
                    ["readPerSecond"] = seconds == 0
                        ? JValue.CreateNull()
                        : new JValue((io.ReadBytes - value.Io.ReadBytes) / seconds),
                    ["writePerSecond"] = seconds == 0
                        ? JValue.CreateNull()
                        : new JValue((io.WriteBytes - value.Io.WriteBytes) / seconds),
                    ["nativeStamp"] = stamp
                });
                value.Cpu = cpu;
                value.Stamp = stamp;
                value.Io = io;
                if (!alive)
                    ended.Add(entry.Key);
            }
            foreach (int pid in ended)
            {
                CloseHandle(meters[pid].Handle);
                meters.Remove(pid);
            }
        }
        return rows;
    }
    public void Dispose()
    {
        current = null;
        harmony.UnpatchSelf();
        lock (gate)
        {
            disposed = true;
            foreach (var meter in meters.Values)
                CloseHandle(meter.Handle);
            meters.Clear();
        }
    }
    private Dictionary<int, int> NativeThreads()
    {
        var counts = new Dictionary<int, int>();
        foreach (int pid in meters.Keys)
            counts[pid] = 0;
        IntPtr snapshot = CreateToolhelp32Snapshot(4, 0);
        if (snapshot == new IntPtr(-1))
            throw new InvalidOperationException("Native thread snapshot unavailable");
        try
        {
            var entry = new ThreadEntry { Size = (uint)Marshal.SizeOf(typeof(ThreadEntry)) };
            if (!Thread32First(snapshot, ref entry))
                throw new InvalidOperationException("Native first thread unavailable");
            do
            {
                int pid = checked((int)entry.Owner);
                if (counts.ContainsKey(pid))
                    counts[pid]++;
            } while (Thread32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return counts;
    }
}
