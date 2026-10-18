using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaAudioResetCases
    {
        public static void Prepare(QaLedger ledger)
        {
            string wave = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav");
            if (!ledger.State.Files.ContainsKey(ledger.Resolve(wave)))
                throw new IOException("Owned mathematical audio fixture missing");
            using var reader = new BinaryReader(File.OpenRead(wave));
            if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
                throw new IOException("Owned WAV header missing");
            reader.BaseStream.Position = 22;
            short channels = reader.ReadInt16();
            int rate = reader.ReadInt32();
            reader.BaseStream.Position = 34;
            short bits = reader.ReadInt16();
            if (channels != 2 || rate != 48000 || bits != 16 || reader.BaseStream.Length != 7680044)
                throw new IOException("Owned forty-second stereo48k fixture changed");
            Console.WriteLine("QA_RESET_SOURCE_VALID " + QaFiles.Hash(wave));
        }
        public static async Task Run(QaLedger ledger)
        {
            await QaAuraCases.Prepare(ledger);
            string wave = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav"),
                path = Path.Combine(ledger.Root, "evidence/R11/native-reset.json");
            try
            {
                await QaAuraCases.Command(
                    "client-a",
                    "audio-setup",
                    new
                    {
                        fixture = wave
                    }
                );
                var result = await QaAuraCases.Command("client-a", "audio-native-reset", null, 60);
                bool passed = result["sample"]!["resetCases"]!.AsArray().Count == 2
                && result["sample"]!["resetCases"]!.AsArray().All(c => c!["passed"]!.GetValue<bool>())
                && result["sample"]!["configurationRestored"]!.GetValue<bool>();
                QaFiles.Write(
                    ledger,
                    path,
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            passed,
                            candidateSha = QaFiles.Hash(
                            Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")
                        ),
                            fixtureSha = QaFiles.Hash(Path.Combine(QaEnvironment.BinRoot, "QaGameplay.dll")),
                            waveSha = QaFiles.Hash(wave),
                            scope = "Actual Unity engine configuration resets, engine callbacks, paused/playing position and clip/stream/subscription/PCM lifecycle; no OS default-device change, ducking, full AUDIO or twelve-hour claim",
                            result,
                            utc = DateTime.UtcNow
                        }
                    )
                );
                if (!passed)
                    throw new IOException("Actual native audio reset did not pass");
                await QaAuraCases.Command("client-a", "audio-clear");
            }
            catch (Exception failure)
            {
                QaFiles.Write(
                    ledger,
                    path,
                    JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        passed = false,
                        failure = failure.ToString(),
                        status = JsonNodeRead(ledger),
                        utc = DateTime.UtcNow
                    })
                );
                throw;
            }
        }
        private static System.Text.Json.Nodes.JsonNode? JsonNodeRead(QaLedger ledger)
        {
            try
            {
                return System.Text.Json.Nodes.JsonNode.Parse(
                    QaFiles.ReadSharedText(
                        Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/qa-gameplay-status.json")
                    )
                );
            }
            catch (Exception failure) when (failure is IOException || failure is JsonException)
            {
                return null;
            }
        }
    }
}
