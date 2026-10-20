using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaLocalFinish
    {
        public static bool Usability;
        public static string? SelectedAction;
        internal static bool RequiresVisualReview;
        internal static System.Diagnostics.Stopwatch? UpgradeClock;
        public static async Task Run(QaLedger ledger)
        {
            if (SelectedAction == "local-manual-capture")
            {
                QaAuraCases.UseLedger(ledger);
                var result = await QaAuraCases.Command("client-a", "manual-capture", seconds: 90);
                string report = Path.Combine(ledger.Root, "evidence/R10/manual-capture.json");
                if (File.Exists(report))
                    QaHistory.Copy(ledger, report, Path.Combine(QaHistory.Root(ledger),
                        "manual-capture-" + QaFiles.Hash(report).Substring(0, 12) + ".json"));
                QaFiles.Write(ledger, report, JsonSerializer.SerializeToUtf8Bytes(result));
                if (result["capture"]?["captured"]?.GetValue<bool>() != true
                    || result["capture"]?["temporaryStateRestored"]?.GetValue<bool>() != true)
                    throw new IOException("Manual capture did not finish or restore its temporary state");
                foreach (var frame in result["capture"]!["frames"]!.AsArray())
                {
                    string source = frame!["file"]!.GetValue<string>();
                    string destination = Path.Combine(ledger.Root, "evidence/R10", Path.GetFileName(source));
                    if (File.Exists(destination))
                        QaHistory.Copy(ledger, destination, Path.Combine(QaHistory.Root(ledger),
                            Path.GetFileNameWithoutExtension(destination) + "-" + QaFiles.Hash(destination).Substring(0, 12) + ".png"));
                    QaFiles.Copy(ledger, source, destination);
                }
                Console.WriteLine("QA_MANUAL_UI_CAPTURED 20 real UI frames; documentation states require visual review");
                return;
            }
            if (SelectedAction != null && SelectedAction.StartsWith("local-case-", StringComparison.Ordinal))
            {
                QaAuraCases.UseLedger(ledger);
                foreach (string id in SelectedAction.Substring("local-case-".Length).Split('-'))
                {
                    var result = await QaAuraCases.Command(
                        "client-a",
                        "catalog-case",
                        new
                        {
                            caseId = id
                        },
                        seconds: id == "UserFixes" || id == "C09manual"
                            || id.StartsWith("K03", StringComparison.Ordinal)
                            || id.StartsWith("L01", StringComparison.Ordinal)
                            || id.StartsWith("L08", StringComparison.Ordinal)
                            ? 65
                            : 35
                    );
                    string path = Path.Combine(ledger.Root, "evidence/R10/case-" + id + ".json");
                    if (File.Exists(path))
                        QaHistory.Copy(
                            ledger,
                            path,
                            Path.Combine(
                                QaHistory.Root(ledger),
                                "case-" + id + "-" + QaFiles.Hash(path).Substring(0, 12) + ".json"
                            )
                        );
                    QaFiles.Write(ledger, path, JsonSerializer.SerializeToUtf8Bytes(result));
                    if (result["capture"]?["blocked"] is System.Text.Json.Nodes.JsonNode blocked)
                        throw new IOException("BLOCKED " + id + ": " + blocked.ToString());
                    if (result["capture"]?["visualReviewRequired"]?.GetValue<bool>() == true)
                        RequiresVisualReview = true;
                    else if (result["capture"]?["passed"]?.GetValue<bool>() != true)
                        throw new IOException("Case did not establish success: " + id);
                    if (id != "H08save" && id != "K03save" && id != "K09save" && id != "K09world" && id != "L08save")
                        await QaAuraCases.Command("client-a", "clear", seconds: 3);
                    Console.WriteLine((result["capture"]?["passed"]?.GetValue<bool>() == true
                        ? "QA_CASE_PASS "
                        : "QA_CASE_CAPTURED_REQUIRES_REVIEW ") + id);
                }
                return;
            }
            if (SelectedAction == "local-playlist-check")
            {
                QaAuraCases.UseLedger(ledger);
                var result = await QaAuraCases.Command("client-a", "playlist-validation", seconds: 20);
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R10/playlist-validation.json"),
                    JsonSerializer.SerializeToUtf8Bytes(result)
                );
                if (result["capture"]?["passed"]?.GetValue<bool>() != true)
                    throw new IOException("Playlist validation did not establish success");
                await QaAuraCases.Command("client-a", "clear", seconds: 3);
                Console.WriteLine("QA_PLAYLIST_VALIDATION_PASS " + result["capture"]?["seconds"]);
                return;
            }
            if (SelectedAction == "local-upgrade-check")
            {
                QaAuraCases.UseLedger(ledger);
                var result = await QaAuraCases.Command("client-a", "upgrade-cycle", seconds: 15);
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R10/upgrade-native.json"),
                    JsonSerializer.SerializeToUtf8Bytes(result)
                );
                if (result["capture"]?["passed"]?.GetValue<bool>() != true)
                    throw new IOException("Upgrade cycle did not establish success");
                await QaAuraCases.Command("client-a", "clear", seconds: 2);
                Console.WriteLine("QA_UPGRADE_NATIVE_CASE_PASS " + result["capture"]?["seconds"]);
                return;
            }
            if (SelectedAction == "local-menu-live")
            {
                await QaLiveMenu.Run(ledger);
                return;
            }
            if (SelectedAction != null)
            {
                await QaAuraCases.PrepareLocal(ledger);
                await QaAuraCases.Command(
                    "client-a",
                    "audio-setup",
                    new
                    {
                        fixture = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav")
                    }
                );
                try
                {
                    var result = await QaAuraCases.Command("client-a", "menu-latency", seconds: 30);
                    QaFiles.Write(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R10", SelectedAction + ".json"),
                        JsonSerializer.SerializeToUtf8Bytes(result)
                    );
                    if (result["capture"]?["passed"]?.GetValue<bool>() != true)
                        throw new IOException("Menu timing capture did not complete its functional checks");
                    foreach (var frame in result["capture"]!["frames"]!.AsArray())
                    {
                        string path = frame!["file"]!.GetValue<string>();
                        QaFiles.Copy(
                            ledger,
                            path,
                            Path.Combine(ledger.Root, "evidence/R10", SelectedAction + "-" + Path.GetFileName(path))
                        );
                    }
                    Console.WriteLine("QA_MENU_LATENCY_CAPTURED " + result["capture"]!["coreSeconds"]);
                }
                finally
                {
                    await QaAuraCases.Command("client-a", "audio-clear");
                }
                return;
            }
            if (Usability)
            {
                await QaAuraCases.PrepareLocal(ledger);
                await QaAuraCases.Command(
                    "client-a",
                    "audio-setup",
                    new
                    {
                        fixture = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav")
                    }
                );
                try
                {
                    if (!File.Exists(Path.Combine(ledger.Root, "evidence/R12/local-finish/usability-mount.json")))
                    {
                        var mount = await QaAuraCases.Command("client-a", "mount-capture", seconds: 30);
                        QaFiles.Write(
                            ledger,
                            Path.Combine(ledger.Root, "evidence/R12/local-finish/usability-mount.json"),
                            JsonSerializer.SerializeToUtf8Bytes(mount)
                        );
                        foreach (var frame in mount["capture"]!["frames"]!.AsArray())
                        {
                            string name = frame!["file"]!.GetValue<string>();
                            QaFiles.Copy(
                                ledger,
                                Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/support", name),
                                Path.Combine(ledger.Root, "evidence/R12/local-finish", name)
                            );
                        }
                    }
                    var result = await QaAuraCases.Command("client-a", "usability-capture", seconds: 30);
                    QaFiles.Write(
                        ledger,
                        Path.Combine(ledger.Root, "evidence/R12/local-finish/usability.json"),
                        JsonSerializer.SerializeToUtf8Bytes(result)
                    );
                    RequiresVisualReview = true;
                    foreach (var frame in result["capture"]!["frames"]!.AsArray())
                    {
                        string path = frame!["file"]!.GetValue<string>();
                        QaFiles.Copy(
                            ledger,
                            path,
                            Path.Combine(ledger.Root, "evidence/R12/local-finish", Path.GetFileName(path))
                        );
                    }
                    Console.WriteLine("QA_USABILITY_CAPTURED " + result["capture"]!["coreSeconds"]);
                }
                finally
                {
                    await QaAuraCases.Command("client-a", "audio-clear");
                }
                return;
            }
            string current = Path.Combine(ledger.Root, "evidence/R12/local-finish/mount.json");
            if (File.Exists(current))
            {
                var accepted = System.Text.Json.Nodes.JsonNode.Parse(QaFiles.ReadSharedText(current));
                if (accepted?["sha"]?.GetValue<string>()
                    == QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll"))
                    && accepted?["capture"]?["passed"]?.GetValue<bool>() == true
                    && accepted?["capture"]?["measurement"]?.GetValue<string>() == "fixed-shoulder-equipment-offset"
                    && accepted?["capture"]?["carriedScale"]?.GetValue<double>() > .79)
                {
                    Console.WriteLine("QA_MOUNT_REUSED same candidate, 12-second four-state evidence");
                    return;
                }
            }
            string prior = Path.Combine(ledger.Root, "evidence/R12/local-finish/mount.json");
            if (File.Exists(prior))
            {
                string archive = Path.Combine(
                    ledger.Root,
                    "evidence/R12/local-finish/prior-" + QaFiles.Hash(prior).Substring(0, 8) + ".json"
                );
                if (!File.Exists(archive))
                    QaFiles.Copy(ledger, prior, archive);
            }
            await QaAuraCases.PrepareLocal(ledger);
            await QaAuraCases.Command(
                "client-a",
                "audio-setup",
                new
                {
                    fixture = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav")
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
            try
            {
                var result = await QaAuraCases.Command("client-a", "mount-capture", seconds: 30);
                QaFiles.Write(
                    ledger,
                    Path.Combine(ledger.Root, "evidence/R12/local-finish/mount.json"),
                    JsonSerializer.SerializeToUtf8Bytes(result)
                );
                foreach (var frame in result["capture"]!["frames"]!.AsArray())
                {
                    string name = frame!["file"]!.GetValue<string>();
                    QaFiles.Copy(
                        ledger,
                        Path.Combine(ledger.Root, "profiles/client-a/BepInEx/config/capture/support", name),
                        Path.Combine(ledger.Root, "evidence/R12/local-finish", name)
                    );
                }
                if (result["capture"]!["passed"]?.GetValue<bool>() != true)
                    throw new IOException(
                        "Mount body/torso check failed; saved all case measurements and full-body views"
                    );
                Console.WriteLine(
                    "QA_MOUNT_FOUR_STATES_NUMERIC_PASS coreSeconds=" + result["capture"]!["coreSeconds"]
                        + "; full-body visual review required"
                );
            }
            finally
            {
                await QaAuraCases.Command("client-a", "audio-clear");
            }
        }
    }
}
