using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class QaGameplay
{
    private static JArray uiClicks;
    private bool UiOperation(string operation, JObject request)
    {
        if (operation == "ui-complete")
        {
            busy = true;
            StartCoroutine(GuardAudio(UiComplete(request)));
            return true;
        }
        if (operation == "ui-persist")
        {
            var plugin = VikingBoombox.BoomboxPlugin.Instance;
            var item = Player.m_localPlayer.GetInventory().GetAllItems().Single(
                i => VikingBoombox.BoomboxItemData.IsDevice(i)
                    && VikingBoombox.BoomboxItemData.Read(i).deck.id == UiIdentity("device")
            );
            sample = new JObject
            {
                ["device"] = JObject.FromObject(VikingBoombox.BoomboxItemData.Read(item).deck),
                ["playlists"] = JArray.FromObject(plugin.MusicLibrary.playlists),
                ["extraSlot"] = VikingBoombox.BoomboxEquipment.SlotPosition?.ToString()
            };
            return true;
        }
        if (operation == "ui-slots")
        {
            busy = true;
            StartCoroutine(GuardAudio(UiSlots(request)));
            return true;
        }
        if (operation != "ui-recipe")
            return false;
        busy = true;
        StartCoroutine(GuardAudio(UiRecipe(request)));
        return true;
    }

    private IEnumerator UiRecipe(JObject request)
    {
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        UiParkWindow();
        var originalKeyboard = Keyboard.current;
        var originalMouse = Mouse.current;
        var keyboard = originalKeyboard == null ? InputSystem.AddDevice<Keyboard>("BoomboxAcceptanceKeyboard") : null;
        var mouse = originalMouse == null ? InputSystem.AddDevice<Mouse>("BoomboxAcceptanceMouse") : null;
        keyboard?.MakeCurrent();
        mouse?.MakeCurrent();
        try
        {
            var body = UiRecipeBody(request);
            using (body as IDisposable)
            {
                while (body.MoveNext())
                    yield return body.Current;
            }
        }
        finally
        {
            if (mouse != null)
                InputSystem.RemoveDevice(mouse);
            if (keyboard != null)
                InputSystem.RemoveDevice(keyboard);
            originalKeyboard?.MakeCurrent();
            originalMouse?.MakeCurrent();
        }
    }

    private IEnumerator UiRecipeBody(JObject request)
    {
        if ((bool?)request["foldersOnly"] == true)
        {
            var resumedPlayer = Player.m_localPlayer;
            var resumedDevice = resumedPlayer.GetInventory().GetAllItems().Single(
                i => VikingBoombox.BoomboxItemData.IsDevice(i)
                    && VikingBoombox.BoomboxItemData.Read(i).deck.id == ((string)request["carry"] ?? UiIdentity("device"))
            );
            var resumedProof =
                new JObject
                {
                    ["scope"] = "Only remaining UI03 folder/play/eject; prior UI01/02 preserved and not repeated",
                    ["preparation"] =
                        "Native InventoryGui.Show opens the already accepted inventory route; no craft/insert/equip lower method",
                    ["device"] = VikingBoombox.BoomboxItemData.Read(resumedDevice).deck.id,
                    ["cassette"] = VikingBoombox.BoomboxItemData.Read(resumedDevice).deck.insertedCassette?.id
                };
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(.3f);
            sample = resumedProof;
            uiClicks = new JArray();
            yield return UiRoute(resumedDevice, (string)request["musicFolder"], resumedProof, true);
            resumedProof["utc"] = DateTime.UtcNow.ToString("O");
            yield break;
        }
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("UI recipe requires native graphics");
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        UiParkWindow();
        if (Keyboard.current == null || Mouse.current == null)
            throw new InvalidOperationException("InputSystem devices missing");
        if (!Keyboard.current.enabled)
            InputSystem.EnableDevice(Keyboard.current);
        if (!Mouse.current.enabled)
            InputSystem.EnableDevice(Mouse.current);
        yield return null;
        yield return null;
        uiClicks = new JArray();
        var module = EventSystem.current.currentInputModule as InputSystemUIInputModule;
        var input = new JObject
        {
            ["mouse"] = Mouse.current.name,
            ["keyboard"] = Keyboard.current.name,
            ["point"] = UiAction(module?.point?.action),
            ["left"] = UiAction(module?.leftClick?.action),
            ["right"] = UiAction(module?.rightClick?.action)
        };
        var pointerMice = module?.point?.action?.controls.Select(c => c.device).OfType<Mouse>().Distinct().ToArray()
            ?? new Mouse[0];
        var clickMice = module?.leftClick?.action?.controls.Select(c => c.device).OfType<Mouse>().Distinct().ToArray()
            ?? new Mouse[0];
        var boundMouse = pointerMice.FirstOrDefault(clickMice.Contains);
        boundMouse?.MakeCurrent();
        var buttons = (IDictionary)AccessTools.Field(typeof(ZInput), "m_buttons").GetValue(
            AccessTools.Field(typeof(ZInput), "m_instance").GetValue(null)
        );
        var use = buttons["Use"];
        var useAction = AccessTools.Property(use.GetType(), "ButtonAction").GetValue(use, null) as InputAction;
        input["use"] = UiAction(useAction);
        var boundKeyboard = useAction?.controls.Select(c => c.device).OfType<Keyboard>().FirstOrDefault();
        boundKeyboard?.MakeCurrent();
        input["selectedMouse"] = Mouse.current.name;
        input["selectedKeyboard"] = Keyboard.current.name;
        input["boundMouse"] = boundMouse != null;
        input["boundKeyboard"] = boundKeyboard != null;
        if (ZNet.instance.InPasswordDialog())
            throw new InvalidOperationException("Native automatic handshake left password dialog active");
        var player = Player.m_localPlayer;
        player.SetIntro(false);
        player.SetGodMode(true);
        if (player.IsDead())
            throw new InvalidOperationException("UI preparation requires alive player");
        var initial = new JObject
        {
            ["player"] = player.GetPlayerName(),
            ["knownRecipe"] = player.IsRecipeKnown("$boombox_name"),
            ["seenAssembly"] = player.HaveSeenTutorial("boombox_assembly"),
            ["devices"] = player.GetInventory().GetAllItems().Count(i => VikingBoombox.BoomboxItemData.IsDevice(i))
        };
        var shown = (HashSet<string>)AccessTools.Field(typeof(Player), "m_shownTutorials").GetValue(player);
        var previousShown = shown.Where(s => s.StartsWith("boombox_", StringComparison.Ordinal)).ToArray();
        try
        {
            shown.RemoveWhere(s => s.StartsWith("boombox_", StringComparison.Ordinal));
            ((HashSet<string>)AccessTools.Field(typeof(VikingBoombox.BoomboxLocalization), "queuedTopics")
                .GetValue(null)).Clear();
            var queued = (IList)AccessTools.Field(typeof(Raven), "m_tempTexts").GetValue(null);
            for (int i = queued.Count - 1; i >= 0; i--)
                if (((string)AccessTools.Field(queued[i].GetType(), "m_key").GetValue(queued[i])).StartsWith(
                    "boombox_",
                    StringComparison.Ordinal
                ))
                    queued.RemoveAt(i);
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
                obj.GetComponent<MeshRenderer>().enabled = false;
                fixtures.Add(obj);
            }
            player.transform.position = origin + Vector3.forward * 1.5f;
            Freeze(player, true);
            Physics.SyncTransforms();
            foreach (var pair in new[] {
                new { name = "Wood", count = 12 },
                new { name = "Resin", count = 7 },
                new { name = "LeatherScraps", count = 2 },
                new { name = "BoneFragments", count = 3 },
                new { name = "Coins", count = 50 }
            })
            {
                var prefab = ObjectDB.instance.GetItemPrefab(pair.name);
                var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_stack = pair.count;
                if (!player.GetInventory().AddItem(item))
                    throw new InvalidOperationException("Raw-material preparation inventory full: " + pair.name);
                player.AddKnownItem(item);
            }
            Localization.instance.SetLanguage("Russian");
            yield return new WaitForSecondsRealtime(1);
            var distance = AccessTools.Field(typeof(GameCamera), "m_distance");
            var previousDistance = (float)distance.GetValue(GameCamera.instance);
            distance.SetValue(GameCamera.instance, 0f);
            GameObject hover = null;
            var aiming = new JArray();
            for (int i = 0; i < 8; i++)
            {
                var aim = origin + Vector3.up * (.2f + i * .2f) - player.GetEyePoint();
                player.SetMoveDir(Vector3.zero);
                player.transform.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
                player.SetLookDir(aim, 0);
                AccessTools.Field(typeof(Player), "m_lookPitch").SetValue(
                    player,
                    Mathf.Atan2(-aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg
                );
                yield return new WaitForSecondsRealtime(.15f);
                hover = player.GetHoverObject();
                aiming.Add(new JObject
                {
                    ["camera"] = Point(GameCamera.instance.transform.position),
                    ["forward"] = Point(GameCamera.instance.transform.forward),
                    ["eye"] = Point(player.GetEyePoint()),
                    ["bench"] = Point(origin),
                    ["hover"] = hover?.name
                });
                if (hover != null && hover.GetComponentInParent<CraftingStation>() == station)
                    break;
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
            yield return null;
            yield return null;
            bool keyRegistered = Keyboard.current.eKey.isPressed;
            yield return new WaitForSecondsRealtime(.1f);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(.5f);
            bool openedByInput = InventoryGui.IsVisible() && player.GetCurrentCraftingStation() == station;
            distance.SetValue(GameCamera.instance, previousDistance);
            var proof =
                new JObject
                {
                    ["route"] = "InputSystem.QueueStateEvent -> native input -> UI raycast -> handler",
                    ["focused"] = Application.isFocused,
                    ["keyRegistered"] = keyRegistered,
                    ["hover"] = hover?.name,
                    ["hoverStation"] = hover != null && hover.GetComponentInParent<CraftingStation>() == station,
                    ["openedByInput"] = openedByInput,
                    ["preparation"] =
                        "Spawned roofed native workbench, numeric alive position/freeze, raw materials and AddKnownItem discovery; no crafted product injected",
                    ["module"] = UnityEngine.EventSystems.EventSystem.current?.currentInputModule?.GetType().FullName
                };
            proof["passwordDialog"] = ZNet.instance.InPasswordDialog();
            proof["aiming"] = aiming;
            proof["inputBindings"] = input;
            if (!openedByInput)
            {
                sample = proof;
                throw new InvalidOperationException("Native use input did not open the crafting station");
            }
            var recipe = ObjectDB.instance.m_recipes.Single(r => r != null && r.name == "Recipe_ValheimBoombox_Item");
            var icons = recipe.m_item.m_itemData.m_shared.m_icons;
            var button = InventoryGui.instance.m_recipeListRoot.GetComponentsInChildren<Image>(false).Where(
                image => image.sprite != null && icons.Contains(image.sprite)
            ).Select(
                image => image.GetComponentInParent<Button>()
            ).FirstOrDefault(
                b => b != null && b.gameObject.activeInHierarchy
            );
            proof["initial"] = initial;
            proof["tutorialPreparation"] =
                "Only boombox seen flags and per-session queued guard cleared on existing QA actor; actor is not claimed fresh";
            proof["recipeButtonVisible"] = button != null;
            if (button != null)
                yield return UiClick((RectTransform)button.transform, false);
            var selected = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe").GetValue(InventoryGui.instance);
            var selectedRecipe = selected == null
                ? null
                : AccessTools.Property(selected.GetType(), "Recipe")?.GetValue(selected, null) as Recipe
                    ?? AccessTools.Field(selected.GetType(), "Recipe")?.GetValue(selected) as Recipe;
            proof["recipeSelectedByInput"] = selectedRecipe == recipe;
            if (button != null && selectedRecipe != recipe)
            {
                proof["recipeEventDispatch"] = UiDispatch((RectTransform)button.transform, false);
                yield return new WaitForSecondsRealtime(.2f);
                selected = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe").GetValue(InventoryGui.instance);
                selectedRecipe = selected == null
                    ? null
                    : AccessTools.Property(selected.GetType(), "Recipe")?.GetValue(selected, null) as Recipe
                        ?? AccessTools.Field(selected.GetType(), "Recipe")?.GetValue(selected) as Recipe;
            }
            proof["recipeSelectedByUiEvent"] = selectedRecipe == recipe;
            proof["required"] = new JArray(
                recipe.m_resources.Select(
                    r => new JObject { ["prefab"] = r.m_resItem.gameObject.name, ["amount"] = r.m_amount }
                )
            );
            proof["requirementArrayLength"] = InventoryGui.instance.m_recipeRequirementList.Length;
            var rendered = InventoryGui.instance.m_recipeRequirementList.Concat(
                InventoryGui.instance.GetComponentsInChildren<Transform>(true).Where(
                    t => t.name == "BoomboxRecipeRequirement5"
                ).Select(
                    t => t.gameObject
                )
            ).Distinct().ToArray();
            proof["requirements"] = new JArray(
                rendered.Select(
                    obj => new JObject
                    {
                        ["name"] = obj.name,
                        ["active"] = obj.activeInHierarchy,
                        ["position"] = Point(((RectTransform)obj.transform).anchoredPosition),
                        ["size"] = Point(((RectTransform)obj.transform).rect.size),
                        ["texts"] = new JArray(
                        obj.GetComponentsInChildren<TMP_Text>(true).Select(
                            t => new JObject
                            {
                                ["name"] = t.name,
                                ["text"] = t.text,
                                ["active"] = t.gameObject.activeInHierarchy
                            }
                        )
                    )
                    }
                )
            );
            yield return new WaitForEndOfFrame();
            var pixels = ScreenCapture.CaptureScreenshotAsTexture();
            var bytes = pixels.EncodeToPNG();
            UnityEngine.Object.Destroy(pixels);
            if (bytes.Length > 8 * 1024 * 1024)
                throw new InvalidOperationException("UI capture exceeds bound");
            var folder = Path.Combine(BepInEx.Paths.ConfigPath, "capture");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "ui-01-recipe.png");
            File.WriteAllBytes(path, bytes);
            proof["png"] = path;
            proof["pngSha"] = Hash(path);
            proof["pngBytes"] = bytes.Length;
            proof["width"] = Screen.width;
            proof["height"] = Screen.height;
            var resourceNames = recipe.m_resources.Select(r => r.m_resItem.gameObject.name).ToArray();
            Func<JObject> counts = () => new JObject(
                resourceNames.Select(
                    name => new JProperty(
                        name,
                        player.GetInventory().GetAllItems().Where(
                            i => i.m_dropPrefab != null && i.m_dropPrefab.name == name
                        ).Sum(
                            i => i.m_stack
                        )
                    )
                )
            );
            Func<JArray> raven = () => new JArray(
                ((IList)AccessTools.Field(typeof(Raven), "m_tempTexts").GetValue(null)).Cast<object>().Select(
                    t => (string)AccessTools.Field(t.GetType(), "m_key").GetValue(t)
                )
            );
            var before = counts();
            var itemsBefore = player.GetInventory().GetAllItems().Where(VikingBoombox.BoomboxItemData.IsDevice).ToArray();
            proof["resourcesBefore"] = before;
            proof["ravenBefore"] = raven();
            proof["craftEnabled"] = InventoryGui.instance.m_craftButton.IsInteractable();
            proof["noCost"] = player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost);
            var carried = itemsBefore.FirstOrDefault(i => VikingBoombox.BoomboxItemData.Read(i).deck.id
                == (string)request["carry"]);
            if (carried != null)
                proof["carryPriorUiCraft"] = VikingBoombox.BoomboxItemData.Read(carried).deck.id;
            if (carried == null && selectedRecipe == recipe && InventoryGui.instance.m_craftButton.IsInteractable())
            {
                yield return UiClick((RectTransform)InventoryGui.instance.m_craftButton.transform, false);
                bool craftStarted = (float)AccessTools.Field(typeof(InventoryGui), "m_craftTimer")
                    .GetValue(InventoryGui.instance)
                    >= 0
                    || player.GetInventory().GetAllItems().Count(VikingBoombox.BoomboxItemData.IsDevice)
                    > itemsBefore.Length;
                proof["craftStartedByInput"] = craftStarted;
                if (!craftStarted)
                    proof["craftEventDispatch"] = UiDispatch(
                        (RectTransform)InventoryGui.instance.m_craftButton.transform,
                        false
                    );
                float until = Time.realtimeSinceStartup + 12;
                while (Time.realtimeSinceStartup < until
                && player.GetInventory().GetAllItems().Count(VikingBoombox.BoomboxItemData.IsDevice) == itemsBefore.Length)
                    yield return null;
            }
            var created = player.GetInventory().GetAllItems().FirstOrDefault(
                i => VikingBoombox.BoomboxItemData.IsDevice(i) && !itemsBefore.Contains(i)
            );
            proof["resourcesAfter"] = counts();
            proof["created"] = created == null ? null : VikingBoombox.BoomboxItemData.Read(created).deck.id;
            if (created != null)
                RememberUiIdentity("device", VikingBoombox.BoomboxItemData.Read(created).deck.id);
            float tutorialUntil = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < tutorialUntil && !UiTutorial("assembly")["observed"].Value<bool>())
                yield return null;
            proof["ravenAfter"] = raven();
            proof["tutorial"] = UiTutorial("assembly");
            sample = proof;
            if ((bool?)request["tail"] == true)
                yield return UiRoute(carried ?? created, (string)request["musicFolder"], proof);
            proof["utc"] = DateTime.UtcNow.ToString("O");
            proof["clicks"] = uiClicks;
            sample = proof;
            busy = false;
        }
        finally
        {
            foreach (var name in previousShown)
                shown.Add(name);
        }
    }

    private static IEnumerator UiClick(RectTransform rect, bool right)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var state = new MouseState
        {
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center))
        };
        var hit = UiRaycast(rect, right);
        var log = new JObject
        {
            ["target"] = rect.name,
            ["device"] = Mouse.current.name,
            ["position"] = Point(state.position),
            ["raycast"] = hit.gameObject?.name,
            ["hitsTarget"] = hit.gameObject != null && hit.gameObject.transform.IsChildOf(rect)
        };
        uiClicks.Add(log);
        InputSystem.QueueStateEvent(Mouse.current, state);
        yield return null;
        yield return null;
        yield return new WaitForSecondsRealtime(.15f);
        InputSystem.QueueStateEvent(Mouse.current, state.WithButton(right ? MouseButton.Right : MouseButton.Left));
        yield return null;
        yield return null;
        yield return new WaitForSecondsRealtime(.1f);
        log["pressed"] = right ? Mouse.current.rightButton.isPressed : Mouse.current.leftButton.isPressed;
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = state.position });
        yield return null;
        yield return null;
        yield return new WaitForSecondsRealtime(.3f);
        log["released"] = !(right ? Mouse.current.rightButton.isPressed : Mouse.current.leftButton.isPressed);
    }

    private static JObject UiAction(InputAction action) => action == null
        ? null
        : new JObject
        {
            ["enabled"] = action.enabled,
            ["controls"] = new JArray(
            action.controls.Select(
                c => new JObject { ["path"] = c.path, ["device"] = c.device.name, ["id"] = c.device.deviceId }
            )
        )
        };
    private static JObject UiTutorial(string topic)
    {
        string key = "boombox_" + topic;
        var text = Tutorial.instance.m_texts.FirstOrDefault(t => t.m_name == key);
        var queued = ((IList)AccessTools.Field(typeof(Raven), "m_tempTexts").GetValue(null)).Cast<object>().Select(
            t => (string)AccessTools.Field(t.GetType(), "m_key").GetValue(t)
        ).ToArray();
        var active = UnityEngine.Object.FindObjectsByType<Raven>(FindObjectsSortMode.None).Select(
            r => AccessTools.Field(typeof(Raven), "m_currentText").GetValue(r)
        ).Where(
            t => t != null
        ).Select(
            t => (string)AccessTools.Field(t.GetType(), "m_key").GetValue(t)
        ).ToArray();
        bool seen = Player.m_localPlayer.HaveSeenTutorial(key);
        return new JObject
        {
            ["key"] = key,
            ["registered"] = text != null,
            ["topic"] = text == null ? null : Localization.instance.Localize(text.m_topic),
            ["text"] = text == null ? null : Localization.instance.Localize(text.m_text),
            ["queuedTopics"] = new JArray(
                (HashSet<string>)AccessTools.Field(typeof(VikingBoombox.BoomboxLocalization), "queuedTopics")
                    .GetValue(null)
            ),
            ["ravenQueue"] = new JArray(queued),
            ["ravenActive"] = new JArray(active),
            ["seen"] = seen,
            ["observed"] = queued.Contains(key) || active.Contains(key) || seen
        };
    }
    private static RaycastResult UiRaycast(RectTransform rect, bool right)
    {
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var data = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)),
            button = right ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        return hits.FirstOrDefault();
    }
    private static JObject UiDispatch(RectTransform rect, bool right)
    {
        var hit = UiRaycast(rect, right);
        bool target = hit.gameObject != null && hit.gameObject.transform.IsChildOf(rect);
        var log = new JObject
        {
            ["route"] = "EventSystem.RaycastAll -> IPointerDown/Up/Click native UI handler; not hardware input",
            ["raycast"] = hit.gameObject?.name,
            ["hitsTarget"] = target
        };
        if (!target)
            return log;
        var canvas = rect.GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var data = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)),
            button = right ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left,
            pointerCurrentRaycast = hit,
            pointerPressRaycast = hit,
            eligibleForClick = true
        };
        data.pressPosition = data.position;
        data.pointerPress = ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.pointerClickHandler);
        return log;
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int mode);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle,
        IntPtr after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private delegate bool UiWindowVisitor(IntPtr handle, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(UiWindowVisitor visitor, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out UiWindowRect rect);
    [StructLayout(LayoutKind.Sequential)]
    private struct UiWindowRect
    {
        public int left, top, right, bottom;
    }
    private static void UiParkWindow(bool visible = false)
    {
        Environment.SetEnvironmentVariable("BOOMBOX_QA_WINDOW_CAPTURE", visible ? "1" : null);
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var found = IntPtr.Zero;
        int area = 0;
        EnumWindows(
        (handle, state) =>
            {
                GetWindowThreadProcessId(handle, out uint pid);
                if (pid == (uint)process.Id && GetClientRect(handle, out var rect))
                {
                    int size = (rect.right - rect.left) * (rect.bottom - rect.top);
                    if (size > area)
                    {
                        area = size;
                        found = handle;
                    }
                }
                return true;
            },
        IntPtr.Zero
        );
        if (found == IntPtr.Zero)
            return;
        ShowWindow(found, 4);
        if (visible)
            SetWindowPos(found, new IntPtr(-1), GetSystemMetrics(76) + 24, GetSystemMetrics(77) + 24, 0, 0, 0x10 | 0x1);
        else
            SetWindowPos(
                found,
                new IntPtr(-2),
                GetSystemMetrics(76) + GetSystemMetrics(78) + 64,
                GetSystemMetrics(77) + GetSystemMetrics(79) + 64,
                0,
                0,
                0x10 | 0x1
            );
    }
}
