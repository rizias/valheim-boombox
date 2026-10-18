using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaRemoteCases
    {
        private static QaLedger ledger = null!;
        private static readonly List<object> cases = new();
        private static DateTime started;
        public static void Preserve(QaLedger owner)
        {
            string current = Path.Combine(owner.Root, "evidence/R05/focused-current.json"),
                saved = Path.Combine(owner.Root, "evidence/R05/focused-attempt1.json");
            if (File.Exists(current) && !File.Exists(saved))
            {
                QaFiles.Copy(owner, current, saved);
                QaFiles.Copy(
                    owner,
                    Path.Combine(owner.Root, "evidence/R09/restore.json"),
                    Path.Combine(owner.Root, "evidence/R05/focused-attempt1-restore.json")
                );
            }
            string setup = Path.Combine(owner.Root, "evidence/R05/reconnect-setup-failure.json");
            if (!File.Exists(setup) && File.Exists(saved))
            {
                string state = Path.Combine(owner.Root, "evidence/R01-R02/client-b-snapshot.json");
                QaFiles.Write(
                    owner,
                    setup,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            passed = false,
                            reason = "Remote case did not start: native terrain-safe teleport settled outside helper0.3m coordinate tolerance; visual point needs only nearby numeric preparation",
                            snapshot = JsonNode.Parse(File.ReadAllText(state)),
                            restore = JsonNode.Parse(
                            File.ReadAllText(Path.Combine(owner.Root, "evidence/R09/restore.json"))
                        ),
                            utc = DateTime.UtcNow
                        }
                    )
                );
            }
            string tail = Path.Combine(owner.Root, "evidence/R05/reconnect-attempt2");
            if (File.Exists(current) && !File.Exists(tail + "-cases.json"))
            {
                QaFiles.Copy(owner, current, tail + "-cases.json");
                QaFiles.Copy(owner, Path.Combine(owner.Root, "evidence/R09/restore.json"), tail + "-restore.json");
                foreach (string role in new[] { "client-a", "client-b" })
                    QaFiles.Copy(
                        owner,
                        Path.Combine(owner.Root, "profiles", role, "BepInEx/config/qa-gameplay-status.json"),
                        tail + "-" + role + ".json"
                    );
                foreach (string role in new[] { "server", "client-a", "client-b" })
                    QaFiles.Copy(
                        owner,
                        Path.Combine(owner.Root, "current", role, "stdout.log"),
                        tail + "-" + role + ".log"
                    );
            }
        }
        public static async Task Run(
            QaLedger owner,
            List<QaSupervised> running,
            string pipe,
            bool reconnectOnly = false
        )
        {
            ledger = owner;
            cases.Clear();
            started = DateTime.UtcNow;
            var timer = Stopwatch.StartNew();
            try
            {
                await QaAuraCases.Prepare(owner);
                foreach (string role in new[] { "client-a", "client-b" })
                {
                    await Command(role, "remote-setup");
                    await Command(role, "remote-watch");
                }
                var a = await Command("client-a", "snapshot");
                var position = a["position"]!.DeepClone();
                await Command(
                    "client-b",
                    "aura-position",
                    new
                    {
                        position = new
                        {
                            x = position["x"]!.GetValue<float>() + 4,
                            y = position["y"]!.GetValue<float>(),
                            z = position["z"]!.GetValue<float>()
                        }
                    }
                );
                await Command(
                    "client-a",
                    "create",
                    new
                    {
                        key = "A",
                        ranks = new[] { 1, 1, 1, 1, 1 },
                        decorations = 0,
                        placed = false,
                        position
                    }
                );
                await Command(
                    "client-b",
                    "create",
                    new
                    {
                        key = "B",
                        ranks = new[] { 1, 1, 1, 1, 1 },
                        decorations = 0,
                        placed = false,
                        position
                    }
                );
                string aid = (await Command("client-a", "snapshot"))["owned"]!.AsArray().Single(
                    n => n!["key"]!.GetValue<string>() == "A"
                )!["id"]!.GetValue<string>();
                string bid = (await Command("client-b", "snapshot"))["owned"]!.AsArray().Single(
                    n => n!["key"]!.GetValue<string>() == "B"
                )!["id"]!.GetValue<string>();
                if (reconnectOnly)
                {
                    var original = JsonNode.Parse(
                        File.ReadAllText(Path.Combine(owner.Root, "evidence/R05/focused-attempt1.json"))
                    )!;
                    if (original["candidateSha"]!.GetValue<string>()
                        != QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")))
                        throw new IOException("Remote prefix SHA mismatch");
                    foreach (var item in original["cases"]!.AsArray().Where(c => c!["passed"]?.GetValue<bool>() == true))
                        cases.Add(item!.DeepClone());
                    await Command(
                        "client-a",
                        "remote-mutate",
                        new
                        {
                            key = "A",
                            rank = 3
                        }
                    );
                    await Seen("client-b", aid);
                    goto reconnect;
                }
                foreach (var pair in new[] { ("client-a", bid), ("client-b", aid) })
                {
                    var seen = await Seen(pair.Item1, pair.Item2);
                    Check(
                        "both-roles-native-model-" + pair.Item1,
                        View(seen, pair.Item2)["model"]!.GetValue<bool>()
                            && View(seen, pair.Item2)["audioSources"]!.GetValue<int>() == 0,
                        seen
                    );
                }
                await Command(
                    "client-a",
                    "remote-mutate",
                    new
                    {
                        key = "A",
                        rank = 3
                    }
                );
                var changed = await Until(
                    "client-b",
                    s => View(s, aid)["redRank"]!.GetValue<int>() == 3
                        && View(s, aid)["decorations"]!.AsArray().Count == 2
                );
                Check("actual-cassette-decoration-update", true, changed);
                var move = await Command("client-a", "aura-move");
                var afterMove = await Seen("client-b", aid);
                var ownMove = await Snapshot("client-a");
                var ownModel = ownMove["sample"]!["ownedModels"]!.AsArray().First(m => m!["source"]!.GetValue<string>()
                    == aid)!["position"]!;
                double traveled = Distance(move["sample"]!["start"]!, move["sample"]!["end"]!);
                Check(
                    "native-movement-follows-owner",
                    traveled > .3 && Distance(ownModel, View(afterMove, aid)["modelPosition"]!) < .35,
                    new
                    {
                        traveled,
                        move,
                        afterMove,
                        ownMove
                    }
                );
                var sent = ownMove["sample"]!["sent"]!.AsArray();
                var shown = afterMove["sample"]!["shown"]!.AsArray();
                var delays = shown.Where(n => n!["source"]!.GetValue<string>() == aid).Select(
                n =>
                    {
                        var match = sent.LastOrDefault(
                        e => e!["source"]!.GetValue<string>() == aid
                                && e["time"]!.GetValue<double>() == n!["time"]!.GetValue<double>()
                    );
                        return match == null
                            ? double.NaN
                            : (n!["utcTicks"]!.GetValue<long>() - match["utcTicks"]!.GetValue<long>())
                                / (double)TimeSpan.TicksPerMillisecond;
                    }
                ).Where(double.IsFinite).ToArray();
                Check(
                    "actual-musical-event-delay",
                    delays.Length >= 3 && delays.All(d => d >= 0 && d <= 250),
                    new
                    {
                        delays,
                        sent,
                        shown
                    }
                );
                int model = View(afterMove, aid)["instance"]!.GetValue<int>();
                await Command(
                    "client-b",
                    "remote-toggle",
                    new
                    {
                        visible = false
                    }
                );
                var hidden = await Until("client-b", s => View(s, aid)["waveView"]!.GetValue<bool>() == false);
                Check(
                    "local-waves-toggle-keeps-model-buffs",
                    View(hidden, aid)["instance"]!.GetValue<int>() == model
                        && View(hidden, aid)["active"]!.GetValue<bool>()
                        && hidden["sample"]!["values"]!.AsArray().All(v => v!.GetValue<double>() > 0),
                    hidden
                );
                await Command(
                    "client-b",
                    "remote-toggle",
                    new
                    {
                        visible = true
                    }
                );
                await Until("client-b", s => View(s, aid)["waveView"]!.GetValue<bool>());
                await Command(
                    "client-a",
                    "toggle",
                    new
                    {
                        key = "A"
                    }
                );
                await Until("client-b", s => !View(s, aid)["playing"]!.GetValue<bool>());
                await Command(
                    "client-a",
                    "toggle",
                    new
                    {
                        key = "A"
                    }
                );
                await Seen("client-b", aid);
                for (int cycle = 0; cycle < 10; cycle++)
                {
                    var before = await Seen("client-b", aid);
                    int old = View(before, aid)["instance"]!.GetValue<int>();
                    await Command(
                        "client-b",
                        "remote-dispose",
                        new
                        {
                            source = aid
                        }
                    );
                    var recreated = await Until("client-b", s => View(s, aid)["instance"]!.GetValue<int>() != old);
                    Check(
                        "repeated-dispose-recreate-" + cycle,
                        recreated["sample"]!["views"]!.AsArray().Count == 1
                            && recreated["sample"]!["liveViewObjects"]!.GetValue<int>() == 1,
                        recreated
                    );
                }
                var ground = (await Command("client-a", "snapshot"))["position"]!.DeepClone();
                await Command(
                    "client-a",
                    "place",
                    new
                    {
                        key = "A",
                        position = ground
                    }
                );
                await Until(
                    "client-b",
                    s => View(s, aid)["parent"] != null
                        && View(s, aid)["playing"]!.GetValue<bool>()
                );
                await Command(
                    "client-a",
                    "remote-pickup",
                    new
                    {
                        key = "A"
                    }
                );
                var picked = await Until("client-b", s => View(s, aid)["parent"] == null);
                Check("native-parent-pickup-recreates-view", picked["sample"]!["views"]!.AsArray().Count == 1, picked);
            reconnect:
                ground = (await Command("client-a", "snapshot"))["position"]!.DeepClone();
                await Command(
                    "client-a",
                    "place",
                    new
                    {
                        key = "A",
                        position = ground
                    }
                );
                var settled = await Until(
                    "client-b",
                    s => View(s, aid)["parent"] != null
                        && View(s, aid)["playing"]!.GetValue<bool>()
                );
                Check(
                    "settled-native-parent-before-reconnect",
                    true,
                    new
                    {
                        a = await Snapshot("client-a"),
                        b = settled
                    }
                );
                QaFiles.Write(
                    owner,
                    Path.Combine(owner.Root, "profiles/client-a/BepInEx/config/qa-command.txt"),
                    Encoding.UTF8.GetBytes("shutdown")
                );
                await running[1].WaitAsync(TimeSpan.FromSeconds(45));
                var gone = await Until("client-b", s => View(s, aid)["playing"]!.GetValue<bool>() == false, 15);
                string game = QaEnvironment.ClientRoot, profile = Path.Combine(owner.Root, "profiles/client-a");
                foreach (string stale in new[] {
                    "qa-command.txt",
                    "qa-gameplay-command.json",
                    "qa-gameplay-status.json",
                    "qa-status.json"
                })
                {
                    string p = Path.Combine(profile, "BepInEx/config", stale);
                    if (File.Exists(p))
                        owner.RemoveOwned(p);
                }
                DateTime restartStarted = DateTime.UtcNow;
                var previous = running[1];
                running[1] = new QaSupervised(
                    owner,
                    "client-a",
                    Path.Combine(game, "valheim.exe"),
                    game,
                    new[] {
                    "-batchmode",
                    "-logFile",
                    "-",
                    "-savedir",
                    Path.Combine(profile, "saves"),
                    "--doorstop-enabled",
                    "true",
                    "--doorstop-target-assembly",
                    Path.Combine(profile, "BepInEx/core/BepInEx.Preloader.dll"),
                    "-screen-fullscreen",
                    "0",
                    "-screen-width",
                    "640",
                    "-screen-height",
                    "480"
                    },
                    pipe: pipe
                );
                previous.Dispose();
                await Until("client-a", s => s["playerId"] != null && s["playerId"]!.GetValue<long>() != 0, 120);
                DateTime adoptDeadline = DateTime.UtcNow.AddSeconds(15);
                while (true)
                {
                    try
                    {
                        await Command(
                            "client-a",
                            "remote-adopt",
                            new
                            {
                                source = aid
                            }
                        );
                        break;
                    }
                    catch (IOException e) when (e.Message.Contains(
                        "Sequence contains no matching element",
                        StringComparison.Ordinal
                    )
                        && DateTime.UtcNow < adoptDeadline)
                    {
                        await Task.Delay(100);
                    }
                }
                var reconnected = await Seen("client-b", aid);
                Check(
                    "same-world-native-reconnect-one-model",
                    reconnected["sample"]!["views"]!.AsArray().Count == 1,
                    new
                    {
                        restartSeconds = (DateTime.UtcNow - restartStarted).TotalSeconds,
                        gone,
                        a = await Snapshot("client-a"),
                        reconnected
                    }
                );
                int parentInstance = View(reconnected, aid)["instance"]!.GetValue<int>();
                await Command(
                    "client-a",
                    "remote-pickup",
                    new
                    {
                        key = "A"
                    }
                );
                var pickedTail = await Until(
                    "client-b",
                    s => View(s, aid)["parent"] == null && View(s, aid)["instance"]!.GetValue<int>() != parentInstance
                );
                Check(
                    "settled-parent-native-pickup-recreates-view",
                    pickedTail["sample"]!["views"]!.AsArray().Count == 1
                        && pickedTail["sample"]!["liveViewObjects"]!.GetValue<int>() == 1,
                    new
                    {
                        parentInstance,
                        before = reconnected,
                        after = pickedTail
                    }
                );
                await Command("client-a", "clear");
                await Command("client-b", "clear");
                Check(
                    "focused-complete",
                    true,
                    new
                    {
                        totalCaseSeconds = timer.Elapsed.TotalSeconds,
                        startupAndRestartAreSeparate = true
                    }
                );
            }
            catch (Exception failure)
            {
                cases.Add(new
                {
                    id = "focused-failure",
                    passed = false,
                    error = failure.ToString()
                });
                Save();
                throw;
            }
        }
        private static Task<JsonNode> Command(
            string role,
            string op,
            object? args = null
        ) => QaAuraCases.Command(role, op, args);
        private static Task<JsonNode> Snapshot(string role) => Command(role, "remote-snapshot");
        private static JsonNode View(
            JsonNode state,
            string id
        ) => state["sample"]!["views"]!.AsArray().Single(v => v!["source"]!.GetValue<string>() == id)!;
        private static Task<JsonNode> Seen(
            string role,
            string id
        ) => Until(role, s => View(s, id)["model"]!.GetValue<bool>() && View(s, id)["playing"]!.GetValue<bool>());
        private static async Task<JsonNode> Until(string role, Func<JsonNode, bool> check, int seconds = 10)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var s = await Snapshot(role);
                    if (check(s))
                        return s;
                }
                catch (Exception e) when (e is InvalidOperationException
                    || e is IOException
                    && e.Message.Contains("not ready", StringComparison.Ordinal))
                {
                }
                await Task.Delay(100);
            }
            throw new IOException("Focused remote native condition not reached: " + role);
        }
        private static double Distance(
            JsonNode a,
            JsonNode b
        ) => Math.Sqrt(new[] { "x", "y", "z" }.Sum(k => Math.Pow(
                    a[k]!.GetValue<double>() - b[k]!.GetValue<double>(),
                    2
                )));
        private static void Check(string id, bool passed, object observed)
        {
            cases.Add(new
            {
                id,
                passed,
                observed,
                utc = DateTime.UtcNow
            });
            Save();
            Console.WriteLine("QA_REMOTE " + id + " " + passed);
            if (!passed)
                throw new IOException(id);
        }
        private static void Save() => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "evidence/R05/focused-current.json"),
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                    started,
                    cases,
                    scope = "Only MP-P05 actual independent native actors, model state/events/toggle/dispose/pickup/reconnect; no video/catalog/solo-craft or point3/combat repeat",
                    utc = DateTime.UtcNow
                }
            )
        );
    }
}
