using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private string remainingId;
    private Vector3 remainingCoast, remainingSea;
    private ZDOID remainingChestId;
    private readonly List<GameObject> remainingRoots = new List<GameObject>();
    private bool RemainingOperation(string operation, JObject request)
    {
        if (!operation.StartsWith("remaining-", StringComparison.Ordinal))
            return false;
        if (RemainingOwnOperation(operation, request))
            return true;
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        if (operation == "remaining-scene")
        {
            busy = true;
            StartCoroutine(GuardAudio(RemainingScene()));
            return true;
        }
        if (operation == "remaining-track")
        {
            remainingId = lifeId = (string)request["source"];
            lifeOwner = (long)request["owner"];
            if (request["coast"] != null)
            {
                remainingCoast = ReadPoint(request["coast"]);
                remainingSea = ReadPoint(request["sea"]);
            }
            return true;
        }
        if (operation == "remaining-snapshot")
            return true;
        if (operation == "remaining-prepare")
        {
            Prepare(player, false);
            return true;
        }
        if (operation == "remaining-synthetic")
        {
            plugin.PlayQueue(RemainingDeck(), new[] { BoomboxSyntheticTrackId.Rhythm(120) }, null, 0);
            return true;
        }
        if (operation == "remaining-create")
        {
            Create("P07", new[] { 2, 3, 4, 5, 1 }, 0, false, player.transform.position);
            var deck = decks["P07"];
            remainingId = lifeId = deck.State.id;
            lifeTape = deck.State.cassetteId;
            lifeOwner = player.GetPlayerID();
            deck.State.upgradeTier = 2;
            deck.State.unlockedDecorationSlots = Enumerable.Range(0, 24).ToList();
            deck.State.unlockedDecorationPrefabs = new List<string> { "MushroomYellow", "TrophySkeleton" };
            var eligible = AccessTools.Method(
                typeof(BoomboxPlugin).Assembly.GetType("VikingBoombox.BoomboxDecorationCatalog"),
                "DataEligible"
            );
            for (int slot = 0; slot < 24; slot++)
            {
                string prefab = slot < 17 ? "MushroomYellow" : "TrophySkeleton";
                if (!(bool)eligible.Invoke(null, new object[] { prefab, slot }))
                    throw new InvalidOperationException("Known native P07 decoration rejected: " + slot + "/" + prefab);
                deck.State.decorationPlacements.Add(
                    new BoomboxDecorationPlacement
                    {
                        slot = slot,
                        prefab = prefab,
                        effect = "Health",
                        effectParticles = false
                    }
                );
            }
            deck.VisualPresenter.RefreshState(true);
            plugin.Save();
            return true;
        }
        if (operation == "remaining-local-ref")
        {
            string root = Environment.GetEnvironmentVariable("BOOMBOX_QA_ROOT"), file = Path.Combine(
                root,
                "current/point3-private-audio",
                role + ".wav"
            );
            if (!File.Exists(file))
                throw new InvalidOperationException("Existing owned P03 audio fixture absent");
            plugin.FFmpegPath.Value = BoomboxQa.QaEnvironment.Encoder;
            plugin.FFprobePath.Value = BoomboxQa.QaEnvironment.MediaProbe;
            plugin.MusicLibrary.Remember(
                new MusicTrack
                {
                    id = "qa-point3-local-" + role,
                    source = "file://" + file,
                    title = "Private owned tone",
                    available = true
                }
            );
            plugin.MusicLibrary.Save();
            if ((bool?)request["play"] == true)
                plugin.PlayQueue(RemainingDeck(), new[] { "qa-point3-local-" + role }, "qa-p07-local-" + role, 0);
            return true;
        }
        if (operation == "remaining-place")
        {
            decks["P07"] = RemainingDeck();
            Place("P07", player.transform.position + player.transform.forward * 1.5f);
            return true;
        }
        if (operation == "remaining-pickup")
        {
            var drop = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Single(
                d => BoomboxItemData.IsDevice(d.m_itemData) && BoomboxItemData.Read(d.m_itemData).deck.id == remainingId
            );
            drop.Pickup(player);
            return true;
        }
        if (operation == "remaining-equip")
        {
            var item = player.GetInventory().GetAllItems().Single(
                i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == remainingId
            );
            if (BoomboxEquipment.Get(player) != item && !player.EquipItem(item, false))
                throw new InvalidOperationException("Native transferred item equip refused");
            plugin.Items.Update();
            decks["P07"] = RemainingDeck();
            return true;
        }
        if (operation == "remaining-store")
        {
            player.StopDoodadControl();
            player.AttachStop();
            Freeze(player, true);
            var shore = remainingCoast + Vector3.Cross(Vector3.up, remainingSea) * 4;
            if (!ZoneSystem.instance.GetGroundHeight(shore, out float ground))
                throw new InvalidOperationException("Native chest shore not loaded");
            shore.y = ground + .1f;
            var playerBody = player.GetComponent<Rigidbody>();
            playerBody.position = player.transform.position = shore;
            Physics.SyncTransforms();
            var item = RemainingDeck().Item;
            player.UnequipItem(item, false);
            BoomboxItems.Persist(RemainingDeck());
            var prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            if (prefab == null || prefab.GetComponent<Container>() == null)
                throw new InvalidOperationException("Verified native chest prefab absent");
            var chestPoint = shore + Vector3.Cross(Vector3.up, remainingSea) * 2;
            if (ZoneSystem.instance.GetGroundHeight(chestPoint, out float chestGround))
                chestPoint.y = chestGround + .1f;
            var chest = UnityEngine.Object.Instantiate(prefab, chestPoint, Quaternion.identity);
            var view = chest.GetComponent<ZNetView>();
            if (!view.IsOwner())
                throw new InvalidOperationException("Native B chest ownership missing");
            remainingChestId = view.GetZDO().m_uid;
            view.GetZDO().Set("boombox.qa.remaining", remainingId);
            chest.GetComponent<Container>().GetInventory().MoveItemToThis(player.GetInventory(), item);
            plugin.Save();
            return true;
        }
        if (operation == "remaining-chest")
        {
            var chest = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Single(
                c => c.GetComponent<ZNetView>()?.IsValid() == true
                    && c.GetComponent<ZNetView>().GetZDO().GetString("boombox.qa.remaining", "") == remainingId
                    && c.GetInventory().GetAllItems().Any(
                    i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == remainingId
                )
            );
            remainingChestId = chest.GetComponent<ZNetView>().GetZDO().m_uid;
            return true;
        }
        if (operation == "remaining-chest-owner")
        {
            var zdo = ZDOMan.instance.GetZDO(new ZDOID((long)request["uid"], (uint)request["object"]));
            if (!ZNet.instance.IsServer()
                || zdo == null
                || zdo.GetString("boombox.qa.remaining", "") != (string)request["source"])
                throw new InvalidOperationException("Owned marked QA chest required");
            zdo.SetOwner((long)request["peer"]);
            return true;
        }
        if (operation == "remaining-take")
        {
            var chest = ZNetScene.instance.FindInstance(remainingChestId)?.GetComponent<Container>();
            if (chest == null || !chest.GetComponent<ZNetView>().IsOwner())
                throw new InvalidOperationException("Native stored chest owner not ready");
            var item = chest.GetInventory().GetAllItems().Single(
                i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == remainingId
            );
            player.GetInventory().MoveItemToThis(chest.GetInventory(), item);
            if (chest.GetInventory().GetAllItems().Count == 0)
                ZNetScene.instance.Destroy(chest.gameObject);
            return true;
        }
        if (operation == "remaining-clean")
        {
            player.StopDoodadControl();
            player.AttachStop();
            Clear();
            foreach (var root in remainingRoots)
                if (root != null && root.GetComponent<ZNetView>()?.IsOwner() == true)
                    ZNetScene.instance.Destroy(root);
            var chest = ZNetScene.instance.FindInstance(remainingChestId);
            if (chest != null && chest.GetComponent<ZNetView>().IsOwner())
                ZNetScene.instance.Destroy(chest);
            foreach (var item in player.GetInventory()
                .GetAllItems()
                .Where(i => i.m_customData.ContainsKey("boombox.qa.remaining.weapon"))
                .ToArray())
            {
                player.UnequipItem(item, false);
                player.GetInventory().RemoveItem(item);
            }
            return true;
        }
        throw new InvalidOperationException("Unknown P07 operation: " + operation);
    }
    private IEnumerator RemainingScene()
    {
        var player = Player.m_localPlayer;
        remainingCoast = FindNativeShore(player.transform.position, out remainingSea);
        if (!player.TeleportTo(remainingCoast, Quaternion.LookRotation(remainingSea), true))
            throw new InvalidOperationException("Native P07 coast teleport rejected");
        float deadline = Time.realtimeSinceStartup + 60;
        while (player.IsTeleporting())
        {
            if (Time.realtimeSinceStartup > deadline)
                throw new TimeoutException("P07 coast loading");
            yield return null;
        }
        player.SetGodMode(true);
        Freeze(player, true);
        sample = new JObject
        {
            ["coast"] = Point(player.transform.position),
            ["sea"] = Point(remainingSea),
            ["water"] = ZoneSystem.instance.m_waterLevel
        };
        busy = false;
    }
    private AudioDeck RemainingDeck()
    {
        var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
            .GetValue(BoomboxPlugin.Instance);
        return registry.Values.Single(d => d != null && d.State.id == remainingId);
    }
    private JObject RemainingSnapshot()
    {
        if (remainingId == null || ZNetScene.instance == null)
            return null;
        var player = Player.m_localPlayer;
        var entries = new JArray();
        Action<string, ItemDrop.ItemData, string> add = (where, item, native) =>
        {
            if (!BoomboxItemData.IsDevice(item))
                return;
            var state = BoomboxItemData.Read(item).deck;
            if (state.id != remainingId)
                return;
            entries.Add(
                new JObject
                {
                    ["location"] = where,
                    ["native"] = native,
                    ["id"] = state.id,
                    ["owner"] = state.owner,
                    ["cassette"] = state.cassetteId,
                    ["tier"] = state.upgradeTier,
                    ["ranks"] = new JArray(
                    new[] { "Red", "Green", "Yellow", "Blue", "Purple" }.Select(
                        c => state.insertedCassette?.Rank(c) ?? 0
                    )
                ),
                    ["slots"] = new JArray(state.unlockedDecorationSlots),
                    ["placements"] = JArray.FromObject(state.decorationPlacements),
                    ["tracks"] = new JArray(state.tracks),
                    ["playlist"] = state.playlistId,
                    ["equipped"] = item.m_equipped
                }
            );
        };
        if (player != null)
            foreach (var item in player.GetInventory().GetAllItems())
                add("inventory", item, player.GetZDOID().ToString());
        foreach (var drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            if (drop.GetComponent<ZNetView>()?.IsValid() == true)
                add("world", drop.m_itemData, drop.GetComponent<ZNetView>().GetZDO().m_uid.ToString());
        foreach (var chest in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            foreach (var item in chest.GetInventory().GetAllItems())
                add("chest", item, chest.GetComponent<ZNetView>()?.GetZDO()?.m_uid.ToString());
        var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
            .GetValue(BoomboxPlugin.Instance);
        var deck = registry.Values.FirstOrDefault(d => d != null && d.State.id == remainingId);
        string other = role == "client-a" ? "client-b" : "client-a";
        return new JObject
        {
            ["source"] = remainingId,
            ["chestId"] = remainingChestId.ToString(),
            ["chestOwner"] = ZDOMan.instance.GetZDO(remainingChestId)?.GetOwner(),
            ["items"] = entries,
            ["ownTrackKnown"] = BoomboxPlugin.Instance.FindTrack("qa-point3-local-" + role) != null,
            ["otherTrackKnown"] = BoomboxPlugin.Instance.FindTrack("qa-point3-local-" + other) != null,
            ["decks"] = registry.Values.Count(d => d != null && d.State.id == remainingId),
            ["deckOwner"] = deck?.State.owner,
            ["storagePaused"] = deck?.StoragePaused,
            ["playing"] = deck?.IsActivePlayback ?? false,
            ["queue"] = deck == null ? null : new JArray(deck.State.tracks),
            ["foreignAudio"] = registry.Values.Count(
                d => d != null && player != null && d.State.owner != player.GetPlayerID() && d.Source != null
            ),
            ["own"] = RemainingOwnSnapshot()
        };
    }
}
