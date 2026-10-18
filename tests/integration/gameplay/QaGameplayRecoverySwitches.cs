using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static double[] CaseRecoveryTicks() => new[] { BoomboxAuraRecipient.HealthTicks, BoomboxAuraRecipient.StaminaTicks, BoomboxAuraRecipient.EitrTicks };
    private static float[] CaseResources(Player p) => new[] { p.GetHealth(), p.GetStamina(), p.GetEitr() };
    private IEnumerator CaseFreshRecovery()
    {
        var before = CaseRecoveryTicks();
        float until = Time.realtimeSinceStartup + 7;
        while (CaseRecoveryTicks().Where((v, i) => v <= before[i]).Any() && Time.realtimeSinceStartup < until)
            yield return null;
        Need(CaseRecoveryTicks().Select((v, i) => v > before[i]).All(v => v), "H07 expected recovery tick absent");
    }
    private IEnumerator CaseRecoverySwitches()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var settings = plugin.Settings;
        var input = InputSystem.settings.backgroundBehavior;
        var periods = new[] {
            settings.Health.Interval.Value,
            settings.Stamina.Interval.Value,
            settings.Eitr.Interval.Value
        };
        bool panel = settings.ShowPlayerPanel.Value;
        var distance = AccessTools.Field(typeof(GameCamera), "m_distance");
        float oldDistance = (float)distance.GetValue(GameCamera.instance);
        var proof =
            new JObject
            {
                ["case"] = "H-07",
                ["scope"] =
                    "Real UI track change, cassette eject/insert, unequip/equip, drop and E pickup immediately after resource ticks. Missing resources and 5-second periods are fixture setup."
            };
        lastCapture = proof;
        var rows = new JArray();
        proof["actions"] = rows;
        float started = Time.realtimeSinceStartup;
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            player.transform.position += Vector3.up * 20;
            Floor(player.transform.position);
            Physics.SyncTransforms();
            settings.Health.Interval.Value = settings.Stamina.Interval.Value = settings.Eitr.Interval.Value = 5;
            settings.ShowPlayerPanel.Value = true;
            Create("H07", new[] { 0, 1, 1, 1, 0 }, 0, false, player.transform.position);
            var deck = decks["H07"];
            string id = deck.State.id, tape = deck.State.insertedCassette.id;
            plugin.PlayQueue(
                deck,
                new[] { BoomboxSyntheticTrackId.Rhythm(100), BoomboxSyntheticTrackId.Rhythm(140) },
                null,
                0
            );
            player.SetHealth(Mathf.Max(1, player.GetMaxHealth() - 25));
            player.UseStamina(25);
            player.UseEitr(25);
            Need(
                CaseResources(player).Select(
                    (v, i) => v < (new[] { player.GetMaxHealth(), player.GetMaxStamina(), player.GetMaxEitr() })[i] - 1
                ).All(
                    v => v
                ),
                "H07 resource deficit fixture failed"
            );
            void Click(string name)
            {
                var route = UiDispatch(UiButton(name), false);
                Need((bool)route["hitsTarget"], "H07 button missed " + name);
            }
            void ItemClick(ItemDrop.ItemData item, bool right)
            {
                Need((bool)UiDispatch(UiInventoryItem(item), right)["hitsTarget"], "H07 inventory click missed");
            }
            foreach (string actionName in new[] { "track", "cassette", "equipment", "drop-pickup" })
            {
                yield return CaseFreshRecovery();
                var ticks = CaseRecoveryTicks();
                var resources = CaseResources(player);
                float actionStart = Time.realtimeSinceStartup;
                InventoryGui.instance.Show(null, 1);
                yield return new WaitForSecondsRealtime(.3f);
                if (actionName == "track")
                {
                    int before = deck.State.trackIndex;
                    Click("BoomboxPlayerNext");
                    yield return new WaitForSecondsRealtime(.2f);
                    Need(deck.State.trackIndex != before, "H07 next track did not change");
                }
                else if (actionName == "cassette")
                {
                    Click("BoomboxMusicButton");
                    yield return new WaitForSecondsRealtime(.2f);
                    Click("Tab3");
                    yield return new WaitForSecondsRealtime(.2f);
                    Click("CassetteEject");
                    yield return new WaitForSecondsRealtime(.15f);
                    Need(deck.State.insertedCassette == null, "H07 cassette did not eject");
                    Click("Close");
                    yield return new WaitForSecondsRealtime(.1f);
                    var physical = player.GetInventory().GetAllItems().Single(
                        i => BoomboxItemData.IsCassette(i) && BoomboxItemData.Read(i).cassette.id == tape
                    );
                    ItemClick(physical, true);
                    yield return new WaitForSecondsRealtime(.2f);
                    Need(deck.State.insertedCassette?.id == tape, "H07 cassette did not reinsert");
                }
                else if (actionName == "equipment")
                {
                    ItemClick(deck.Item, true);
                    yield return UiAwait(() => BoomboxEquipment.Get(player) != deck.Item, "H07 unequip");
                    ItemClick(deck.Item, true);
                    yield return UiAwait(() => BoomboxEquipment.Get(player) == deck.Item, "H07 equip");
                }
                else
                {
                    ItemClick(deck.Item, false);
                    yield return null;
                    var route = UiDispatch((RectTransform)InventoryGui.instance.m_dropButton.transform, false);
                    Need((bool)route["hitsTarget"], "H07 native outside-drop button missed");
                    yield return new WaitForSecondsRealtime(.15f);
                    var drop = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Single(
                        d => BoomboxItemData.IsDevice(d.m_itemData) && BoomboxItemData.Read(d.m_itemData).deck.id == id
                    );
                    drop.GetComponent<Rigidbody>().isKinematic = true;
                    InventoryGui.instance.Hide();
                    distance.SetValue(GameCamera.instance, 0f);
                    GameObject hover = null;
                    for (int n = 0; n < 12; n++)
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
                        if (hover != null && hover.GetComponentInParent<ItemDrop>() == drop)
                            break;
                    }
                    Need(hover != null && hover.GetComponentInParent<ItemDrop>() == drop, "H07 pickup aim missed");
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
                    yield return null;
                    yield return null;
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    yield return UiAwait(
                        () => BoomboxEquipment.Get(player) != null
                            && BoomboxItemData.Read(BoomboxEquipment.Get(player)).deck.id == id,
                        "H07 native pickup/equip"
                    );
                    InventoryGui.instance.Show(null, 1);
                    yield return new WaitForSecondsRealtime(.2f);
                }
                if (!deck.State.playing)
                {
                    Click("BoomboxPlayerPlay");
                    yield return new WaitForSecondsRealtime(.2f);
                }
                float duration = Time.realtimeSinceStartup - actionStart;
                Need(duration < 4.5f, "H07 action exceeded pre-next-tick observation window");
                var afterTicks = CaseRecoveryTicks();
                var afterResources = CaseResources(player);
                Need(ticks.SequenceEqual(afterTicks), "H07 action granted an immediate extra tick");
                Need(
                    afterResources.Select((v, i) => v <= resources[i] + .1f).All(v => v),
                    "H07 action granted immediate resource recovery"
                );
                rows.Add(new JObject
                {
                    ["action"] = actionName,
                    ["duration"] = duration,
                    ["ticksBefore"] = new JArray(ticks),
                    ["ticksAfter"] = new JArray(afterTicks),
                    ["resourcesBefore"] = new JArray(resources),
                    ["resourcesAfter"] = new JArray(afterResources)
                });
                InventoryGui.instance.Hide();
            }
            proof["seconds"] = Time.realtimeSinceStartup - started;
            proof["passed"] = true;
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            Freeze(player, false);
            distance.SetValue(GameCamera.instance, oldDistance);
            InputSystem.settings.backgroundBehavior = input;
            settings.Health.Interval.Value = periods[0];
            settings.Stamina.Interval.Value = periods[1];
            settings.Eitr.Interval.Value = periods[2];
            settings.ShowPlayerPanel.Value = panel;
        }
    }
}
