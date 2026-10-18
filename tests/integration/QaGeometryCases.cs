using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaGeometryCases
    {
        public static async Task Run(QaLedger ledger)
        {
            await QaAuraCases.Prepare(ledger);
            await QaAuraCases.Command(
                "client-a",
                "audio-setup",
                new
                {
                    fixture = Path.Combine(ledger.Root, "current/membrane-literal/membranes.wav")
                }
            );
            await QaAuraCases.Command(
                "client-a",
                "visual-setup",
                new
                {
                    key = "AUDIO"
                }
            );
            var status = await QaAuraCases.Command("client-a", "geometry-capture");
            string folder = Path.Combine(ledger.Root, "evidence/R12/model03");
            foreach (var shot in status["capture"]!["shots"]!.AsArray())
            {
                string name = shot!["file"]!.GetValue<string>();
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/model03", name),
                    Path.Combine(folder, name)
                );
                Console.WriteLine("QA_NATIVE_MACRO " + name);
            }
            QaFiles.Write(ledger, Path.Combine(folder, "geometry.json"), JsonSerializer.SerializeToUtf8Bytes(status));
            await QaAuraCases.Command("client-a", "audio-clear");
        }
    }
}
