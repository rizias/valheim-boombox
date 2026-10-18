using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaController
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 1 && args[1] == "environment-tests")
                {
                    QaEnvironmentChecks.Run(args[0]);
                    return 0;
                }
                if (args.Length > 1 && args[1] == "environment-check")
                {
                    string core = QaEnvironment.Core, managed = QaEnvironment.Managed, qaRoot = QaEnvironment.QaRoot;
                    _ = QaEnvironment.SourceRoot(args[0]);
                    foreach (string file in new[] {
                        "BepInEx.dll",
                        "0Harmony.dll",
                        "Mono.Cecil.dll",
                        "BepInEx.Preloader.dll"
                    })
                        if (!File.Exists(Path.Combine(core, file)))
                            throw new IOException("Missing QA core: " + file);
                    if (!File.Exists(Path.Combine(managed, "assembly_valheim.dll")))
                        throw new IOException("Missing game Managed assembly");
                    Console.WriteLine(
                        JsonSerializer.Serialize(
                            new
                            {
                                client = QaEnvironment.ClientExe,
                                managed,
                                core,
                                qaRoot,
                                loaderPresent = File.Exists(Path.Combine(QaEnvironment.ClientRoot, "winhttp.dll")),
                                serverConfigured = !string.IsNullOrWhiteSpace(
                                Environment.GetEnvironmentVariable("VALHEIM_QA_SERVER")
                            ),
                                compiler = QaEnvironment.Compiler,
                                scope = "Read-only input validation; no native game launched"
                            }
                        )
                    );
                    return 0;
                }
                if (args.Length > 1 && args[1] == "child-launch")
                    return QaChildLaunch.Run(args);
                if (args.Length > 1 && args[1] == "audio-worker")
                    return QaAudioChecks.Worker(args[0]);
                if (args.Length > 2 && args[1] == "membrane-worker")
                    return QaMembraneChecks.Worker(args[0], args[2]);
                if (args.Length > 1 && args[1] == "membrane-literal")
                    return QaMembraneChecks.Literal(args[0]);
                if (args.Length > 1 && args[1].StartsWith("test-worker-", StringComparison.Ordinal))
                    return QaProcessChecks.Worker(args[1], args[0]);
                using var ledger = new QaLedger(args[0]);
                foreach (string name in new[] { "qa.dll", "compile.rsp", "qa.runtimeconfig.json" })
                {
                    string generated = Path.Combine(QaEnvironment.BinRoot, name);
                    if (File.Exists(generated))
                        ledger.AdoptExisting(
                            generated,
                            ledger.State.Files.TryGetValue(generated, out var owned)
                                ? owned.Owner
                                : "controller-build"
                        );
                }
                string action = args.Length > 1 ? args[1] : "status";
                if (action == "local-upgrade-check")
                {
                    QaLocalFinish.SelectedAction = action;
                    QaListenSession.Run(ledger, true).GetAwaiter().GetResult();
                    return 0;
                }
                if (action == "migration-mp")
                {
                    QaLocalFinish.SelectedAction = action;
                    QaListenSession.Run(ledger, false).GetAwaiter().GetResult();
                    return 0;
                }
                if (action == "local-playlist-check")
                {
                    QaLocalFinish.SelectedAction = action;
                    QaListenSession.Run(ledger, true).GetAwaiter().GetResult();
                    return 0;
                }
                if (action == "prepare-slot-integrations")
                {
                    QaUiIntegrations.Install(ledger, true);
                    return 0;
                }
                if (action == "local-reload-H08" || action == "local-reload-K03" || action == "local-reload-K09"
                || action == "local-reload-L01"
                || action == "local-reload-L07"
                || action == "local-reload-L08")
                {
                    string caseName = action.Substring("local-reload-".Length);
                    string? journeyAudio = null, journeyBackup = null;
                    if (caseName == "L08")
                    {
                        journeyAudio = Path.Combine(ledger.Root, "profiles/client-a/Музыка R10/R10 local music.wav");
                        journeyBackup = Path.Combine(
                            QaHistory.Root(ledger),
                            "L08-original-audio-" + QaFiles.Hash(journeyAudio) + ".wav"
                        );
                        QaHistory.Copy(ledger, journeyAudio, journeyBackup);
                        QaFiles.Copy(
                            ledger,
                            Path.Combine(ledger.Root, "current/r12-video/demo-source.wav"),
                            journeyAudio
                        );
                    }
                    try
                    {
                        foreach (string phase in caseName == "K09"
                            ? new[] { "save", "world", "inventory" }
                            : new[] { "save", "load" })
                        {
                            QaLocalFinish.SelectedAction = "local-case-" + (caseName == "L07" && phase == "save"
                                ? "L05-"
                                : "") + caseName + phase;
                            QaListenSession.Run(ledger, true).GetAwaiter().GetResult();
                            string history = Path.Combine(
                                QaHistory.Root(ledger),
                                caseName + "-" + phase + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                            );
                            foreach (string relative in new[] {
                                "current/client-a/stdout.log",
                                "evidence/R10/case-" + caseName + phase + ".json",
                                "evidence/R10/" + QaLocalFinish.SelectedAction + "-total.json",
                                "evidence/R12/local-finish/restore.json",
                                "profiles/client-a/BepInEx/config/qa-" + caseName + "-checkpoint.json"
                            })
                            {
                                string source = Path.Combine(ledger.Root, relative);
                                if (File.Exists(source))
                                    QaHistory.Copy(ledger, source, Path.Combine(history, Path.GetFileName(source)));
                            }
                        }
                    }
                    finally
                    {
                        if (journeyAudio != null && journeyBackup != null)
                        {
                            QaFiles.Copy(ledger, journeyBackup, journeyAudio);
                            if (QaFiles.Hash(journeyBackup) != QaFiles.Hash(journeyAudio))
                                throw new IOException("L08 original audio restore mismatch");
                        }
                    }
                    return 0;
                }
                if (action.StartsWith("local-case-", StringComparison.Ordinal)
                && action.Substring("local-case-".Length).Split('-').All(id => new[] {
                    "C05",
                    "C06",
                    "C09",
                    "C09manual",
                    "C13",
                    "C16",
                    "D06",
                    "E08",
                    "F10",
                    "G06",
                    "G07",
                    "G08",
                    "H07",
                    "H08save",
                    "H08load",
                    "K01",
                    "K02",
                    "K03save",
                    "K03load",
                    "K07",
                    "K08",
                    "K10",
                    "L03",
                    "L05"
                    }.Contains(id)))
                {
                    QaLocalFinish.SelectedAction = action;
                    QaListenSession.Run(ledger, true).GetAwaiter().GetResult();
                    return 0;
                }
                if (action == "build-upgrade-probe")
                {
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    return 0;
                }
                if (action == "prepare-visibility-profile")
                {
                    QaVisibilitySupport.Prepare(ledger);
                    return 0;
                }
                if (action == "clear-visibility-profile")
                {
                    QaVisibilitySupport.Clear(ledger);
                    return 0;
                }
                if (action == "storage-checks")
                    QaStorageChecks.Run(ledger);
                else if (action == "registry-checks")
                    QaRegistryChecks.Run(ledger);
                else if (action == "process-checks")
                    QaProcessChecks.Run(ledger).GetAwaiter().GetResult();
                else if (action == "build-candidate")
                    QaBuildCandidate.Run(ledger).GetAwaiter().GetResult();
                else if (action == "metadata-checks")
                    QaMetadataChecks.Run(ledger);
                else if (action == "crack-origin-checks")
                    QaCrackOriginChecks.Run(ledger);
                else if (action == "local-finish")
                {
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaListenSession.Run(ledger, true).GetAwaiter().GetResult();
                }
                else if (action == "local-usability")
                {
                    QaLocalFinish.Usability = true;
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaListenSession.Run(ledger, true).GetAwaiter().GetResult();
                }
                else if (action == "local-menu-baseline" || action == "local-menu-fixed" || action == "local-menu-live"
                    || action == "local-manual-capture")
                {
                    QaLocalFinish.SelectedAction = action;
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    if (action == "local-manual-capture")
                        QaVideoDemo.PrepareSource(ledger);
                    QaListenSession.Run(ledger, true).GetAwaiter().GetResult();
                }
                else if (action == "release-stage")
                {
                    if (File.Exists(Path.Combine(QaEnvironment.CandidateRoot, "release-0.2.0/ValheimBoombox-0.2.0.zip"))
                    || File.Exists(
                        Path.Combine(QaEnvironment.CandidateRoot, "release-0.2.0/Rizias-ValheimBoombox-0.2.0.zip")
                    ))
                        QaReleaseStage.Deliver(ledger);
                    else
                        QaReleaseStage.Run(ledger).GetAwaiter().GetResult();
                }
                else if (action == "release-source")
                    QaReleaseSource.Run(ledger).GetAwaiter().GetResult();
                else if (action == "release-refresh")
                    QaReleaseRefresh.Run(ledger).GetAwaiter().GetResult();
                else if (action == "release-brand")
                    QaReleaseRefresh.Brand(ledger);
                else if (action == "release-prune-captures")
                    QaReleaseRefresh.PruneCaptureCopies(ledger);
                else if (action == "archive-baseline")
                    QaReleaseRefresh.ArchiveBaseline(ledger);
                else if (action == "demo-final-review")
                    QaVideoDemo.ReviewFinal(ledger).GetAwaiter().GetResult();
                else if (action == "audio-checks")
                    QaAudioChecks.Run(ledger).GetAwaiter().GetResult();
                else if (action == "membrane-control")
                    QaMembraneChecks.Run(ledger, true).GetAwaiter().GetResult();
                else if (action == "membrane-checks")
                    QaMembraneChecks.Run(ledger, false).GetAwaiter().GetResult();
                else if (action == "build-incompatible")
                    QaBuildCandidate.Run(ledger, true).GetAwaiter().GetResult();
                else if (action == "network-bootstrap")
                    QaNetworkSession.Run(ledger).GetAwaiter().GetResult();
                else if (action == "network-perf-probe")
                    QaPerfCases.Execute(ledger, false).GetAwaiter().GetResult();
                else if (action == "network-perf-soak")
                    QaPerfCases.Execute(ledger, true).GetAwaiter().GetResult();
                else if (action == "perf-csv-check")
                    QaPerfArtifacts.Validate(ledger);
                else if (action == "network-candidate")
                {
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "candidate").GetAwaiter().GetResult();
                }
                else if (action == "build-gameplay")
                {
                    QaUiCases.PreserveSlots(ledger);
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                }
                else if (action == "network-ui-recipe")
                {
                    QaUiCases.Preserve(ledger);
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "ui-recipe").GetAwaiter().GetResult();
                }
                else if (action == "network-ui-slots")
                {
                    QaUiCases.Slots = true;
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "ui-recipe").GetAwaiter().GetResult();
                }
                else if (action == "network-ui-complete")
                {
                    QaUiCases.Combo = true;
                    try
                    {
                        QaUiIntegrations.Install(ledger);
                        QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                        QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                        QaNetworkSession.Run(ledger, "ui-recipe").GetAwaiter().GetResult();
                    }
                    finally
                    {
                        QaUiIntegrations.Restore(ledger);
                    }
                }
                else if (action == "network-auras" || action.StartsWith("network-auras-", StringComparison.Ordinal))
                {
                    string scenario = QaAuraCompletion.Scenario(action);
                    QaAuraCompletion.PreserveFailure(ledger);
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, scenario).GetAwaiter().GetResult();
                }
                else if (action == "network-point3")
                {
                    QaPoint3Cases.PrepareFiles(ledger);
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "aura-point3").GetAwaiter().GetResult();
                }
                else if (action == "network-point3-tail")
                {
                    QaPoint3Cases.PrepareTail(ledger);
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "aura-point3-tail").GetAwaiter().GetResult();
                }
                else if (action == "point3-evidence")
                    QaPoint3Cases.Evidence(ledger);
                else if (action == "preserve-damage")
                {
                    QaDamageCases.PreserveBootFailure(ledger);
                    QaDamageCases.PrepareCurrent(ledger);
                    QaDamageCases.RestoreCurrent();
                }
                else if (action == "network-damage")
                {
                    QaDamageCases.PrepareCurrent(ledger);
                    try
                    {
                        QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                        QaNetworkSession.Run(ledger, "damage").GetAwaiter().GetResult();
                    }
                    finally
                    {
                        QaDamageCases.RestoreCurrent();
                    }
                }
                else if (action == "network-visual")
                {
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "visual").GetAwaiter().GetResult();
                }
                else if (action == "network-remote-point5")
                {
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "remote-point5").GetAwaiter().GetResult();
                }
                else if (action == "network-remote-point5-tail")
                {
                    QaRemoteCases.Preserve(ledger);
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "remote-point5-tail").GetAwaiter().GetResult();
                }
                else if (action == "network-config-point4")
                {
                    QaConfigTail.Prepare(ledger);
                    try
                    {
                        QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                        QaNetworkSession.Run(ledger, "config-point4").GetAwaiter().GetResult();
                    }
                    finally
                    {
                        QaConfigTail.Restore();
                    }
                }
                else if (action == "network-video-probe")
                {
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    ledger.Preflight(244 * QaLedger.MiB);
                    QaNetworkSession.Run(ledger, "video-probe").GetAwaiter().GetResult();
                }
                else if (action == "network-catalog")
                {
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "model03-catalog").GetAwaiter().GetResult();
                }
                else if (action == "network-model03")
                {
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "model03").GetAwaiter().GetResult();
                }
                else if (action == "network-support")
                {
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "model03-support").GetAwaiter().GetResult();
                }
                else if (action == "network-demo")
                {
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaVideoDemo.PrepareSource(ledger);
                    ledger.Preflight(192 * QaLedger.MiB);
                    QaNetworkSession.Run(ledger, "model03-demo").GetAwaiter().GetResult();
                }
                else if (action == "video-verify")
                    QaVideoProbe.VerifyExisting(ledger).GetAwaiter().GetResult();
                else if (action == "demo-failure-preview")
                    QaVideoDemo.FailurePreview(ledger).GetAwaiter().GetResult();
                else if (action == "demo-remux")
                    QaVideoDemo.RemuxExisting(ledger).GetAwaiter().GetResult();
                else if (action == "demo-media-verify")
                    QaVideoDemo.VerifyExistingDemo(ledger).GetAwaiter().GetResult();
                else if (action == "network-audio")
                {
                    QaReplaceRecoveryChecks.Run(ledger);
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "audio").GetAwaiter().GetResult();
                }
                else if (action == "network-audio-music")
                {
                    QaNativeMusicCases.Preserve(ledger);
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "audio-music").GetAwaiter().GetResult();
                }
                else if (action == "network-audio-reset")
                {
                    QaAudioResetCases.Prepare(ledger);
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "audio-reset").GetAwaiter().GetResult();
                }
                else if (action == "network-lifecycle")
                {
                    QaLifecycleCurrent.Prepare(ledger);
                    try
                    {
                        QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                        QaNetworkSession.Run(ledger, "lifecycle-current").GetAwaiter().GetResult();
                    }
                    finally
                    {
                        QaLifecycleCurrent.Restore();
                    }
                }
                else if (action == "network-lifecycle-teleport")
                {
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaNetworkSession.Run(ledger, "lifecycle-teleport").GetAwaiter().GetResult();
                }
                else if (action == "network-listen")
                {
                    QaNativeAudioCases.BuildProbe(ledger).GetAwaiter().GetResult();
                    QaBuildGameplay.Run(ledger).GetAwaiter().GetResult();
                    QaListenSession.Run(ledger).GetAwaiter().GetResult();
                }
                else if (action == "network-missing-client")
                    QaNetworkSession.Run(ledger, "missing-client").GetAwaiter().GetResult();
                else if (action == "network-missing-server")
                    QaNetworkSession.Run(ledger, "missing-server").GetAwaiter().GetResult();
                else if (action == "network-incompatible-client")
                    QaNetworkSession.Run(ledger, "incompatible-client").GetAwaiter().GetResult();
                else if (action == "preserve-bootstrap")
                    QaEvidence.PreserveBootstrap(ledger);
                else if (action == "preserve-join-one")
                    QaEvidence.PreserveJoinOne(ledger);
                else if (action == "preserve-missing-client")
                {
                    if (!QaJoinChecks.Rejected(ledger, "missing-client"))
                        throw new IOException("Previous JOIN-02 evidence not complete");
                }
                else if (action == "preserve-incompatible-failure")
                    QaJoinChecks.PreserveFailure(ledger);
                else if (action == "complete-r08")
                    QaJoinChecks.Complete(ledger);
                else if (action == "recover")
                {
                    QaUiIntegrations.AccountGenerated(ledger);
                    ledger.RecoverReservations();
                    Exception? configError = null;
                    try
                    {
                        ledger.RestoreConfigRollback();
                    }
                    catch (Exception error)
                    {
                        configError = error;
                    }
                    Exception? loaderError = null;
                    try
                    {
                        ledger.RestoreServerLoader();
                    }
                    catch (Exception error)
                    {
                        loaderError = error;
                    }
                    try
                    {
                        QaRegistry.Recover(ledger);
                    }
                    catch
                    {
                        ledger.RecoveryRequired();
                        throw;
                    }
                    if (configError != null)
                    {
                        ledger.RecoveryRequired();
                        throw configError;
                    }
                    if (loaderError != null)
                    {
                        ledger.RecoveryRequired();
                        throw loaderError;
                    }
                }
                else if (action == "enroll-bootstrap")
                    QaRecovery.EnrollBootstrapArtifacts(ledger);
                else if (action == "init")
                {
                    foreach (string category in new[] {
                        "baseline",
                        "profiles/server",
                        "profiles/client-a",
                        "profiles/client-b",
                        "current",
                        "evidence"
                    })
                        Directory.CreateDirectory(ledger.Resolve(Path.Combine(ledger.Root, category)));
                    ledger.Preflight(64 * QaLedger.MiB);
                }
                else if (action != "status")
                    throw new ArgumentException("Unknown QA action");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    action,
                    phase = ledger.State.Phase,
                    generation = ledger.State.Generation,
                    files = ledger.State.Files.Count,
                    bytes = ledger.State.Files.Values.Sum(f => f.Bytes),
                    reservations = ledger.State.Reservations.Count,
                    limits = new
                    {
                        total = ledger.TotalCap,
                        runtime = ledger.RuntimeCap,
                        evidence = ledger.EvidenceCap,
                        stage = ledger.StageCap
                    },
                    free = new DriveInfo(Path.GetPathRoot(ledger.Root)!).AvailableFreeSpace
                }));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
