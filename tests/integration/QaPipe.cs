using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BoomboxQa
{
    public sealed class QaPipe : IDisposable
    {
        private readonly QaLedger ledger;
        private readonly CancellationTokenSource stopping = new();
        private readonly Task loop;
        private readonly object gate = new();
        private readonly HashSet<NamedPipeServerStream> connections = new();
        private readonly List<Task> handlers = new();
        private int disposed;
        public readonly string Name = "BoomboxQa-" + Guid.NewGuid().ToString("N");
        public QaPipe(QaLedger owner)
        {
            ledger = owner;
            loop = Accept();
        }
        private async Task Accept()
        {
            while (!stopping.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(
                    Name,
                    PipeDirection.InOut,
                    16,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );
                try
                {
                    await server.WaitForConnectionAsync(stopping.Token);
                }
                catch (OperationCanceledException)
                {
                    server.Dispose();
                    break;
                }
                lock (gate)
                {
                    connections.Add(server);
                    handlers.Add(Handle(server));
                }
            }
        }
        private async Task Handle(NamedPipeServerStream connection)
        {
            try
            {
                using (connection)
                using (var reader = new StreamReader(connection, new UTF8Encoding(false), false, 4096, true))
                using (var writer = new StreamWriter(
                    connection,
                    new UTF8Encoding(false),
                    4096,
                    true
                )
                {
                    AutoFlush = true
                })
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync(stopping.Token)) != null)
                    {
                        string answer = "OK";
                        try
                        {
                            if (line.Length > 65536)
                                throw new IOException("Quota request too large");
                            using var doc = JsonDocument.Parse(line);
                            var request = doc.RootElement;
                            string role = request.GetProperty("role").GetString()!;
                            if (role != "server" && role != "client-a" && role != "client-b")
                                throw new IOException("Unknown QA role");
                            string path = request.GetProperty("path").GetString()!;
                            long length = request.GetProperty("length").GetInt64();
                            string kind = request.GetProperty("kind").GetString()!;
                            string id = request.TryGetProperty("id", out var idValue) ? idValue.GetString()! : "";
                            string? backup = request.TryGetProperty("backup", out var backupValue)
                                ? backupValue.GetString()
                                : null;
                            if (kind == "open")
                                ledger.Resolve(path);
                            if (kind == "delete-check")
                                ledger.NativeCheck(role, path);
                            else if (kind == "delete-done")
                                ledger.NativeRemoved(role, path);
                            else if (kind == "move-reserve")
                                ledger.NativeMoveReserve(role, id, path, request.GetProperty("target").GetString()!);
                            else if (kind == "copy-reserve")
                            {
                                string target = request.GetProperty("target").GetString()!;
                                ledger.NativeReserve(
                                    role,
                                    id,
                                    target,
                                    Math.Max(
                                        length,
                                        File.Exists(target)
                                            ? new FileInfo(target).Length
                                            : 0
                                    )
                                );
                            }
                            else if (kind == "delete-reserve")
                                ledger.NativeReserve(role, id, path, length);
                            else if (kind == "move-commit" || kind == "move-failed")
                                ledger.NativeMoveFinish(
                                    role,
                                    id,
                                    path,
                                    request.GetProperty("target").GetString()!,
                                    kind == "move-commit"
                                );
                            else if (kind == "copy-commit"
                                || kind == "copy-failed"
                                || kind == "delete-commit"
                                || kind == "delete-failed")
                            {
                                string target = request.TryGetProperty("target", out var destination)
                                    ? destination.GetString()!
                                    : path;
                                ledger.NativeCommit(
                                    role,
                                    id,
                                    target,
                                    File.Exists(target)
                                        ? new FileInfo(target).Length
                                        : 0
                                );
                            }
                            else if (kind == "reserve")
                                ledger.NativeReserve(role, id, path, length);
                            else if (kind == "commit")
                                ledger.NativeCommit(role, id, path, length);
                            else if (kind == "replace-reserve")
                                ledger.NativeReplaceReserve(
                                    role,
                                    id,
                                    path,
                                    request.GetProperty("target").GetString()!,
                                    backup
                                );
                            else if (kind == "replace-commit")
                            {
                                try
                                {
                                    ledger.NativeReplaceCommit(
                                        role,
                                        id,
                                        path,
                                        request.GetProperty("target").GetString()!,
                                        backup
                                    );
                                }
                                catch
                                {
                                    ledger.RecoveryRequired();
                                    throw;
                                }
                            }
                            else if (kind == "replace-failed")
                                ledger.RecoveryRequired();
                            else
                                ledger.NativeWrite(role, path, length);
                        }
                        catch (Exception error)
                        {
                            answer = "DENY " + error.Message;
                        }
                        await writer.WriteLineAsync(answer);
                    }
                }
            }
            catch (Exception error) when (stopping.IsCancellationRequested
            && (error is IOException || error is ObjectDisposedException || error is OperationCanceledException))
            {
            }
            finally
            {
                lock (gate)
                    connections.Remove(connection);
            }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            stopping.Cancel();
            loop.GetAwaiter().GetResult();
            Task[] pending;
            lock (gate)
            {
                foreach (var connection in connections)
                    connection.Dispose();
                pending = handlers.ToArray();
            }
            if (!Task.WhenAll(pending).Wait(TimeSpan.FromSeconds(10)))
            {
                ledger.RecoveryRequired();
                throw new IOException("QA pipe handlers did not stop");
            }
            stopping.Dispose();
        }
    }
}
