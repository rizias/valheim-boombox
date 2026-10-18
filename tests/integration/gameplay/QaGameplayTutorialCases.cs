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
    private static string TutorialCheckpoint => Path.Combine(BepInEx.Paths.ConfigPath, "qa-L01-checkpoint.json");
    private static string[] TutorialTopics => new[] { "assembly", "equipment" };
    private static HashSet<string> TutorialSeen(
        Player player
    ) => (HashSet<string>)AccessTools.Field(typeof(Player), "m_shownTutorials").GetValue(player);
    private static Dictionary<string, string> TutorialKnown(
        Player player
    ) => (Dictionary<string, string>)AccessTools.Field(typeof(Player), "m_knownTexts").GetValue(player);
    private static string TutorialTopic(
        object text
    ) => (string)AccessTools.Field(text.GetType(), "m_key").GetValue(text);
    private static IList TutorialQueue => (IList)AccessTools.Field(typeof(Raven), "m_tempTexts").GetValue(null);
    private IEnumerator CaseTutorials(bool save, bool rereadOnly = false)
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var position = player.transform.position;
        var rotation = player.transform.rotation;
        bool enabled = Raven.m_tutorialsEnabled, hadPreference = PlatformPrefs.HasKey("TutorialsEnabled");
        int preference = PlatformPrefs.GetInt("TutorialsEnabled", 1);
        var input = InputSystem.settings.backgroundBehavior;
        var frames = new JArray();
        var distance = AccessTools.Field(typeof(GameCamera), "m_distance");
        float oldDistance = (float)distance.GetValue(GameCamera.instance);
        var queued = (HashSet<string>)AccessTools.Field(typeof(BoomboxLocalization), "queuedTopics").GetValue(null);
        var oldQueue = queued.ToArray();
        var temporary = TutorialQueue.Cast<object>().ToArray();
        var checkpoint = save || rereadOnly
            ? new JObject
            {
                ["sha"] = productSha,
                ["seen"] = new JArray(TutorialSeen(player)),
                ["texts"] = JObject.FromObject(TutorialKnown(player))
            }
            : JObject.Parse(File.ReadAllText(TutorialCheckpoint));
        var proof =
            new JObject
            {
                ["case"] = save ? "L-01" : "L-02/L-03",
                ["frames"] = frames,
                ["scope"] =
                    "Real empty-device/equip triggers, native Raven spawn preparation and E interaction; native character save/reload, compendium selection. Tutorial setting uses the existing native GameplaySettings callback path."
            };
        lastCapture = proof;
        Raven raven = null;
        bool oldAuto = true;
        try
        {
            Need((string)checkpoint["sha"] == productSha, "Tutorial checkpoint candidate mismatch");
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            TutorialQueue.Clear();
            Directory.CreateDirectory(Path.Combine(BepInEx.Paths.ConfigPath, "capture"));
            proof["fpsLimit"] = Application.targetFrameRate;
            if (rereadOnly)
            {
                proof["case"] = "L-03";
                proof["preReadFixture"] = true;
                foreach (string topic in TutorialTopics)
                {
                    player.SetSeenTutorial("boombox_" + topic);
                    player.AddKnownText(
                        "$boombox_tutorial_" + topic + "_topic",
                        "$boombox_tutorial_" + topic + "_text"
                    );
                }
            }
            if (save)
            {
                Raven.m_tutorialsEnabled = true;
                foreach (string topic in TutorialTopics)
                {
                    TutorialSeen(player).Remove("boombox_" + topic);
                    queued.Remove("boombox_" + topic);
                    TutorialKnown(player).Remove("$boombox_tutorial_" + topic + "_topic");
                }
                var item = BoomboxItemPrefabs.NewItem(BoomboxItemData.DevicePrefab);
                item.m_customData["boombox.qa.fixture"] = "L01";
                Need(player.GetInventory().AddItem(item), "L01 empty device preparation failed");
                player.AddKnownItem(item);
                plugin.Items.Update();
                raven = UnityEngine.Object.FindObjectsByType<Raven>(FindObjectsSortMode.None).First(r => !r.m_isMunin);
                oldAuto = raven.m_autoTalk;
                raven.m_autoTalk = false;
                foreach (string topic in TutorialTopics)
                {
                    if (topic == "equipment")
                    {
                        InventoryGui.instance.Show(null, 1);
                        yield return new WaitForSecondsRealtime(.5f);
                        Need((bool)UiDispatch(UiInventoryItem(item), true)["hitsTarget"], "L01 equip pointer missed");
                        yield return UiAwait(() => BoomboxEquipment.Get(player) == item, "L01 equipped");
                        InventoryGui.instance.Hide();
                    }
                    var text = TutorialQueue.Cast<object>().SingleOrDefault(t => TutorialTopic(t) == "boombox_" + topic);
                    Need(text != null, "L01 action did not queue " + topic);
                    string body = Localization.instance.Localize("$boombox_tutorial_" + topic + "_text");
                    Need(
                        body.Length > 40 && !body.Contains("$boombox_") && !body.Contains("fusion"),
                        "L01 unresolved/obsolete tutorial"
                    );
                    AccessTools.Method(typeof(Raven), "Spawn").Invoke(raven, new[] { text, (object)true });
                    yield return TransportWait(
                        () => (bool)AccessTools.Method(typeof(Raven), "IsSpawned").Invoke(raven, null),
                        "L01 raven spawn failed",
                        8
                    );
                    CasePlatformPlayer(player, raven.transform.position + Vector3.back * 1.4f, Quaternion.identity);
                    player.GetComponent<ZSyncTransform>().SyncNow();
                    distance.SetValue(GameCamera.instance, 0f);
                    GameObject hover = null;
                    var aimLog = new JArray();
                    proof["aiming"] = aimLog;
                    proof["ravenColliders"] = new JArray(
                        raven.GetComponentsInChildren<Collider>(true).Select(
                            c => c.name + ":" + LayerMask.LayerToName(c.gameObject.layer) + ":" + c.enabled + ":"
                                + c.isTrigger
                        )
                    );
                    for (int n = 0; n < 15; n++)
                    {
                        var target = raven.GetComponentsInChildren<Collider>().FirstOrDefault(c => c.enabled);
                        var aim = (target != null
                            ? target.bounds.center
                            : raven.transform.position + Vector3.up * .5f) - GameCamera.instance.transform.position;
                        player.transform.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
                        player.SetLookDir(aim, 0);
                        AccessTools.Field(typeof(Player), "m_lookPitch").SetValue(
                            player,
                            Mathf.Atan2(-aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg
                        );
                        yield return new WaitForSecondsRealtime(.1f);
                        hover = player.GetHoverObject();
                        aimLog.Add(hover == null ? "none" : hover.name);
                        if (hover != null && hover.GetComponentInParent<Raven>() == raven)
                            break;
                    }
                    Need(
                        hover != null
                            && hover.GetComponentInParent<Raven>() == raven,
                        "L01 native hover missed raven"
                    );
                    Need(!player.HaveSeenTutorial("boombox_" + topic), "L01 tutorial was read before E");
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
                    yield return null;
                    yield return null;
                    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                    yield return TransportWait(
                        () => player.HaveSeenTutorial("boombox_" + topic)
                            && Chat.instance.IsDialogVisible(raven.gameObject),
                        "L01 E did not read tutorial",
                        3
                    );
                    Need(
                        TutorialKnown(player).ContainsKey("$boombox_tutorial_" + topic + "_topic"),
                        "L01 unique compendium entry missing"
                    );
                    yield return UiFrame("L01-" + topic + ".png", frames);
                    Chat.instance.ClearNpcText(raven.gameObject);
                }
                proof["readViaE"] = new JArray(TutorialTopics);
                proof["distinctEntries"] = true;
                foreach (string topic in TutorialTopics)
                    TutorialKnown(player).Remove("$boombox_tutorial_" + topic + "_topic");
                TutorialKnown(player)["$boombox_name"] = "$boombox_tutorial_assembly_text";
                Clear();
                CasePlatformPlayer(player, position, rotation);
                player.GetComponent<ZSyncTransform>().SyncNow();
                Game.instance.SavePlayerProfile(true);
                File.WriteAllText(TutorialCheckpoint, checkpoint.ToString());
                proof["legacySharedLabelPrepared"] = true;
                proof["passed"] = true;
                yield break;
            }
            foreach (string topic in TutorialTopics)
            {
                Need(player.HaveSeenTutorial("boombox_" + topic), "L02 read flag lost on reload");
                Need(
                    TutorialKnown(player).TryGetValue("$boombox_tutorial_" + topic + "_topic", out var text)
                    && text == "$boombox_tutorial_" + topic + "_text",
                    "L03 legacy shared-label entry was not repaired"
                );
                int before = TutorialQueue.Count;
                BoomboxLocalization.Tutorial(topic);
                Need(TutorialQueue.Count == before, "L02 read topic queued again");
            }
            if (!rereadOnly)
            {
                proof["reloadPassed"] = true;
                proof["legacyEntriesRepaired"] = true;
            }
            foreach (string topic in TutorialTopics)
            {
                InventoryGui.instance.Show(null, 1);
                yield return new WaitForSecondsRealtime(.3f);
                InventoryGui.instance.OnOpenTexts();
                yield return new WaitForSecondsRealtime(.3f);
                Need(
                    InventoryGui.IsVisible()
                        && InventoryGui.instance.m_textsDialog.gameObject.activeInHierarchy
                        && !player.InCutscene(),
                    "L03 inventory/compendium not open"
                );
                var dialog = InventoryGui.instance.m_textsDialog;
                var entries = (IList)AccessTools.Field(typeof(TextsDialog), "m_texts").GetValue(dialog);
                string heading = Localization.instance.Localize("$boombox_tutorial_" + topic + "_topic");
                var entry = entries.Cast<object>().Single(
                    e => (string)AccessTools.Field(e.GetType(), "m_topic").GetValue(e) == heading
                );
                var element = (GameObject)AccessTools.Field(entry.GetType(), "m_listElement").GetValue(entry);
                var button = element.GetComponentInChildren<Button>(true);
                var ensure = AccessTools.Field(typeof(TextsDialog), "m_recipeEnsureVisible").GetValue(dialog);
                AccessTools.Method(ensure.GetType(), "CenterOnItem").Invoke(
                    ensure,
                    new object[] { (RectTransform)element.transform }
                );
                yield return new WaitForSecondsRealtime(.3f);
                var click = UiDispatch((RectTransform)button.transform, false);
                proof["rereadClick"] = click;
                if (!(bool)click["hitsTarget"])
                    yield return UiFrame("L03-pointer-failure.png", frames);
                Need((bool)click["hitsTarget"], "L03 compendium entry pointer missed");
                yield return null;
                Need(
                    ((TMP_Text)AccessTools.Field(typeof(TextsDialog), "m_textArea").GetValue(dialog)).text
                        == Localization.instance.Localize("$boombox_tutorial_" + topic + "_text"),
                    "L03 reread text differs"
                );
                yield return UiFrame("L03-" + topic + ".png", frames);
                AccessTools.Method(typeof(TextsDialog), "OnClose").Invoke(dialog, null);
                InventoryGui.instance.Hide();
            }
            proof["rereadPassed"] = true;
            var menu = UnityEngine.Object.FindFirstObjectByType<Menu>();
            AccessTools.Method(typeof(Menu), "Show").Invoke(menu, null);
            AccessTools.Method(typeof(Menu), "OnSettings").Invoke(menu, null);
            yield return null;
            var settingsWindow = UnityEngine.Object.FindFirstObjectByType<Settings>();
            try
            {
                var type = AccessTools.TypeByName("Valheim.SettingsGui.GameplaySettings");
                var panel = UnityEngine.Object.FindObjectsByType(
                    type,
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
                    .Single();
                var toggle = (Toggle)AccessTools.Field(type, "m_tutorialsEnabled").GetValue(panel);
                toggle.isOn = false;
                AccessTools.Method(type, "OnOkAsync").Invoke(panel, new object[] { null });
                Need(
                    !Raven.m_tutorialsEnabled && PlatformPrefs.GetInt("TutorialsEnabled", -1) == 0,
                    "L03 native tutorial setting did not apply"
                );
            }
            finally
            {
                if (settingsWindow != null)
                    UnityEngine.Object.Destroy(settingsWindow.gameObject);
                AccessTools.Method(typeof(Menu), "Hide").Invoke(menu, null);
                Game.Unpause();
            }
            raven = UnityEngine.Object.FindObjectsByType<Raven>(FindObjectsSortMode.None).First(r => !r.m_isMunin);
            oldAuto = raven.m_autoTalk;
            AccessTools.Method(typeof(Raven), "FlyAway").Invoke(raven, new object[] { true });
            yield return TransportWait(
                () => (bool)AccessTools.Method(typeof(Raven), "IsAway").Invoke(raven, null),
                "L03 bird did not hide",
                4
            );
            TutorialSeen(player).Remove("boombox_assembly");
            queued.Remove("boombox_assembly");
            BoomboxLocalization.Tutorial("assembly");
            var disabledText = TutorialQueue.Cast<object>().First(t => TutorialTopic(t) == "boombox_assembly");
            AccessTools.Method(typeof(Raven), "Spawn").Invoke(raven, new[] { disabledText, (object)true });
            yield return new WaitForSecondsRealtime(.4f);
            Need(
                !(bool)AccessTools.Method(typeof(Raven), "IsSpawned").Invoke(raven, null),
                "L03 disabled tutorial appeared"
            );
            proof["disabledPassed"] = true;
            proof["passed"] = true;
        }
        finally
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.settings.backgroundBehavior = input;
            distance.SetValue(GameCamera.instance, oldDistance);
            Raven.m_tutorialsEnabled = enabled;
            if (hadPreference)
                PlatformPrefs.SetInt("TutorialsEnabled", preference);
            else
                PlatformPrefs.DeleteKey("TutorialsEnabled");
            if (raven != null)
            {
                raven.m_autoTalk = oldAuto;
                AccessTools.Method(typeof(Raven), "FlyAway").Invoke(raven, new object[] { true });
            }
            queued.Clear();
            queued.UnionWith(oldQueue);
            TutorialQueue.Clear();
            foreach (var text in temporary)
                TutorialQueue.Add(text);
            plugin.CloseMenu();
            InventoryGui.instance.Hide();
            CasePlatformPlayer(player, position, rotation);
            player.GetComponent<ZSyncTransform>().SyncNow();
            Freeze(player, false);
            if (!save)
            {
                TutorialSeen(player).Clear();
                TutorialSeen(player).UnionWith(checkpoint["seen"].Select(v => (string)v));
                var known = TutorialKnown(player);
                known.Clear();
                foreach (var pair in (JObject)checkpoint["texts"])
                    known[pair.Key] = (string)pair.Value;
                Game.instance.SavePlayerProfile(true);
            }
        }
    }
}
