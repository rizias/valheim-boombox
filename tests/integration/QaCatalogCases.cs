using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaCatalogCases
    {
        public static async Task Run(QaLedger ledger)
        {
            await QaAuraCases.Prepare(ledger);
            await QaAuraCases.Command("client-a", "audio-setup");
            await QaAuraCases.Command(
                "client-a",
                "visual-setup",
                new
                {
                    key = "AUDIO"
                }
            );
            try
            {
                var result = await QaAuraCases.Command("client-a", "catalog-capture", seconds: 150);
                foreach (var page in result["capture"]!["pages"]!.AsArray())
                {
                    string name = page!["file"]!.GetValue<string>();
                    QaFiles.Copy(
                        ledger,
                        Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/catalog", name),
                        Path.Combine(ledger.Root, "evidence/R12/catalog", name)
                    );
                }
                foreach (var frame in result["capture"]!["capeFrames"]!.AsArray())
                {
                    string name = frame!["file"]!.GetValue<string>();
                    QaFiles.Copy(
                        ledger,
                        Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/support", name),
                        Path.Combine(ledger.Root, "evidence/R12/catalog", name)
                    );
                }
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R12/catalog/catalog.json"),
                    JsonSerializer.SerializeToUtf8Bytes(result)
                );
                Console.WriteLine(
                    "QA_CATALOG_CAPTURED " + result["capture"]!["entries"]!.AsArray().Count
                        + " variants; visual review pending"
                );
            }
            finally
            {
                await QaAuraCases.Command("client-a", "audio-clear");
            }
        }
    }
}
