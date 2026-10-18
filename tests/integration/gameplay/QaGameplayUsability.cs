using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{

    private static readonly System.Collections.Generic.Dictionary<string, (
        double total,
        double peak,
        int count
    )> usabilityCosts = new System.Collections.Generic.Dictionary<string, (
        double,
        double,
        int
    )>();
    private static void CostStart(out long __state)
    {
        __state = System.Diagnostics.Stopwatch.GetTimestamp();
    }
    private static void CostEnd(System.Reflection.MethodBase __originalMethod, long __state)
    {
        double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - __state) * 1000d
            / System.Diagnostics.Stopwatch.Frequency;
        string name = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
        usabilityCosts.TryGetValue(name, out var prior);
        usabilityCosts[name] = (prior.total + ms, Math.Max(prior.peak, ms), prior.count + 1);
    }
    private IEnumerator UsabilityCapture()
    {
        yield return AwaitAudio();
        float started = Time.realtimeSinceStartup;
        var plugin = BoomboxPlugin.Instance;
        var player = Player.m_localPlayer;
        var rows = new JArray();
        var frames = new JArray();
        var proof = new JObject
        {
            ["cases"] = rows,
            ["frames"] = frames,
            ["passed"] = false,
            ["visualReviewRequired"] = true
        };
        lastCapture = proof;
        int width = Screen.width, height = Screen.height;
        string language = Localization.instance.GetSelectedLanguage();
        var oldTrack = audioDeck.State.TrackId;
        try
        {
            rows.Add(new JObject
            {
                ["id"] = "worn-audio",
                ["worn"] = audioDeck.State.onBack,
                ["spatialBlend"] = audioDeck.Source.spatialBlend,
                ["reverb"] = audioDeck.Source.reverbZoneMix,
                ["bypass"] = audioDeck.Source.bypassReverbZones
            });
            var vis = player.GetVisEquipment();
            var joint = vis.m_backShield;
            rows.Add(new JObject
            {
                ["id"] = "native-shield-frame",
                ["joint"] = joint.name,
                ["parent"] = joint.parent.name,
                ["anchor"] = audioDeck.Mount.BackAnchor.name,
                ["localPosition"] = Point(joint.localPosition),
                ["localRotation"] = Point(joint.localEulerAngles)
            });
            var shieldPrefab = ObjectDB.instance.GetItemPrefab("ShieldBanded");
            var shield = shieldPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
            shield.m_dropPrefab = shieldPrefab;
            Need(player.GetInventory().AddItem(shield), "No shield fixture space");
            player.EquipItem(shield, false);
            player.HideHandItems(false, false);
            yield return new WaitForSecondsRealtime(.3f);
            Camera.main.Render();
            float withShield = audioDeck.Mount.OutwardOffset;
            player.UnequipItem(shield, false);
            yield return new WaitForSecondsRealtime(.3f);
            Camera.main.Render();
            rows.Add(new JObject
            {
                ["id"] = "shield-removal",
                ["before"] = withShield,
                ["after"] = audioDeck.Mount.OutwardOffset,
                ["equipmentDepth"] = audioDeck.Mount.EquipmentSurfaceDepth
            });
            player.GetInventory().RemoveItem(shield);
            Screen.SetResolution(2560, 1440, FullScreenMode.Windowed);
            Localization.instance.SetLanguage("Russian");
            yield return new WaitForSecondsRealtime(.5f);
            plugin.Open(audioDeck);
            yield return new WaitForSecondsRealtime(.5f);
            if (!plugin.MenuOpen)
            {
                plugin.Open(audioDeck);
                yield return new WaitForSecondsRealtime(.2f);
            }
            var window = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Single(
                t => t.name == "BoomboxMenuWindow" && t.gameObject.activeInHierarchy
            );
            var handle = window.Find("WindowDragHandle").gameObject;
            var before = window.anchoredPosition;
            var pointer = new PointerEventData(
                EventSystem.current
            )
            {
                button = PointerEventData.InputButton.Left,
                delta = new Vector2(100, -40)
            };
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(handle, pointer, ExecuteEvents.dragHandler);
            yield return new WaitForSecondsRealtime(.2f);
            rows.Add(new JObject
            {
                ["id"] = "window-drag-2k",
                ["before"] = Point(before),
                ["after"] = Point(window.anchoredPosition),
                ["scale"] = Point(window.localScale),
                ["width"] = Screen.width,
                ["height"] = Screen.height
            });
            UiDispatch(UiButton("Tab4"), false);
            yield return new WaitForSecondsRealtime(.3f);
            var action = UiButton("DecorationAction").GetComponent<Button>();
            rows.Add(new JObject
            {
                ["id"] = "button-components",
                ["components"] = new JArray(action.GetComponents<Component>().Select(c => c.GetType().FullName))
            });
            var colors = new JArray();
            for (int i = 0; i < 15; i++)
            {
                yield return new WaitForEndOfFrame();
                colors.Add(action.GetComponentInChildren<TMP_Text>().color.ToString());
                yield return new WaitForSecondsRealtime(.03f);
            }
            rows.Add(
                new JObject
                {
                    ["id"] = "button-stability",
                    ["colors"] = colors,
                    ["distinct"] = colors.Select(x => (string)x).Distinct().Count()
                }
            );
            var previewInput = window.GetComponentsInChildren<Component>(true).First(c => c.GetType().Name
                == "BoomboxTrophyPreviewInput");
            var previewModel = GameObject.Find("BoomboxDecorationPreview").transform.GetChild(0);
            var position = previewModel.localPosition;
            var rotation = previewModel.localRotation;
            ExecuteEvents.Execute(previewInput.gameObject, pointer, ExecuteEvents.beginDragHandler);
            ExecuteEvents.Execute(previewInput.gameObject, pointer, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(previewInput.gameObject, pointer, ExecuteEvents.endDragHandler);
            float rotated = Quaternion.Angle(rotation, previewModel.localRotation);
            UiDispatch(UiButton("ResetPreview"), false);
            yield return null;
            rows.Add(new JObject
            {
                ["id"] = "preview-reset",
                ["rotatedDegrees"] = rotated,
                ["rotationError"] = Quaternion.Angle(rotation, previewModel.localRotation),
                ["positionError"] = Vector3.Distance(position, previewModel.localPosition)
            });
            yield return HudFrame("usability-decorations-2k.png", frames);
            UiDispatch(UiButton("Tab2"), false);
            yield return new WaitForSecondsRealtime(.2f);
            yield return HudFrame("usability-folders-2k.png", frames);
            plugin.CloseMenu();
            var timings = new JArray();
            proof["performance"] = timings;
            var profiler = new Harmony("local.boombox.qa.usability-cost");
            foreach (var method in new[] {
                AccessTools.Method(typeof(AudioDeck), "Update"),
                AccessTools.Method(typeof(BoomboxMount), "BeforeCamera"),
                AccessTools.Method(typeof(AudioDeck).Assembly.GetType("VikingBoombox.MusicEffectRunner"), "Present")
            }.Where(m => m != null))
                profiler.Patch(
                    method,
                    prefix: new HarmonyMethod(typeof(QaGameplay), nameof(CostStart)),
                    postfix: new HarmonyMethod(typeof(QaGameplay), nameof(CostEnd))
                );
            bool oldEffects = plugin.Settings.ShowVisualEffects.Value;
            try
            {
                foreach (bool enabled in new[] { false, true, false })
                {
                    plugin.Settings.ShowVisualEffects.Value = enabled;
                    usabilityCosts.Clear();
                    var samples = new System.Collections.Generic.List<float>();
                    float last = Time.realtimeSinceStartup, end = last + 1.5f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        yield return new WaitForEndOfFrame();
                        Camera.main.Render();
                        float now = Time.realtimeSinceStartup;
                        samples.Add((now - last) * 1000);
                        last = now;
                        yield return null;
                    }
                    samples.Sort();
                    timings.Add(
                        new JObject
                        {
                            ["effects"] = enabled,
                            ["frames"] = samples.Count,
                            ["medianFrameMs"] = samples[samples.Count / 2],
                            ["p95FrameMs"] = samples[(int)(samples.Count * .95f)],
                            ["manualCameraRender"] = true,
                            ["costs"] = new JArray(
                            usabilityCosts.Select(
                                k => new JObject
                                {
                                    ["method"] = k.Key,
                                    ["meanMs"] = k.Value.total / k.Value.count,
                                    ["maxMs"] = k.Value.peak,
                                    ["calls"] = k.Value.count
                                }
                            )
                        )
                        }
                    );
                }
            }
            finally
            {
                profiler.UnpatchSelf();
                plugin.Settings.ShowVisualEffects.Value = oldEffects;
            }
            plugin.PlayQueue(
                audioDeck,
                new System.Collections.Generic.List<string> { BoomboxSyntheticTrackId.ComputerAudio },
                null,
                0
            );
            yield return new WaitForSecondsRealtime(1);
            rows.Add(new JObject
            {
                ["id"] = "computer-audio",
                ["status"] = audioDeck.Status,
                ["clip"] = audioDeck.Source.clip != null,
                ["isPlaying"] = audioDeck.Source.isPlaying,
                ["syntheticActive"] = audioDeck.IsSyntheticActive
            });
            rows.Add(new JObject
            {
                ["id"] = "windows-output-reaction",
                ["status"] = "NOT_VERIFIED_QUIET_MODE",
                ["reason"] = "No audible Windows-output fixture is allowed in quiet QA."
            });
            proof["windowsOutputReactionVerified"] = false;
            audioDeck.TogglePlayback();
            yield return new WaitForSecondsRealtime(.2f);
            rows.Add(
                new JObject
                {
                    ["id"] = "computer-stop",
                    ["active"] = audioDeck.IsSyntheticActive,
                    ["status"] = audioDeck.Status
                }
            );
            proof["coreSeconds"] = Time.realtimeSinceStartup - started;
        }
        finally
        {
            plugin.CloseMenu();
            Localization.instance.SetLanguage(language);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            lastCapture = proof;
        }
    }


}
