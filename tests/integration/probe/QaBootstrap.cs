using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

[BepInPlugin("local.boombox.qa.bootstrap", "Boombox QA Bootstrap", "1.0.0")]
public sealed class QaBootstrap : BaseUnityPlugin
{
    private string role, root;
    private Harmony harmony;
    private QaProductLog productLog;
    private JObject listenWorld;
    private bool actorReady;
    private float actorPreparationSeconds;
    private int actorWorldWaitFrames;
    private bool fastUi;
    private int fastUiFrameRate = 60;
    private QaQuiet quiet;
    private static System.Reflection.Assembly cachedProductAssembly;
    private static string cachedProductSha;
    private void Awake()
    {
        root = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT");
        role = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROLE");
        if (role != "server" && role != "client-a" && role != "client-b")
            throw new InvalidOperationException("QA role missing");
        string profile = Path.GetFullPath(Path.Combine(root, "profiles", role));
        if (!Path.GetFullPath(Paths.ConfigPath).StartsWith(
            profile + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase
        ))
            throw new InvalidOperationException("QA config profile isolation failed");
        harmony = new Harmony("local.boombox.qa.bootstrap");
        productLog = new QaProductLog();
        BepInEx.Logging.Logger.Listeners.Add(productLog);
        QaWriteGuard.Install(harmony, role);
        QaGuardChecks.Run();
        QaPreferences.Install(harmony);
        PlatformPrefs.SetInt("AutoBackups", 0);
        QaTransport.Install(harmony);
        var intro = AccessTools.Method(typeof(FejdStartup), "TryPlayIntroCinematic")
            ?? AccessTools.Method(typeof(FejdStartup), "PlayIntroCinematic");
        string skip = intro.ReturnType == typeof(IEnumerator) ? nameof(SkipIntroRoutine) : nameof(SkipIntro);
        harmony.Patch(intro, prefix: new HarmonyMethod(typeof(QaBootstrap), skip));
        Application.runInBackground = true;
        fastUi = Environment.GetEnvironmentVariable("BOOMBOX_QA_FAST_UI") == "1";
        string caseName = Environment.GetEnvironmentVariable("BOOMBOX_QA_CASE") ?? "";
        if (caseName.StartsWith("local-case-L", StringComparison.Ordinal) || caseName == "migration-mp")
            fastUiFrameRate = 30;
        if (role != "server")
        {
            quiet = new QaQuiet();
            quiet.Tick();
        }
        StartCoroutine(Report());
        StartCoroutine(Commands());
        if (role != "server")
        {
            StartCoroutine(Join());
            StartCoroutine(PrepareActor());
        }
        Logger.LogInfo("QA bootstrap role=" + role + " config=" + Paths.ConfigPath);
    }
    private static bool SkipIntro() => false;
    private void Update()
    {
        quiet?.Tick();
        if (!fastUi)
            return;
        Settings.ReduceBackgroundUsage = false;
        Application.targetFrameRate = fastUiFrameRate;
        QualitySettings.vSyncCount = 0;
    }
    private static bool SkipIntroRoutine(ref IEnumerator __result)
    {
        __result = Empty();
        return false;
    }
    private static IEnumerator Empty()
    {
        yield break;
    }
    private IEnumerator PrepareActor()
    {
        while (Game.instance == null
            || TextViewer.instance == null
            || ZNet.instance == null
            || ZNet.instance.GetWorld() == null
            || ZNet.GetConnectionStatus() != ZNet.ConnectionStatus.Connected)
        {
            if (Game.instance != null && ZNet.instance != null && ZNet.instance.GetWorld() == null)
                actorWorldWaitFrames++;
            yield return null;
        }
        Logger.LogInfo(
            "QA spawn preparation: connected world=" + ZNet.instance.GetWorldUID() + " waitedWorldFrames="
                + actorWorldWaitFrames
        );
        var loadDelay = AccessTools.Field(typeof(Game), "m_respawnLoadDuration");
        float previousDelay = (float)loadDelay.GetValue(Game.instance);
        loadDelay.SetValue(Game.instance, 0f);
        Game.instance.SkipIntro();
        while (Player.m_localPlayer == null)
            yield return null;
        loadDelay.SetValue(Game.instance, previousDelay);
        float start = Time.realtimeSinceStartup;
        var actor = Player.m_localPlayer;
        yield return null;
        if (CinematicsManager.IsPlaying())
            CinematicsManager.Stop();
        if (Valkyrie.m_instance != null)
            Valkyrie.m_instance.DropPlayer(true);
        actor.SetIntro(false);
        actor.SetSleeping(false);
        for (int step = 0; step < 8 && actor.InCutscene(); step++)
        {
            var animator = actor.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.Update(3f);
            yield return null;
        }
        actorPreparationSeconds = Time.realtimeSinceStartup - start;
        float quietDeadline = Time.realtimeSinceStartup + 5;
        while (quiet != null && (!quiet.Muted || !quiet.Offscreen) && Time.realtimeSinceStartup < quietDeadline)
            yield return null;
        if (quiet != null && (!quiet.Muted || !quiet.Offscreen))
        {
            Logger.LogError("QA quiet mode could not be confirmed; refusing gameplay checks");
            Application.Quit();
            yield break;
        }
        actorReady = !actor.InCutscene() && !actor.IsTeleporting() && !actor.IsDead();
        Logger.LogInfo(
            "QA actor ready=" + actorReady + " preparationSeconds=" + actorPreparationSeconds + " intro="
                + actor.InIntro()
        );
    }
    private IEnumerator Join()
    {
        FejdStartup menu = null;
        float deadline = Time.realtimeSinceStartup + 100;
        while ((menu = UnityEngine.Object.FindFirstObjectByType<FejdStartup>()) == null
            && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (menu == null)
            throw new InvalidOperationException("QA client menu missing");
        string profileName = role.Replace('-', '_');
        var profile = new PlayerProfile(profileName, FileHelpers.FileSource.Local);
        if (PlayerProfile.HaveProfile(profileName))
        {
            if (!profile.Load())
                throw new InvalidOperationException("QA profile could not be loaded");
        }
        else
            profile.SetName("QA_" + role);
        profile.m_firstSpawn = false;
        if (!profile.Save())
            throw new InvalidOperationException("QA native profile preparation failed");
        Game.SetProfile(profileName, FileHelpers.FileSource.Local);
        PlatformPrefs.SetString("profile", profileName);
        if (Environment.GetEnvironmentVariable("BOOMBOX_QA_LISTEN_HOST") == "1")
        {
            if (role != "client-a")
                throw new InvalidOperationException("Listen host belongs to native A");
            SaveWithBackups save;
            bool loaded = SaveSystem.TryGetSaveByName("BoomboxQA", SaveDataType.World, out save) && !save.IsDeleted;
            var world = loaded ? World.LoadWorld(save) : new World("BoomboxQA", "boombox-qa-20261004");
            if (world.m_dataError != World.SaveDataError.None)
                throw new InvalidOperationException("Listen world native load failed " + world.m_dataError);
            world.m_fileSource = FileHelpers.FileSource.Local;
            if (!loaded)
            {
                SaveSystem.SetSaveNumber(0);
                world.SaveWorldFWLData(DateTime.Now);
            }
            listenWorld = new JObject
            {
                ["loadedExisting"] = loaded,
                ["name"] = world.m_name,
                ["seed"] = world.m_seedName,
                ["uid"] = world.m_uid,
                ["source"] = world.m_fileSource.ToString(),
                ["meta"] = world.GetSaveFWLPath()
            };
            ZNet.ResetServerHost();
            ZNet.SetServer(true, true, false, "Boombox QA listen", "qa-local-password", world);
        }
        else
        {
            if (Environment.GetEnvironmentVariable("BOOMBOX_QA_LISTEN_SESSION") == "1")
            {
                float hostDeadline = Time.realtimeSinceStartup + 150;
                while (!System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(
                    p => p.Port == QaTransport.Port && System.Net.IPAddress.IsLoopback(p.Address)
                ))
                {
                    if (Time.realtimeSinceStartup > hostDeadline)
                        throw new TimeoutException("QA host listener did not become ready");
                    yield return new WaitForSecondsRealtime(.25f);
                }
            }
            ZNet.SetServer(false, false, false, "", "", null);
            ZNet.SetServerHost("127.0.0.1", QaTransport.Port, OnlineBackendType.CustomSocket);
        }
        AccessTools.Field(typeof(FejdStartup), "m_startingWorld").SetValue(menu, true);
        AccessTools.Method(typeof(FejdStartup), "TransitionToMainScene").Invoke(menu, null);
    }
    private IEnumerator Report()
    {
        while (true)
        {
            if (Environment.GetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE") == "1")
            {
                yield return new WaitForSecondsRealtime(.1f);
                continue;
            }
            var peers = ZNet.instance == null
                ? new JArray()
                : new JArray(ZNet.instance.GetPeers().Select(p => new JObject
                {
                    ["uid"] = p.m_uid,
                    ["playerId"] = ZDOMan.instance?.GetZDO(p.m_characterID)?.GetLong(ZDOVars.s_playerID, 0L) ?? 0L,
                    ["legacyPlayerId"] = p.m_playerID,
                    ["characterId"] = p.m_characterID.ToString(),
                    ["name"] = p.m_playerName,
                    ["ready"] = p.IsReady(),
                    ["server"] = p.m_server
                }));
            var players = new JArray(Player.GetAllPlayers().Select(p => new JObject
            {
                ["id"] = p.GetPlayerID(),
                ["character"] = p.GetZDOID().ToString(),
                ["owner"] = p.GetComponent<ZNetView>()?.GetZDO()?.GetOwner() ?? 0L,
                ["zdoPlayerId"] = p.GetComponent<ZNetView>()?.GetZDO()?.GetLong(ZDOVars.s_playerID, 0L) ?? 0L,
                ["name"] = p.GetPlayerName(),
                ["position"] = p.transform.position.ToString("F3"),
                ["x"] = p.transform.position.x,
                ["y"] = p.transform.position.y,
                ["z"] = p.transform.position.z,
                ["proof"] = p.GetComponent<ZNetView>()?.GetZDO()?.GetString("boombox.qa.proof", "") ?? ""
            }));
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var menu = UnityEngine.Object.FindFirstObjectByType<FejdStartup>();
            var errorLabel = menu == null
                ? null
                : (TMPro.TMP_Text)AccessTools.Field(typeof(FejdStartup), "m_connectionFailedError").GetValue(menu);
            var state = new JObject
            {
                ["role"] = role,
                ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["dataPath"] = Application.dataPath,
                ["executable"] = process.MainModule.FileName,
                ["startUTC"] = process.StartTime.ToUniversalTime().ToString("O"),
                ["config"] = Paths.ConfigPath,
                ["save"] = Utils.GetSaveDataPath(FileHelpers.FileSource.Local),
                ["version"] = global::Version.GetVersionString(),
                ["connection"] = AccessTools.Method(typeof(ZNet), "GetConnectionStatus")
                    .Invoke(ZNet.instance, null)
                    .ToString(),
                ["peers"] = peers,
                ["players"] = players,
                ["worldUID"] = ZNet.instance == null || ZNet.instance.GetWorld() == null
                    ? 0
                    : ZNet.instance.GetWorldUID(),
                ["hostPort"] = ZNet.instance == null ? 0 : ZNet.instance.GetHostPort(),
                ["localPlayer"] = Player.m_localPlayer == null ? 0 : Player.m_localPlayer.GetPlayerID(),
                ["nativeUID"] = ZNet.instance != null ? ZNet.GetUID() : 0L,
                ["isServer"] = ZNet.instance != null && ZNet.instance.IsServer(),
                ["listenWorld"] = listenWorld,
                ["hud"] = Hud.instance != null,
                ["actorReady"] = actorReady,
                ["actorPreparationSeconds"] = actorPreparationSeconds,
                ["canEditBalance"] = ZNet.instance != null && ZNet.instance.IsServer(),
                ["quiet"] = new JObject
                {
                    ["audioSessionMuted"] = role == "server" || quiet?.Muted == true,
                    ["windowOffscreen"] = role == "server" || quiet?.Offscreen == true
                },
                ["actorWorldWaitFrames"] = actorWorldWaitFrames,
                ["joinReason"] = errorLabel == null ? "" : errorLabel.text,
                ["product"] = ProductEvidence(),
                ["preferences"] = QaPreferences.Evidence(),
                ["plugins"] = new JArray(Chainloader.PluginInfos.Keys.OrderBy(s => s)),
                ["utc"] = DateTime.UtcNow.ToString("O")
            };
            try
            {
                File.WriteAllText(Path.Combine(Paths.ConfigPath, "qa-status.json"), state.ToString());
            }
            catch (IOException error)
            {
                Logger.LogWarning("QA report retry: " + error.Message);
            }
            yield return new WaitForSecondsRealtime(1);
        }
    }
    private static JObject ProductEvidence()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
            a => a.GetType("VikingBoombox.BoomboxNetworkProtocol", false) != null
        );
        if (assembly == null)
            return new JObject { ["loaded"] = false, ["activeAuraSources"] = 0, ["decks"] = 0 };
        var protocol = assembly.GetType("VikingBoombox.BoomboxNetworkProtocol");
        string sha;
        if (cachedProductAssembly != assembly)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
            using (var file = File.OpenRead(assembly.Location))
                cachedProductSha = BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
            cachedProductAssembly = assembly;
        }
        sha = cachedProductSha;
        var sources = (System.Collections.IEnumerable)assembly.GetType("VikingBoombox.BoomboxAuraNetwork")
            .GetProperty("Sources")
            .GetValue(null);
        int active = 0;
        foreach (var source in sources)
            if ((bool)source.GetType().GetField("Active").GetValue(source))
                active++;
        var plugin = assembly.GetType("VikingBoombox.BoomboxPlugin").GetField("Instance").GetValue(null);
        var decks = plugin == null
            ? null
            : (System.Collections.IDictionary)assembly.GetType("VikingBoombox.BoomboxPlugin").GetField(
            "Decks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        ).GetValue(
            plugin
        );
        return new JObject
        {
            ["loaded"] = true,
            ["sha"] = sha,
            ["version"] = (string)protocol.GetField("Version").GetRawConstantValue(),
            ["protocol"] = (int)protocol.GetField("Protocol").GetRawConstantValue(),
            ["activeAuraSources"] = active,
            ["decks"] = decks?.Count ?? 0
        };
    }
    private IEnumerator Commands()
    {
        string command = Path.Combine(Paths.ConfigPath, "qa-command.txt");
        string applied = "";
        while (true)
        {
            string action = File.Exists(command) ? File.ReadAllText(command) : "";
            if (action.StartsWith("teleport:", StringComparison.Ordinal)
                && action != applied
                && Player.m_localPlayer != null
                && !Player.m_localPlayer.IsTeleporting())
            {
                string[] coordinates = action.Substring(9).Split(':');
                var destination = new Vector3(
                    float.Parse(coordinates[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(coordinates[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(coordinates[2], System.Globalization.CultureInfo.InvariantCulture)
                );
                Player.m_localPlayer.TeleportTo(destination, Player.m_localPlayer.transform.rotation, false);
                applied = action;
            }
            if (action.StartsWith("proof:", StringComparison.Ordinal) && Player.m_localPlayer != null)
            {
                var view = Player.m_localPlayer.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && view.IsOwner())
                    view.GetZDO().Set("boombox.qa.proof", action.Substring(6));
            }
            if (action == "shutdown")
            {
                if (Game.instance != null)
                    AccessTools.Method(typeof(Game), "Shutdown").Invoke(Game.instance, new object[] { true });
                Application.Quit();
                yield break;
            }
            yield return new WaitForSecondsRealtime(.5f);
        }
    }
    private void OnDestroy()
    {
        try
        {
            if (quiet != null)
            {
                AudioListener.pause = true;
                quiet.Dispose();
                quiet = null;
                System.Console.WriteLine("QA_QUIET_AUDIO_RESTORED " + role);
            }
        }
        finally
        {
            harmony?.UnpatchSelf();
            if (productLog != null)
                BepInEx.Logging.Logger.Listeners.Remove(productLog);
        }
    }
}
