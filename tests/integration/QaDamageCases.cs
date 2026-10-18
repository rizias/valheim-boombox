using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static partial class QaDamageCases
    {
        public static async Task Run(QaLedger ledger)
        {
            var cases = new List<object>();
            void Save() => QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R06/cases.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                    fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                    cases,
                    utc = DateTime.UtcNow
                })
            );
            try
            {
                await QaAuraCases.Prepare(ledger);
                await QaAuraCases.Command("server", "combat-host");
                await QaAuraCases.Command("client-a", "combat-mute");
                await QaAuraCases.Command("client-b", "combat-mute");
                var a = await QaAuraCases.Command("client-a", "snapshot");
                var position = a["position"]!.DeepClone();
                position["y"] = position["y"]!.GetValue<float>() + 30f;
                foreach (string actor in new[] { "client-a", "client-b" })
                    await QaAuraCases.Command(
                        actor,
                        "floor",
                        new
                        {
                            position
                        }
                    );
                await QaAuraCases.Teleport(
                    "client-a",
                    position["x"]!.GetValue<float>(),
                    position["y"]!.GetValue<float>(),
                    position["z"]!.GetValue<float>()
                );
                await QaAuraCases.Teleport(
                    "client-b",
                    position["x"]!.GetValue<float>() + 4f,
                    position["y"]!.GetValue<float>(),
                    position["z"]!.GetValue<float>()
                );
                foreach (string actor in new[] { "client-a", "client-b" })
                    await QaAuraCases.Command(actor, "freeze");
                await QaAuraCases.Command(
                    "client-a",
                    "create",
                    new
                    {
                        key = "A",
                        ranks = new[] { 2, 2, 2, 2, 1 },
                        decorations = 1,
                        placed = true,
                        position
                    }
                );
                await QaAuraCases.Command(
                    "client-b",
                    "create",
                    new
                    {
                        key = "B",
                        ranks = new[] { 1, 3, 3, 3, 2 },
                        decorations = 2,
                        placed = true,
                        position = new
                        {
                            x = position["x"]!.GetValue<float>() + 4f,
                            y = position["y"]!.GetValue<float>(),
                            z = position["z"]!.GetValue<float>()
                        }
                    }
                );
                await Task.Delay(2500);
                await QaAuraCases.Command(
                    "server",
                    "combat-area",
                    new
                    {
                        position
                    }
                );
                DateTime areaDeadline = DateTime.UtcNow.AddSeconds(45);
                JsonNode area;
                do
                {
                    await Task.Delay(1000);
                    area = await QaAuraCases.Command("server", "combat-snapshot");
                    if (DateTime.UtcNow > areaDeadline)
                        throw new IOException("Native D active area/remote Player objects not ready");
                } while (!area["combat"]!["areaReady"]!.GetValue<bool>()
                    || !area["combat"]!["areaLoaded"]!.GetValue<bool>()
                    || area["combat"]!["sourcePlayers"]!.AsArray().Count < 2);
                cases.Add(new
                {
                    id = "DAMAGE-native-area-precondition",
                    passed = true,
                    observation = area
                });
                Save();
                await QaAuraCases.Command("server", "combat-clean-old");
                await Task.Delay(1000);
                var d = await QaAuraCases.Command(
                    "server",
                    "combat-spawn",
                    new
                    {
                        position = new
                        {
                            x = position["x"]!.GetValue<float>() + 3f,
                            y = position["y"]!.GetValue<float>() + 1f,
                            z = position["z"]!.GetValue<float>()
                        }
                    }
                );
                string id = d["combat"]!["npc"]!["id"]!.GetValue<string>();
                string[] parts = id.Split(':');
                long uid = long.Parse(parts[0]);
                uint obj = uint.Parse(parts[1]);
                await Task.Delay(2000);
                foreach (string role in new[] { "client-a", "client-b" })
                    await QaAuraCases.Command(
                        role,
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                var native = JsonNode.Parse(
                    QaFiles.ReadSharedText(Path.Combine(ledger.Root, "profiles/server/BepInEx/config/qa-status.json"))
                )!;
                long peerA = native["peers"]!.AsArray().First(
                    p => p!["playerId"]!.GetValue<long>() == a["playerId"]!.GetValue<long>()
                )!["uid"]!.GetValue<long>();
                var b = await QaAuraCases.Command("client-b", "snapshot");
                long peerB = native["peers"]!.AsArray().First(
                    p => p!["playerId"]!.GetValue<long>() == b["playerId"]!.GetValue<long>()
                )!["uid"]!.GetValue<long>();
                long peerD = d["combat"]!["npc"]!["owner"]!.GetValue<long>();
                foreach (var owner in new[] {
                    (role: "server", peer: peerD),
                    (role: "client-a", peer: peerA),
                    (role: "client-b", peer: peerB)
                })
                {
                    await QaAuraCases.Command(
                        "server",
                        "combat-owner",
                        new
                        {
                            uid,
                            @object = obj,
                            peer = owner.peer
                        }
                    );
                    await Task.Delay(2000);
                    var before = await QaAuraCases.Command(
                        owner.role,
                        "combat-control",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    if (before["combat"]!["npc"]!["isOwner"]!.GetValue<bool>() != true
                    || before["combat"]!["npc"]!["owner"]!.GetValue<long>() != owner.peer)
                        throw new IOException("Native NPC owner not established");
                    var sent = await QaAuraCases.Command(
                        "client-a",
                        "combat-strike",
                        new
                        {
                            key = "A",
                            uid,
                            @object = obj
                        }
                    );
                    await Task.Delay(1500);
                    var after = await QaAuraCases.Command(owner.role, "combat-snapshot");
                    double loss = before["combat"]!["npc"]!["health"]!.GetValue<double>()
                        - after["combat"]!["npc"]!["health"]!.GetValue<double>();
                    bool passed = Math.Abs(loss - 11.575) <= .01;
                    cases.Add(new
                    {
                        id = "DAMAGE-01-" + owner.role,
                        passed,
                        expected = 11.575,
                        actual = loss,
                        before,
                        after,
                        sent
                    });
                    Save();
                    if (!passed)
                        throw new IOException("Native damage mismatch on " + owner.role + ": " + loss);
                    var duplicate = await QaAuraCases.Command(
                        "client-a",
                        "combat-replay",
                        new
                        {
                            uid,
                            @object = obj
                        }
                    );
                    await Task.Delay(1000);
                    var observer = await QaAuraCases.Command(
                        "client-b",
                        "combat-replay",
                        new
                        {
                            uid,
                            @object = obj,
                            @event = sent["combat"]!["event"]!.DeepClone()
                        }
                    );
                    await Task.Delay(1000);
                    var replay = await QaAuraCases.Command(owner.role, "combat-snapshot");
                    passed = duplicate["combat"]!["replay"]!["valid"]!.GetValue<bool>()
                        && observer["combat"]!["replay"]!["valid"]!.GetValue<bool>()
                        && Math.Abs(
                        replay["combat"]!["npc"]!["health"]!.GetValue<double>()
                        - after["combat"]!["npc"]!["health"]!.GetValue<double>()
                    )
                        <= .001;
                    cases.Add(new
                    {
                        id = "DAMAGE-02-" + owner.role,
                        passed,
                        duplicate,
                        observer,
                        replay
                    });
                    Save();
                    if (!passed)
                        throw new IOException("Replay/observer repeated native HP damage");
                }
                await QaAuraCases.Command(
                    "client-a",
                    "create",
                    new
                    {
                        key = "C",
                        ranks = new[] { 1, 1, 1, 1, 1 },
                        decorations = 0,
                        placed = true,
                        position = new
                        {
                            x = position["x"]!.GetValue<float>() + 2f,
                            y = position["y"]!.GetValue<float>(),
                            z = position["z"]!.GetValue<float>()
                        }
                    }
                );
                await Task.Delay(2500);
                var cBefore = await QaAuraCases.Command(
                    "client-b",
                    "combat-control",
                    new
                    {
                        uid,
                        @object = obj
                    }
                );
                var cSent = await QaAuraCases.Command(
                    "client-a",
                    "combat-strike",
                    new
                    {
                        key = "A",
                        uid,
                        @object = obj
                    }
                );
                await Task.Delay(1500);
                var cAfter = await QaAuraCases.Command("client-b", "combat-snapshot");
                double cLoss = cBefore["combat"]!["npc"]!["health"]!.GetValue<double>()
                    - cAfter["combat"]!["npc"]!["health"]!.GetValue<double>();
                bool cPassed = Math.Abs(cLoss - 12.075) <= .01;
                cases.Add(new
                {
                    id = "DAMAGE-04-add-C",
                    passed = cPassed,
                    expected = 12.075,
                    actual = cLoss,
                    before = cBefore,
                    after = cAfter,
                    sent = cSent
                });
                Save();
                if (!cPassed)
                    throw new IOException("Current owner aggregate after C did not reach native NPC: " + cLoss);
                var pulseBefore = await QaAuraCases.Command(
                    "client-b",
                    "combat-control",
                    new
                    {
                        uid,
                        @object = obj
                    }
                );
                await QaAuraCases.Command(
                    "client-a",
                    "combat-pulse",
                    new
                    {
                        key = "A"
                    }
                );
                await Task.Delay(1500);
                var pulseAfter = await QaAuraCases.Command("client-b", "combat-snapshot");
                double pulseLoss = pulseBefore["combat"]!["npc"]!["health"]!.GetValue<double>()
                    - pulseAfter["combat"]!["npc"]!["health"]!.GetValue<double>();
                bool pulsePassed = Math.Abs(pulseLoss - 12.075) <= .01;
                cases.Add(new
                {
                    id = "DAMAGE-02-one-Pulse-opening",
                    passed = pulsePassed,
                    actual = pulseLoss,
                    expected = 12.075,
                    before = pulseBefore,
                    after = pulseAfter
                });
                Save();
                if (!pulsePassed)
                    throw new IOException("Pulse opening did not apply exactly one native hit: " + pulseLoss);
                await QaAuraCases.Command(
                    "client-b",
                    "combat-control",
                    new
                    {
                        uid,
                        @object = obj
                    }
                );
                await QaAuraCases.Command(
                    "client-b",
                    "safety-tame",
                    new
                    {
                        tamed = true
                    }
                );
                await Task.Delay(1000);
                await QaAuraCases.Command(
                    "client-a",
                    "combat-strike",
                    new
                    {
                        key = "A",
                        uid,
                        @object = obj
                    }
                );
                await Task.Delay(1500);
                var tameAfter = await QaAuraCases.Command("client-b", "combat-snapshot");
                bool tamePassed = Math.Abs(tameAfter["combat"]!["npc"]!["health"]!.GetValue<double>() - 1000) <= .001;
                cases.Add(new
                {
                    id = "DAMAGE-03-tamed",
                    passed = tamePassed,
                    observed = tameAfter
                });
                Save();
                if (!tamePassed)
                    throw new IOException("Native tamed NPC received wave damage");
                await QaAuraCases.Command("server", "safety-push");
                foreach (bool pvp in new[] { false, true })
                {
                    var pA = await QaAuraCases.Command(
                        "client-a",
                        "safety-player",
                        new
                        {
                            pvp
                        }
                    );
                    var pB = await QaAuraCases.Command(
                        "client-b",
                        "safety-player",
                        new
                        {
                            pvp
                        }
                    );
                    await QaAuraCases.Command(
                        "client-a",
                        "combat-strike",
                        new
                        {
                            key = "A",
                            uid,
                            @object = obj
                        }
                    );
                    await Task.Delay(1500);
                    foreach (string actor in new[] { "client-a", "client-b" })
                    {
                        var after = await QaAuraCases.Command(actor, "snapshot");
                        var before = actor == "client-a" ? pA : pB;
                        bool passed = after["vitals"]!["health"]!.GetValue<double>()
                            >= before["vitals"]!["health"]!.GetValue<double>() - .02
                            && after["safety"]!["playerWaveCalls"]!.GetValue<int>()
                            == before["safety"]!["playerWaveCalls"]!.GetValue<int>();
                        var velocity = after["safety"]!["velocity"]!;
                        passed &= Math.Sqrt(
                            Math.Pow(velocity["x"]!.GetValue<double>(), 2)
                            + Math.Pow(velocity["z"]!.GetValue<double>(), 2)
                        )
                            <= .05;
                        cases.Add(new
                        {
                            id = "DAMAGE-03-" + actor + "-pvp-" + pvp,
                            passed,
                            before,
                            after
                        });
                        Save();
                        if (!passed)
                            throw new IOException("Wave affected native player HP/RPC/velocity");
                    }
                }
                await QaAuraCases.Command(
                    "server",
                    "combat-owner",
                    new
                    {
                        uid,
                        @object = obj,
                        peer = peerD
                    }
                );
                await Task.Delay(2000);
                await QaAuraCases.Command("server", "combat-clear");
                await QaAuraCases.Command("client-a", "clear");
                await QaAuraCases.Command("client-b", "clear");
                Save();
            }
            catch (Exception failure)
            {
                cases.Add(new
                {
                    id = "DAMAGE-cycle",
                    passed = false,
                    failure = failure.Message
                });
                Save();
                throw;
            }
        }
    }
}
