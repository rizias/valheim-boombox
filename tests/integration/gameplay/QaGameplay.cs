using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

[BepInPlugin("local.boombox.qa.gameplay", "Boombox QA Gameplay", "1.0.0")]
[BepInDependency("local.valheim.boombox")]
[DefaultExecutionOrder(15000)]
public sealed partial class QaGameplay : BaseUnityPlugin
{
    private string role, commandId, action, error, productSha;
    private bool busy;
    private readonly Dictionary<string, AudioDeck> decks = new Dictionary<string, AudioDeck>();
    private readonly List<GameObject> fixtures = new List<GameObject>();
    private readonly List<ScriptableObject> controls = new List<ScriptableObject>();
    private JObject sample;
    private float moveUntil;
    private Vector3 moveStart;

    private void Awake()
    {
        role = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROLE");
        productSha = Hash(typeof(BoomboxPlugin).Assembly.Location);
        if (role != "server" && role != "client-a" && role != "client-b")
            throw new InvalidOperationException("QA gameplay role missing");
        InstallCombatArea();
        InstallSafety();
        InstallRecoveryReloadObserver();
        InstallTransportPersistence();
        StartCoroutine(Commands());
        StartCoroutine(Report());
    }

    private IEnumerator Commands()
    {
        string path = Path.Combine(Paths.ConfigPath, "qa-gameplay-command.json");
        while (true)
        {
            if ((!busy || perfBridge != null) && File.Exists(path))
            {
                JObject request = null;
                try
                {
                    request = JObject.Parse(File.ReadAllText(path));
                }
                catch (IOException) { }
                catch (Newtonsoft.Json.JsonException) { }
                if (request != null && (string)request["id"] != commandId)
                {
                    commandId = (string)request["id"];
                    action = (string)request["action"];
                    error = null;
                    sample = null;
                    try
                    {
                        if (perfBridge != null && !action.StartsWith("perf-", StringComparison.Ordinal))
                            throw new InvalidOperationException("Performance series owns gameplay commands");
                        if (action == "sample")
                        {
                            busy = true;
                            StartCoroutine(
                                Sample((float?)request["duration"] ?? 5.4f, (float?)request["missing"] ?? 50f)
                            );
                        }
                        else if (action == "manual-capture")
                        {
                            busy = true;
                            StartCoroutine(GuardAudio(ManualCapture()));
                        }
                        else if (action == "move")
                        {
                            moveStart = Player.m_localPlayer.transform.position;
                            moveUntil = Time.realtimeSinceStartup + 2f;
                            busy = true;
                        }
                        else
                            Execute(action, request);
                    }
                    catch (Exception failure)
                    {
                        error = failure.ToString();
                        Logger.LogError(error);
                        busy = false;
                    }
                }
            }
            yield return new WaitForSecondsRealtime(.1f);
        }
    }

    private void LateUpdate()
    {
        if (perfBridge != null)
        {
            PerfTick();
            return;
        }
        LifecycleTick();
        if (moveUntil <= 0f)
            return;
        if (Player.m_localPlayer == null)
        {
            moveUntil = 0;
            busy = false;
            error = "Movement actor disappeared";
            return;
        }
        Player.m_localPlayer.SetMoveDir(Vector3.forward);
        Player.m_localPlayer.SetRun(false);
        if (Time.realtimeSinceStartup < moveUntil)
            return;
        Player.m_localPlayer.SetMoveDir(Vector3.zero);
        sample = new JObject
        {
            ["travel"] = Vector3.Distance(moveStart, Player.m_localPlayer.transform.position),
            ["start"] = Point(moveStart),
            ["end"] = Point(Player.m_localPlayer.transform.position),
            ["velocity"] = Point(Player.m_localPlayer.GetVelocity())
        };
        moveUntil = 0;
        busy = false;
    }

    private IEnumerator Report()
    {
        while (true)
        {
            if (perfBridge != null || Environment.GetEnvironmentVariable("BOOMBOX_QA_CAPTURE_ACTIVE") == "1")
            {
                yield return new WaitForSecondsRealtime(.1f);
                continue;
            }
            JObject snapshot;
            try
            {
                snapshot = Snapshot();
            }
            catch (Exception failure)
            {
                snapshot = new JObject { ["role"] = role, ["id"] = commandId, ["error"] = failure.ToString() };
            }
            snapshot["busy"] = busy;
            snapshot["action"] = action;
            snapshot["commandError"] = error;
            snapshot["sample"] = sample;
            try
            {
                File.WriteAllText(Path.Combine(Paths.ConfigPath, "qa-gameplay-status.json"), snapshot.ToString());
            }
            catch (IOException failure)
            {
                Logger.LogWarning(failure.Message);
            }
            yield return new WaitForSecondsRealtime(.5f);
        }
    }

