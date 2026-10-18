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
    private static int migrationFaults;
    private static readonly Type MigrationSaveType = typeof(BoomboxPlugin).Assembly.GetType(
        "VikingBoombox.BoomboxMigrationSave"
    );
    private static bool RejectMigrationProfile(ref bool __result)
    {
        if (AccessTools.Field(MigrationSaveType, "profileCall").GetValue(null) == null)
            return true;
        migrationFaults++;
        __result = false;
        return false;
    }
    private static bool RejectMigrationWorld()
    {
        if (AccessTools.Field(MigrationSaveType, "requestedWorld").GetValue(null) == null)
            return true;
        System.Threading.Interlocked.Increment(ref migrationFaults);
        return false;
    }
    private static object MigrationStore() => AccessTools.Field(typeof(BoomboxPlugin), "Store").GetValue(BoomboxPlugin.Instance);
    private static WorldState MigrationWorld() => (WorldState)AccessTools.Field(MigrationStore().GetType(), "World").GetValue(MigrationStore());
    private static ItemDrop.ItemData[] MigratedInventory(
        string id
    ) => Player.m_localPlayer.GetInventory().GetAllItems().Where(
        i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == id
    ).ToArray();
    private static ItemDrop[] MigratedDrops(
        string id
    ) => UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(
        d => BoomboxItemData.IsDevice(d.m_itemData) && BoomboxItemData.Read(d.m_itemData).deck.id == id
    ).ToArray();
    private static void ReloadMigrationFile(string path, string text)
    {
        var plugin = BoomboxPlugin.Instance;
        AccessTools.Method(typeof(BoomboxPlugin), "Unload").Invoke(plugin, null);
        File.WriteAllText(path, text);
    }
    private static void MigrationFill(List<ItemDrop.ItemData> filler)
    {
        var inventory = Player.m_localPlayer.GetInventory();
        var prefab = ObjectDB.instance.GetItemPrefab("Wood");
        var wood = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        wood.m_dropPrefab = prefab;
        wood.m_stack = wood.m_shared.m_maxStackSize;
        var api = AccessTools.TypeByName("ExtraSlots.API");
        var slots = ((IEnumerable)AccessTools.Method(api, "GetExtraSlots").Invoke(null, null)).Cast<object>().ToArray();
        var add = AccessTools.Method(
            typeof(Inventory),
            "AddItem",
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) }
        );
        for (int y = 0; y < inventory.GetHeight(); y++)
            for (int x = 0; x < inventory.GetWidth(); x++)
            {
                if (inventory.GetItemAt(x, y) != null)
                    continue;
                var pos = new Vector2i(x, y);
                var slot = slots.FirstOrDefault(
                    s => (Vector2i)AccessTools.Property(s.GetType(), "GridPosition").GetValue(s, null) == pos
                );
                if (slot != null
                    && !(bool)AccessTools.Method(slot.GetType(), "ItemFits").Invoke(slot, new object[] { wood }))
                    continue;
                var raw = wood.Clone();
                if ((bool)add.Invoke(inventory, new object[] { raw, raw.m_stack, x, y, false }))
                    filler.Add(inventory.GetItemAt(x, y));
            }
        Need(!inventory.CanAddItem(wood), "Migration ordinary bag is not full");
    }
    private IEnumerator CaseLegacyMigration(string id)
    {
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        var rows = new JArray();
        var proof =
            new JObject
            {
                ["case"] = id.Insert(1, "-"),
                ["scope"] =
                    "Legacy JSON loaded by ordinary plugin Update/LoadWorld; actual native saves. Faults affect only migration-scoped save calls. No direct Migrate invocation.",
                ["rows"] = rows
            };
        lastCapture = proof;
        yield return CaseReadyInventory();
        var store = MigrationStore();
        string path = (string)AccessTools.Property(store.GetType(), "WorldPath").GetValue(store, null);
        plugin.Save();
        string original = File.ReadAllText(path);
        var fault = new Harmony("local.boombox.qa.legacy-save-failure");
        var fillers = new List<ItemDrop.ItemData>();
        string activeId = null;
        try
        {
            foreach (bool onBack in new[] { true, false })
                foreach (string capacity in id == "K08"
                    ? new[] { "full-free-slot", "full-occupied-slot" }
                    : new[] { "free" })
                {
                    Clear();
                    yield return null;
                    if (capacity == "full-occupied-slot")
                    {
                        var occupied = CaseDevice("K08-slot", "Red", 1);
                        Need(player.EquipItem(occupied, false), "K08 occupied slot setup failed");
                    }
                    if (capacity != "free")
                        MigrationFill(fillers);
                    var legacy = new DeckState
                    {
                        owner = player.GetPlayerID(),
                        onBack = onBack,
                        upgradeTier = 4,
                        tracks = new List<string> {
                            BoomboxSyntheticTrackId.Rhythm(100),
                            BoomboxSyntheticTrackId.Rhythm(120)
                        },
                        trackIndex = 1,
                        seconds = 12.5f,
                        volume = .37f,
                        playing = false,
                        cassetteId = Guid.NewGuid().ToString("N"),
                        Position = player.transform.position + player.transform.forward * 2
                    };
                    activeId = legacy.id;
                    var expected = JObject.FromObject(legacy);
                    var source = new WorldState
                    {
                        worldId = ZNet.instance.GetWorldUID(),
                        decks = new List<DeckState> { legacy }
                    };
                    migrationFaults = 0;
                    if (id == "K10")
                        fault.Patch(
                            onBack
                                ? AccessTools.Method(typeof(PlayerProfile), "Save", Type.EmptyTypes)
                                : AccessTools.Method(typeof(ZNet), "SaveWorldThread"),
                            prefix: new HarmonyMethod(
                                typeof(QaGameplay),
                                onBack
                                    ? nameof(RejectMigrationProfile)
                                    : nameof(RejectMigrationWorld)
                            )
                        );
                    ReloadMigrationFile(path, JsonConvert.SerializeObject(source));
                    var row = new JObject { ["onBack"] = onBack, ["capacity"] = capacity, ["id"] = activeId };
                    rows.Add(row);
                    if (id == "K10")
                    {
                        yield return TransportWait(
                            () => migrationFaults > 0 && !plugin.AudioWorldActive,
                            "K10 migration save fault not reached",
                            6
                        );
                        Need(
                            !MigrationWorld().migratedDecks.Contains(activeId)
                                && MigratedInventory(activeId).Length == 0
                                && MigratedDrops(activeId).Length == 0,
                            "K10 uncommitted delivery/ack remains"
                        );
                        var retained = JObject.FromObject(MigrationWorld().decks.Single(d => d.id == activeId));
                        foreach (string key in new[] {
                            "id",
                            "tracks",
                            "upgradeTier",
                            "cassetteId",
                            "trackIndex",
                            "seconds",
                            "volume"
                        })
                            Need(JToken.DeepEquals(expected[key], retained[key]), "K10 changed legacy source " + key);
                        row["rollbackVerified"] = true;
                        row["faultCalls"] = migrationFaults;
                        fault.UnpatchSelf();
                    }
                    yield return TransportWait(
                        () => plugin.AudioWorldActive && MigrationWorld().migratedDecks.Contains(activeId),
                        "Migration ordinary load did not finish",
                        8
                    );
                    var inventory = MigratedInventory(activeId);
                    var drops = MigratedDrops(activeId);
                    bool inBag = onBack && capacity != "full-occupied-slot";
                    Need(
                        inventory.Length == (inBag ? 1 : 0) && drops.Length == (inBag ? 0 : 1),
                        "Migration wrong destination/duplicate"
                    );
                    var item = inventory.FirstOrDefault() ?? drops[0].m_itemData;
                    var actual = JObject.FromObject(BoomboxItemData.Read(item).deck);
                    foreach (string key in new[] {
                        "id",
                        "tracks",
                        "upgradeTier",
                        "cassetteId",
                        "trackIndex",
                        "seconds",
                        "volume"
                    })
                        Need(JToken.DeepEquals(expected[key], actual[key]), "Migration changed " + key);
                    Need(
                        ((JObject)actual["insertedCassette"]["properties"]).Count == 0,
                        "Migration granted free cassette properties"
                    );
                    row["inventoryCopies"] = inventory.Length;
                    row["worldCopies"] = drops.Length;
                    row["actual"] = actual;
                    row["passed"] = true;
                    foreach (var drop in drops)
                        ZNetScene.instance.Destroy(drop.gameObject);
                    foreach (var existing in inventory)
                    {
                        player.UnequipItem(existing, false);
                        player.GetInventory().RemoveItem(existing);
                    }
                    foreach (var filler in fillers)
                        player.GetInventory().RemoveItem(filler);
                    fillers.Clear();
                    activeId = null;
                    ReloadMigrationFile(path, original);
                    yield return TransportWait(() => plugin.AudioWorldActive, "Migration baseline reload failed", 5);
                }
        }
        finally
        {
            fault.UnpatchSelf();
            if (activeId != null)
            {
                foreach (var drop in MigratedDrops(activeId))
                    ZNetScene.instance.Destroy(drop.gameObject);
                foreach (var item in MigratedInventory(activeId))
                {
                    player.UnequipItem(item, false);
                    player.GetInventory().RemoveItem(item);
                }
            }
            foreach (var filler in fillers)
                player.GetInventory().RemoveItem(filler);
            Clear();
            ReloadMigrationFile(path, original);
            Freeze(player, false);
        }
        yield return TransportWait(() => plugin.AudioWorldActive, "Migration final baseline reload failed", 5);
        proof["passed"] = true;
    }
}
