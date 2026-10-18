using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;

public static class QaWriteGuard
{
    private static readonly object Gate = new object();
    private static NamedPipeClientStream pipe;
    private static StreamReader reader;
    private static StreamWriter writer;
    private static string role;
    private const long Chunk = 64 * 1024;
    private sealed class StreamState
    {
        public string Id, Path;
        public long Approved;
        public bool Writable, Reserved;
    }
    private static readonly Dictionary<FileStream, StreamState> Streams = new Dictionary<FileStream, StreamState>();
    public static void Install(Harmony harmony, string actor)
    {
        role = actor;
        pipe = new NamedPipeClientStream(
            ".",
            Environment.GetEnvironmentVariable("BOOMBOX_QA_PIPE"),
            PipeDirection.InOut
        );
        pipe.Connect(5000);
        reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        foreach (var method in typeof(FileStream).GetConstructors().Where(
            m => m.GetParameters().Length > 1 && m.GetParameters()[0].ParameterType == typeof(string)
        ))
            harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeOpen)),
                postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterOpen))
            );
        harmony.Patch(
            AccessTools.Method(typeof(FileStream), "Write", new[] { typeof(byte[]), typeof(int), typeof(int) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeWrite)),
            postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterWrite))
        );
        harmony.Patch(
            AccessTools.Method(typeof(FileStream), "SetLength", new[] { typeof(long) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeLength)),
            postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterWrite))
        );
        harmony.Patch(
            AccessTools.Method(typeof(FileStream), "WriteByte", new[] { typeof(byte) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeByte)),
            postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterWrite))
        );
        harmony.Patch(
            AccessTools.Method(typeof(FileStream), "Dispose", new[] { typeof(bool) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeDispose)),
            finalizer: new HarmonyMethod(typeof(QaWriteGuard), nameof(DisposeFinished))
        );
        harmony.Patch(
            AccessTools.Method(typeof(File), "Move", new[] { typeof(string), typeof(string) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeMove)),
            postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterMove)),
            finalizer: new HarmonyMethod(typeof(QaWriteGuard), nameof(MoveFailed))
        );
        harmony.Patch(
            AccessTools.Method(typeof(File), "Delete", new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeDelete)),
            postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterDelete)),
            finalizer: new HarmonyMethod(typeof(QaWriteGuard), nameof(DeleteFailed))
        );
        harmony.Patch(
            AccessTools.Method(typeof(File), "Replace", new[] { typeof(string), typeof(string), typeof(string) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeReplace)),
            postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterReplace)),
            finalizer: new HarmonyMethod(typeof(QaWriteGuard), nameof(ReplaceFailed))
        );
        harmony.Patch(
            AccessTools.Method(typeof(File), "Copy", new[] { typeof(string), typeof(string), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(QaWriteGuard), nameof(BeforeCopy)),
            postfix: new HarmonyMethod(typeof(QaWriteGuard), nameof(AfterCopy)),
            finalizer: new HarmonyMethod(typeof(QaWriteGuard), nameof(CopyFailed))
        );
        Request("open", Path.Combine(BepInEx.Paths.ConfigPath, "qa-guard-sentinel.txt"), 0);
    }
    private static void BeforeOpen(MethodBase __originalMethod, object[] __args)
    {
        var parameters = __originalMethod.GetParameters();
        int access = Array.FindIndex(parameters, p => p.ParameterType == typeof(FileAccess));
        if (access >= 0 && (FileAccess)__args[access] == FileAccess.Read)
            return;
        int modeIndex = Array.FindIndex(parameters, p => p.ParameterType == typeof(FileMode));
        if (modeIndex < 0)
            return;
        var mode = (FileMode)__args[modeIndex];
        string path = (string)__args[0];
        long bytes = mode == FileMode.Create || mode == FileMode.CreateNew || mode == FileMode.Truncate
            ? 0
            : File.Exists(path)
                ? new FileInfo(path).Length
                : 0;
        Request("open", path, bytes);
    }
    private static void AfterOpen(MethodBase __originalMethod, FileStream __instance, object[] __args)
    {
        lock (Gate)
        {
            int access = Array.FindIndex(__originalMethod.GetParameters(), p => p.ParameterType == typeof(FileAccess));
            if (access >= 0 && (FileAccess)__args[access] == FileAccess.Read)
                return;
            if (!Streams.ContainsKey(__instance))
                Streams[__instance] = new StreamState
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Path = __instance.Name,
                    Approved = __instance.Length,
                    Writable = true
                };
        }
    }
    private static StreamState State(FileStream stream)
    {
        if (Streams.TryGetValue(stream, out var state))
            return state;
        state = new StreamState
        {
            Id = Guid.NewGuid().ToString("N"),
            Path = stream.Name,
            Approved = stream.Length,
            Writable = true
        };
        Streams[stream] = state;
        return state;
    }
    private static void Ensure(FileStream stream, long projected)
    {
        lock (Gate)
        {
            if (projected <= stream.Length && projected <= State(stream).Approved)
                return;
            var state = State(stream);
            if (projected <= state.Approved)
                return;
            long target = projected > long.MaxValue - Chunk ? long.MaxValue : ((projected + Chunk - 1) / Chunk) * Chunk;
            if (state.Reserved)
            {
                state.Approved = stream.Length;
                Request("commit", state.Path, stream.Length, null, state.Id);
                state.Reserved = false;
            }
            try
            {
                Request("reserve", state.Path, target, null, state.Id);
            }
            catch (IOException error) when (error.Message.IndexOf("quota exceeded", StringComparison.OrdinalIgnoreCase)
                >= 0)
            {
                Request("reserve", state.Path, projected, null, state.Id);
                target = projected;
            }
            state.Approved = target;
            state.Reserved = true;
        }
    }
    private static void BeforeWrite(FileStream __instance, int count)
    {
        if (!__instance.CanSeek)
            return;
        Ensure(__instance, Math.Max(__instance.Length, __instance.Position + count));
    }
    private static void BeforeLength(FileStream __instance, long __0) => Ensure(__instance, __0);
    private static void BeforeByte(FileStream __instance)
    {
        if (__instance.CanSeek)
            Ensure(__instance, Math.Max(__instance.Length, __instance.Position + 1));
    }
    private static void AfterWrite(FileStream __instance)
    {
        lock (Gate)
        {
            if (!Streams.TryGetValue(__instance, out var state))
                return;
            if (state.Reserved && __instance.Length >= state.Approved)
            {
                Request("commit", state.Path, __instance.Length, null, state.Id);
                state.Approved = __instance.Length;
                state.Reserved = false;
            }
            else if (!state.Reserved && __instance.Length != state.Approved)
            {
                Request("write", state.Path, __instance.Length);
                state.Approved = __instance.Length;
            }
        }
    }
    private static void BeforeDispose(FileStream __instance, out Exception __state)
    {
        __state = null;
        lock (Gate)
        {
            if (Streams.TryGetValue(__instance, out var state))
            {
                try
                {
                    if (state.Reserved)
                        Request("commit", state.Path, __instance.Length, null, state.Id);
                    Streams.Remove(__instance);
                }
                catch (Exception error)
                {
                    __state = error;
                }
            }
        }
    }
    private static Exception DisposeFinished(Exception __exception, Exception __state) => __exception ?? __state;
    private static void BeforeMove(string __0, string __1, out string __state)
    {
        __state = null;
        string id = Guid.NewGuid().ToString("N");
        Request("move-reserve", __0, 0, __1, id);
        __state = id;
    }
    private static void AfterMove(string __0, string __1, ref string __state)
    {
        Request("move-commit", __0, 0, __1, __state);
        __state = null;
    }
    private static Exception MoveFailed(Exception __exception, string __0, string __1, string __state)
    {
        if (__exception != null && __state != null)
            Request("move-failed", __0, 0, __1, __state);
        return __exception;
    }
    private static void BeforeReplace(string __0, string __1, string __2, out string __state)
    {
        __state = null;
        string id = Guid.NewGuid().ToString("N");
        Request("replace-reserve", __0, 0, __1, id, __2);
        __state = id;
    }
    private static void AfterReplace(
        string __0,
        string __1,
        string __2,
        string __state
    ) => Request("replace-commit", __0, 0, __1, __state, __2);
    private static Exception ReplaceFailed(Exception __exception, string __0, string __1, string __2, string __state)
    {
        if (__exception != null && __state != null)
            Request("replace-failed", __0, 0, __1, __state, __2);
        return __exception;
    }
    private static void BeforeCopy(string __0, string __1, out string __state)
    {
        __state = null;
        string id = Guid.NewGuid().ToString("N");
        Request("copy-reserve", __0, new FileInfo(__0).Length, __1, id);
        __state = id;
    }
    private static void AfterCopy(string __0, string __1, ref string __state)
    {
        Request("copy-commit", __0, 0, __1, __state);
        __state = null;
    }
    private static Exception CopyFailed(Exception __exception, string __0, string __1, string __state)
    {
        if (__exception != null && __state != null)
            Request("copy-failed", __0, 0, __1, __state);
        return __exception;
    }
    private static void BeforeDelete(string __0, out string __state)
    {
        __state = null;
        string id = Guid.NewGuid().ToString("N");
        Request("delete-reserve", __0, File.Exists(__0) ? new FileInfo(__0).Length : 0, null, id);
        __state = id;
    }
    private static void AfterDelete(string __0, ref string __state)
    {
        Request("delete-commit", __0, 0, null, __state);
        __state = null;
    }
    private static Exception DeleteFailed(Exception __exception, string __0, string __state)
    {
        if (__exception != null && __state != null)
            Request("delete-failed", __0, 0, null, __state);
        return __exception;
    }
    private static void Request(
        string kind,
        string path,
        long length,
        string target = null,
        string id = null,
        string backup = null
    )
    {
        lock (Gate)
        {
            var request = new JObject
            {
                ["kind"] = kind,
                ["role"] = role,
                ["path"] = Path.GetFullPath(path),
                ["length"] = length
            };
            if (target != null)
                request["target"] = Path.GetFullPath(target);
            if (id != null)
                request["id"] = id;
            if (backup != null)
                request["backup"] = Path.GetFullPath(backup);
            writer.WriteLine(request.ToString(Newtonsoft.Json.Formatting.None));
            string answer = reader.ReadLine();
            if (answer != "OK")
                throw new IOException("QA write denied: " + answer);
        }
    }
}
