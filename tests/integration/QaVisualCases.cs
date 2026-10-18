using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Linq;

namespace BoomboxQa
{
    public static class QaVisualCases
    {
        public static async Task Run(QaLedger ledger)
        {
            await QaAuraCases.Prepare(ledger);
            var a = await QaAuraCases.Command("client-a", "snapshot");
            var position = a["position"]!.DeepClone();
            await QaAuraCases.Command("client-a", "combat-mute");
            await QaAuraCases.Command("client-b", "combat-mute");
            await QaAuraCases.Command(
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
            await QaAuraCases.Command(
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
            await QaAuraCases.Command(
                "client-a",
                "visual-setup",
                new
                {
                    key = "A"
                }
            );
            await QaAuraCases.Command(
                "client-b",
                "visual-setup",
                new
                {
                    key = "B"
                }
            );
            await Task.Delay(2000);
            var own = await QaAuraCases.Command(
                "client-a",
                "capture",
                new
                {
                    key = "A",
                    view = "front",
                    remote = false
                }
            );
            Copy(ledger, "client-a", own);
            string id = own["capture"]!["source"]!.GetValue<string>();
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            JsonNode? remote = null;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    remote = await QaAuraCases.Command(
                        "client-b",
                        "capture",
                        new
                        {
                            source = id,
                            view = "front",
                            remote = true
                        }
                    );
                    break;
                }
                catch (IOException failure) when (failure.Message.Contains(
                    "Sequence contains no matching element",
                    StringComparison.Ordinal
                ))
                {
                    await Task.Delay(1000);
                }
            }
            if (remote == null)
                throw new IOException("Actual native remote view not available for capture");
            Copy(ledger, "client-b", remote);
            if (remote["capture"]!["foreignAudioSources"]!.GetValue<int>() != 0
            || remote["capture"]!["foreignAudioDecks"]!.GetValue<int>() != 0)
                throw new IOException("Foreign visual root contains audio/gameplay component");
            foreach (string view in new[] { "quarter", "side", "context" })
            {
                Copy(
                    ledger,
                    "client-a",
                    await QaAuraCases.Command(
                        "client-a",
                        "capture",
                        new
                        {
                            key = "A",
                            view,
                            remote = false
                        }
                    )
                );
                Copy(
                    ledger,
                    "client-b",
                    await QaAuraCases.Command(
                        "client-b",
                        "capture",
                        new
                        {
                            source = id,
                            view,
                            remote = true
                        }
                    )
                );
            }
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R05/capture-probe.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        captureCompleted = true,
                        visualAcceptance = "PENDING image review",
                        candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                        sourceId = id,
                        own,
                        remote,
                        fidelity = new
                        {
                            source = "Actual A-own native game model",
                            candidate = "Actual B BoomboxRemoteView.Model",
                            scaleAndCamera = "Same FOV34, camera distance1.8m and model-relative angles; transform scale not recorded",
                            medium = "Native textured 3D asset/material/rune system, no replacement primitives or imagegen",
                            checklist = "runes/materials/silhouette/attachment arrangement require side-by-side inspection; capture metadata alone is not PASS"
                        },
                        scope = "Actual native A-own and B-remote Camera.Render; fixed paths, no image/video generation or PNG sequence",
                        utc = DateTime.UtcNow
                    }
                )
            );
            await QaAuraCases.Command("client-a", "clear");
            await QaAuraCases.Command("client-b", "clear");
        }

        private static void Copy(QaLedger ledger, string role, JsonNode status)
        {
            string name = status["capture"]!["file"]!.GetValue<string>();
            string source = Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/capture", name);
            QaFiles.Copy(ledger, source, Path.Combine(ledger.Root, "evidence/R05", role + "-" + name));
            QaFiles.Write(
                ledger,
                Path.Combine(
                    ledger.Root,
                    "evidence/R05",
                    role + "-" + Path.GetFileNameWithoutExtension(name) + ".json"
                ),
                JsonSerializer.SerializeToUtf8Bytes(status)
            );
            Console.WriteLine("QA_NATIVE_RENDER " + role + " " + name);
        }
    }
}
