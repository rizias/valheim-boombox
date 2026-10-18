using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static partial class QaUiCases
    {
        public static bool Slots;
        public static bool Combo;
        public static void PreserveSlots(QaLedger ledger)
        {
            string source = Path.Combine(ledger.Root, "evidence/R01-R02/client-a-ui-slots.json"),
                target = Path.Combine(ledger.Root, "evidence/R10/ui-06-price-design-partial.json");
            if (!File.Exists(source) || File.Exists(target))
                return;
            QaFiles.Copy(ledger, source, target);
            foreach (string name in new[] { "ui-06-first-free.png", "ui-06-last-price.png" })
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture", name),
                    Path.Combine(ledger.Root, "evidence/R10", name)
                );
            QaFiles.Copy(
                ledger,
                Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                Path.Combine(ledger.Root, "evidence/R10/ui-06-price-restore.json")
            );
        }
        public static void Preserve(QaLedger ledger)
        {
            string prior = Path.Combine(ledger.Root, "evidence/R01-R02/client-a-ui-recipe.json");
            if (!File.Exists(prior))
                return;
            string kept = Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt1-input-failure.json");
            if (!File.Exists(kept))
            {
                QaFiles.Copy(ledger, prior, kept);
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt1-restore.json")
                );
            }
            string current = Path.Combine(ledger.Root, "evidence/R10/ui-01-current.json"),
                modal = Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt2-modal-failure.json");
            if (File.Exists(current) && !File.Exists(modal))
            {
                QaFiles.Copy(ledger, current, modal);
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-recipe.png"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt2-modal.png")
                );
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt2-restore.json")
                );
            }
            string aim = Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt3-aim-failure.json");
            if (File.Exists(current) && File.Exists(modal) && !File.Exists(aim))
            {
                QaFiles.Copy(ledger, current, aim);
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-recipe.png"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt3-aim.png")
                );
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt3-restore.json")
                );
            }
            string functional = Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt4-functional-layout.json");
            if (File.Exists(current) && File.Exists(aim) && !File.Exists(functional))
            {
                QaFiles.Copy(ledger, current, functional);
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-recipe.png"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt4-layout.png")
                );
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt4-restore.json")
                );
            }
            string partial = Path.Combine(ledger.Root, "evidence/R10/ui-01-layout-ui02-current.json");
            if (!File.Exists(partial)
                && File.Exists(prior)
                && JsonNode.Parse(File.ReadAllText(prior))?["sha"]?.GetValue<string>()
                == "37125B8332A039C84303E9D2E0CF74BE547CCBCC4D5B50D346C0BFC881EAC8A8")
            {
                QaFiles.Copy(ledger, prior, partial);
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/ui-01-recipe.png"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-five-materials.png")
                );
                QaFiles.Copy(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R09/restore.json"),
                    Path.Combine(ledger.Root, "evidence/R10/ui-01-ui02-restore.json")
                );
            }
        }
        public static async Task Run(QaLedger ledger)
        {
            await QaAuraCases.Prepare(ledger);
            if (Slots)
            {
                var slotsResult = await QaAuraCases.Command(
                    "client-a",
                    "ui-slots",
                    new
                    {
                        carry = (string?)null
                    },
                    seconds: 75
                );
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R10/ui-06-current.json"),
                    JsonSerializer.SerializeToUtf8Bytes(slotsResult)
                );
                foreach (string name in new[] { "ui-06-first-free.png", "ui-06-last-price.png", "ui-06-all24.png" })
                {
                    string path = Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture", name);
                    ledger.AdoptExisting(path, "client-a");
                    QaFiles.Copy(ledger, path, Path.Combine(ledger.Root, "evidence/R10", name));
                }
                Console.WriteLine("QA_UI06_RESULT " + slotsResult["sample"]!.ToJsonString());
                return;
            }
            string prior = Path.Combine(ledger.Root, "evidence/R10/ui-01-attempt4-functional-layout.json");
            string? carry = File.Exists(prior)
                ? JsonNode.Parse(File.ReadAllText(prior))?["sample"]?["created"]?.GetValue<string>()
                : null;
            string musicFolder = Path.GetFullPath(Path.Combine(ledger.Root, "profiles", "client-a", "Музыка R10"));
            string wav = Path.Combine(musicFolder, "R10 local music.wav");
            if (!File.Exists(wav))
            {
                using var memory = new MemoryStream();
                using (var writer = new BinaryWriter(memory, Encoding.ASCII, true))
                {
                    int samples = 24000;
                    writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                    writer.Write(36 + samples * 2);
                    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16);
                    writer.Write((short)1);
                    writer.Write((short)1);
                    writer.Write(8000);
                    writer.Write(16000);
                    writer.Write((short)2);
                    writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data"));
                    writer.Write(samples * 2);
                    for (int i = 0; i < samples; i++)
                        writer.Write((short)(Math.Sin(i * Math.PI * 2 * 440 / 8000) * 4000));
                }
                QaFiles.Write(ledger, wav, memory.ToArray());
            }
            bool foldersOnly = File.Exists(Path.Combine(ledger.Root, "evidence/R10/ui-01-layout-ui02-current.json"));
            var result = await QaAuraCases.Command(
                "client-a",
                "ui-recipe",
                new
                {
                    carry,
                    tail = true,
                    foldersOnly,
                    musicFolder
                },
                seconds: 90
            );
            QaFiles.Write(
                ledger,
                Path.Combine(
                    ledger.Root,
                    foldersOnly
                        ? "evidence/R10/ui-03-current.json"
                        : "evidence/R10/ui-01-current.json"
                ),
                JsonSerializer.SerializeToUtf8Bytes(result)
            );
            string? capture = result["sample"]?["png"]?.GetValue<string>();
            if (capture != null)
            {
                ledger.AdoptExisting(capture, "client-a");
                QaFiles.Copy(
                    ledger,
                    capture,
                    Path.Combine(
                        ledger.Root,
                        foldersOnly
                            ? "evidence/R10/ui-03-folders.png"
                            : "evidence/R10/ui-01-recipe.png"
                    )
                );
            }
            foreach (var frame in result["sample"]?["routeTail"]?.AsArray() ?? new JsonArray())
            {
                string? file = frame?["file"]?.GetValue<string>();
                if (file == null)
                    continue;
                ledger.AdoptExisting(file, "client-a");
                QaFiles.Copy(ledger, file, Path.Combine(ledger.Root, "evidence/R10", Path.GetFileName(file)));
            }
            Console.WriteLine("QA_UI01_CAPTURE " + result["sample"]!.ToJsonString());
        }
    }
}
