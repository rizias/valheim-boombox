using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator CaseCassetteCombinations()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var input = InputSystem.settings.backgroundBehavior;
        var proof =
            new JObject
            {
                ["case"] = "F-10",
                ["scope"] =
                    "32 masks of five properties: native item tooltip, actual cassette menu labels, spawned cassette bands and installed symbols. Numeric/material composition, not icon-art approval."
            };
        lastCapture = proof;
        var rows = new JArray();
        proof["combinations"] = rows;
        ItemDrop drop = null;
        float start = Time.realtimeSinceStartup;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            var item = CaseDevice("F10", "Red", 1);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.6f);
            float ready = Time.realtimeSinceStartup + 2;
            while (UiRaycast(UiInventoryItem(item), true).gameObject == null && Time.realtimeSinceStartup < ready)
                yield return null;
            Need((bool)UiDispatch(UiInventoryItem(item), true)["hitsTarget"], "F10 equip raycast missed");
            yield return UiAwait(() => BoomboxEquipment.Get(player) == item, "F10 equip");
            Need((bool)UiDispatch(UiButton("BoomboxMusicButton"), false)["hitsTarget"], "F10 open missed");
            yield return new WaitForSecondsRealtime(.3f);
            Need((bool)UiDispatch(UiButton("Tab3"), false)["hitsTarget"], "F10 cassette tab missed");
            yield return new WaitForSecondsRealtime(.2f);
            var deck = plugin.Current;
            Need(deck.State.id == BoomboxItemData.Read(item).deck.id, "F10 wrong device");
            var physical = BoomboxItemPrefabs.NewItem(BoomboxItemData.ArchivePrefab);
            physical.m_customData["boombox.qa.fixture"] = "F10-independent-display-fixture";
            drop = ItemDrop.DropItem(physical, 1, player.transform.position + Vector3.up * 10, Quaternion.identity);
            Need(drop != null, "F10 cassette fixture missing");
            drop.GetComponent<Rigidbody>().isKinematic = true;
            BoomboxItems.RegisterDrop(drop);
            var tooltip = AccessTools.Method(
                typeof(ItemDrop.ItemData),
                "GetTooltip",
                new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) }
            );
            string[] properties = { "Red", "Yellow", "Green", "Blue", "Purple" };
            for (int mask = 0; mask < 32; mask++)
            {
                var tape = deck.State.insertedCassette;
                tape.properties.Clear();
                var payload = BoomboxItemData.Read(drop.m_itemData);
                payload.cassette.properties.Clear();
                for (int p = 0; p < 5; p++)
                    if ((mask & (1 << p)) != 0)
                    {
                        tape.properties[properties[p]] = p + 1;
                        payload.cassette.properties[properties[p]] = p + 1;
                    }
                BoomboxItems.Persist(deck);
                BoomboxItemData.Write(drop.m_itemData, payload);
                yield return new WaitForSecondsRealtime(.2f);
                var active = properties.Where(p => tape.Rank(p) > 0).ToArray();
                string tip = (string)tooltip.Invoke(null, new object[] { drop.m_itemData, 1, false, 0f, 0, false });
                var bullets = tip.Split('\n').Where(l => l.StartsWith("• ", StringComparison.Ordinal)).ToArray();
                Need(bullets.Length == active.Length, "F10 tooltip property count mismatch mask=" + mask);
                var labels = new JObject();
                foreach (string property in properties)
                {
                    string label = UiButton("Property" + property).GetComponentInChildren<TMP_Text>().text;
                    int rank = tape.Rank(property);
                    Need(
                        label.Split('\n')[0].Contains(" " + rank),
                        "F10 menu rank mismatch " + property + " mask=" + mask
                    );
                    labels[property] = label;
                    if (rank > 0)
                        Need(
                            bullets.Any(
                                l => l.Contains(
                                    Localization.instance.Localize("$boombox_property_" + property.ToLowerInvariant())
                                        + " "
                                        + rank
                                        + ":"
                                )
                            ),
                            "F10 tooltip rank mismatch " + property
                        );
                }
                var bands = drop.transform.Find("attach/CassetteBands");
                var materials = bands == null ? new Material[0] : bands.GetComponent<MeshRenderer>().sharedMaterials;
                Need(
                    materials.Select(m => m.name.Replace("Cassette band ", "")).SequenceEqual(active),
                    "F10 cassette bands mismatch mask=" + mask
                );
                var symbols = deck.Visual.transform.Find("InstalledCassetteProperties");
                Need(symbols != null, "F10 installed symbols root missing");
                var names = symbols.Cast<Transform>().Where(t => t.gameObject.activeSelf).Select(
                    t => t.name.Replace("(Clone)", "").Replace("Property", "")
                ).ToArray();
                Need(names.SequenceEqual(active), "F10 installed symbols mismatch mask=" + mask);
                rows.Add(new JObject
                {
                    ["mask"] = mask,
                    ["ranks"] = JObject.FromObject(tape.properties),
                    ["menu"] = labels,
                    ["tooltip"] = tip,
                    ["bands"] = new JArray(materials.Select(m => m.name)),
                    ["installedSymbols"] = new JArray(names)
                });
            }
            proof["seconds"] = Time.realtimeSinceStartup - start;
            proof["passed"] = true;
        }
        finally
        {
            if (drop != null)
                ZNetScene.instance.Destroy(drop.gameObject);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
            InputSystem.settings.backgroundBehavior = input;
        }
    }
}
