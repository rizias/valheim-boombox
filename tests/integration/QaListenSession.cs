using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaListenSession
    {
        private static QaLedger ledger = null!;
        private static string[] Roles = { "client-a", "client-b" };
        private static readonly List<object> cases = new();
        public static async Task Run(QaLedger owner, bool localFinish = false)
        {
            var cycleClock = Stopwatch.StartNew();
            QaLocalFinish.RequiresVisualReview = false;
            Roles = localFinish ? new[] { "client-a" } : new[] { "client-a", "client-b" };
            ledger = owner;
            cases.Clear();
            if (Process.GetProcessesByName("valheim").Length != 0
                || Process.GetProcessesByName("valheim_server").Length != 0)
                throw new IOException("Listen QA requires quiescent game jobs");
            using var pipe = new QaPipe(ledger);
            var registry = new QaRegistry(ledger);
            var running = new Dictionary<string, QaSupervised>();
            string before = QaRegistryData.Hash(QaRegistryData.Capture());
            Exception? failure = null;
            bool strictUpgrade = QaLocalFinish.SelectedAction == "local-upgrade-check";
            bool playlistCheck = QaLocalFinish.SelectedAction == "local-playlist-check";
            bool caseCheck = QaLocalFinish.SelectedAction?.StartsWith("local-case-", StringComparison.Ordinal) == true;
            bool migrationMp = QaLocalFinish.SelectedAction == "migration-mp";
            bool manualCapture = QaLocalFinish.SelectedAction == "local-manual-capture";
            bool boundedCycle = strictUpgrade || playlistCheck || caseCheck || migrationMp || manualCapture;
            int deadlineHit = 0;
            double? readySeconds = null, workSeconds = null;
            int nativePid = 0;
            if (boundedCycle)
                QaLocalFinish.UpgradeClock = null;
            using var deadline = new System.Threading.CancellationTokenSource();
            var watchdog = boundedCycle
                ? Task.Run(async () =>
                {
                    try
                    {
                        while (QaLocalFinish.UpgradeClock == null)
                        {
                            await Task.Delay(100, deadline.Token);
                            if (cycleClock.Elapsed.TotalSeconds > 180)
                                break;
                        }
                        if (QaLocalFinish.UpgradeClock != null)
                            await Task.Delay(
                            TimeSpan.FromSeconds(migrationMp
                                    ? 110
                                    : 118),
                            deadline.Token
                        );
                        System.Threading.Interlocked.Exchange(ref deadlineHit, 1);
                        foreach (var job in running.Values.ToArray())
                            job.Job.Stop();
                    }
                    catch (OperationCanceledException) { }
                })
                : Task.CompletedTask;
            try
            {
                if (ledger.State.External.Count != 0)
                    throw new IOException("Server loader state not empty before listen variant");
                string previous = Path.Combine(
                    ledger.Root,
                    localFinish
                        ? "evidence/R12/local-finish/cases.json"
                        : "evidence/R09/listen-host/cases.json"
                );
                if (File.Exists(previous))
                    QaFiles.Copy(
                        ledger,
                        previous,
                        Path.Combine(ledger.Root, "evidence/R09/listen-host/previous-cases.json")
                    );
                QaProfiles.Prepare(ledger, localFinish ? "local-finish" : "listen");
                foreach (string role in Roles)
                {
                    if (boundedCycle && cycleClock.Elapsed.TotalSeconds >= 180)
                        throw new TimeoutException("QA startup budget exhausted");
                    string profile = Path.Combine(ledger.Root, "profiles", role);
                    ledger.ReserveFile(Path.Combine(profile, "BepInEx/config/BepInEx.cfg"), 64 * 1024);
                    foreach (string name in new[] { "qa-status.json", "qa-command.txt" })
                    {
                        string file = Path.Combine(profile, "BepInEx/config", name);
                        if (File.Exists(file))
                            ledger.RemoveOwned(file);
                    }
                    string game = QaEnvironment.ClientExe;
                    var args = new[] {
                        "-batchmode",
                        "-screen-fullscreen",
                        "0",
                        "-screen-width",
                        "640",
                        "-screen-height",
                        "480",
                        "-logFile",
                        "-",
                        "-savedir",
                        Path.Combine(profile, "saves"),
                        "--doorstop-enabled",
                        "true",
                        "--doorstop-target-assembly",
                        Path.Combine(profile, "BepInEx/core/BepInEx.Preloader.dll")
                    };
                    if (QaLocalFinish.SelectedAction != null && !migrationMp)
                        args = args.Where(a => a != "-batchmode").Select(a => a == "640"
                            ? "2560"
                            : a == "480"
                                ? "1440"
                                : a).ToArray();
                    if (strictUpgrade || playlistCheck || (caseCheck && QaLocalFinish.SelectedAction != "local-case-C16"))
                        args = args.Select(a => a == "2560"
                            ? "1280"
                            : a == "1440"
                                ? "720"
                                : a).ToArray();
                    if (manualCapture)
                        args = args.Select(a => a == "2560" ? "1440" : a == "1440" ? "900" : a).ToArray();
                    running[role] = new QaSupervised(
                        ledger,
                        role,
                        game,
                        Path.GetDirectoryName(game)!,
                        args,
                        maximumLogBytes: boundedCycle ? 512 * 1024 : 12 * QaLedger.MiB,
                        pipe: pipe.Name,
                        listenHost: role == "client-a",
                        listenSession: true,
                        bufferedLogs: boundedCycle,
                        fastUi: boundedCycle
                    );
                    if (role == "client-a" && !migrationMp)
                    {
                        await Until(
                            () => Read("client-a"),
                            s => s["isServer"]?.GetValue<bool>() == true && s["localPlayer"]?.GetValue<long>() != 0
                            && s["hud"]?.GetValue<bool>() == true
                            && (QaLocalFinish.SelectedAction == null || s["actorReady"]?.GetValue<bool>() == true)
                            && s["worldUID"]?.GetValue<long>() != 0
                            && (!manualCapture || (s["quiet"]?["audioSessionMuted"]?.GetValue<bool>() == true
                                && s["quiet"]?["windowOffscreen"]?.GetValue<bool>() == true))
                            && QaListeners.OwnedLoopback(s["pid"]!.GetValue<int>(), 24870),
                            boundedCycle
                                ? Math.Max(1, 180 - (int)cycleClock.Elapsed.TotalSeconds)
                                : QaLocalFinish.SelectedAction == null
                                    ? 300
                                    : QaLocalFinish.SelectedAction == "local-menu-live"
                                        ? 180
                                        : 30,
                            running
                        );
                        Console.WriteLine(
                            "QA_LISTEN_HOST_READY actual local Player/HUD and owned native loopback listener"
                        );
                        readySeconds = cycleClock.Elapsed.TotalSeconds;
                        if (boundedCycle)
                            QaLocalFinish.UpgradeClock = Stopwatch.StartNew();
                        nativePid = Read("client-a")?["pid"]?.GetValue<int>() ?? 0;
                    }
                }
                if (migrationMp)
                {
                    await Until(
                        () => Read("client-a"),
                        s => s["actorReady"]?.GetValue<bool>() == true && s["worldUID"]?.GetValue<long>() != 0
                        && s["quiet"]?["audioSessionMuted"]?.GetValue<bool>() == true,
                        Math.Max(1, 180 - (int)cycleClock.Elapsed.TotalSeconds),
                        running
                    );
                    readySeconds = cycleClock.Elapsed.TotalSeconds;
                    QaLocalFinish.UpgradeClock = Stopwatch.StartNew();
                    nativePid = Read("client-a")!["pid"]!.GetValue<int>();
                    Console.WriteLine("QA_LISTEN_HOST_READY parallel startup, quiet host");
                }
                if (localFinish)
                {
                    await QaLocalFinish.Run(ledger);
                    workSeconds = cycleClock.Elapsed.TotalSeconds;
                    return;
                }
                var connected = await Until(() => Read("client-a"), s => Connected(running), 240, running);
                var b = Read("client-b")!;
                Add(
                    "NET-BASE-04-real-listen-A-plus-client-B",
                    true,
                    new
                    {
                        a = connected,
                        b,
                        ownedLoopback = true,
                        dedicatedProcesses = Process.GetProcessesByName("valheim_server").Length
                    }
                );
                string proof = Guid.NewGuid().ToString("N");
                Write("client-a", "proof:" + proof);
                await Until(
                    () => Read("client-b"),
                    s => s["players"]!.AsArray().Any(
                        p => p!["proof"]?.GetValue<string>() == proof
                            && p["id"]!.GetValue<long>() == connected["localPlayer"]!.GetValue<long>()
                    ),
                    20,
                    running
                );
                Add(
                    "NET-BASE-04-native-ZDO-proof",
                    true,
                    new
                    {
                        proof,
                        observer = Read("client-b")
                    }
                );
                await Move("client-a", "client-b", 4, running);
                await Move("client-b", "client-a", -4, running);
                foreach (string role in Roles)
                    await Command(role, "listen-panel");
                await Task.Delay(1500);
                var aPanel = await Command("client-a", "listen-snapshot");
                var bPanel = await Command("client-b", "listen-snapshot");
                bool panel = aPanel["listen"]!["canEdit"]!.GetValue<bool>()
                    && !bPanel["listen"]!["canEdit"]!.GetValue<bool>()
                    && bPanel["listen"]!["balanceReceived"]!.GetValue<bool>()
                    && aPanel["listen"]!["inputs"]!.AsArray().Count == 6
                    && bPanel["listen"]!["inputs"]!.AsArray().Count == 6
                    && aPanel["listen"]!["inputs"]!.AsArray().All(i => i!["interactable"]!.GetValue<bool>())
                    && bPanel["listen"]!["inputs"]!.AsArray().All(i => !i!["interactable"]!.GetValue<bool>())
                    && aPanel["listen"]!["radius"]!.GetValue<float>() == bPanel["listen"]!["radius"]!.GetValue<float>();
                Add(
                    "R03-listen-panel-permission-readonly-preparation",
                    panel,
                    new
                    {
                        aPanel,
                        bPanel,
                        scope = "Real PartyPanel presentation and permission/input state read; no user input/config mutation acceptance"
                    }
                );
                foreach (string role in Roles)
                    await Command(role, "listen-panel-close");
                if (migrationMp)
                {
                    await QaMigrationMp.Run(ledger);
                    workSeconds = cycleClock.Elapsed.TotalSeconds;
                }
                Save();
                Console.WriteLine("QA_LISTEN_HOST_PASS native A+B and two-way action observation");
            }
            catch (Exception error)
            {
                failure = error;
                if (!localFinish)
                {
                    cases.Add(new
                    {
                        id = "listen-cycle",
                        passed = false,
                        error = error.ToString(),
                        a = Read("client-a"),
                        b = Read("client-b")
                    });
                    Save();
                }
                throw;
            }
            finally
            {
                var errors = new List<Exception>();
                bool ownedProcessesStopped = true;
                try
                {
                    if (boundedCycle && nativePid == 0)
                        nativePid = Read("client-a")?["pid"]?.GetValue<int>() ?? 0;
                    foreach (string role in Roles.Reverse())
                    {
                        if (!running.TryGetValue(role, out var process) || process.Wrapper.HasExited)
                            continue;
                        try
                        {
                            Write(role, "shutdown");
                            await process.WaitAsync(
                                TimeSpan.FromSeconds(
                                    caseCheck
                                        ? Math.Max(
                                        1,
                                        Math.Min(30, 110 - (QaLocalFinish.UpgradeClock?.Elapsed.TotalSeconds ?? 0))
                                    )
                                        : boundedCycle
                                            ? 5
                                            : 30
                                )
                            );
                        }
                        catch (Exception error)
                        {
                            Console.Error.WriteLine("Listen bounded graceful stop: " + error.Message);
                        }
                    }
                    foreach (var process in running.Values)
                    {
                        try
                        {
                            process.Job.Stop();
                        }
                        catch (Exception error)
                        {
                            errors.Add(error);
                        }
                    }
                    foreach (var process in running.Values)
                    {
                        try
                        {
                            await process.WaitAsync(TimeSpan.FromSeconds(boundedCycle ? 1 : 20));
                        }
                        catch (Exception error)
                        {
                            errors.Add(error);
                        }
                        try
                        {
                            process.Job.WaitEmpty();
                        }
                        catch (Exception error)
                        {
                            ownedProcessesStopped = false;
                            errors.Add(error);
                        }
                        try
                        {
                            process.Dispose();
                        }
                        catch (Exception error)
                        {
                            errors.Add(error);
                        }
                    }
                    if (boundedCycle && nativePid != 0)
                    {
                        var exitDeadline = DateTime.UtcNow.AddSeconds(caseCheck
                            ? Math.Max(0, Math.Min(15, 115 - (QaLocalFinish.UpgradeClock?.Elapsed.TotalSeconds ?? 0)))
                            : 2);
                        while (DateTime.UtcNow < exitDeadline
                        && Process.GetProcessesByName("valheim").Any(p =>
                        {
                            using (p)
                                return p.Id == nativePid;
                        }))
                            await Task.Delay(50);
                    }
                    try
                    {
                        deadline.Cancel();
                        await watchdog;
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }
                catch (Exception error)
                {
                    ownedProcessesStopped = false;
                    errors.Add(error);
                }
                try
                {
                    pipe.Dispose();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
                try
                {
                    registry.Restore();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
                try
                {
                    foreach (string role in Roles)
                    {
                        string cache = ledger.Resolve(
                            Path.Combine(ledger.Root, "profiles", role, "BepInEx/cache/harmony_interop_cache.dat")
                        );
                        if (ledger.State.Reservations.ContainsKey(cache))
                            ledger.CommitFile(cache, role);
                        string loaderConfig = ledger.Resolve(
                            Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/BepInEx.cfg")
                        );
                        if (ledger.State.Reservations.ContainsKey(loaderConfig))
                            ledger.CommitFile(loaderConfig, role);
                        ledger.Reconcile(Path.Combine(ledger.Root, "profiles", role));
                    }
                    string after = QaRegistryData.Hash(QaRegistryData.Capture());
                    QaFiles.Write(
                        ledger,
                        Path.Combine(
                            ledger.Root,
                            localFinish
                                ? "evidence/R12/local-finish/restore.json"
                                : "evidence/R09/listen-host/restore.json"
                        ),
                        JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            preferencesBefore = before,
                            preferencesAfter = after,
                            sharedPreferencesRestored = before == after,
                            ownedProcessesStopped,
                            serverLoaderUntouched = ledger.State.External.Count == 0,
                            roles = Roles
                        })
                    );
                    if (before != after || ledger.State.External.Count != 0)
                        throw new IOException("Listen restore/source loader mismatch");
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
                if (boundedCycle)
                {
                    foreach (string checkedRole in Roles)
                    {
                        string logPath = Path.Combine(ledger.Root, "current", checkedRole, "stdout.log");
                        if (File.Exists(logPath))
                        {
                            string log = QaFiles.ReadSharedText(logPath);
                            if (log.Contains("PlayerProfile.HaveLogoutPoint", StringComparison.Ordinal))
                                errors.Add(
                                    new IOException("QA requested respawn before world initialization: " + checkedRole)
                                );
                            int shutdown = log.LastIndexOf("ZNet OnDestroy", StringComparison.Ordinal);
                            string tail = shutdown >= 0 ? log.Substring(shutdown) : log;
                            if (tail.Contains("Exception:", StringComparison.Ordinal)
                                && tail.Contains("VikingBoombox.", StringComparison.Ordinal))
                                errors.Add(
                                    new IOException(
                                        "Product exception during shutdown; inspect preserved client-a stdout.log"
                                    )
                                );
                        }
                    }
                    double seconds = cycleClock.Elapsed.TotalSeconds;
                    double? fromReadySeconds = QaLocalFinish.UpgradeClock?.Elapsed.TotalSeconds;
                    bool completed = failure == null
                        && errors.Count == 0
                        && workSeconds.HasValue
                        && fromReadySeconds.HasValue
                        && fromReadySeconds.Value <= 120;
                    bool requiresVisualReview = QaLocalFinish.RequiresVisualReview;
                    bool passed = completed
                        && !requiresVisualReview
                        && failure == null
                        && errors.Count == 0
                        && deadlineHit == 0;
                    QaFiles.Write(
                        ledger,
                        Path.Combine(
                            ledger.Root,
                            caseCheck || manualCapture
                                ? "evidence/R10/" + QaLocalFinish.SelectedAction + "-total.json"
                                : migrationMp
                                    ? "evidence/R09/migration-mp-total.json"
                                    : playlistCheck
                                        ? "evidence/R10/playlist-validation-total.json"
                                        : "evidence/R10/upgrade-native-total.json"
                        ),
                        JsonSerializer.SerializeToUtf8Bytes(
                            new
                            {
                                passed,
                                completed,
                                requiresVisualReview,
                                seconds,
                                readySeconds,
                                workSeconds,
                                fromReadySeconds,
                                limitFromReadySeconds = 120,
                                deadlineHit = deadlineHit != 0,
                                includes = "cold launch reported separately; timed check starts when world and actor are ready and includes shutdown/restoration",
                                failure = failure?.Message,
                                cleanupErrors = errors.Select(e => e.Message).ToArray()
                            }
                        )
                    );
                    Console.WriteLine((migrationMp
                        ? "QA_MIGRATION_MP_TOTAL "
                        : "QA_UPGRADE_TOTAL ") + seconds.ToString("0.000") + "s passed=" + passed);
                    if (!passed && failure == null && errors.Count == 0)
                        throw new IOException(requiresVisualReview
                            ? "Capture requires visual review; no passing verdict"
                            : "Bounded QA cycle did not complete within its deadline");
                }
                if (errors.Count != 0)
                {
                    ledger.RecoveryRequired();
                    if (failure != null)
                        errors.Insert(0, failure);
                    throw new AggregateException("Listen cleanup incomplete", errors);
                }
            }
        }
        private static bool Connected(Dictionary<string, QaSupervised> running)
        {
            var a = Read("client-a");
            var b = Read("client-b");
            if (a == null || b == null)
                return false;
            if (a["isServer"]?.GetValue<bool>() != true || b["isServer"]?.GetValue<bool>() != false
            || a["localPlayer"]?.GetValue<long>() == 0
            || b["localPlayer"]?.GetValue<long>() == 0
            || a["localPlayer"]!.GetValue<long>() == b["localPlayer"]!.GetValue<long>()
            || a["worldUID"]!.GetValue<long>() != b["worldUID"]!.GetValue<long>()
            || b["connection"]?.GetValue<string>() != "Connected")
                return false;
            foreach (var entry in new[] { (role: "client-a", state: a), (role: "client-b", state: b) })
            {
                if (QaLocalFinish.SelectedAction == "migration-mp"
                && (entry.state["quiet"]?["audioSessionMuted"]?.GetValue<bool>() != true
                || entry.state["quiet"]?["windowOffscreen"]?.GetValue<bool>() != true))
                    return false;
                using var process = Process.GetProcessById(entry.state["pid"]!.GetValue<int>());
                if (!running[entry.role].Job.Contains(process)
                    || process.MainModule!.FileName != entry.state["executable"]!.GetValue<string>()
                    || process.StartTime.ToUniversalTime()
                    != DateTime.Parse(entry.state["startUTC"]!.GetValue<string>()).ToUniversalTime())
                    return false;
                var peers = entry.state["peers"]!.AsArray();
                if (peers.Count != 1
                    || peers[0]!["ready"]?.GetValue<bool>() != true
                    || peers[0]!["uid"]!.GetValue<long>() == 0)
                    return false;
                var players = entry.state["players"]!.AsArray();
                if (players.Count < 2 || players.Select(p => p!["id"]!.GetValue<long>()).Distinct().Count() < 2
                || players.Select(p => p!["character"]!.GetValue<string>()).Distinct().Count() < 2
                || players.Any(p => p!["character"]!.GetValue<string>() == "0:0"))
                    return false;
            }
            long uidA = a["nativeUID"]!.GetValue<long>(),
                uidB = b["nativeUID"]!.GetValue<long>(),
                idA = a["localPlayer"]!.GetValue<long>(),
                idB = b["localPlayer"]!.GetValue<long>();
            if (uidA == 0 || uidB == 0 || uidA == uidB || b["peers"]![0]!["uid"]!.GetValue<long>() != uidA
            || b["peers"]![0]!["server"]?.GetValue<bool>() != true
            || a["peers"]![0]!["uid"]!.GetValue<long>() != uidB)
                return false;
            var ownA = a["players"]!.AsArray().SingleOrDefault(p => p!["id"]!.GetValue<long>() == idA);
            var seenA = b["players"]!.AsArray().SingleOrDefault(p => p!["id"]!.GetValue<long>() == idA);
            var ownB = b["players"]!.AsArray().SingleOrDefault(p => p!["id"]!.GetValue<long>() == idB);
            var seenB = a["players"]!.AsArray().SingleOrDefault(p => p!["id"]!.GetValue<long>() == idB);
            if (ownA == null || seenA == null || ownB == null || seenB == null
            || ownA["character"]!.GetValue<string>() != seenA["character"]!.GetValue<string>()
            || ownB["character"]!.GetValue<string>() != seenB["character"]!.GetValue<string>()
            || ownA["character"]!.GetValue<string>() == ownB["character"]!.GetValue<string>()
            || a["peers"]![0]!["characterId"]!.GetValue<string>() != ownB["character"]!.GetValue<string>())
                return false;
            foreach (var native in new[] {
                (node: ownA, id: idA, uid: uidA),
                (node: seenA, id: idA, uid: uidA),
                (node: ownB, id: idB, uid: uidB),
                (node: seenB, id: idB, uid: uidB)
            })
                if (native.node["owner"]!.GetValue<long>() != native.uid
                    || native.node["zdoPlayerId"]!.GetValue<long>() != native.id)
                    return false;
            return QaListeners.OwnedLoopback(a["pid"]!.GetValue<int>(), 24870)
                && a["hud"]!.GetValue<bool>()
                && b["hud"]!.GetValue<bool>();
        }
        private static async Task Move(
            string actor,
            string observer,
            float offset,
            Dictionary<string, QaSupervised> running
        )
        {
            var before = await Until(
                () => Read(actor),
                s => s["players"]!.AsArray().Any(p => p!["id"]!.GetValue<long>() == s["localPlayer"]!.GetValue<long>()),
                10,
                running
            );
            long id = before["localPlayer"]!.GetValue<long>();
            var local = before["players"]!.AsArray().Single(p => p!["id"]!.GetValue<long>() == id)!;
            float x = local["x"]!.GetValue<float>() + offset, y = local["y"]!.GetValue<float>(), z = local["z"]!.GetValue<float>();
            await Task.Delay(QaLocalFinish.SelectedAction == "migration-mp" ? 250 : 3000);
            Write(
                actor,
                "teleport:" + x.ToString("R", CultureInfo.InvariantCulture) + ":"
                    + y.ToString("R", CultureInfo.InvariantCulture)
                    + ":"
                    + z.ToString("R", CultureInfo.InvariantCulture)
            );
            var after = await Until(
            () => Read(observer),
            s =>
                {
                    var own = Read(actor)?["players"]?.AsArray().SingleOrDefault(p => p?["id"]?.GetValue<long>() == id);
                    if (own == null || Math.Abs(own["x"]!.GetValue<float>() - x) > .3)
                        return false;
                    return s["players"]!.AsArray().Any(
                    p => p!["id"]!.GetValue<long>() == id
                            && new[] { "x", "y", "z" }.All(
                        axis => Math.Abs(p[axis]!.GetValue<float>() - own[axis]!.GetValue<float>()) <= .3
                    )
                );
                },
            30,
            running
            );
            Add(
                "NET-BASE-04-native-TeleportTo-" + actor,
                true,
                new
                {
                    actor,
                    observer,
                    id,
                    x,
                    y,
                    z,
                    before,
                    after,
                    actorAfter = Read(actor),
                    positionCriterion = "Requested X displacement reached; observer XYZ matches actual actor after native terrain/collision correction"
                }
            );
        }
        private static JsonNode? Read(string role, bool gameplay = false)
        {
            try
            {
                var status = JsonNode.Parse(
                    QaFiles.ReadSharedText(Path.Combine(
                            ledger.Root,
                            "profiles",
                            role,
                            "BepInEx/config",
                            gameplay
                            ? "qa-gameplay-status.json"
                            : "qa-status.json"
                        ))
                );
                return status?["utc"] != null
                    && DateTime.UtcNow - DateTime.Parse(status["utc"]!.GetValue<string>()).ToUniversalTime()
                    < TimeSpan.FromSeconds(3)
                    ? status
                    : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        private static async Task<JsonNode> Until(
            Func<JsonNode?> read,
            Func<JsonNode, bool> condition,
            int seconds,
            Dictionary<string, QaSupervised>? running = null
        )
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                if (running != null)
                    foreach (var process in running.Values)
                        if (process.Fault != null || process.Wrapper.HasExited)
                            throw new IOException("Listen native process ended before proof");
                var state = read();
                if (state != null && condition(state))
                    return state;
                await Task.Delay(500);
            }
            throw new TimeoutException("Native listen proof deadline");
        }
        private static void Write(
            string role,
            string text
        ) => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-command.txt"),
            Encoding.UTF8.GetBytes(text)
        );
        private static async Task<JsonNode> Command(string role, string operation)
        {
            string id = Guid.NewGuid().ToString("N");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-gameplay-command.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    id,
                    action = operation
                })
            );
            var state = await Until(
                () => Read(role, true),
                s => s["id"]?.GetValue<string>() == id && s["busy"]?.GetValue<bool>() == false,
                30
            );
            if (state["commandError"] != null || state["error"] != null)
                throw new IOException("Listen panel probe: " + (state["commandError"] ?? state["error"]));
            return state;
        }
        private static void Add(string id, bool passed, object observation)
        {
            cases.Add(new
            {
                id,
                passed,
                observation
            });
            Save();
            if (!passed)
                throw new IOException("Listen criterion failed: " + id);
        }
        private static void Save() => QaFiles.Write(
            ledger,
            Path.Combine(ledger.Root, "evidence/R09/listen-host/cases.json"),
            JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    candidateSha = QaFiles.Hash(Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll")),
                    cases,
                    scope = "Native open listen-host A + real B from same client install; existing profiles/custom TCP loopback. Steam/platform auth/crossplay/NAT/full CONFIG GUI not verified.",
                    utc = DateTime.UtcNow
                }
            )
        );
    }
}
