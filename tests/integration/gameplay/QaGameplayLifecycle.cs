using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private string lifeId,
        lifeTape,
        lifeCharacter,
        lifeWatch,
        lifeActionUtc,
        lifeActionName,
        lifeDeathAt,
        lifeReturnAt,
        lifeRemovedAt,
        lifeActiveAt,
        lifeDisconnectAt;
    private long lifeOwner, lifePeer;
    private float lifeNext;
    private readonly List<JObject> lifeSamples = new List<JObject>();
    private int lifeTeleportWaves;
    private int[] lifeTeleportImpulses;
    private bool LifecycleOperation(string operation, JObject request)
    {
        if (!operation.StartsWith("life-", StringComparison.Ordinal))
            return false;
        var player = Player.m_localPlayer;
        if (operation == "life-track" || operation == "life-watch")
        {
            lifeId = (string)request["source"];
            lifeTape = (string)request["cassette"];
            lifeOwner = (long)request["owner"];
            lifePeer = (long?)request["peer"] ?? 0;
            if (operation == "life-watch")
            {
                lifeWatch = (string)request["phase"];
                lifeSamples.Clear();
                lifeRemovedAt = lifeActiveAt = lifeDisconnectAt = null;
            }
            return true;
        }
        if (operation == "life-snapshot")
            return true;
        if (player == null || BoomboxPlugin.Instance == null || !BoomboxPlugin.Instance.AudioWorldActive)
            throw new InvalidOperationException("Native lifecycle player/product not ready");
        if (operation == "life-create")
        {
            Create("LIFE", new[] { 1, 1, 1, 1, 1 }, 0, false, player.transform.position);
            var created = decks["LIFE"];
            lifeId = created.State.id;
            lifeTape = created.State.cassetteId;
            lifeOwner = player.GetPlayerID();
            lifeCharacter = player.GetZDOID().ToString();
            lifeDeathAt = lifeReturnAt = null;
        }
        else if (operation == "life-pause" || operation == "life-resume")
        {
            var deck = LifeDeck();
            if (deck.State.playing == (operation == "life-pause"))
                deck.TogglePlayback();
        }
        else if (operation == "life-unequip")
            player.UnequipItem(LifeDeck().Item, false);
        else if (operation == "life-equip")
        {
            var item = player.GetInventory().GetAllItems().Single(
                i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == lifeId
            );
            if (!player.EquipItem(item, false))
                throw new InvalidOperationException("Ordinary native EquipItem refused recovered apparatus");
        }
        else if (operation == "life-death")
        {
            player.SetGodMode(false);
            var hit = new HitData
            {
                m_point = player.GetCenterPoint(),
                m_pushForce = 0,
                m_hitType = HitData.HitType.Self
            };
            hit.m_damage.m_damage = 10000f;
            player.Damage(hit);
        }
        else if (operation == "life-loot")
        {
            var grave = UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None).Single(
                t => t.GetComponent<Container>().GetInventory().GetAllItems().Any(
                    i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == lifeId
                )
            );
            if (!grave.Interact(player, false, false))
                throw new InvalidOperationException("Native TombStone.Interact rejected loot");
        }
        else if (operation == "life-teleport")
        {
            if (!player.TeleportTo(ReadPoint(request["position"]), player.transform.rotation, false))
                throw new InvalidOperationException("Native lifecycle TeleportTo refused");
            var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
                .GetValue(BoomboxPlugin.Instance);
            lifeTeleportWaves = registry.Values.FirstOrDefault(d => d != null
                && d.State.id == lifeId)?.EmittedWaves
                ?? 0;
            lifeTeleportImpulses = UnityEngine.Object.FindObjectsByType<BassWave>(FindObjectsSortMode.None)
                .Select(w => w.GetInstanceID())
                .ToArray();
        }
        else if (operation == "life-real-audio")
        {
            var plugin = BoomboxPlugin.Instance;
            plugin.FFmpegPath.Value = BoomboxQa.QaEnvironment.Encoder;
            plugin.FFprobePath.Value = BoomboxQa.QaEnvironment.MediaProbe;
            string id = "qa-r07-audio";
            plugin.MusicLibrary.Remember(
                new MusicTrack
                {
                    id = id,
                    source = "file://"
                        + Path.Combine(BoomboxQa.QaEnvironment.FixturesRoot, "audio/Музыка с пробелами/блоки.wv"),
                    title = "R07 actual audio",
                    available = true
                }
            );
            var deck = LifeDeck();
            deck.State.volume = .05f;
            plugin.PlayQueue(deck, new[] { id }, null, 0);
        }
        else if (operation == "life-place")
        {
            LifeDeck();
            Place("LIFE", ReadPoint(request["position"]));
        }
        else if (operation == "life-clean-placed")
        {
            foreach (var drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(
                d => BoomboxItemData.IsDevice(d.m_itemData) && BoomboxItemData.Read(d.m_itemData).deck.id == lifeId
            ).ToArray())
            {
                var view = drop.GetComponent<ZNetView>();
                view.ClaimOwnership();
                if (!view.IsOwner())
                    throw new InvalidOperationException("Owned QA item cleanup claim failed");
                ZNetScene.instance.Destroy(drop.gameObject);
            }
        }
        else
            throw new InvalidOperationException("Unknown lifecycle operation " + operation);
        lifeActionUtc = DateTime.UtcNow.ToString("O");
        lifeActionName = operation;
        return true;
    }
    private AudioDeck LifeDeck()
    {
        var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
            .GetValue(BoomboxPlugin.Instance);
        var deck = registry.Values.Single(d => d != null && d.State.id == lifeId);
        decks["LIFE"] = deck;
        return deck;
    }
    private void LifecycleTick()
    {
        if (lifeId == null || Time.realtimeSinceStartup < lifeNext)
            return;
        lifeNext = Time.realtimeSinceStartup + .1f;
        var player = Player.m_localPlayer;
        if (player != null && player.GetPlayerID() == lifeOwner)
        {
            if (player.IsDead() && lifeDeathAt == null)
                lifeDeathAt = DateTime.UtcNow.ToString("O");
            if (lifeDeathAt != null
                && !player.IsDead()
                && player.GetZDOID().ToString() != lifeCharacter
                && lifeReturnAt == null)
                lifeReturnAt = DateTime.UtcNow.ToString("O");
        }
        if (lifeWatch == null)
            return;
        var state = LifeLight();
        if ((int)state["activeSources"] == 0
            && ((JArray)state["values"]).All(v => (float)v == 0f)
            && (int)state["effects"] == 0
            && (int)state["hudIcons"] == 0
            && lifeRemovedAt == null)
            lifeRemovedAt = (string)state["utc"];
        if ((int)state["activeSources"] == 1
            && (int)state["effects"] == 5
            && (int)state["hudIcons"] == 5
            && lifeActiveAt == null)
            lifeActiveAt = (string)state["utc"];
        if (role == "server" && lifePeer != 0 && !ZNet.instance.GetPeers().Any(p => p.m_uid == lifePeer)
        && lifeDisconnectAt == null)
            lifeDisconnectAt = (string)state["utc"];
        if (lifeSamples.Count == 256)
            lifeSamples.RemoveAt(0);
        lifeSamples.Add(state);
    }
    private JObject LifeLight()
    {
        var player = Player.m_localPlayer;
        var aggregate = player != null ? BoomboxAuraNetwork.For(player) : new BoomboxAuraAggregate();
        var effects = player?.GetSEMan().GetStatusEffects().OfType<BoomboxAuraStatus>().ToArray()
            ?? Array.Empty<BoomboxAuraStatus>();
        int iconCount = AuraHudRows().Count;
        var remote = BoomboxRemoteNetwork.Current?.Views.Where(v => v.SourceId == lifeId).ToArray()
            ?? Array.Empty<BoomboxRemoteView>();
        var registry = BoomboxPlugin.Instance == null
            ? null
            : (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
                .GetValue(BoomboxPlugin.Instance);
        var local = registry?.Values.FirstOrDefault(d => d != null && d.State.id == lifeId);
        return new JObject
        {
            ["utc"] = DateTime.UtcNow.ToString("O"),
            ["dead"] = player != null && player.IsDead(),
            ["teleporting"] = player != null && player.IsTeleporting(),
            ["character"] = player?.GetZDOID().ToString(),
            ["activeSources"] = BoomboxAuraNetwork.Sources.Count(s => s.Id == lifeId && s.Active),
            ["sourceCount"] = BoomboxAuraNetwork.Sources.Count(s => s.Id == lifeId),
            ["values"] = new JArray(aggregate.Values),
            ["effects"] = effects.Length,
            ["hudIcons"] = iconCount,
            ["tooltips"] = new JArray(effects.Select(e => e.GetTooltipString())),
            ["remotePlaying"] = remote.Count(v => v.Playing),
            ["remoteEvents"] = remote.Sum(v => v.PresentedEvents),
            ["localWaves"] = local?.EmittedWaves ?? 0,
            ["localMotion"] = local?.VisualStrength ?? 0f,
            ["worldImpulseIds"] = new JArray(
                UnityEngine.Object.FindObjectsByType<BassWave>(FindObjectsSortMode.None).Select(w => w.GetInstanceID())
            ),
            ["audioPlaying"] = local?.Source?.isPlaying ?? false,
            ["audioFrame"] = local?.Source?.clip != null ? local.Source.timeSamples : -1
        };
    }
    private JObject LifecycleSnapshot()
    {
        if (lifeId == null)
            return null;
        var entries = new JArray();
        Action<string, ItemDrop.ItemData, string> add = (location, item, native) =>
        {
            if (!BoomboxItemData.IsDevice(item))
                return;
            var state = BoomboxItemData.Read(item).deck;
            if (state.id != lifeId)
                return;
            entries.Add(
                new JObject
                {
                    ["location"] = location,
                    ["native"] = native,
                    ["id"] = state.id,
                    ["cassette"] = state.insertedCassette?.id,
                    ["cassetteId"] = state.cassetteId,
                    ["ranks"] = new JArray(
                    new[] { "Red", "Green", "Yellow", "Blue", "Purple" }.Select(
                        c => state.insertedCassette?.Rank(c) ?? 0
                    )
                ),
                    ["tier"] = state.upgradeTier,
                    ["slots"] = state.unlockedDecorationSlots.Count,
                    ["equipped"] = item.m_equipped
                }
            );
        };
        var player = Player.m_localPlayer;
        if (player != null)
            foreach (var item in player.GetInventory().GetAllItems())
                add("inventory", item, player.GetZDOID().ToString());
        foreach (var grave in UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None))
            foreach (var item in grave.GetComponent<Container>().GetInventory().GetAllItems())
                add("grave", item, grave.GetComponent<ZNetView>()?.GetZDO()?.m_uid.ToString());
        foreach (var drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            if (drop.GetComponent<ZNetView>()?.IsValid() == true)
                add("world", drop.m_itemData, drop.GetComponent<ZNetView>().GetZDO().m_uid.ToString());
        var equipment = (Dictionary<Humanoid, ItemDrop.ItemData>)AccessTools.Field(typeof(BoomboxEquipment), "equipped")
            .GetValue(null);
        var matching = equipment.Where(p => BoomboxItemData.IsDevice(p.Value)
            && BoomboxItemData.Read(p.Value).deck.id == lifeId).ToArray();
        return new JObject
        {
            ["source"] = lifeId,
            ["owner"] = lifeOwner,
            ["cassette"] = lifeTape,
            ["action"] = lifeActionName,
            ["actionUtc"] = lifeActionUtc,
            ["deathAt"] = lifeDeathAt,
            ["returnAt"] = lifeReturnAt,
            ["removedAt"] = lifeRemovedAt,
            ["activeAt"] = lifeActiveAt,
            ["disconnectAt"] = lifeDisconnectAt,
            ["teleportWaves"] = lifeTeleportWaves,
            ["teleportImpulseIds"] = lifeTeleportImpulses == null ? new JArray() : new JArray(lifeTeleportImpulses),
            ["state"] = LifeLight(),
            ["items"] = entries,
            ["equipmentMappings"] = matching.Length,
            ["destroyedEquipmentOwners"] = matching.Count(p => p.Key == null),
            ["equipmentHumanoids"] = new JArray(matching.Select(p => p.Key != null ? p.Key.GetInstanceID() : 0)),
            ["samples"] = new JArray(lifeSamples.Select(s => s.DeepClone()))
        };
    }
}
