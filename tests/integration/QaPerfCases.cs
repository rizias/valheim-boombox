using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaPerfCases
    {
        private sealed class CacheRecord
        {
            public long Files
            {
                get;
                set;
            }
            public long Bytes
            {
                get;
                set;
            }
        }
        private sealed class Reader : IDisposable
        {
            public readonly string Name = "BoomboxQa-Perf-" + Guid.NewGuid().ToString("N");
            private readonly NamedPipeServerStream pipe;
            private readonly CancellationTokenSource stop = new();
            private readonly Task task;
            private readonly object gate = new();
            private JsonNode? latest;
            public Exception? Error;
            public volatile bool Eof;
            public long Received;
            public Reader()
            {
                pipe = new NamedPipeServerStream(
                    Name,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous
                );
                task = Receive();
            }
            private async Task Receive()
            {
                try
                {
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var input = new StreamReader(pipe, new UTF8Encoding(false), false, 32768, true);
                    while (!stop.IsCancellationRequested)
                    {
                        string? line = await input.ReadLineAsync(stop.Token);
                        if (line == null)
                        {
                            Eof = true;
                            return;
                        }
                        if (line.Length > 32768)
                            throw new IOException("Telemetry line cap exceeded");
                        JsonNode value = JsonNode.Parse(line)!;
                        lock (gate)
                        {
                            latest = value;
                            Received = Stopwatch.GetTimestamp();
                        }
                    }
                }
                catch (Exception failure) when (stop.IsCancellationRequested
                    && (failure is IOException
                        || failure is ObjectDisposedException || failure is OperationCanceledException))
                {
                }
                catch (Exception failure)
                {
                    Error = failure;
                }
            }
            public JsonNode? Read()
            {
                lock (gate)
                    return latest?.DeepClone();
            }
            public void Dispose()
            {
                stop.Cancel();
                pipe.Dispose();
                if (!task.Wait(TimeSpan.FromSeconds(3)))
                    throw new IOException("Owned telemetry reader did not stop");
                stop.Dispose();
            }
        }
        public static async Task Execute(QaLedger ledger, bool soak)
        {
            if (!soak)
                QaPerfArtifacts.PreserveFailed(ledger);
            await QaNativeAudioCases.BuildProbe(ledger);
            await QaBuildGameplay.Run(ledger);
            ledger.Preflight(128 * QaLedger.MiB);
            if (!soak)
            {
                await QaNetworkSession.Run(ledger, "perf-probe");
                return;
            }
            using (var proof = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(ledger.Root, "evidence/R11/perf-probe.json"))
            ))
                if (!proof.RootElement.GetProperty("functionalPass").GetBoolean())
                    throw new IOException("Actual two-minute probe has not passed");
            await QaNetworkSession.Run(ledger, "perf-soak");
            await QaNetworkSession.Run(ledger, "perf-control");
            foreach (string name in new[] { "perf-soak", "perf-control" })
            {
                using var restore = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                            ledger.Root,
                            "evidence/R11",
                            name + "-restore.json"
                        )));
                var state = restore.RootElement;
                if (!state.GetProperty("sharedPreferencesRestored").GetBoolean()
                || !state.GetProperty("ownedProcessesStopped").GetBoolean()
                || !state.GetProperty("serverBootstrapRemoved").GetBoolean())
                    throw new IOException("PERF04 native restore incomplete");
            }
            if (ledger.State.Reservations.Count != 0
                || ledger.State.NativeReservations.Count != 0
                || ledger.State.External.Count != 0
                || ledger.State.Phase != "ready")
                throw new IOException("PERF04 ledger not quiescent");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R11/perf04.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        scope = "Actual twelve-hour run closed and fresh native control completed; both restores/jobs/budgets verified",
                        candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                        utc = DateTime.UtcNow
                    }
                )
            );
        }
        public static async Task Run(QaLedger ledger, List<QaSupervised> running, string scenario)
        {
            bool soak = scenario == "perf-soak";
            int warm = soak ? 1200 : 0, measure = soak ? 43200 : 120;
            string folder = Path.Combine(ledger.Root, "evidence/R11"), csv = Path.Combine(
                folder,
                scenario + ".csv"
            ), evidence = Path.Combine(
                folder,
                scenario + ".json"
            );
            if (File.Exists(csv) || File.Exists(evidence))
                throw new IOException("Fixed performance evidence exists; preserve before authorized retry");
            string sha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll"));
            string[] roles = { "server", "client-a", "client-b" };
            var readers = roles.Select(_ => new Reader()).ToArray();
            var native = new Dictionary<string, JsonNode>();
            var seen = new Dictionary<string, long>();
            var verified = new HashSet<(int pid, string created)>();
            var window = new QaPerfWindow();
            var timer = Stopwatch.StartNew();
            DateTime utc = DateTime.UtcNow;
            JsonNode? final = null;
            object? growth = null;
            var caches = new Dictionary<string, CacheRecord>();
            long samples = 0;
            QaQuotaStream? sink = null;
            var batch = new StringBuilder(65536);
            try
            {
                for (int index = 0; index < roles.Length; index++)
                {
                    string role = roles[index];
                    JsonNode status = await Fresh(ledger, role);
                    int pid = status["pid"]!.GetValue<int>();
                    using var process = Process.GetProcessById(pid);
                    if (!running[index].Job.Contains(process)
                        || process.MainModule!.FileName != status["executable"]!.GetValue<string>()
                        || process.StartTime.ToUniversalTime()
                        != DateTime.Parse(status["startUTC"]!.GetValue<string>()).ToUniversalTime())
                        throw new IOException("Actual native role identity mismatch");
                    native[role] = status.DeepClone();
                    seen[role] = -1;
                    Send(
                        ledger,
                        role,
                        index == 1 ? "perf-start" : "perf-observe",
                        new
                        {
                            pipe = readers[index].Name,
                            warm,
                            measure
                        }
                    );
                }
                sink = new QaQuotaStream(ledger, csv, "perf-metrics", 96 * QaLedger.MiB);
                batch.AppendLine(
                    "sample,role,pid,created,kind,alive,phase,step,seconds,cpu_ticks,cpu_one_core_percent,private,threads,handles,read_bytes,write_bytes,read_bps,write_bps,managed,mono,unity_allocated,unity_reserved,gc0,gc1,gc2,frame_median_ms,frame_p95_ms,observer_ms,buffer,launches,underruns,callbacks,nonzero,position,resources"
                );
                double nextFlush = 15, nextNotice = 60;
                while (timer.Elapsed.TotalSeconds < warm + measure + 90)
                {
                    foreach (var process in running)
                        if (process.Fault != null || process.Wrapper.HasExited)
                            throw new IOException("Native role exited during performance run");
                    var rows = new JsonNode?[3];
                    bool ready = true;
                    for (int index = 0; index < readers.Length; index++)
                    {
                        var reader = readers[index];
                        rows[index] = reader.Read();
                        if (reader.Error != null)
                            throw new IOException("Telemetry failed", reader.Error);
                        if (reader.Eof)
                            throw new IOException("Telemetry closed during measurement");
                        if (rows[index] == null)
                        {
                            ready = false;
                            continue;
                        }
                        if ((Stopwatch.GetTimestamp() - reader.Received) / (double)Stopwatch.Frequency > 5)
                            throw new IOException("Native telemetry stalled");
                        string role = roles[index];
                        var row = rows[index]!;
                        if (row["role"]!.GetValue<string>() != role
                            || row["pid"]!.GetValue<int>() != native[role]["pid"]!.GetValue<int>()
                            || row["sha"]!.GetValue<string>() != sha)
                            throw new IOException("Bridge identity/product mismatch");
                        if (row["sequence"]!.GetValue<long>() <= seen[role])
                            ready = false;
                        if (row["failure"] != null
                            || row["errors"]!.GetValue<long>() != 0
                            || row["frameBufferFull"]!.GetValue<bool>())
                            throw new IOException("Actual native failure: " + row.ToJsonString());
                    }
                    if (!ready)
                    {
                        if (timer.Elapsed.TotalSeconds > 45 && rows.Any(r => r == null))
                            throw new TimeoutException("Telemetry startup deadline");
                        await Task.Delay(100);
                        continue;
                    }
                    JsonNode actor = rows[1]!;
                    for (int index = 0; index < rows.Length; index++)
                    {
                        JsonNode row = rows[index]!;
                        string role = roles[index];
                        seen[role] = row["sequence"]!.GetValue<long>();
                        foreach (var process in row["processes"]!.AsArray())
                        {
                            int pid = process!["pid"]!.GetValue<int>();
                            string created = process["created"]!.GetValue<string>();
                            int parent = process["parent"]!.GetValue<int>();
                            bool game = process["kind"]!.GetValue<string>() == "game";
                            if (parent != (game ? running[index].Wrapper.Id : native[role]["pid"]!.GetValue<int>()))
                                throw new IOException("Actual native parent identity mismatch");
                            if (process["alive"]!.GetValue<bool>() && verified.Add((pid, created)))
                            {
                                try
                                {
                                    using var owned = Process.GetProcessById(pid);
                                    if (!running[index].Job.Contains(owned)
                                        || owned.StartTime.ToUniversalTime()
                                        != DateTime.Parse(created).ToUniversalTime()
                                        || !owned.MainModule!.FileName.Equals(
                                        process["exe"]!.GetValue<string>(),
                                        StringComparison.OrdinalIgnoreCase
                                    ))
                                        throw new IOException("Owned process identity mismatch");
                                }
                                catch (ArgumentException)
                                {
                                    if (game)
                                        throw;
                                }
                                catch (InvalidOperationException)
                                {
                                    if (game)
                                        throw;
                                }
                            }
                            Append(batch, samples, role, row, actor, process);
                        }
                        if (soak)
                            window.Add(role, row, actor);
                        if (!caches.ContainsKey(role))
                            caches[role] = Cache(row["cachePath"]!.GetValue<string>());
                    }
                    samples++;
                    if (timer.Elapsed.TotalSeconds >= nextFlush || batch.Length >= 262144)
                    {
                        Flush(sink, batch);
                        nextFlush = timer.Elapsed.TotalSeconds + 15;
                    }
                    if (timer.Elapsed.TotalSeconds >= nextNotice)
                    {
                        Console.WriteLine(
                            "QA_PERF " + scenario + " elapsed=" + actor["seconds"] + " series=" + actor["series"]
                                + " samples="
                                + samples
                        );
                        nextNotice = timer.Elapsed.TotalSeconds + 60;
                    }
                    if (actor["phase"]!.GetValue<string>() == "cleared")
                    {
                        if (!actor["complete"]!.GetValue<bool>()
                            || actor["seconds"]!.GetValue<double>() < warm + measure
                            || actor["resources"]![0]!.GetValue<int>() != 0
                            || actor["resources"]![3]!.GetValue<int>() != 0
                            || actor["resources"]![7]!.GetValue<int>() != 0
                            || actor["resources"]![8]!.GetValue<int>() != 0
                            || actor["resources"]![9]!.GetValue<int>() != 0
                            || actor["nonzero"]!.GetValue<long>() <= 0
                            || actor["fullFixtureGates"]!.GetValue<int>() < 2)
                            throw new IOException("Actual duration/PCM/full24/cleanup gate failed");
                        if (rows.Any(r => r!["resources"]![1]!.GetValue<int>() != 0))
                        {
                            await Task.Delay(1000);
                            continue;
                        }
                        if (rows.Any(
                            r => r!["processes"]!.AsArray().Any(
                                p => p!["kind"]!.GetValue<string>() != "game" && p["alive"]!.GetValue<bool>()
                            )
                        ))
                        {
                            await Task.Delay(1000);
                            continue;
                        }
                        final = actor.DeepClone();
                        break;
                    }
                    await Task.Delay(1000);
                }
                if (final == null)
                    throw new TimeoutException("Bounded performance deadline");
                Flush(sink, batch);
                if (soak)
                    growth = window.Finish();
                for (int index = 0; index < roles.Length; index++)
                {
                    var row = readers[index].Read()!;
                    var after = Cache(row["cachePath"]!.GetValue<string>());
                    var before = caches[roles[index]];
                    if (after.Files != before.Files || after.Bytes != before.Bytes)
                        throw new IOException("Audio cache grew");
                    Send(
                        ledger,
                        roles[index],
                        "perf-observe-stop",
                        new
                        {
                        }
                    );
                }
                var closing = Stopwatch.StartNew();
                while (readers.Any(r => !r.Eof) && closing.Elapsed.TotalSeconds < 10)
                    await Task.Delay(100);
                if (readers.Any(r => !r.Eof || r.Error != null))
                    throw new IOException("Owned bridge EOF missing");
                QaFiles.Write(
                    ledger,
                    evidence,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            functionalPass = true,
                            perf03Pass = soak,
                            warmSeconds = warm,
                            measureSeconds = measure,
                            samples,
                            candidateSha = sha,
                            fixtureSha,
                            utcStart = utc,
                            utcEnd = DateTime.UtcNow,
                            seconds = timer.Elapsed.TotalSeconds,
                            growth,
                            native,
                            final,
                            caches,
                            scope = "Actual numeric AudioDeck/play/pause/seek/next/menu/equip/place/pickup; programmatic handlers, not R10 GUI. PERF01/02/high-rate/archive not accepted"
                        }
                    )
                );
            }
            catch (Exception failure)
            {
                if (sink != null)
                    Flush(sink, batch);
                foreach (string role in roles)
                {
                    try
                    {
                        Send(
                            ledger,
                            role,
                            "perf-stop",
                            new
                            {
                            }
                        );
                    }
                    catch (Exception stopFailure)
                    {
                        Console.Error.WriteLine("Bounded perf stop: " + stopFailure.Message);
                    }
                }
                QaFiles.Write(
                    ledger,
                    evidence,
                    JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        functionalPass = false,
                        perf03Pass = false,
                        failure = failure.ToString(),
                        candidateSha = sha,
                        utcStart = utc,
                        utcEnd = DateTime.UtcNow,
                        seconds = timer.Elapsed.TotalSeconds,
                        samples,
                        native,
                        statuses = readers.Select(r => r.Read()).ToArray()
                    })
                );
                throw;
            }
            finally
            {
                sink?.Dispose();
                foreach (var reader in readers)
                    reader.Dispose();
            }
        }
        private static void Flush(QaQuotaStream sink, StringBuilder batch)
        {
            if (batch.Length == 0)
                return;
            byte[] bytes = Encoding.UTF8.GetBytes(batch.ToString());
            sink.Write(bytes, 0, bytes.Length);
            sink.Flush();
            batch.Clear();
        }
        public static async Task PrepareScene(QaLedger ledger)
        {
            await QaAuraCases.Prepare(ledger);
            var a = await Fresh(ledger, "client-a");
            var b = await Fresh(ledger, "client-b");
            long owner = a["localPlayer"]!.GetValue<long>();
            var point = a["players"]!.AsArray().Single(p => p!["id"]!.GetValue<long>() == owner)!;
            var other = b["players"]!.AsArray().Single(p => p!["id"]!.GetValue<long>()
                == b["localPlayer"]!.GetValue<long>())!;
            double distance = Math.Pow(
                point["x"]!.GetValue<double>() - other["x"]!.GetValue<double>(),
                2
            )
                + Math.Pow(
                point["y"]!.GetValue<double>() - other["y"]!.GetValue<double>(),
                2
            )
                + Math.Pow(
                point["z"]!.GetValue<double>() - other["z"]!.GetValue<double>(),
                2
            );
            if (distance > 64)
                await QaAuraCases.Teleport(
                    "client-b",
                    point["x"]!.GetValue<float>() + 4,
                    point["y"]!.GetValue<float>(),
                    point["z"]!.GetValue<float>()
                );
        }
        private static void Send(QaLedger ledger, string role, string action, object arguments)
        {
            var request = JsonSerializer.SerializeToNode(arguments)!.AsObject();
            request["id"] = Guid.NewGuid().ToString("N");
            request["action"] = action;
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-gameplay-command.json"),
                JsonSerializer.SerializeToUtf8Bytes(request)
            );
        }
        private static async Task<JsonNode> Fresh(QaLedger ledger, string role)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 5)
            {
                try
                {
                    var result = JsonNode.Parse(
                        QaFiles.ReadSharedText(
                            Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json")
                        )
                    );
                    if (result != null && result["pid"] != null)
                        return result;
                }
                catch (IOException) { }
                catch (JsonException) { }
                await Task.Delay(100);
            }
            throw new IOException("Fresh native identity missing: " + role);
        }
        private static CacheRecord Cache(string path)
        {
            var result = new CacheRecord();
            if (!Directory.Exists(path))
                return result;
            foreach (string file in QaInventory.Files(path))
            {
                result.Files++;
                result.Bytes += new FileInfo(file).Length;
            }
            return result;
        }
        private static void Append(
            StringBuilder output,
            long sample,
            string role,
            JsonNode row,
            JsonNode actor,
            JsonNode process
        )
        {
            output.Append(sample).Append(',').Append(role);
            foreach (string key in new[] { "pid", "created", "kind", "alive" })
                output.Append(',').Append(Cell(process[key]));
            output.Append(',').Append(Cell(actor["phase"])).Append(',').Append(Cell(actor["step"])).Append(',').Append(
                Cell(actor["seconds"])
            );
            foreach (string key in new[] {
                "cpuTicks",
                "cpuOneCorePercent",
                "private",
                "threads",
                "handles",
                "read",
                "write",
                "readPerSecond",
                "writePerSecond"
            })
                output.Append(',').Append(Cell(process[key]));
            foreach (string key in new[] {
                "managed",
                "monoUsed",
                "unityAllocated",
                "unityReserved",
                "gc0",
                "gc1",
                "gc2",
                "frameMedian",
                "frameP95",
                "observerMs",
                "buffer",
                "launches",
                "underruns",
                "callbacks",
                "nonzero",
                "position"
            })
                output.Append(',').Append(Cell(row[key]));
            output.Append(",\"")
                .Append(string.Join(
                    ";",
                    row["resources"]!.AsArray()
                    .Select(v => v!.ToJsonString())
                ))
                .AppendLine(
                "\""
            );
        }
        private static string Cell(JsonNode? value)
        {
            if (value == null)
                return "";
            string text = value is JsonValue scalar && scalar.TryGetValue<string>(out string? word)
                ? word!
                : value.ToJsonString();
            return "\"" + text.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }
    }
}
