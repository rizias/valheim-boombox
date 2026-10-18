using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static partial class QaUiCases
    {
        public static async Task RunCombo(QaLedger ledger, List<QaSupervised> running, string pipe)
        {
            await QaAuraCases.Prepare(ledger);
            var result = await QaAuraCases.Command("client-a", "ui-complete", seconds: 100);
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R10/ui-complete-current.json"),
                JsonSerializer.SerializeToUtf8Bytes(result)
            );
            foreach (var frame in result["sample"]!["frames"]!.AsArray())
            {
                string path = frame!["file"]!.GetValue<string>();
                ledger.AdoptExisting(path, "client-a");
                QaFiles.Copy(ledger, path, Path.Combine(ledger.Root, "evidence/R10", Path.GetFileName(path)));
            }
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/qa-command.txt"),
                Encoding.UTF8.GetBytes("shutdown")
            );
            await running[1].WaitAsync(TimeSpan.FromSeconds(45));
            running[1].Job.WaitEmpty();
            foreach (string stale in new[] {
                "qa-command.txt",
                "qa-status.json",
                "qa-gameplay-command.json",
                "qa-gameplay-status.json"
            })
            {
                string path = Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config", stale);
                if (File.Exists(path))
                    ledger.RemoveOwned(path);
            }
            string game = QaEnvironment.ClientRoot, profile = Path.Combine(ledger.Root, "profiles/client-a");
            var previous = running[1];
            running[1] = new QaSupervised(
                ledger,
                "client-a",
                Path.Combine(game, "valheim.exe"),
                game,
                new[] {
                "-batchmode",
                "-nographics",
                "-logFile",
                "-",
                "-savedir",
                Path.Combine(profile, "saves"),
                "--doorstop-enabled",
                "true",
                "--doorstop-target-assembly",
                Path.Combine(profile, "BepInEx/core/BepInEx.Preloader.dll")
                },
                pipe: pipe
            );
            previous.Dispose();
            DateTime deadline = DateTime.UtcNow.AddSeconds(150);
            while (true)
            {
                try
                {
                    var state = JsonNode.Parse(
                        QaFiles.ReadSharedText(Path.Combine(profile, "BepInEx/config/qa-status.json"))
                    )!;
                    if (state["connection"]?.GetValue<string>() == "Connected"
                        && state["localPlayer"]?.GetValue<long>() != 0)
                        break;
                }
                catch (IOException) { }
                catch (JsonException) { }
                if (DateTime.UtcNow >= deadline || running[1].Fault != null || running[1].Wrapper.HasExited)
                    throw new IOException("UI save/reload native A did not become ready");
                await Task.Delay(250);
            }
            var saved = await QaAuraCases.Command("client-a", "ui-persist", seconds: 30);
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R10/ui-persistence-current.json"),
                JsonSerializer.SerializeToUtf8Bytes(saved)
            );
            if (!JsonNode.DeepEquals(
                result["sample"]!["after"]!["unlockedDecorationSlots"],
                saved["sample"]!["device"]!["unlockedDecorationSlots"]
            )
            || !JsonNode.DeepEquals(
                result["sample"]!["after"]!["decorationPlacements"],
                saved["sample"]!["device"]!["decorationPlacements"]
            )
            || !saved["sample"]!["playlists"]!.AsArray().Any(
                p => p!["id"]!.GetValue<string>() == result["sample"]!["playlist"]!["id"]!.GetValue<string>()
                    && JsonNode.DeepEquals(p["tracks"], result["sample"]!["playlist"]!["tracks"])
            ))
                throw new IOException("Native reload lost UI24/placement/playlist state");
            Console.WriteLine("QA_R10_COMBO_NATIVE_SAVE_PASS " + result["sample"]!.ToJsonString());
        }
    }
}
