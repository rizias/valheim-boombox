using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaVideoProbe
    {
        private const long Cap = 64 * QaLedger.MiB;
        private static string Encoder => QaEnvironment.Encoder;
        private static string Probe => QaEnvironment.MediaProbe;
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
            string temporary = Path.Combine(ledger.Root, "current/r12-video/probe-video.mp4");
            string final = Path.Combine(ledger.Root, "evidence/R12/probe.mp4");
            JsonNode status = await Transfer(ledger, temporary, "video-probe", new JsonObject());
            JsonNode capture = status["capture"]!.DeepClone();
            JsonNode mixed = await Transfer(
                ledger,
                final,
                "video-mux",
                new JsonObject
                {
                    ["input"] = temporary,
                    ["audio"] = capture["audio"]!["file"]!.DeepClone(),
                    ["timing"] = capture["timing"]!.DeepClone(),
                    ["audioDelta"] = capture["audioDelta"]!.DeepClone(),
                    ["duration"] = capture["duration"]!.DeepClone()
                }
            );
            await Verify(ledger, status, capture, mixed);
            ledger.RemoveOwned(temporary);
            await QaAuraCases.Command("client-a", "audio-clear");
        }

        public static async Task VerifyExisting(QaLedger ledger)
        {
            var status = JsonNode.Parse(
                File.ReadAllText(Path.Combine(ledger.Root, "evidence/R01-R02/client-a-video-probe.json"))
            )!;
            var mixed = JsonNode.Parse(
                File.ReadAllText(Path.Combine(ledger.Root, "evidence/R01-R02/client-a-video-mux.json"))
            )!;
            await Verify(ledger, status, status["capture"]!.DeepClone(), mixed);
            string temporary = Path.Combine(ledger.Root, "current/r12-video/probe-video.mp4");
            if (File.Exists(temporary))
                ledger.RemoveOwned(temporary);
        }

        internal static async Task Verify(
            QaLedger ledger,
            JsonNode status,
            JsonNode capture,
            JsonNode mixed,
            string stem = "probe"
        )
        {
            string final = Path.Combine(ledger.Root, "evidence/R12/" + stem + ".mp4");
            string suffix = stem == "probe" ? "" : "-" + stem;
            using (var inspect = new QaSupervised(
                ledger,
                "r12-media" + suffix,
                Probe,
                ledger.Project,
                new[] {
                "-v",
                "error",
                "-count_frames",
                "-show_entries",
                "stream=codec_type,codec_name,width,height,nb_read_frames,duration:format=duration,size",
                "-of",
                "json",
                final
                },
                QaLedger.MiB
            ))
                if (await inspect.WaitAsync(TimeSpan.FromSeconds(20)) != 0)
                    throw new IOException("Media probe failed");
            string mediaText = File.ReadAllText(Path.Combine(
                    ledger.Root,
                    "current/r12-media" + suffix + "/stdout.log"
                ));
            var media = JsonNode.Parse(mediaText.Substring(mediaText.IndexOf('{')))!;
            var video = media["streams"]!.AsArray().Single(s => s!["codec_type"]!.GetValue<string>() == "video")!;
            if (int.Parse(video["nb_read_frames"]!.GetValue<string>()) != capture["count"]!.GetValue<int>() ||
            !media["streams"]!.AsArray().Any(s => s!["codec_type"]!.GetValue<string>() == "audio"))
                throw new IOException("Encoded video/audio count differs");
            using var cadence = new QaSupervised(
                ledger,
                "r12-cadence" + suffix,
                Encoder,
                ledger.Project,
                new[]
                    {
                "-v", "error", "-i", final, "-map", "0:v:0", "-an", "-vf", "mpdecimate=hi=768:lo=256:frac=0.1",
                "-fps_mode", "vfr", "-enc_time_base", "1:1000000", "-f", "framemd5", "pipe:1"
                    },
                QaLedger.MiB
            );
            int exit = await cadence.WaitAsync(TimeSpan.FromSeconds(20));
            int changing = File.ReadLines(Path.Combine(ledger.Root, "current/r12-cadence" + suffix + "/stdout.log"))
            .Count(line => line.Length > 0 && char.IsDigit(line[0]) && line.Contains(','));
            double actualDuration = double.Parse(
                media["format"]!["duration"]!.GetValue<string>(),
                System.Globalization.CultureInfo.InvariantCulture
            );
            double effective = changing / actualDuration;
            using (var audio = new QaSupervised(
                ledger,
                "r12-audio-output" + suffix,
                Encoder,
                ledger.Project,
                new[] {
                "-hide_banner",
                "-i",
                final,
                "-map",
                "0:a:0",
                "-vn",
                "-af",
                "astats=metadata=1:reset=0",
                "-f",
                "null",
                "-"
                },
                QaLedger.MiB
            ))
                if (await audio.WaitAsync(TimeSpan.FromSeconds(20)) != 0)
                    throw new IOException("Final AAC decode failed");
            string audioStats = File.ReadAllText(Path.Combine(
                    ledger.Root,
                    "current/r12-audio-output" + suffix + "/stderr.log"
                ));
            if (!audioStats.Contains("RMS level dB:", StringComparison.Ordinal)
            || audioStats.Contains("RMS level dB: -inf", StringComparison.Ordinal))
                throw new IOException("Final AAC has no measured energy");
            string evidence = Path.Combine(ledger.Root, "evidence/R12/" + stem + ".json");
            byte[] bytes =
                JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    passed = exit == 0 && effective >= 50,
                    mediaFile = final,
                    capture,
                    mixed,
                    media,
                    changing,
                    effective,
                    actualDuration,
                    audioStats,
                    candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                    loadedSha = status["sha"]!.GetValue<string>(),
                    scope = stem == "probe"
                            ? "2s actual model-only GPU/NVENC and SourceDSP probe; geometry/full24/game context not accepted"
                            : "Actual native daylight context Camera/GPU/NVENC/SourceDSP; pixels pending root review; not full catalog/cape/FX acceptance"
                }
            );
            if (bytes.Length > 2 * QaLedger.MiB)
                throw new IOException("Metadata cap");
            QaFiles.Write(ledger, evidence, bytes);
            string preview = Path.Combine(ledger.Root, "evidence/R12/" + stem + "-quarter.png");
            await Preview(ledger, final, preview, "r12-still" + suffix);
            if (exit != 0 || effective < 50)
                throw new IOException("Actual decoded cadence below 50 changing frames/s: " + effective);
            Console.WriteLine("QA_NATIVE_VIDEO_PROBE encoded/audio/cadence complete " + final);
        }

        internal static async Task Preview(QaLedger ledger, string final, string preview, string role, int frame = -1)
        {
            string[] arguments = frame < 0
                ? new[] {
                "-v",
                "error",
                "-ss",
                "0.5",
                "-i",
                final,
                "-map",
                "0:v:0",
                "-frames:v",
                "1",
                "-c:v",
                "png",
                "-f",
                "image2pipe",
                "pipe:1"
                }
                : new[] {
                "-v",
                "error",
                "-i",
                final,
                "-map",
                "0:v:0",
                "-vf",
                "select=eq(n\\," + frame.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
                "-frames:v",
                "1",
                "-c:v",
                "png",
                "-f",
                "image2pipe",
                "pipe:1"
                };
            using (var still = new QaSupervised(ledger, role, Encoder, ledger.Project, arguments, 2 * QaLedger.MiB))
                if (await still.WaitAsync(TimeSpan.FromSeconds(20)) != 0)
                    throw new IOException("Single preview frame failed");
            if (File.Exists(preview))
                ledger.RemoveOwned(preview);
            using (var input = File.OpenRead(Path.Combine(ledger.Root, "current/" + role + "/stdout.log")))
            {
                int prefix = 0;
                while (input.ReadByte() != '\n')
                    if (++prefix > 128)
                        throw new IOException("Preview wrapper header missing");
                using var output = new QaQuotaStream(ledger, preview, "r12-preview", 2 * QaLedger.MiB);
                var block = new byte[65536];
                int count;
                while ((count = input.Read(block, 0, block.Length)) > 0)
                    output.Write(block, 0, count);
                output.Flush();
            }
        }

        internal static async Task<JsonNode> Transfer(
            QaLedger ledger,
            string target,
            string operation,
            JsonObject request,
            int seconds = 120
        )
        {
            if (File.Exists(target))
                ledger.RemoveOwned(target);
            string name = "BoomboxQA-R12-" + Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream(
                name,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous
            );
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 15));
            Task receiver = Receive();
            request["pipe"] = name;
            try
            {
                JsonNode result = await QaAuraCases.Command("client-a", operation, request, seconds: seconds);
                await receiver;
                return result;
            }
            catch
            {
                cancellation.Cancel();
                pipe.Dispose();
                try
                {
                    await receiver;
                }
                catch { }
                throw;
            }
            async Task Receive()
            {
                await pipe.WaitForConnectionAsync(cancellation.Token);
                ledger.ReserveFile(target, Cap);
                try
                {
                    using var file = new FileStream(
                        ledger.Resolve(target),
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read,
                        65536
                    );
                    var buffer = new byte[65536];
                    int count;
                    long written = 0;
                    while ((count = await pipe.ReadAsync(buffer, cancellation.Token)) > 0)
                    {
                        if (written + count > Cap)
                            throw new IOException("Video exceeds reserved 64 MiB");
                        file.Write(buffer, 0, count);
                        written += count;
                    }
                    file.Flush(true);
                }
                finally
                {
                    if (File.Exists(target))
                        ledger.CommitFile(target, "r12-video");
                }
            }
        }
    }
}
