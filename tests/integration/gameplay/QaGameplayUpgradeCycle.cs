using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator UpgradeCycle()
    {
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        var inventory = player.GetInventory();
        var frames = new JArray();
        var steps = new JArray();
        var proof =
            new JObject
            {
                ["candidate"] = productSha,
                ["frames"] = frames,
                ["steps"] = steps,
                ["scope"] =
                    "UI recipe display and paid cassette upgrades; fixture device/cassette and raw resources are injected, not claimed as native initial crafting"
            };
        lastCapture = proof;
        float started = Time.realtimeSinceStartup;
        string language = Localization.instance.GetSelectedLanguage();
        int fps = Application.targetFrameRate, sync = QualitySettings.vSyncCount;
        bool background = Settings.ReduceBackgroundUsage;
        try
        {
            Settings.ReduceBackgroundUsage = false;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            void Phase(string value)
            {
                proof["phase"] = value;
                proof["phaseSeconds"] = Time.realtimeSinceStartup - started;
                Logger.LogInfo("QA_UPGRADE_PHASE " + value + " seconds=" + (Time.realtimeSinceStartup - started));
            }
            void Click(string name)
            {
                Phase("click:" + name);
                var click = UiDispatch(UiButton(name), false);
                Need((bool)click["hitsTarget"], "UI raycast missed " + name);
            }
            Phase("prepare");
            UiParkWindow(true);
            Localization.instance.SetLanguage("Russian");
            player.SetGodMode(true);
            Clear();
            Need(
                !player.NoCostCheat() && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost),
                "Free crafting enabled"
            );
            var origin = player.transform.position + Vector3.up * 35;
            var bench = UnityEngine.Object.Instantiate(
                ZNetScene.instance.GetPrefab("piece_workbench"),
                origin,
                Quaternion.identity
            );
            fixtures.Add(bench);
            var station = bench.GetComponent<CraftingStation>();
            var roof = station.m_roofCheckPoint.position;
            var points = new[] {
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
                new Vector3(10, .4f, 10)
            };
            for (int i = 0; i < points.Length; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.layer = LayerMask.NameToLayer("piece");
                obj.transform.position = points[i];
                obj.transform.localScale = sizes[i];
                obj.GetComponent<Renderer>().enabled = false;
                fixtures.Add(obj);
            }
            player.transform.position = origin + Vector3.forward * 1.5f;
            Freeze(player, true);
            Physics.SyncTransforms();
            var item = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
            var payload = BoomboxItemData.Read(item);
            item.m_customData["boombox.qa.fixture"] = "UPGRADE";
            payload.deck.upgradeTier = 1;
            payload.deck.owner = player.GetPlayerID();
            payload.deck.onBack = false;
            payload.deck.playing = false;
            var tapeItem = BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab);
            payload.deck.insertedCassette = BoomboxItemData.Read(tapeItem).cassette;
            foreach (string property in new[] { "Green", "Yellow", "Blue", "Purple" })
                payload.deck.insertedCassette.properties[property] = 3;
            payload.deck.cassetteId = payload.deck.insertedCassette.id;
            BoomboxItemData.Write(item, payload);
            Need(inventory.AddItem(item), "Fixture device rejected");
            plugin.Items.Update();
            var registry = (Dictionary<string, AudioDeck>)AccessTools.Field(typeof(BoomboxPlugin), "Decks")
                .GetValue(plugin);
            var deck = decks["UPGRADE"] = registry[payload.deck.id];
            var cassette = deck.State.insertedCassette;
            string id = cassette.id, deviceId = deck.State.id;
            var other = cassette.properties.Where(x => x.Key != "Red").ToDictionary(x => x.Key, x => x.Value);
            var materialNames = new HashSet<string>(
                Enumerable.Range(1, 7)
                    .SelectMany(rank => BoomboxCassetteProgression.DefaultRecipe(rank)
                    .Ingredients)
                    .Select(
                    x => x.Prefab
                )
            );
            foreach (var material in inventory.GetAllItems().ToArray())
                if (material.m_dropPrefab != null && materialNames.Contains(material.m_dropPrefab.name))
                    inventory.RemoveItem(material);
            Player.m_localPlayer.AddKnownItem(deck.Item);
            Need(BoomboxEquipment.Known, "Fixture device not known");
            yield return null;
            if (CinematicsManager.IsPlaying())
                CinematicsManager.Stop();
            if (Valkyrie.m_instance != null)
                Valkyrie.m_instance.DropPlayer(true);
            player.SetIntro(false);
            player.SetSleeping(false);
            var animator = player.GetComponentInChildren<Animator>();
            for (int i = 0; i < 8 && player.InCutscene(); i++)
            {
                animator.Update(3f);
                yield return null;
            }
            Need(!player.InCutscene(), "QA spawn animation not settled");
            proof["actorPreparation"] = new JObject
            {
                ["intro"] = player.InIntro(),
                ["cutscene"] = player.InCutscene(),
                ["valkyrie"] = Valkyrie.m_instance != null
            };
            InventoryGui.instance.Show(null, 1);
            Phase("inventory-open");
            yield return new WaitForSecondsRealtime(.3f);
            float buttonDeadline = Time.realtimeSinceStartup + 1;
            while (!UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Any(
                b => b.name == "BoomboxMusicButton" && b.gameObject.activeInHierarchy
            )
            && Time.realtimeSinceStartup < buttonDeadline)
                yield return null;
            proof["inventoryVisible"] = InventoryGui.IsVisible();
            proof["nativeVisible"] = plugin.NativeInventoryVisible;
            proof["cutscene"] = player.InCutscene();
            proof["menuOpenBeforeClick"] = plugin.MenuOpen;
            if (!UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Any(
                b => b.name == "BoomboxMusicButton" && b.gameObject.activeInHierarchy
            ))
            {
                proof["buttons"] = new JArray(
                    UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Select(
                        b => b.name + ":" + b.gameObject.activeInHierarchy
                    )
                );
                yield return HudFrame("upgrade-missing-button.jpg", frames);
                Need(false, "Music button not active after native inventory open");
            }
            Click("BoomboxMusicButton");
            float menuDeadline = Time.realtimeSinceStartup + 1;
            while (!UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Any(
                b => b.name == "Tab3" && b.gameObject.activeInHierarchy
            )
            && Time.realtimeSinceStartup < menuDeadline)
                yield return null;
            Need(plugin.MenuOpen, "Real inventory music button did not open menu");
            Click("Tab3");
            yield return new WaitForSecondsRealtime(.15f);
            Click("PropertyRed");
            yield return null;
            yield return null;
            string[] trophies = {
                "TrophyEikthyr",
                "TrophyTheElder",
                "TrophyBonemass",
                "TrophyDragonQueen",
                "TrophyGoblinKing",
                "TrophySeekerQueen",
                "TrophyFader"
            };
            int Count(
                string prefab
            ) => inventory.GetAllItems().Where(x => x.m_dropPrefab != null && x.m_dropPrefab.name == prefab).Sum(
                x => x.m_stack
            );
            void Give(string prefab, int count)
            {
                var source = ObjectDB.instance.GetItemPrefab(prefab);
                Need(source != null, "Missing game prefab " + prefab);
                var item = source.GetComponent<ItemDrop>().m_itemData.Clone();
                item.m_dropPrefab = source;
                item.m_stack = count;
                Need(inventory.AddItem(item), "Fixture inventory rejected " + prefab);
                player.AddKnownItem(item);
            }
            for (int rank = 1; rank <= 7; rank++)
            {
                Need(Time.realtimeSinceStartup - started < 12, "Native cassette cycle exceeded12s");
                var cost = BoomboxCassetteProgression.DefaultRecipe(rank);
                Phase("rank:" + rank);
                foreach (var ingredient in cost.Ingredients.Where(x => !x.Prefab.StartsWith("Trophy")))
                    Give(ingredient.Prefab, ingredient.Amount);
                if (rank == 2)
                {
                    Give("TrophyEikthyr", 10);
                    yield return null;
                    yield return null;
                    Need(!UiButton("CassetteUpgrade").GetComponent<Button>().interactable, "Eikthyr allowed rankII");
                    Click("CassetteUpgrade");
                    Need(cassette.Rank("Red") == 1, "Disabled click advanced rank");
                    proof["wrongTrophyRejected"] = true;
                }
                Give(trophies[rank - 1], 1);
                yield return null;
                yield return null;
                var requirements = typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxCraftingRequirements");
                var shown = (Recipe)AccessTools.Field(requirements, "recipe").GetValue(null);
                Need(
                    shown.m_resources.Any(x => x.m_resItem.gameObject.name == trophies[rank - 1]),
                    "Shown recipe has wrong boss trophy"
                );
                var before = cost.Ingredients.ToDictionary(x => x.Prefab, x => Count(x.Prefab));
                Need(
                    UiButton("CassetteUpgrade").GetComponent<Button>().interactable,
                    "Upgrade button disabled with exact recipe"
                );
                Click("CassetteUpgrade");
                yield return null;
                Need(cassette.Rank("Red") == rank, "UI did not advance selected rank");
                foreach (var ingredient in cost.Ingredients)
                    Need(
                        Count(ingredient.Prefab) == before[ingredient.Prefab] - ingredient.Amount,
                        "Wrong native spend: " + ingredient.Prefab
                    );
                Need(
                    cassette.id == id && deck.State.id == deviceId && other.All(x => cassette.Rank(x.Key) == x.Value),
                    "Upgrade replaced cassette or other ranks"
                );
                var saved = BoomboxItemData.Read(deck.Item).deck.insertedCassette;
                Need(saved.id == id && saved.Rank("Red") == rank, "Item payload not persisted");
                steps.Add(
                    new JObject
                    {
                        ["rank"] = rank,
                        ["trophy"] = trophies[rank - 1],
                        ["spent"] = true,
                        ["sameCassette"] = true
                    }
                );
            }
            yield return null;
            Need(!UiButton("CassetteUpgrade").GetComponent<Button>().interactable, "Max rank button still enabled");
            proof["seconds"] = Time.realtimeSinceStartup - started;
            proof["passed"] = true;
        }
        finally
        {
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
            Localization.instance.SetLanguage(language);
            UiParkWindow();
            Settings.ReduceBackgroundUsage = background;
            Application.targetFrameRate = fps;
            QualitySettings.vSyncCount = sync;
        }
    }
}
