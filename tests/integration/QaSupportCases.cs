using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaSupportCases
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
            try
            {
                await QaAuraCases.Command(
                    "client-a",
                    "visual-setup",
                    new
                    {
                        key = "AUDIO"
                    }
                );
                var status = await QaAuraCases.Command(
                    "client-a",
                    "support-capture",
                    new
                    {
                        contextIconOnly = true
                    },
                    seconds: 150
                );
                string folder = Path.Combine(ledger.Root, "evidence/R12/support");
                foreach (string name in new[] {
                    "daylight-context.png",
                    "icon-candidate-256.png",
                    "icon-preview-128.png"
                })
                    QaFiles.Copy(
                        ledger,
                        Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/support", name),
                        Path.Combine(folder, name)
                    );
                QaFiles.Write(
                    ledger,
                    Path.Combine(folder, "support.json"),
                    JsonSerializer.SerializeToUtf8Bytes(status)
                );
                Console.WriteLine("QA_NATIVE_SUPPORT_CONTEXT_ICON complete; review/approval pending");
            }
            finally
            {
                await QaAuraCases.Command("client-a", "audio-clear");
            }
        }
    }
}
