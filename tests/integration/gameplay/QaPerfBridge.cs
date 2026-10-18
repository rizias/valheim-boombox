using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class QaPerfBridge : IDisposable, ILogListener
{
    private readonly object gate = new object();
    private readonly Queue<string> pending = new Queue<string>();
    private readonly NamedPipeClientStream pipe;
    private readonly Thread thread;
    private readonly QaPerfMeters meters;
    private bool stopping;
    private long errors;
    private string firstError;
    public string Error;
    public long Errors => Interlocked.Read(ref errors);
    public string FirstError => firstError;
    public QaPerfBridge(string name, string project)
    {
        if (!name.StartsWith("BoomboxQa-Perf-", StringComparison.Ordinal) || name.Length > 100)
            throw new InvalidOperationException("Owned performance pipe invalid");
        meters = new QaPerfMeters(project);
        pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        thread = new Thread(Write) { IsBackground = true, Name = "Boombox QA bounded performance telemetry" };
        try
        {
            BepInEx.Logging.Logger.Listeners.Add(this);
            Application.logMessageReceived += UnityLog;
            thread.Start();
        }
        catch
        {
            BepInEx.Logging.Logger.Listeners.Remove(this);
            Application.logMessageReceived -= UnityLog;
            pipe.Dispose();
            meters.Dispose();
            throw;
        }
    }
    public void Publish(JObject value)
    {
        string message = value.ToString(Newtonsoft.Json.Formatting.None);
        if (Encoding.UTF8.GetByteCount(message) > 16384)
            throw new InvalidOperationException("Performance metadata cap exceeded");
        lock (gate)
        {
            if (stopping)
                return;
            if (pending.Count == 4)
                throw new InvalidOperationException("Performance consumer stalled");
            pending.Enqueue(message);
        }
    }
    private void Write()
    {
        try
        {
            pipe.Connect(5000);
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 16384, true) { AutoFlush = true })
            {
                while (true)
                {
                    string message = null;
                    lock (gate)
                    {
                        if (stopping)
                            break;
                        if (pending.Count > 0)
                            message = pending.Dequeue();
                    }
                    if (message == null)
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                    var value = JObject.Parse(message);
                    value["processes"] = meters.Snapshot();
                    if (meters.Error != null)
                        throw new InvalidOperationException(meters.Error);
                    writer.WriteLine(value.ToString(Newtonsoft.Json.Formatting.None));
                }
            }
        }
        catch (Exception failure)
        {
            lock (gate)
                if (!stopping)
                    Error = failure.ToString();
        }
        finally
        {
            meters.Dispose();
        }
    }
    public void LogEvent(object sender, LogEventArgs args)
    {
        if (args.Source.SourceName == "Valheim Boombox" && (args.Level & (LogLevel.Error | LogLevel.Fatal)) != 0)
            Record(args.Data?.ToString());
    }
    private void UnityLog(string message, string trace, LogType kind)
    {
        if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
            Record(message);
    }
    private void Record(string message)
    {
        Interlocked.Increment(ref errors);
        string bounded = message ?? "";
        if (bounded.Length > 1024)
            bounded = bounded.Substring(0, 1024);
        Interlocked.CompareExchange(ref firstError, bounded, null);
    }
    public void Dispose()
    {
        BepInEx.Logging.Logger.Listeners.Remove(this);
        Application.logMessageReceived -= UnityLog;
        lock (gate)
        {
            stopping = true;
            pending.Clear();
        }
        pipe.Dispose();
        if (!thread.Join(2000))
            throw new InvalidOperationException("Owned telemetry thread did not stop");
    }
}
