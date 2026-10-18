using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaNativeMusicCases
    {
        public static void Preserve(QaLedger ledger)
        {
            string path = Path.Combine(ledger.Root, "evidence/R11/native-music-current.json");
            if (!File.Exists(path))
                return;
            string saved = Path.Combine(
                ledger.Root,
                "evidence/R11/native-music-prior-" + QaFiles.Hash(path).Substring(0, 8)
            );
            if (!File.Exists(saved + ".json"))
                QaFiles.Copy(ledger, path, saved + ".json");
            if (!File.Exists(saved + "-restore.json"))
                QaFiles.Copy(ledger, Path.Combine(ledger.Root, "evidence/R09/restore.json"), saved + "-restore.json");
        }
        public static async Task Run(QaLedger ledger)
        {
            await QaAuraCases.Prepare(ledger);
            try
            {
                var result = await QaAuraCases.Command("client-a", "audio-native-music", seconds: 45);
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R11/native-music-current.json"),
                    JsonSerializer.SerializeToUtf8Bytes(result)
                );
                if (result["sample"]?["passed"]?.GetValue<bool>() != true)
                    throw new IOException("Native music case incomplete");
                Console.WriteLine("QA_NATIVE_MUSIC_PASS " + result["sample"]!.ToJsonString());
            }
            catch
            {
                string status = Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/qa-gameplay-status.json");
                if (File.Exists(status))
                    QaFiles.Write(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R11/native-music-current.json"),
                        System.Text.Encoding.UTF8.GetBytes(QaFiles.ReadSharedText(status))
                    );
                throw;
            }
        }
    }
}