    private JObject Snapshot()
    {
        var player = Player.m_localPlayer;
        var sources = new JArray(BoomboxAuraNetwork.Sources.Select(s => new JObject
        {
            ["id"] = s.Id,
            ["owner"] = s.Owner,
            ["peer"] = s.Peer,
            ["active"] = s.Active,
            ["worn"] = s.Worn,
            ["worldItem"] = s.WorldItem.ToString(),
            ["position"] = Point(s.Position),
            ["ranks"] = new JArray(s.Ranks)
        }));
        var result = new JObject
        {
            ["role"] = role,
            ["id"] = commandId,
            ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
            ["sha"] = productSha,
            ["sources"] = sources,
            ["combat"] = CombatSnapshot(),
            ["capture"] = lastCapture,
            ["utc"] = DateTime.UtcNow.ToString("O")
        };
        result["safety"] = SafetySnapshot();
        result["audio"] = AudioSnapshot();
        result["lifecycle"] = LifecycleSnapshot();
        result["remaining"] = RemainingSnapshot();
        result["listen"] = ListenSnapshot();
        result["damageComplete"] = lastDamageComplete?.DeepClone();
        result["damageResult"] = lastDamageResult?.DeepClone();
        result["damageResults"] = damageResults.DeepClone();
        result["freshPendingState"] = lastFreshPendingState?.DeepClone();
        result["serverOwnerProof"] = serverOwnerProof?.DeepClone();
        if (player == null)
            return result;
        result["playerId"] = player.GetPlayerID();
        result["position"] = Point(player.transform.position);
        result["teleporting"] = player.IsTeleporting();
        result["dead"] = player.IsDead();
        result["vitals"] = Vitals(player);
        result["effects"] = new JArray(
            player.GetSEMan().GetStatusEffects().OfType<BoomboxAuraStatus>().Select(
                s => new JObject
                {
                    ["name"] = s.name,
                    ["tooltip"] = s.GetTooltipString(),
                    ["speed"] = s.m_speedModifier
                }
            )
        );
        result["owned"] = new JArray(decks.Where(p => p.Value != null).Select(p => new JObject
        {
            ["key"] = p.Key,
            ["id"] = p.Value.State.id,
            ["playing"] = p.Value.State.playing,
            ["active"] = p.Value.IsActivePlayback || p.Value.IsSyntheticActive,
            ["position"] = Point(p.Value.transform.position),
            ["worldItem"] = p.Value.WorldDrop?.GetComponent<ZNetView>()?.GetZDO()?.m_uid.ToString(),
            ["nativeOwner"] = p.Value.WorldDrop?.GetComponent<ZNetView>()?.GetZDO()?.GetOwner(),
            ["beats"] = p.Value.EmittedWaves
        }));
        result["audioDecks"] = new JArray(
            UnityEngine.Object.FindObjectsByType<AudioDeck>(FindObjectsSortMode.None).Select(d => new JObject
            {
                ["id"] = d.State.id,
                ["owner"] = d.State.owner,
                ["audioSource"] = d.Source != null,
                ["playing"] = d.Source != null && d.Source.isPlaying
            })
        );
        return result;
    }

    private IEnumerator Sample(float duration, float missing)
    {
        var player = Player.m_localPlayer;
        player.SetHealth(Mathf.Max(1f, player.GetMaxHealth() - missing));
        player.AddStamina(player.GetMaxStamina());
        player.UseStamina(Mathf.Min(missing, player.GetMaxStamina()));
        player.AddEitr(player.GetMaxEitr());
        player.UseEitr(Mathf.Min(missing, player.GetMaxEitr()));
        var before = Vitals(player);
        double h = BoomboxAuraRecipient.HealthTicks,
            s = BoomboxAuraRecipient.StaminaTicks,
            e = BoomboxAuraRecipient.EitrTicks;
        yield return new WaitForSecondsRealtime(duration);
        sample = new JObject
        {
            ["before"] = before,
            ["after"] = Vitals(player),
            ["duration"] = duration,
            ["ticks"] = new JArray(
                BoomboxAuraRecipient.HealthTicks - h,
                BoomboxAuraRecipient.StaminaTicks - s,
                BoomboxAuraRecipient.EitrTicks - e
            )
        };
        busy = false;
    }

    private static JObject Vitals(Player player) => new JObject
    {
        ["health"] = player.GetHealth(),
        ["stamina"] = player.GetStamina(),
        ["eitr"] = player.GetEitr(),
        ["maxHealth"] = player.GetMaxHealth(),
        ["maxStamina"] = player.GetMaxStamina(),
        ["maxEitr"] = player.GetMaxEitr()
    };
    private static JObject Point(Vector3 point) => new JObject { ["x"] = point.x, ["y"] = point.y, ["z"] = point.z };
    private static Vector3 ReadPoint(
        JToken point
    ) => new Vector3((float)point["x"], (float)point["y"], (float)point["z"]);
    private static string Hash(string path)
    {
        using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
    private void OnDestroy()
    {
        try
        {
            StopPerfTelemetry();
        }
        finally
        {
            StopRecoveryReloadObserver();
            if (point3Observer == this)
                point3Observer = null;
            StopAuraMotion();
            StopCombatArea();
            safetyHarmony?.UnpatchSelf();
            foreach (var fixture in fixtures)
                if (fixture != null)
                    UnityEngine.Object.Destroy(fixture);
            foreach (var control in controls)
                if (control != null)
                    UnityEngine.Object.Destroy(control);
        }
    }
}
