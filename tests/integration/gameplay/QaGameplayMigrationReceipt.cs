using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static string ReceiptCheckpoint => Path.Combine(BepInEx.Paths.ConfigPath, "qa-K09-checkpoint.json");
    private IEnumerator CaseMigrationReceipt(string phase)
    {
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        UiParkWindow();
        var store = MigrationStore();
        string path = (string)AccessTools.Property(store.GetType(), "WorldPath").GetValue(store, null);
        JObject checkpoint = phase == "save"
            ? new JObject { ["sha"] = productSha, ["originalWorld"] = File.ReadAllText(path) }
            : JObject.Parse(File.ReadAllText(ReceiptCheckpoint));
        var proof =
            new JObject
            {
                ["case"] = "K-09",
                ["phase"] = phase,
                ["scope"] =
                    "Actual saved native destination with missing migration acknowledgement, loaded in a fresh process. Both world receipt and inventory identity are checked."
            };
        lastCapture = proof;
        bool changed = false, completed = false;
        try
        {
            Need((string)checkpoint["sha"] == productSha, "K09 candidate changed");
            Freeze(player, true);
            changed = phase != "save";
            if (phase != "save")
            {
                string id = (string)checkpoint["id"];
                bool expectedBag = phase == "inventory";
                yield return TransportWait(
                    () => plugin.AudioWorldActive && MigrationWorld().migratedDecks.Contains(id),
                    "K09 missing acknowledgement was not recovered",
                    6
                );
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var inventory = MigratedInventory(id);
                    var drops = MigratedDrops(id);
                    Need(
                        inventory.Length == (expectedBag ? 1 : 0) && drops.Length == (expectedBag ? 0 : 1),
                        "K09 duplicate/lost saved destination"
                    );
                    var item = inventory.FirstOrDefault() ?? drops[0].m_itemData;
                    Need(
                        JToken.DeepEquals(checkpoint["item"], JToken.Parse(CaseStable(item).ToString())),
                        "K09 changed saved item"
                    );
                    proof[repeat == 0 ? "freshProcessCopies" : "repeatLoadCopies"] = inventory.Length + drops.Length;
                    if (repeat == 0)
                    {
                        ReloadMigrationFile(path, File.ReadAllText(path));
                        yield return TransportWait(
                            () => plugin.AudioWorldActive && MigrationWorld().migratedDecks.Contains(id),
                            "K09 acknowledged repeat load failed",
                            5
                        );
                    }
                }
                foreach (var drop in MigratedDrops(id))
                    ZNetScene.instance.Destroy(drop.gameObject);
                foreach (var item in MigratedInventory(id))
                {
                    player.UnequipItem(item, false);
                    player.GetInventory().RemoveItem(item);
                }
                ReloadMigrationFile(path, (string)checkpoint["originalWorld"]);
                yield return TransportWait(() => plugin.AudioWorldActive, "K09 baseline restore failed", 5);
            }
            if (phase != "inventory")
            {
                bool onBack = phase == "world";
                var legacy = new DeckState
                {
                    owner = player.GetPlayerID(),
                    onBack = onBack,
                    upgradeTier = 4,
                    tracks = new List<string> { BoomboxSyntheticTrackId.Rhythm(100) },
                    cassetteId = Guid.NewGuid().ToString("N"),
                    playing = false,
                    Position = player.transform.position + player.transform.forward * 2
                };
                checkpoint["id"] = legacy.id;
                checkpoint["preparationIncomplete"] = true;
                File.WriteAllText(ReceiptCheckpoint, checkpoint.ToString());
                changed = true;
                ReloadMigrationFile(
                    path,
                    JsonConvert.SerializeObject(
                        new WorldState { worldId = ZNet.instance.GetWorldUID(), decks = new List<DeckState> { legacy } }
                    )
                );
                yield return TransportWait(
                    () => plugin.AudioWorldActive && MigrationWorld().migratedDecks.Contains(legacy.id),
                    "K09 preparation migration failed",
                    6
                );
                var inventory = MigratedInventory(legacy.id);
                var drops = MigratedDrops(legacy.id);
                Need(
                    inventory.Length == (onBack ? 1 : 0) && drops.Length == (onBack ? 0 : 1),
                    "K09 preparation wrong destination"
                );
                var item = inventory.FirstOrDefault() ?? drops[0].m_itemData;
                checkpoint["id"] = legacy.id;
                checkpoint["item"] = CaseStable(item);
                checkpoint["next"] = onBack ? "inventory" : "world";
                if (!onBack)
                    Need(
                        drops[0].GetComponent<ZNetView>().GetZDO().GetString(BoomboxMigration.WorldReceiptKey, "")
                            == legacy.id,
                        "K09 native world receipt missing"
                    );
                MigrationWorld().migratedDecks.Remove(legacy.id);
                plugin.Save();
                Need(
                    !JObject.Parse(File.ReadAllText(path))["migratedDecks"].Any(v => (string)v == legacy.id),
                    "K09 acknowledgement removal was not saved"
                );
                Game.instance.SavePlayerProfile(true);
                ZNet.instance.Save(true, false, false);
                checkpoint["preparationIncomplete"] = false;
                File.WriteAllText(ReceiptCheckpoint, checkpoint.ToString());
                proof["prepared"] = checkpoint.DeepClone();
            }
            else
            {
                Game.instance.SavePlayerProfile(true);
                ZNet.instance.Save(true, false, false);
            }
            proof["passed"] = true;
            completed = true;
        }
        finally
        {
            try
            {
                if (changed && !completed)
                {
                    string id = (string)checkpoint["id"];
                    if (id != null)
                    {
                        foreach (var drop in MigratedDrops(id))
                            ZNetScene.instance.Destroy(drop.gameObject);
                        foreach (var item in MigratedInventory(id))
                        {
                            player.UnequipItem(item, false);
                            player.GetInventory().RemoveItem(item);
                        }
                    }
                    ReloadMigrationFile(path, (string)checkpoint["originalWorld"]);
                    Game.instance.SavePlayerProfile(true);
                    ZNet.instance.Save(true, false, false);
                    proof["baselineRestoredAfterFailure"] = true;
                }
            }
            finally
            {
                Freeze(player, false);
            }
        }
    }
}
