using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private void Execute(string operation, JObject request)
    {
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        if (RemoteOperation(operation, request))
            return;
        if (Point3Operation(operation, request))
            return;
        if (PerfOperation(operation, request))
            return;
        if (AuraMotionOperation(operation, request))
            return;
        if (AuraConfigOperation(operation, request))
            return;
        if (NativeMusicOperation(operation))
            return;
        if (AudioResetOperation(operation, request))
            return;
        if (AudioOperation(operation, request))
            return;
        if (LifecycleOperation(operation, request))
            return;
        if (RemainingOperation(operation, request))
            return;
        if (ListenOperation(operation, request))
            return;
        if (CaptureOperation(operation, request))
            return;
        if (UiOperation(operation, request))
            return;
        if (SafetyOperation(operation, request))
            return;
        if (Combat(operation, request))
            return;
        if (operation == "host")
        {
            plugin.Settings.Party.Radius.Value = 20f;
            foreach (var strength in plugin.Settings.Party.Strength)
                strength.Value = 1f;
            plugin.Settings.CassetteRedDamage.Value = plugin.Settings.CassettePurpleSpeed.Value = .05f;
            plugin.Settings.DecorationDamage.Value = plugin.Settings.DecorationSpeed.Value = .0025f;
            plugin.Settings.CassetteGreenHealth.Value =
                plugin.Settings.CassetteYellowStamina.Value =
                plugin.Settings.CassetteBlueEitr.Value =
                1f;
            plugin.Settings.DecorationHealth.Value =
                plugin.Settings.DecorationStamina.Value =
                plugin.Settings.DecorationEitr.Value =
                .1f;
            foreach (var recovery in new[] { plugin.Settings.Health, plugin.Settings.Stamina, plugin.Settings.Eitr })
            {
                recovery.Initial.Value = recovery.PerLevel.Value = 1f;
                recovery.Interval.Value = 5f;
            }
            plugin.Settings.MusicOnly.Value = false;
            return;
        }
        if (operation == "worldowner")
        {
            if (!ZNet.instance.IsServer())
                throw new InvalidOperationException("Native world ownership fixture requires server");
            var item = ZDOMan.instance.GetZDO(new ZDOID((long)request["uid"], (uint)request["object"]));
            if (item == null || item.GetPrefab() != BoomboxItemData.DevicePrefab.GetStableHashCode())
                throw new InvalidOperationException("Native fixture item missing");
            item.SetOwner((long)request["peer"]);
            return;
        }
        if (operation == "snapshot")
            return;
        if (player == null || !BoomboxItemPrefabs.Ready || !plugin.AudioWorldActive)
            throw new InvalidOperationException("Native player/product not ready");
        switch (operation)
        {
            case "prepare":
                Prepare(player);
                break;
            case "floor":
                Floor(ReadPoint(request["position"]));
                break;
            case "teleport":
                if (!player.TeleportTo(ReadPoint(request["position"]), player.transform.rotation, false))
                    throw new InvalidOperationException("Native TeleportTo refused fixture precondition");
                break;
            case "create":
                Create(
                    (string)request["key"],
                    (int[])((JArray)request["ranks"]).Select(t => (int)t).ToArray(),
                    (int)request["decorations"],
                    (bool)request["placed"],
                    ReadPoint(request["position"])
                );
                break;
            case "toggle":
                decks[(string)request["key"]].TogglePlayback();
                break;
            case "place":
                Place((string)request["key"], ReadPoint(request["position"]));
                break;
            case "moveitem":
                var drop = decks[(string)request["key"]].WorldDrop;
                if (drop == null || !drop.GetComponent<ZNetView>().IsOwner())
                    throw new InvalidOperationException("Expected native-owned world item");
                drop.transform.position = ReadPoint(request["position"]);
                BoomboxItems.Persist(decks[(string)request["key"]]);
                break;
            case "owner":
                var view = decks[(string)request["key"]].WorldDrop.GetComponent<ZNetView>();
                if (!view.IsOwner())
                    throw new InvalidOperationException("Ownership fixture sender is not native owner");
                view.GetZDO().SetOwner((long)request["peer"]);
                break;
            case "freeze":
                Freeze(player, true);
                break;
            case "unfreeze":
                Freeze(player, false);
                break;
            case "zeroeitr":
                player.ClearFood();
                AccessTools.Method(typeof(Player), "UpdateFood").Invoke(player, new object[] { 0f, true });
                break;
            case "food":
                Feed(player);
                break;
            case "wall":
                Wall(ReadPoint(request["position"]));
                break;
            case "clear":
                Clear();
                break;
            case "snapshot":
                break;
            default:
                throw new InvalidOperationException("Unknown gameplay fixture operation " + operation);
        }
    }

    private void Prepare(Player player, bool clear = true)
    {
        if (clear)
            Clear();
        player.SetIntro(false);
        player.SetGodMode(true);
        Feed(player);
        var control = ScriptableObject.CreateInstance<SE_Stats>();
        control.name = "BoomboxQa_NaturalRegenControl";
        control.m_hidden = true;
        control.m_healthRegenMultiplier = control.m_staminaRegenMultiplier = control.m_eitrRegenMultiplier = 0f;
        player.GetSEMan().AddStatusEffect(control, false, 0, 0, 0);
        controls.Add(control);
        var settings = BoomboxPlugin.Instance.Settings;
        settings.ShowVisualEffects.Value =
            settings.DecorationParticles.Value =
            settings.DecorationMovement.Value =
            settings.DecorationMusicMovement.Value =
            settings.MusicEnchantment.Value =
            settings.BoatHelp.Value =
            false;
        settings.MusicOnly.Value = false;
    }

    private static void Feed(Player player)
    {
        player.ClearFood();
        var foodPrefab = ObjectDB.instance.GetItemPrefab("YggdrasilPorridge");
        var food = foodPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
        food.m_dropPrefab = foodPrefab;
        if (!player.EatFood(food))
            throw new InvalidOperationException("Native fixture food rejected");
        AccessTools.Method(typeof(Player), "UpdateFood").Invoke(player, new object[] { 0f, true });
    }

    private void Create(string key, int[] ranks, int count, bool placed, Vector3 position)
    {
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        var item = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
        var payload = BoomboxItemData.Read(item);
        var state = payload.deck;
        item.m_customData["boombox.qa.fixture"] = key;
        state.upgradeTier = 1;
        state.owner = player.GetPlayerID();
        state.onBack = false;
        state.playing = false;
        var tapeItem = BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab);
        var tape = BoomboxItemData.Read(tapeItem).cassette;
        string[] properties = { "Red", "Green", "Yellow", "Blue", "Purple" };
        for (int i = 0; i < properties.Length; i++)
            if (ranks[i] > 0)
                tape.properties[properties[i]] = ranks[i];
        state.insertedCassette = tape;
        state.cassetteId = tape.id;
        state.tracks = new List<string> { BoomboxSyntheticTrackId.Rhythm(120) };
        state.unlockedDecorationSlots = Enumerable.Range(0, 24).ToList();
        state.unlockedDecorationPrefabs = new List<string> { "MushroomYellow" };
        string[] effects = { "Damage", "Health", "Stamina", "Eitr", "Speed" };
        for (int i = 0; i < 5; i++)
            for (int n = 0; n < count; n++)
                state.decorationPlacements.Add(
                    new BoomboxDecorationPlacement
                    {
                        slot = i * count + n,
                        prefab = "MushroomYellow",
                        effect = effects[i],
                        effectParticles = false
                    }
                );
        BoomboxItemData.Write(item, payload);
        if (!player.GetInventory().AddItem(item))
            throw new InvalidOperationException("Native fixture inventory rejected device");
        plugin.Items.Update();
        var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
            .GetValue(plugin);
        var deck = registry[state.id];
        decks[key] = deck;
        if (placed)
            Place(key, position);
        else
        {
            if (!player.EquipItem(item, false))
                throw new InvalidOperationException(
                    "Native EquipItem rejected fixture: attack=" + player.InAttack() + ", dodge=" + player.InDodge()
                        + ", dead="
                        + player.IsDead()
                        + ", swimming="
                        + player.IsSwimming()
                        + ", ground="
                        + player.IsOnGround()
                        + ", teleport="
                        + player.IsTeleporting()
                );
            plugin.Items.Update();
        }
        deck.TogglePlayback();
    }

    private void Place(string key, Vector3 position)
    {
        var deck = decks[key];
        if (!BoomboxPlugin.Instance.Items.PutDown(deck, position, Quaternion.identity, null))
            throw new InvalidOperationException("Product native PutDown rejected fixture");
        var drop = deck.WorldDrop;
        drop.GetComponent<Rigidbody>().isKinematic = true;
    }

    private void Floor(Vector3 position)
    {
        var floor = new GameObject("Boombox QA fixed numeric floor");
        floor.transform.position = position + Vector3.down;
        floor.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);
        fixtures.Add(floor);
    }

    private void Wall(Vector3 position)
    {
        var wall = new GameObject("Boombox QA line of sight wall");
        wall.transform.position = position + Vector3.up * 2;
        wall.AddComponent<BoxCollider>().size = new Vector3(.5f, 8f, 20f);
        fixtures.Add(wall);
    }

    private static void Freeze(Player player, bool frozen)
    {
        var body = player.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.constraints = frozen ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.FreezeRotation;
    }

    private void Clear()
    {
        var player = Player.m_localPlayer;
        if (player != null && BoomboxPlugin.Instance != null)
        {
            var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
                .GetValue(BoomboxPlugin.Instance);
            foreach (var deck in registry.Values.ToArray())
                if (deck != null && deck.Item != null && deck.Item.m_customData.ContainsKey("boombox.qa.fixture"))
                    decks[deck.State.id] = deck;
        }
        foreach (var deck in decks.Values.Distinct().ToArray())
            if (deck != null)
            {
                if (deck.State.playing)
                    deck.TogglePlayback();
                if (deck.WorldDrop != null)
                {
                    var view = deck.WorldDrop.GetComponent<ZNetView>();
                    if (view != null && view.IsValid())
                    {
                        view.ClaimOwnership();
                        if (view.IsOwner())
                            ZNetScene.instance.Destroy(deck.WorldDrop.gameObject);
                    }
                }
                else if (player != null && deck.Item != null)
                {
                    player.UnequipItem(deck.Item, false);
                    player.GetInventory().RemoveItem(deck.Item);
                }
            }
        decks.Clear();
    }
}
