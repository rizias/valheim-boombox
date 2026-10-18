using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class TestAudioSession
{
    public sealed class Snapshot
    {
        public uint Pid;
        public string Id;
        public bool Muted;
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class EnumeratorClass
    {
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Enumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out Device device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Device
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            uint context,
            IntPtr parameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object result
        );
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Manager
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out Sessions sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Sessions
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Control
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr callback);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr callback);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Volume
    {
        [PreserveSig] int SetMasterVolume(float value, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool value, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool value);
    }
    public static Snapshot[] Inspect(uint pid)
    {
        return Visit(pid, null, null);
    }
    public static Snapshot[] Mute(uint pid)
    {
        return Visit(pid, true, null);
    }
    public static Snapshot[] Restore(uint pid, string id, bool muted)
    {
        return Visit(pid, muted, id);
    }
    public static Snapshot[] CloseTestAndRestore(uint pid, bool muted)
    {
        return Visit(pid, muted, null, true);
    }
    private static Snapshot[] Visit(uint pid, bool? set, string onlyId, bool close = false)
    {
        if (pid == 0)
            throw new ArgumentException("A concrete test process is required");
        if (set.HasValue || close)
        {
            using (var process = System.Diagnostics.Process.GetProcessById((int)pid))
            {
                string root = Environment.GetEnvironmentVariable("VALHEIM_QA_CLIENT");
                if (string.IsNullOrWhiteSpace(root)
                    || !System.IO.Path.IsPathRooted(root)
                    ||
                Environment.GetEnvironmentVariable("VALHEIM_QA_PROCESS_ID") != pid.ToString()
                    ||
                Environment.GetEnvironmentVariable("VALHEIM_QA_PROCESS_START_TICKS")
                    != process.StartTime.ToUniversalTime().Ticks.ToString()
                    ||
                !string.Equals(
                    process.MainModule.FileName,
                    System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "valheim.exe")),
                    StringComparison.OrdinalIgnoreCase
                ))
                    throw new InvalidOperationException(
                        "Exact configured QA executable, PID and start time are required before audio changes"
                    );
            }
        }
        var result = new List<Snapshot>();
        object enumeratorObject = new EnumeratorClass(), managerObject = null;
        Device device = null;
        Sessions sessions = null;
        try
        {
            Marshal.ThrowExceptionForHR(((Enumerator)enumeratorObject).GetDefaultAudioEndpoint(0, 1, out device));
            var iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out managerObject));
            Marshal.ThrowExceptionForHR(((Manager)managerObject).GetSessionEnumerator(out sessions));
            Marshal.ThrowExceptionForHR(sessions.GetCount(out int count));
            for (int i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(sessions.GetSession(i, out var instance));
                try
                {
                    var control = (Control)instance;
                    Marshal.ThrowExceptionForHR(control.GetProcessId(out uint owner));
                    if (owner != pid)
                        continue;
                    Marshal.ThrowExceptionForHR(control.GetSessionInstanceIdentifier(out var pointer));
                    string id = Marshal.PtrToStringUni(pointer);
                    Marshal.FreeCoTaskMem(pointer);
                    if (onlyId != null && id != onlyId)
                        continue;
                    var volume = (Volume)instance;
                    if (close)
                    {
                        using (var process = System.Diagnostics.Process.GetProcessById((int)pid))
                        {
                            if (Environment.GetEnvironmentVariable("VALHEIM_QA_PROCESS_START_TICKS")
                                != process.StartTime.ToUniversalTime().Ticks.ToString())
                                throw new InvalidOperationException("QA process identity changed before close");
                            if (!process.CloseMainWindow() || !process.WaitForExit(20000))
                                throw new InvalidOperationException("Test process did not close; mute preserved");
                        }
                        close = false;
                    }
                    if (set.HasValue)
                    {
                        var context = Guid.Empty;
                        Marshal.ThrowExceptionForHR(volume.SetMute(set.Value, ref context));
                    }
                    Marshal.ThrowExceptionForHR(volume.GetMute(out bool muted));
                    result.Add(new Snapshot { Pid = owner, Id = id, Muted = muted });
                }
                finally
                {
                    Marshal.ReleaseComObject(instance);
                }
            }
        }
        finally
        {
            if (sessions != null)
                Marshal.ReleaseComObject(sessions);
            if (managerObject != null)
                Marshal.ReleaseComObject(managerObject);
            if (device != null)
                Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumeratorObject);
        }
        return result.ToArray();
    }
}
