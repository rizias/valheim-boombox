using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using VikingBoombox;

internal sealed class QaVideoPipe : IDisposable
{
    internal const int Width = 1280, Height = 720;
    internal const long Maximum = 64L * 1024 * 1024;
    internal static string Executable => BoomboxQa.QaEnvironment.Encoder;
    private static readonly Type ProcessApi = typeof(AudioDeck).Assembly.GetType("VikingBoombox.MusicProcess", true);
    private static readonly MethodInfo LaunchApi = ProcessApi.GetMethod(
        "Launch",
        BindingFlags.Public | BindingFlags.Static
    );
    private static readonly MethodInfo KillApi = ProcessApi.GetMethod(
        "Kill",
        BindingFlags.Public | BindingFlags.Static
    );
    private readonly object gate = new object();
    private readonly Queue<byte[]> free = new Queue<byte[]>();
    private readonly SortedDictionary<int, byte[]> ready = new SortedDictionary<int, byte[]>();
    private readonly System.Diagnostics.Process process;
    private readonly NamedPipeClientStream sink;
    private readonly Thread input, output, diagnostic;
    private readonly StringBuilder log = new StringBuilder();
    private int issued, written, pending;
    private long bytes;
    private bool closing, finished;
    private string error;
    internal int Pending => Volatile.Read(ref pending);
    internal int Issued => Volatile.Read(ref issued);
    internal int Written => Volatile.Read(ref written);
    internal long Bytes => Interlocked.Read(ref bytes);
    internal string Error => Volatile.Read(ref error);
    internal bool ThreadsStopped => (input == null || !input.IsAlive) && !output.IsAlive && !diagnostic.IsAlive;

    internal QaVideoPipe(string pipe, bool raw, string[] arguments)
    {
        sink = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
        try
        {
            process = (System.Diagnostics.Process)LaunchApi.Invoke(null, new object[] { Executable, raw, arguments });
        }
        catch
        {
            sink.Dispose();
            throw;
        }
        if (raw)
            for (int i = 0; i < 8; i++)
                free.Enqueue(new byte[Width * Height * 4]);
        output = new Thread(Pump) { IsBackground = true, Name = "QA video encoded output" };
        diagnostic = new Thread(Diagnostics) { IsBackground = true, Name = "QA video diagnostics" };
        output.Start();
        diagnostic.Start();
        if (raw)
        {
            input = new Thread(Feed) { IsBackground = true, Name = "QA video raw input" };
            input.Start();
        }
    }

    internal void Capture(Camera camera, RenderTexture target)
    {
        if (Error != null)
            throw new InvalidOperationException(Error);
        byte[] buffer;
        int sequence;
        lock (gate)
        {
            if (closing || free.Count == 0)
                throw new InvalidOperationException(
                    "Eight-frame GPU pool exhausted: issued=" + issued + ",written=" + written + ",pending=" + pending
                        + ",encoded="
                        + Bytes
                );
            buffer = free.Dequeue();
            sequence = issued++;
            Interlocked.Increment(ref pending);
        }
        bool submitted = false;
        var previous = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            AsyncGPUReadback.Request(
            target,
            0,
            TextureFormat.RGBA32,
            request =>
                {
                    try
                    {
                        if (request.hasError)
                            throw new InvalidOperationException("GPU readback failed");
                        request.GetData<byte>().CopyTo(buffer);
                        lock (gate)
                        {
                            if (Error == null)
                                ready.Add(sequence, buffer);
                            else
                                free.Enqueue(buffer);
                            Monitor.PulseAll(gate);
                        }
                    }
                    catch (Exception failure)
                    {
                        lock (gate)
                            free.Enqueue(buffer);
                        Fail(failure.Message);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref pending);
                        lock (gate)
                            Monitor.PulseAll(gate);
                    }
                }
            );
            submitted = true;
        }
        finally
        {
            camera.targetTexture = previous;
            if (!submitted)
            {
                Interlocked.Decrement(ref pending);
                lock (gate)
                    free.Enqueue(buffer);
                Fail("Camera render/readback submission failed");
            }
        }
    }

    private void Feed()
    {
        int next = 0;
        try
        {
            while (true)
            {
                byte[] frame;
                lock (gate)
                {
                    while (!ready.TryGetValue(next, out frame))
                    {
                        if (Error != null)
                            throw new InvalidOperationException(Error);
                        if (closing && Pending == 0 && next == issued)
                        {
                            process.StandardInput.Close();
                            return;
                        }
                        Monitor.Wait(gate, 100);
                    }
                    ready.Remove(next);
                }
                process.StandardInput.BaseStream.Write(frame, 0, frame.Length);
                Interlocked.Increment(ref written);
                next++;
                lock (gate)
                    free.Enqueue(frame);
            }
        }
        catch (Exception failure)
        {
            Fail(failure.Message);
        }
    }

    private void Pump()
    {
        try
        {
            sink.Connect(5000);
            var buffer = new byte[65536];
            int count;
            while ((count = process.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (Bytes + count > Maximum)
                    throw new InvalidOperationException("Encoded stream exceeds 64 MiB");
                sink.Write(buffer, 0, count);
                Interlocked.Add(ref bytes, count);
            }
            sink.Flush();
        }
        catch (Exception failure)
        {
            Fail(failure.Message);
        }
        finally
        {
            sink.Dispose();
        }
    }

    private void Diagnostics()
    {
        try
        {
            var buffer = new char[4096];
            int count;
            while ((count = process.StandardError.Read(buffer, 0, buffer.Length)) > 0)
                lock (log)
                {
                    int keep = Math.Min(count, 16384 - log.Length);
                    if (keep > 0)
                        log.Append(buffer, 0, keep);
                }
        }
        catch (Exception failure)
        {
            Fail(failure.Message);
        }
    }

    private void Fail(string value)
    {
        Interlocked.CompareExchange(ref error, value, null);
        try
        {
            KillApi.Invoke(null, new object[] { process });
        }
        catch (TargetInvocationException) { }
        lock (gate)
            Monitor.PulseAll(gate);
    }

    internal System.Collections.IEnumerator FinishAsync()
    {
        var completion = System.Threading.Tasks.Task.Run(() => Finish());
        while (!completion.IsCompleted)
            yield return null;
        if (completion.IsFaulted)
            throw completion.Exception.GetBaseException();
    }

    private void Finish()
    {
        if (Pending != 0)
            throw new InvalidOperationException("GPU requests still pending");
        lock (gate)
        {
            closing = true;
            Monitor.PulseAll(gate);
        }
        if (input != null && !input.Join(5000))
            Fail("Encoder input timeout");
        if (!process.WaitForExit(5000))
            Fail("Encoder exit timeout");
        if (!output.Join(5000) || !diagnostic.Join(5000))
            Fail("Encoder drain timeout");
        string diagnostics;
        lock (log)
            diagnostics = log.ToString();
        if (Error != null || process.ExitCode != 0 || Bytes == 0 || input != null && Written != Issued)
            throw new InvalidOperationException((Error ?? "Encoder failure") + ": " + diagnostics);
        finished = true;
    }

    public void Dispose()
    {
        if (!finished)
            Fail("Capture aborted");
        sink.Dispose();
        if (input != null)
            input.Join(3000);
        output.Join(3000);
        diagnostic.Join(3000);
        if (!ThreadsStopped)
            throw new InvalidOperationException("Encoder cleanup remains pending");
        process.Dispose();
    }
}
