using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private bool demoSaved;
    private Vector3 demoOrigin;
    private Quaternion demoRotation;
    private ItemDrop.ItemData demoCape, demoOldCape;
    private JObject demoScene;
    private OwnAudioSessionMute demoMute;
    private float demoVolume;
    private bool demoMovement, demoMusicMovement;

    private IEnumerator DemoSetup(JObject request = null)
    {
        yield return AwaitAudio();
        var player = Player.m_localPlayer;
        Game.Unpause();
        Game.FadeTimeScale(1f);
        if (demoSaved)
            throw new InvalidOperationException("Demo scene already owned");
        demoOrigin = player.transform.position;
        demoRotation = player.transform.rotation;
        demoSaved = true;
        demoVolume = audioDeck.State.volume;
        var settings = BoomboxPlugin.Instance.Settings;
        demoMovement = settings.DecorationMovement.Value;
        demoMusicMovement = settings.DecorationMusicMovement.Value;
        settings.DecorationMovement.Value = settings.DecorationMusicMovement.Value = true;
        demoMute = new OwnAudioSessionMute();
        audioDeck.State.volume = .55f;
        BoomboxItems.Persist(audioDeck);
        var capeField = AccessTools.Field(typeof(Humanoid), "m_shoulderItem");
        if (capeField == null)
            throw new InvalidOperationException("Native shoulder field missing");
        demoOldCape = (ItemDrop.ItemData)capeField.GetValue(player);
        var capePrefab = ObjectDB.instance.GetItemPrefab("CapeDeerHide");
        if (capePrefab != null && capePrefab.GetComponent<ItemDrop>() != null)
        {
            demoCape = capePrefab.GetComponent<ItemDrop>().m_itemData.Clone();
            demoCape.m_dropPrefab = capePrefab;
            if (!player.GetInventory().AddItem(demoCape) || !player.EquipItem(demoCape, false))
                throw new InvalidOperationException("Native CapeDeerHide equip refused");
        }
        var catalog = typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxDecorationCatalog", true);
        var eligible = catalog.GetMethod("DataEligible", BindingFlags.NonPublic | BindingFlags.Static);
        var state = audioDeck.State;
        string[] hanging = {
            "MushroomYellow",
            "Raspberry",
            "Blueberries",
            "GreydwarfEye",
            "Resin",
            "Ruby",
            "Copper",
            "Tin",
            "Bronze",
            "Iron",
            "Silver",
            "Gold",
            BoomboxItemData.DevicePrefab,
            "SurtlingCore",
            "BlackCore",
            BoomboxItemData.DevicePrefab,
            "CryptKey"
        };
        string[] heads = {
            "TrophySkeleton",
            "TrophyBoar",
            "TrophyDeer",
            "TrophyNeck",
            "TrophyGreydwarf",
            "TrophyDraugr",
            "TrophyWolf"
        };
        state.decorationPlacements.Clear();
        state.unlockedDecorationSlots = Enumerable.Range(0, 24).ToList();
        state.unlockedDecorationPrefabs.Clear();
        for (int slot = 0; slot < 24; slot++)
        {
            string prefab = slot < 17 ? hanging[slot] : heads[slot - 17];
            if (!(bool)eligible.Invoke(null, new object[] { prefab, slot }))
            {
                if (slot >= 17 || slot == 12 || slot == 15)
                    throw new InvalidOperationException("Required native decoration unavailable: " + prefab);
                prefab = "MushroomYellow";
                if (!(bool)eligible.Invoke(null, new object[] { prefab, slot }))
                    throw new InvalidOperationException("Native hanging fallback unavailable");
            }
            state.unlockedDecorationPrefabs.Add(prefab);
            state.decorationPlacements.Add(
                new BoomboxDecorationPlacement
                {
                    slot = slot,
                    prefab = prefab,
                    effect = "Health",
                    effectParticles = false
                }
            );
        }
        state.unlockedDecorationPrefabs = state.unlockedDecorationPrefabs.Distinct(StringComparer.Ordinal).ToList();
        BoomboxPlugin.Instance.Save();
        BoomboxItems.Persist(audioDeck);
        audioDeck.VisualPresenter.RefreshState(true);
        Vector3 coast, sea;
        if (request?["coast"] != null)
        {
            coast = ReadPoint(request["coast"]);
            sea = ReadPoint(request["sea"]);
        }
        else
            coast = FindNativeShore(player.transform.position, out sea);
        yield return DemoReadyTeleport();
        if (!player.TeleportTo(coast, Quaternion.LookRotation(sea), true))
            throw new InvalidOperationException("Native coast teleport refused");
        yield return DemoWaitTeleport();
        yield return new WaitForSecondsRealtime(4);
        float water = ZoneSystem.instance.m_waterLevel;
        var landing = player.transform.position;
        if (request?["coast"] == null)
        {
            bool found = false;
            for (int radius = 0; radius <= 16 && !found; radius += 2)
                for (int around = 0; around < 16 && !found; around++)
                {
                    var point = landing + Quaternion.AngleAxis(around * 22.5f, Vector3.up) * Vector3.forward * radius;
                    if (!ZoneSystem.instance.GetGroundHeight(point, out float h) || h < water + .3f || h > water + 2)
                        continue;
                    for (int look = 0; look < 16 && !found; look++)
                    {
                        var facing = Quaternion.AngleAxis(look * 22.5f, Vector3.up) * Vector3.forward;
                        if (!ZoneSystem.instance.GetGroundHeight(point + facing * 10, out float h10)
                        || !ZoneSystem.instance.GetGroundHeight(point + facing * 20, out float h20)
                        || h10 >= water
                        || h20 >= water)
                            continue;
                        point.y = h + .1f;
                        if (Physics.CheckSphere(
                            point + Vector3.up * 1.5f,
                            3,
                            LayerMask.GetMask("Default", "static_solid"),
                            QueryTriggerInteraction.Ignore
                        ))
                            continue;
                        sea = facing;
                        coast = point;
                        found = true;
                    }
                }
            if (!found)
                throw new InvalidOperationException("No valid loaded shore near procedural candidate " + landing);
            yield return DemoReadyTeleport();
            if (!player.TeleportTo(coast, Quaternion.LookRotation(sea), true))
                throw new InvalidOperationException("Loaded shore teleport refused");
            yield return DemoWaitTeleport();
            yield return DemoReadyTeleport();
            landing = player.transform.position;
        }
        if (!ZoneSystem.instance.GetGroundHeight(landing, out float ground)
            || ground < water + .3f
            || ground > water + 2
            ||
        !ZoneSystem.instance.GetGroundHeight(
            landing + sea * 10,
            out float ten
        )
            || !ZoneSystem.instance.GetGroundHeight(
            landing + sea * 20,
            out float twenty
        )
            || ten >= water
            || twenty >= water)
            throw new InvalidOperationException("Loaded native coast/water 10/20m validation failed");
        if (Physics.CheckSphere(
            landing + Vector3.up * 1.5f,
            3,
            LayerMask.GetMask("Default", "static_solid"),
            QueryTriggerInteraction.Ignore
        ))
            throw new InvalidOperationException("Native coast landing obstructed");
        var frame = audioDeck.Visual.transform;
        Vector3 quarter = Vector3.ProjectOnPlane(frame.forward + frame.right, Vector3.up).normalized;
        float yaw = Vector3.SignedAngle(quarter, -sea, Vector3.up);
        yield return DemoReadyTeleport();
        if (!player.TeleportTo(landing, Quaternion.AngleAxis(yaw, Vector3.up) * player.transform.rotation, true))
            throw new InvalidOperationException("Native heading calibration refused");
        yield return DemoWaitTeleport();
        yield return new WaitForSecondsRealtime(.5f);
        quarter = Vector3.ProjectOnPlane(frame.forward + frame.right, Vector3.up).normalized;
        float dot = Vector3.Dot(-quarter, sea);
        if (dot < .98f)
            throw new InvalidOperationException("Actual model heading disagrees with sea: " + dot);
        var decorations = frame.Find("BoomboxDecorations");
        for (int slot = 0; slot < 24; slot++)
        {
            var holder = decorations.Find("DecorationSlot_" + slot);
            if (holder == null
            || !holder.GetComponentsInChildren<Renderer>(true).Any(
                r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
            ))
                throw new InvalidOperationException("Native decoration has no active renderers: " + slot);
        }
        var stored = BoomboxItemData.Read(audioDeck.Item).deck;
        if (stored.decorationPlacements.Count != 24
        || state.decorationPlacements.Any(
            p => !stored.decorationPlacements.Any(s => s.slot == p.slot && s.prefab == p.prefab)
        ))
            throw new InvalidOperationException("Native stored24 placements disagree");
        demoScene = new JObject
        {
            ["landing"] = Point(player.transform.position),
            ["sea"] = Point(sea),
            ["nativeGround"] = ground,
            ["water"] = water,
            ["waterGround10"] = ten,
            ["waterGround20"] = twenty,
            ["cameraToSeaDot"] = dot,
            ["cape"] = demoCape == null ? null : "CapeDeerHide",
            ["placements"] = new JArray(
                state.decorationPlacements.Select(p => new JObject { ["slot"] = p.slot, ["prefab"] = p.prefab })
            ),
            ["source"] = state.id,
            ["baseTris"] = 17567,
            ["volume"] = state.volume,
            ["scope"] = "Native scene/stored-rendered24; not full catalog/cape/FX acceptance"
        };
        lastCapture = demoScene;
    }


    private IEnumerator DemoWaitTeleport()
    {
        float deadline = Time.realtimeSinceStartup + 60;
        while (Player.m_localPlayer.IsTeleporting())
        {
            if (Time.realtimeSinceStartup > deadline)
                throw new TimeoutException("Native demo teleport");
            yield return null;
        }
    }

    private IEnumerator DemoReadyTeleport()
    {
        float deadline = Time.realtimeSinceStartup + 5;
        var cooldown = AccessTools.Field(typeof(Player), "m_teleportCooldown");
        while (Player.m_localPlayer.IsTeleporting() || (float)cooldown.GetValue(Player.m_localPlayer) < 2f)
        {
            if (Time.realtimeSinceStartup > deadline)
                throw new InvalidOperationException(
                    "Teleport readiness: cooldown=" + cooldown.GetValue(Player.m_localPlayer) + ", paused="
                        + Game.IsPaused()
                        + ", timeScale="
                        + Time.timeScale
                );
            yield return null;
        }
    }

    private IEnumerator DemoRewind()
    {
        if (!audioDeck.Seek(0))
            throw new InvalidOperationException("Real demo Seek(0) refused");
        yield return AwaitAudio();
        if (!audioDeck.Source.isPlaying || audioDeck.Source.time > 2)
            throw new InvalidOperationException("Demo seek did not produce fresh playing PCM");
        lastCapture = new JObject
        {
            ["rewound"] = true,
            ["timeSamples"] = audioDeck.Source.timeSamples,
            ["playing"] = audioDeck.Source.isPlaying
        };
    }

    private IEnumerator DemoClear()
    {
        if (!demoSaved)
            yield break;
        try
        {
            if (audioDeck != null)
            {
                if (audioDeck.State.playing)
                    audioDeck.TogglePlayback();
                audioDeck.Source.Stop();
                audioDeck.State.volume = demoVolume;
                BoomboxItems.Persist(audioDeck);
            }
        }
        finally
        {
            demoMute?.Dispose();
            demoMute = null;
        }
        var settings = BoomboxPlugin.Instance.Settings;
        settings.DecorationMovement.Value = demoMovement;
        settings.DecorationMusicMovement.Value = demoMusicMovement;
        var player = Player.m_localPlayer;
        if (demoCape != null)
        {
            player.UnequipItem(demoCape, false);
            player.GetInventory().RemoveItem(demoCape);
            if (demoOldCape != null && !player.EquipItem(demoOldCape, false))
                throw new InvalidOperationException("Original cape restore refused");
        }
        float restoreDeadline = Time.realtimeSinceStartup + 4;
        while (player.IsTeleporting()
            || (float)AccessTools.Field(typeof(Player), "m_teleportCooldown").GetValue(player) < 2f)
        {
            if (Time.realtimeSinceStartup >= restoreDeadline)
                throw new InvalidOperationException("Native teleport cooldown did not settle for restore");
            yield return null;
        }
        if (!player.TeleportTo(demoOrigin, demoRotation, true))
            throw new InvalidOperationException("Demo position restore refused");
        yield return DemoWaitTeleport();
        demoCape = demoOldCape = null;
        demoSaved = false;
        lastCapture = new JObject
        {
            ["demoSceneRestored"] = true,
            ["volumeRestored"] = demoVolume,
            ["outerMuteClosed"] = demoMute == null
        };
    }

    private static bool RefineNativeShore(Vector3 coarse, Vector3 sea, out Vector3 coast)
    {
        float water = ZoneSystem.instance.m_waterLevel;
        for (float distance = 0; distance <= 120; distance += 1)
        {
            var p = coarse + sea * distance;
            float y = WorldGenerator.instance.GetHeight(p);
            if (y < water + .3f || y > water + 2 || WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Meadows)
                continue;
            if (WorldGenerator.instance.GetHeight(p + sea * 10) >= water
                || WorldGenerator.instance.GetHeight(p + sea * 20) >= water)
                continue;
            Vector3 across = Vector3.Cross(Vector3.up, sea);
            if (Mathf.Abs(WorldGenerator.instance.GetHeight(p + across) - y) > .5f
            || Mathf.Abs(WorldGenerator.instance.GetHeight(p - across) - y) > .5f)
                continue;
            coast = new Vector3(p.x, y + .1f, p.z);
            return true;
        }
        coast = default(Vector3);
        return false;
    }
}
