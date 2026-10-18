using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static string JourneyCheckpoint => Path.Combine(BepInEx.Paths.ConfigPath, "qa-L08-checkpoint.json");
    private static void JourneyClick(RectTransform rect, JArray steps)
    {
        var click = UiDispatch(rect, false);
        steps.Add(click);
        Need((bool)click["hitsTarget"], "L08 pointer missed " + rect.name);
    }
    private static int JourneyCount(
        string prefab
    ) => Player.m_localPlayer.GetInventory().GetAllItems().Where(
        i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab
    ).Sum(
        i => i.m_stack
    );
    private static void JourneyGive(string prefab, int amount)
    {
        var source = ObjectDB.instance.GetItemPrefab(prefab);
        var item = source.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_dropPrefab = source;
        item.m_stack = amount;
        Need(Player.m_localPlayer.GetInventory().AddItem(item), "L08 raw material inventory full: " + prefab);
        Player.m_localPlayer.AddKnownItem(item);
    }
    private static GameObject JourneyRow(Recipe recipe, string id = null)
    {
        var rows = (IEnumerable)AccessTools.Field(typeof(InventoryGui), "m_availableRecipes")
            .GetValue(InventoryGui.instance);
        foreach (var row in rows)
        {
            if ((Recipe)AccessTools.Property(row.GetType(), "Recipe").GetValue(row, null) != recipe)
                continue;
            var item = (ItemDrop.ItemData)AccessTools.Property(row.GetType(), "ItemData").GetValue(row, null);
            if (id != null && (item == null || BoomboxItemData.Read(item).deck.id != id))
                continue;
            return (GameObject)AccessTools.Property(row.GetType(), "InterfaceElement").GetValue(row, null);
        }
        return null;
    }
    private IEnumerator JourneyCraft(Recipe recipe, JArray steps, string id = null)
    {
        foreach (var cost in recipe.m_resources)
            JourneyGive(cost.m_resItem.gameObject.name, cost.GetAmount(id == null ? 1 : 2));
        yield return TransportWait(() => JourneyRow(recipe, id) != null, "L08 recipe row missing: " + recipe.name, 4);
        var row = JourneyRow(recipe, id);
        InventoryGui.instance.m_recipeEnsureVisible.CenterOnItem((RectTransform)row.transform);
        yield return new WaitForSecondsRealtime(.3f);
        JourneyClick((RectTransform)row.transform, steps);
        yield return new WaitForSecondsRealtime(.2f);
        Need(InventoryGui.instance.m_craftButton.interactable, "L08 crafting unavailable: " + recipe.name);
        var before = recipe.m_resources.ToDictionary(
            r => r.m_resItem.gameObject.name,
            r => JourneyCount(r.m_resItem.gameObject.name)
        );
        var items = Player.m_localPlayer.GetInventory().GetAllItems().ToArray();
        JourneyClick((RectTransform)InventoryGui.instance.m_craftButton.transform, steps);
        yield return TransportWait(
            () => id == null
                ? Player.m_localPlayer.GetInventory().GetAllItems().Any(
                i => !items.Contains(i) && i.m_dropPrefab?.name == recipe.m_item.gameObject.name
            )
                : MigratedInventory(id).Any(i => BoomboxItemData.Read(i).deck.upgradeTier == 2),
            "L08 paid craft failed: " + recipe.name,
            8
        );
        foreach (var cost in recipe.m_resources)
            Need(
                JourneyCount(cost.m_resItem.gameObject.name) == before[cost.m_resItem.gameObject.name] - cost.GetAmount(
                    id == null
                        ? 1
                        : 2
                ),
                "L08 wrong spend: " + cost.m_resItem.gameObject.name
            );
        steps.Add(new JObject { ["recipe"] = recipe.name, ["paid"] = true });
    }
    private static CraftingStation JourneyStation(string prefab, Vector3 origin, List<GameObject> roots)
    {
        var root = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(prefab), origin, Quaternion.identity);
        roots.Add(root);
        var station = root.GetComponent<CraftingStation>();
        var roof = station.m_roofCheckPoint.position;
        var positions = new[] {
            roof + Vector3.up,
            roof + Vector3.right * 3,
            roof + Vector3.left * 3,
            roof + Vector3.forward * 3,
            roof + Vector3.back * 3,
            origin - Vector3.up * .2f
        };
        var sizes = new[] {
            new Vector3(6, .25f, 6),
            new Vector3(.25f, 4, 6),
            new Vector3(.25f, 4, 6),
            new Vector3(6, 4, .25f),
            new Vector3(6, 4, .25f),
            new Vector3(20, .4f, 20)
        };
        for (int n = 0; n < positions.Length; n++)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roots.Add(piece);
            piece.layer = LayerMask.NameToLayer("piece");
            piece.transform.position = positions[n];
            piece.transform.localScale = sizes[n];
            piece.GetComponent<Renderer>().enabled = false;
        }
        Physics.SyncTransforms();
        return station;
    }
    private IEnumerator JourneyOpenStation(CraftingStation station, JArray steps)
    {
        var player = Player.m_localPlayer;
        CasePlatformPlayer(
            player,
            station.transform.position + Vector3.forward * 1.5f,
            Quaternion.LookRotation(Vector3.back)
        );
        player.GetComponent<ZSyncTransform>().SyncNow();
        GameObject hover = null;
        for (int n = 0; n < 15; n++)
        {
            var aim = station.transform.position + Vector3.up * (.2f + n * .1f) - GameCamera.instance.transform.position;
            player.SetLookDir(aim, 0);
            AccessTools.Field(typeof(Player), "m_lookPitch").SetValue(
                player,
                Mathf.Atan2(-aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg
            );
            yield return new WaitForSecondsRealtime(.1f);
            hover = player.GetHoverObject();
            if (hover != null && hover.GetComponentInParent<CraftingStation>() == station)
                break;
        }
        Need(hover != null && hover.GetComponentInParent<CraftingStation>() == station, "L08 station hover missing");
        Cover.GetCoverForPoint(station.m_roofCheckPoint.position, out float cover, out bool roof);
        steps.Add(new JObject
        {
            ["stationReady"] = station.name,
            ["usable"] = station.CheckUsable(player, false),
            ["inRange"] = station.InUseDistance(player),
            ["cover"] = cover,
            ["roof"] = roof,
            ["cutscene"] = player.InCutscene(),
            ["keyboardEnabled"] = Keyboard.current.enabled,
            ["chatFocus"] = Chat.instance != null && Chat.instance.HasFocus()
        });
        Need(
            station.CheckUsable(player, false) && station.InUseDistance(player) && !player.InCutscene(),
            "L08 prepared station/actor is not usable"
        );
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        yield return TransportWait(
            () => InventoryGui.IsVisible() && player.GetCurrentCraftingStation() == station,
            "L08 E did not open station",
            3
        );
        yield return new WaitForSecondsRealtime(.3f);
        steps.Add(new JObject { ["stationViaE"] = station.name });
    }
    private IEnumerator CaseJourney(bool save)
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var input = InputSystem.settings.backgroundBehavior;
        var position = player.transform.position;
        var rotation = player.transform.rotation;
        var distance = AccessTools.Field(typeof(GameCamera), "m_distance");
        float oldDistance = (float)distance.GetValue(GameCamera.instance);
        var roots = new List<GameObject>();
        var steps = new JArray();
        var frames = new JArray();
        uiClicks = steps;
        var proof =
            new JObject
            {
                ["case"] = "L-08",
                ["phase"] = save ? "save" : "reload",
                ["steps"] = steps,
                ["frames"] = frames,
                ["scope"] =
                    "One device/tape: native E station, paid craft, RMB insertion/equip, UI playlist/play, paid cassette and device upgrades, native drop/E pickup, actual character save/new process. Only raw materials and roofed stations are fixtures."
            };
        lastCapture = proof;
        JObject checkpoint = null;
        bool success = false;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            UiParkWindow();
            Directory.CreateDirectory(Path.Combine(BepInEx.Paths.ConfigPath, "capture"));
            if (!Keyboard.current.enabled)
                InputSystem.EnableDevice(Keyboard.current);
            if (Mouse.current != null && !Mouse.current.enabled)
                InputSystem.EnableDevice(Mouse.current);
            yield return null;
            yield return null;
            var buttons = (IDictionary)AccessTools.Field(typeof(ZInput), "m_buttons").GetValue(
                AccessTools.Field(typeof(ZInput), "m_instance").GetValue(null)
            );
            var use = buttons["Use"];
            var useAction = (InputAction)AccessTools.Property(use.GetType(), "ButtonAction").GetValue(use, null);
            var boundKeyboard = useAction.controls.Select(c => c.device).OfType<Keyboard>().FirstOrDefault();
            boundKeyboard?.MakeCurrent();
            if (boundKeyboard != null && !boundKeyboard.enabled)
                InputSystem.EnableDevice(boundKeyboard);
            proof["useInput"] = UiAction(useAction);
            if (!save)
            {
                checkpoint = JObject.Parse(File.ReadAllText(JourneyCheckpoint));
                Need((string)checkpoint["sha"] == productSha, "L08 candidate changed");
                var copies = MigratedInventory((string)checkpoint["id"]);
                Need(copies.Length == 1, "L08 saved device missing/duplicated");
                Need(
                    JToken.DeepEquals(checkpoint["item"], JToken.Parse(CaseStable(copies[0]).ToString())),
                    "L08 saved identity/cassette/music changed"
                );
                proof["restoredItem"] = CaseStable(copies[0]);
                proof["passed"] = true;
                success = true;
                yield break;
            }
            yield return CaseReadyInventory();
            Need(
                !player.NoCostCheat() && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost),
                "L08 free craft is enabled"
            );
            var package = new ZPackage();
            player.GetInventory().Save(package);
            checkpoint = new JObject
            {
                ["sha"] = productSha,
                ["inventory"] = Convert.ToBase64String(package.GetArray()),
                ["playlists"] = JArray.FromObject(plugin.MusicLibrary.playlists)
            };
            var origin = position + Vector3.up * 35;
            var bench = JourneyStation("piece_workbench", origin, roots);
            distance.SetValue(GameCamera.instance, 0f);
            yield return JourneyOpenStation(bench, steps);
            var before = player.GetInventory().GetAllItems().ToArray();
            yield return JourneyCraft(
                ObjectDB.instance.m_recipes.Single(r => r.name == "Recipe_ValheimBoombox_Item"),
                steps
            );
            var device = player.GetInventory().GetAllItems().Single(i => BoomboxItemData.IsDevice(i)
                && !before.Contains(i));
            device.m_customData["boombox.qa.fixture"] = "L08";
            string id = BoomboxItemData.Read(device).deck.id;
            checkpoint["id"] = id;
            before = player.GetInventory().GetAllItems().ToArray();
            yield return JourneyCraft(
                ObjectDB.instance.m_recipes.Single(r => r.name == "Recipe_ValheimBoombox_CassetteArchive"),
                steps
            );
            var tape = player.GetInventory().GetAllItems().Single(i => BoomboxItemData.IsCassette(i)
                && !before.Contains(i));
            string tapeId = BoomboxItemData.Read(tape).cassette.id;
            Need((bool)UiDispatch(UiInventoryItem(tape), true)["hitsTarget"], "L08 cassette RMB missed");
            yield return UiAwait(
                () => BoomboxItemData.Read(device).deck.insertedCassette?.id == tapeId,
                "L08 cassette insert"
            );
            Need((bool)UiDispatch(UiInventoryItem(device), true)["hitsTarget"], "L08 equip RMB missed");
            yield return UiAwait(() => BoomboxEquipment.Get(player) == device, "L08 equip");
            JourneyClick(UiButton("BoomboxMusicButton"), steps);
            yield return new WaitForSecondsRealtime(.3f);
            JourneyClick(UiButton("Tab0"), steps);
            yield return new WaitForSecondsRealtime(.2f);
            string source = "file://"
                + Path.GetFullPath(Path.Combine(BepInEx.Paths.ConfigPath, "../../Музыка R10/R10 local music.wav"));
            var selectedTrack = plugin.Catalog.playlists.SelectMany(p => p.tracks).First(
                t => string.Equals(t.source, source, StringComparison.OrdinalIgnoreCase)
            );
            var add = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(
                b => b.name == "TrackAdd" && b.gameObject.activeInHierarchy
                    && b.transform.parent.GetComponentsInChildren<TMP_Text>().Any(
                    t => t.text.Contains(selectedTrack.Label)
                )
                    && UiRaycast((RectTransform)b.transform, false).gameObject?.transform.IsChildOf(b.transform) == true
            );
            JourneyClick((RectTransform)add.transform, steps);
            yield return null;
            JourneyClick(UiButton("PopupNewPlaylist"), steps);
            yield return null;
            string longName = "QA — Зимний вечер, далёкие берега и песни северного пути 2026".PadRight(60, '!')
                .Substring(0, 60);
            yield return UiType("PopupName", longName);
            JourneyClick(UiButton("PopupCreate"), steps);
            yield return new WaitForSecondsRealtime(.2f);
            var oldIds = checkpoint["playlists"].Select(p => (string)p["id"]).ToArray();
            var playlist = plugin.MusicLibrary.playlists.Single(p => !oldIds.Contains(p.id));
            Need(playlist.tracks.SequenceEqual(new[] { selectedTrack.id }), "L08 selected another audio fixture");
            JourneyClick(UiButton("Tab1"), steps);
            yield return new WaitForSecondsRealtime(.2f);
            JourneyClick(UiButton("PlaylistEntry" + plugin.MusicLibrary.playlists.IndexOf(playlist)), steps);
            yield return null;
            var field = UiInput("PlaylistName");
            field.ActivateInputField();
            yield return null;
            yield return null;
            field.MoveTextEnd(false);
            field.ForceLabelUpdate();
            yield return new WaitForSecondsRealtime(.2f);
            yield return UiFrame("L07-confirm-long-name-end.png", frames);
            field.DeactivateInputField();
            proof["selectedPlaylist"] = JObject.FromObject(playlist);
            JourneyClick(UiButton("PlaylistPlay"), steps);
            yield return TransportWait(
            () =>
                {
                    var active = TransportDeck(id);
                    return active != null && (active.IsActivePlayback || active.IsSyntheticActive);
                },
            "L08 selected music did not start",
            6
            );
            proof["playback"] = new JObject
            {
                ["fileAudio"] = TransportDeck(id).IsActivePlayback,
                ["synthetic"] = TransportDeck(id).IsSyntheticActive,
                ["track"] = TransportDeck(id).State.TrackId
            };
            JourneyClick(UiButton("Tab3"), steps);
            yield return TransportWait(
                () => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Any(
                    b => b.name == "PropertyGreen" && b.gameObject.activeInHierarchy
                ),
                "L08 cassette tab not ready",
                2
            );
            JourneyClick(UiButton("PropertyGreen"), steps);
            var cassetteCost = BoomboxCassetteProgression.Recipe(plugin.Settings, 1);
            foreach (var cost in cassetteCost.Ingredients)
                JourneyGive(cost.Prefab, cost.Amount);
            yield return new WaitForSecondsRealtime(.3f);
            var counts = cassetteCost.Ingredients.ToDictionary(c => c.Prefab, c => JourneyCount(c.Prefab));
            JourneyClick(UiButton("CassetteUpgrade"), steps);
            yield return null;
            Need(
                BoomboxItemData.Read(device).deck.insertedCassette.id == tapeId
                && BoomboxItemData.Read(device).deck.insertedCassette.Rank("Green") == 1,
                "L08 cassette upgrade failed/changed identity"
            );
            foreach (var cost in cassetteCost.Ingredients)
                Need(JourneyCount(cost.Prefab) == counts[cost.Prefab] - cost.Amount, "L08 cassette spend incorrect");
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            var forge = JourneyStation("forge", origin + Vector3.right * 15, roots);
            yield return JourneyOpenStation(forge, steps);
            JourneyClick((RectTransform)InventoryGui.instance.m_tabUpgrade.transform, steps);
            yield return null;
            yield return JourneyCraft(
                ObjectDB.instance.m_recipes.Single(r => r.name == "Recipe_BoomboxUpgrade_2"),
                steps,
                id
            );
            device = MigratedInventory(id).Single();
            Need(
                BoomboxItemData.Read(device).deck.insertedCassette.id == tapeId,
                "L08 device upgrade changed cassette"
            );
            InventoryGui.instance.Hide();
            player.SetHealth(Mathf.Max(1, player.GetMaxHealth() - 5));
            float health = player.GetHealth();
            var healthWait = new JArray();
            proof["healthWait"] = healthWait;
            yield return TransportWait(
            () =>
                {
                    var active = TransportDeck(id);
                    healthWait.Add(new JObject
                    {
                        ["health"] = player.GetHealth(),
                        ["ticks"] = BoomboxAuraRecipient.HealthTicks,
                        ["aura"] = new JArray(BoomboxAuraNetwork.For(player).Values),
                        ["equipped"] = BoomboxEquipment.Get(player) != null,
                        ["playing"] = active != null && (active.IsActivePlayback || active.IsSyntheticActive),
                        ["stored"] = active?.StoragePaused
                    });
                    return player.GetHealth() > health + .001f;
                },
            "L08 playing upgraded cassette did not restore health",
            plugin.Settings.Health.Interval.Value + 3
            );
            proof["recovery"] = new JObject { ["before"] = health, ["after"] = player.GetHealth() };
            var transfer = position + new Vector3(35, 0, 35);
            transfer.y = ZoneSystem.instance.GetGroundHeight(transfer) + .1f;
            CasePlatformPlayer(player, transfer, Quaternion.identity);
            player.GetComponent<ZSyncTransform>().SyncNow();
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.4f);
            JourneyClick(UiInventoryItem(device), steps);
            yield return null;
            JourneyClick((RectTransform)InventoryGui.instance.m_dropButton.transform, steps);
            yield return new WaitForSecondsRealtime(.3f);
            InventoryGui.instance.Hide();
            var drop = MigratedDrops(id).Single();
            GameObject hover = null;
            var pickupAim = new JArray();
            proof["pickupAim"] = pickupAim;
            yield return new WaitForSecondsRealtime(1.5f);
            var stand = drop.transform.position - Vector3.forward * 1.2f;
            stand.y = ZoneSystem.instance.GetGroundHeight(stand) + .1f;
            CasePlatformPlayer(player, stand, Quaternion.identity);
            player.GetComponent<ZSyncTransform>().SyncNow();
            yield return new WaitForSecondsRealtime(.2f);
            for (int n = 0; n < 15; n++)
            {
                var center = drop.GetComponentsInChildren<Collider>().First(c => c.enabled
                    && !c.isTrigger).bounds.center;
                var aim = center - GameCamera.instance.transform.position;
                player.transform.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
                player.SetLookDir(aim, 0);
                AccessTools.Field(typeof(Player), "m_lookPitch").SetValue(
                    player,
                    Mathf.Atan2(-aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg
                );
                yield return new WaitForSecondsRealtime(.1f);
                hover = player.GetHoverObject();
                pickupAim.Add(
                    new JObject
                    {
                        ["target"] = Point(center),
                        ["eye"] = Point(player.GetEyePoint()),
                        ["hover"] = hover?.name
                    }
                );
                if (hover != null && hover.GetComponentInParent<ItemDrop>() == drop)
                    break;
            }
            if (hover == null || hover.GetComponentInParent<ItemDrop>() != drop)
                yield return UiFrame("L08-pickup-aim.png", frames);
            Need(hover != null && hover.GetComponentInParent<ItemDrop>() == drop, "L08 pickup hover missed");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return TransportWait(
                () => BoomboxEquipment.Get(player) != null
                    && BoomboxItemData.Read(BoomboxEquipment.Get(player)).deck.id == id,
                "L08 E pickup failed",
                4
            );
            device = MigratedInventory(id).Single();
            Need(BoomboxItemData.Read(device).deck.insertedCassette.id == tapeId, "L08 transfer changed cassette");
            checkpoint["item"] = CaseStable(device);
            checkpoint["playlistId"] = playlist.id;
            CasePlatformPlayer(player, position, rotation);
            player.GetComponent<ZSyncTransform>().SyncNow();
            plugin.Items.Flush();
            Game.instance.SavePlayerProfile(true);
            File.WriteAllText(JourneyCheckpoint, checkpoint.ToString());
            proof["savedItem"] = CaseStable(device);
            proof["passed"] = true;
            success = true;
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            player.SetCraftingStation(null);
            CasePlatformPlayer(player, position, rotation);
            player.GetComponent<ZSyncTransform>().SyncNow();
            Freeze(player, false);
            distance.SetValue(GameCamera.instance, oldDistance);
            InputSystem.settings.backgroundBehavior = input;
            foreach (var root in roots)
                if (root != null)
                {
                    var view = root.GetComponent<ZNetView>();
                    if (view != null && view.IsValid())
                        ZNetScene.instance.Destroy(root);
                    else
                        UnityEngine.Object.Destroy(root);
                }
            if (checkpoint != null && (!save || !success))
            {
                Clear();
                player.UnequipAllItems();
                player.GetInventory().RemoveAll();
                player.GetInventory().Load(new ZPackage(Convert.FromBase64String((string)checkpoint["inventory"])));
                foreach (var item in player.GetInventory().GetAllItems().Where(i => i.m_equipped).ToArray())
                {
                    item.m_equipped = false;
                    player.EquipItem(item, false);
                }
                var prior = checkpoint["playlists"].Select(p => (string)p["id"]).ToArray();
                foreach (var list in plugin.MusicLibrary.playlists.Where(p => !prior.Contains(p.id)).ToArray())
                    plugin.MusicLibrary.Delete(list);
                Game.instance.SavePlayerProfile(true);
            }
        }
    }
}
