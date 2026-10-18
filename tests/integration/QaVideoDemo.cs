using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaVideoDemo
    {
        public static async Task ReviewFinal(QaLedger ledger)
        {
            var proof = JsonNode.Parse(QaFiles.ReadSharedText(Path.Combine(ledger.Root, "evidence/R12/demo.json")))!;
            var capture = proof["capture"]!;
            var frames = capture["frames"]!.AsArray().Skip(capture["warmupCount"]!.GetValue<int>()).ToArray();
            double origin = frames[0]!["time"]!.GetValue<double>();
            foreach (var sample in new[] { (4d, "main"), (16d, "mini12"), (24d, "mini15") })
            {
                int frame = Array.FindIndex(frames, f => f!["time"]!.GetValue<double>() - origin >= sample.Item1);
                if (frame < 0)
                    throw new IOException("Video section missing: " + sample.Item2);
                await QaVideoProbe.Preview(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R12/demo.mp4"),
                    Path.Combine(ledger.Root, "evidence/R12/demo-" + sample.Item2 + ".png"),
                    "demo-final-" + sample.Item2,
                    frame
                );
            }
        }
        public static void PrepareSource(QaLedger ledger)
        {
            PreserveFailure(ledger);
            string fxFailure = Path.Combine(ledger.Root, "evidence/R01-R02/client-a-fx-refraction.json");
            if (File.Exists(fxFailure))
            {
                string receipt = Path.Combine(
                    ledger.Root,
                    "evidence/R12/fx-prior-" + QaFiles.Hash(fxFailure).Substring(0, 8) + ".json"
                );
                if (!File.Exists(receipt))
                    QaFiles.Copy(ledger, fxFailure, receipt);
            }
            string failed = Path.Combine(ledger.Root, "evidence/R01-R02/client-a-demo-setup.json");
            string kept = Path.Combine(ledger.Root, "evidence/R12/demo-setup-failed.json");
            if (File.Exists(failed) && !File.Exists(kept))
            {
                var prior = JsonNode.Parse(QaFiles.ReadSharedText(failed));
                if (prior?["commandError"] != null)
                    QaFiles.Copy(ledger, failed, kept);
            }
            string path = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav");
            if (File.Exists(path))
            {
                if (!ledger.State.Files.ContainsKey(ledger.Resolve(path)))
                    throw new IOException("Unowned existing demo WAV");
                ValidateSource(path);
            }
            else
            {
                WriteWave(ledger, path);
                ValidateSource(path);
            }
            Console.WriteLine("QA_DEMO_SOURCE_VALID bytes=" + new FileInfo(path).Length + " sha=" + QaFiles.Hash(path));
        }

        private static void PreserveFailure(QaLedger ledger)
        {
            string status = Path.Combine(ledger.Root, "evidence/R01-R02/client-a-video-demo-probe.json");
            string kept = Path.Combine(ledger.Root, "evidence/R12/demo-probe-failed.json");
            if (!File.Exists(status))
                return;
            var prior = JsonNode.Parse(QaFiles.ReadSharedText(status));
            if (prior?["commandError"] == null)
                return;
            string prefix = "demo-probe-failed";
            if (File.Exists(kept))
            {
                var first = JsonNode.Parse(QaFiles.ReadSharedText(kept));
                if (first?["id"]?.GetValue<string>() == prior["id"]?.GetValue<string>())
                    return;
                prefix = "demo-probe-failed-" + QaFiles.Hash(status).Substring(0, 8);
                kept = Path.Combine(ledger.Root, "evidence/R12/" + prefix + ".json");
                if (File.Exists(kept))
                    return;
            }
            QaFiles.Copy(ledger, status, kept);
            string raw = Path.Combine(ledger.Root, "current/r12-video/demo-probe-video.mp4");
            if (File.Exists(raw))
                QaFiles.Copy(ledger, raw, Path.Combine(ledger.Root, "evidence/R12/" + prefix + "-video.mp4"));
            string wave = Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/demo-probe.wav");
            if (File.Exists(wave))
                QaFiles.Copy(ledger, wave, Path.Combine(ledger.Root, "evidence/R12/" + prefix + ".wav"));
        }

        public static async Task FailurePreview(QaLedger ledger)
        {
            PreserveFailure(ledger);
            await QaVideoProbe.Preview(
                ledger,
                Path.Combine(ledger.Root, "current/r12-video/demo-probe-video.mp4"),
                Path.Combine(ledger.Root, "evidence/R12/demo-probe-failed-quarter.png"),
                "r12-failed-still",
                150
            );
        }

        public static async Task RemuxExisting(QaLedger ledger)
        {
            var status = JsonNode.Parse(
                QaFiles.ReadSharedText(Path.Combine(ledger.Root, "evidence/R01-R02/client-a-video-demo-probe.json"))
            )!;
            var capture = status["capture"]!.DeepClone();
            var frames = capture["frames"]!.AsArray();
            string filter = "settb=1/1000000,setpts='("
                + string.Join(
                "+",
                frames.Select(
                    (
                            f,
                            i
                        ) => "eq(N," + i + ")*"
                    + f!["time"]!.GetValue<double>().ToString("F9", CultureInfo.InvariantCulture)
                )
            )
                + ")/TB',trim=start_frame="
                + capture["warmupCount"]!.GetValue<int>()
                + ",setpts=PTS-STARTPTS";
            if (Encoding.UTF8.GetByteCount(filter) > 2 * QaLedger.MiB)
                throw new IOException("Timing metadata cap");
            string timing = Path.Combine(ledger.Root, "current/r12-video/demo-probe-mux-timestamps.txt");
            QaFiles.Write(ledger, timing, Encoding.UTF8.GetBytes(filter));
            capture["originalTiming"] = capture["timing"]!.DeepClone();
            capture["timing"] = timing;
            capture["muxRepair"] =
                "Frame-index pre-roll trim and microsecond timebase preserve all original frames; no interpolation";
            double delta = capture["audioDelta"]!.GetValue<double>();
            string audioFilter = delta >= 0
                ? "adelay=" + Math.Round(delta * 1000).ToString(CultureInfo.InvariantCulture) + ":all=1"
                : "atrim=start=" + (-delta).ToString("R", CultureInfo.InvariantCulture) + ",asetpts=PTS-STARTPTS";
            string raw = Path.Combine(ledger.Root, "current/r12-video/demo-probe-video.mp4");
            string final = Path.Combine(ledger.Root, "evidence/R12/demo-probe.mp4");
            using (var mux = new QaSupervised(
                ledger,
                "r12-offline-mux",
                QaEnvironment.Encoder,
                ledger.Project,
                new[]
                    {
                "-hide_banner",
                "-v",
                "error",
                "-nostdin",
                "-i",
                raw,
                "-i",
                capture["audio"]!["file"]!.GetValue<string>(),
                "-map",
                "0:v:0",
                "-map",
                "1:a:0",
                "-c:v",
                "h264_nvenc",
                "-preset",
                "p4",
                "-cq",
                "23",
                "-b:v",
                "0",
                "-bf",
                "0",
                "-pix_fmt",
                "yuv420p",
                "-fps_mode",
                "passthrough",
                "-enc_time_base",
                "1:1000000",
                "-/filter:v",
                timing,
                "-af",
                audioFilter,
                "-c:a",
                "aac",
                "-b:a",
                "192k",
                "-t",
                capture["duration"]!.GetValue<double>().ToString("R", CultureInfo.InvariantCulture),
                "-movflags",
                "+frag_keyframe+empty_moov",
                "-f",
                "mp4",
                "pipe:1"
                    },
                64 * QaLedger.MiB
            ))
                if (await mux.WaitAsync(TimeSpan.FromSeconds(30)) != 0)
                    throw new IOException("Actual captured media offline mux failed");
            if (File.Exists(final))
                ledger.RemoveOwned(final);
            using (var input = File.OpenRead(Path.Combine(ledger.Root, "current/r12-offline-mux/stdout.log")))
            {
                int prefix = 0;
                while (input.ReadByte() != '\n')
                    if (++prefix > 128)
                        throw new IOException("Mux wrapper header missing");
                using var output = new QaQuotaStream(ledger, final, "r12-video", 64 * QaLedger.MiB);
                var block = new byte[65536];
                int count;
                while ((count = input.Read(block, 0, block.Length)) > 0)
                    output.Write(block, 0, count);
                output.Flush();
            }
            var mixed = new JsonObject { ["offline"] = true, ["bytes"] = new FileInfo(final).Length };
            await QaVideoProbe.Verify(ledger, status, capture, mixed, "demo-probe");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R12/demo-probe-offline-mux.json"),
                JsonSerializer.SerializeToUtf8Bytes(mixed)
            );
        }

        public static async Task VerifyExistingDemo(QaLedger ledger)
        {
            string oldProof = Path.Combine(ledger.Root, "evidence/R12/demo-probe.json");
            string preserved = Path.Combine(ledger.Root, "evidence/R12/demo-probe-prior-timebase.json");
            if (File.Exists(oldProof) && !File.Exists(preserved))
                QaFiles.Copy(ledger, oldProof, preserved);
            var status = JsonNode.Parse(
                QaFiles.ReadSharedText(Path.Combine(ledger.Root, "evidence/R01-R02/client-a-video-demo-probe.json"))
            )!;
            var capture = status["capture"]!.DeepClone();
            capture["originalTiming"] = capture["timing"]!.DeepClone();
            capture["timing"] = Path.Combine(ledger.Root, "current/r12-video/demo-probe-mux-timestamps.txt");
            capture["muxRepair"] =
                "Frame-index pre-roll trim and microsecond timebase; all109 original retained frames encoded, no interpolation";
            var mixed = new JsonObject
            {
                ["offline"] = true,
                ["bytes"] = new FileInfo(Path.Combine(ledger.Root, "evidence/R12/demo-probe.mp4")).Length
            };
            await QaVideoProbe.Verify(ledger, status, capture, mixed, "demo-probe");
        }

        private static void ValidateSource(string path)
        {
            if (new FileInfo(path).Length != 7680044)
                throw new IOException("Incomplete fixed demo WAV size");
            using var input = File.OpenRead(path);
            using var reader = new BinaryReader(input);
            if (reader.ReadUInt32() != 0x46464952 || reader.ReadInt32() != 7680036 || reader.ReadUInt32() != 0x45564157
            || reader.ReadUInt32() != 0x20746d66
            || reader.ReadInt32() != 16
            || reader.ReadInt16() != 1
            || reader.ReadInt16() != 2
            || reader.ReadInt32() != 48000
            || reader.ReadInt32() != 192000
            || reader.ReadInt16() != 4
            || reader.ReadInt16() != 16
            || reader.ReadUInt32() != 0x61746164
            || reader.ReadInt32() != 7680000)
                throw new IOException("Fixed demo WAV PCM header differs");
        }

        public static async Task Run(QaLedger ledger, bool local = false)
        {
            string wave = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav");
            ValidateSource(wave);
            if (local)
                await QaAuraCases.PrepareLocal(ledger);
            else
                await QaAuraCases.Prepare(ledger);
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
                await QaAuraCases.Command(
                    "client-a",
                    "visual-setup",
                    new
                    {
                        key = "AUDIO"
                    }
                );
                var accepted = JsonNode.Parse(
                    QaFiles.ReadSharedText(Path.Combine(ledger.Root, "evidence/R12/demo-probe.json"))
                )!;
                var scene = accepted["capture"]!["scene"]!;
                await QaAuraCases.Command(
                    "client-a",
                    "demo-setup",
                    local
                        ? new JsonObject()
                        : new JsonObject
                        {
                            ["coast"] = scene["landing"]!.DeepClone(),
                            ["sea"] = scene["sea"]!.DeepClone()
                        },
                    seconds: 150
                );
                string fxPath = Path.Combine(ledger.Root, "evidence/R12/fx-refraction.json");
                var fx = File.Exists(fxPath) ? JsonNode.Parse(QaFiles.ReadSharedText(fxPath)) : null;
                string candidate = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"));
                if ((!local && fx?["sha"]?.GetValue<string>() != candidate)
                    || fx?["capture"]?["passed"]?.GetValue<bool>() != true)
                {
                    fx = await QaAuraCases.Command("client-a", "fx-refraction", seconds: 30);
                    QaFiles.Write(ledger, fxPath, JsonSerializer.SerializeToUtf8Bytes(fx));
                    foreach (var frame in fx["capture"]!["frames"]!.AsArray())
                    {
                        string name = frame!["file"]!.GetValue<string>();
                        QaFiles.Copy(
                            ledger,
                            Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/support", name),
                            Path.Combine(ledger.Root, "evidence/R12", name)
                        );
                    }
                }
                else
                    Console.WriteLine("QA_FX_REUSED same candidate, successful native proof");
                var priorVideo = JsonNode.Parse(
                    QaFiles.ReadSharedText(Path.Combine(ledger.Root, "evidence/R12/demo-probe.json"))
                )!;
                bool sameAcceptedProbe = local
                    && priorVideo["loadedSha"]?.GetValue<string>() == candidate
                    && priorVideo["passed"]?.GetValue<bool>() == true;
                if (!sameAcceptedProbe)
                    await Record(ledger, "demo-probe", 2);
                string review = Path.Combine(ledger.Root, "current/r12-video/demo-review.txt");
                if (File.Exists(review))
                    ledger.RemoveOwned(review);
                QaFiles.Write(ledger, review, Encoding.UTF8.GetBytes(sameAcceptedProbe ? "ACCEPT" : "PENDING"));
                Console.WriteLine(
                    "QA_DEMO_CONTEXT_REVIEW_WAIT actual demo-probe-quarter.png; write ACCEPT or REJECT to " + review
                );
                DateTime deadline = DateTime.UtcNow.AddMinutes(4);
                while (true)
                {
                    string decision = QaFiles.ReadSharedText(review).Trim();
                    if (decision == "ACCEPT")
                        break;
                    if (decision == "REJECT")
                        throw new IOException("Actual context probe rejected; long recording not started");
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException("Native context review deadline; long recording not started");
                    await Task.Delay(500);
                }
                await Record(ledger, "demo", 30);
            }
            finally
            {
                try
                {
                    await QaAuraCases.Command("client-a", "demo-clear", seconds: 90);
                }
                finally
                {
                    await QaAuraCases.Command("client-a", "audio-clear");
                }
            }
        }

        private static async Task Record(QaLedger ledger, string stem, int seconds)
        {
            await QaAuraCases.Command("client-a", "demo-rewind");
            string raw = Path.Combine(ledger.Root, "current/r12-video/" + stem + "-video.mp4");
            string final = Path.Combine(ledger.Root, "evidence/R12/" + stem + ".mp4");
            var status = await QaVideoProbe.Transfer(ledger, raw, "video-" + stem,
                new JsonObject { ["stem"] = stem, ["seconds"] = seconds, ["context"] = true });
            var capture = status["capture"]!.DeepClone();
            var mixed = await QaVideoProbe.Transfer(
                ledger,
                final,
                "video-" + stem + "-mux",
                new JsonObject
                {
                    ["input"] = raw,
                    ["audio"] = capture["audio"]!["file"]!.DeepClone(),
                    ["timing"] = capture["timing"]!.DeepClone(),
                    ["audioDelta"] = capture["audioDelta"]!.DeepClone(),
                    ["duration"] = capture["duration"]!.DeepClone()
                }
            );
            await QaVideoProbe.Verify(ledger, status, capture, mixed, stem);
            ledger.RemoveOwned(raw);
        }

        private static void WriteWave(QaLedger ledger, string path)
        {
            const int rate = 48000, frames = rate * 40;
            if (File.Exists(path))
                ledger.RemoveOwned(path);
            using var stream = new QaQuotaStream(ledger, path, "r12-demo-source", 10 * QaLedger.MiB);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(new byte[] { 82, 73, 70, 70 });
            writer.Write(36 + frames * 4);
            writer.Write(new byte[] { 87, 65, 86, 69, 102, 109, 116, 32 });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(rate);
            writer.Write(rate * 4);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write(new byte[] { 100, 97, 116, 97 });
            writer.Write(frames * 4);
            double[] notes = { 220, 261.625565, 293.664768, 329.627557, 293.664768, 261.625565, 246.941651, 196 };
            var block = new byte[32768];
            for (int start = 0; start < frames;)
            {
                int taken = Math.Min(block.Length / 4, frames - start);
                for (int n = 0; n < taken; n++)
                {
                    double t = (start + n) / (double)rate, beat = t % .5;
                    double envelope = beat < .3 ? Math.Sin(Math.PI * beat / .3) : 0;
                    double bass = .55 * envelope * Math.Sin(2 * Math.PI * 93.75 * t);
                    double lead = .12 * Math.Exp(-8 * beat)
                        * Math.Sin(2 * Math.PI * notes[(int)(t * 2) % notes.Length] * t);
                    double fade = Math.Min(1, Math.Min(t / .05, (40 - t) / .1));
                    short sample = (short)(32767 * (bass + lead) * fade);
                    for (int c = 0; c < 2; c++)
                    {
                        block[n * 4 + c * 2] = (byte)sample;
                        block[n * 4 + c * 2 + 1] = (byte)(sample >> 8);
                    }
                }
                writer.Write(block, 0, taken * 4);
                start += taken;
            }
            writer.Flush();
        }
    }
}
