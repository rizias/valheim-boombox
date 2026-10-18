using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VikingBoombox
{
    [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
    internal static class BoomboxNativeBuffRows
    {
        private static void Prefix(List<StatusEffect> statusEffects)
        => statusEffects.RemoveAll(effect => effect is BoomboxAuraStatus);
    }

    internal sealed class BoomboxBuffHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal TMP_Text Detail;
        internal GameObject Popup;
        internal string Text;
        public void OnPointerEnter(PointerEventData data)
        {
            Detail.text = Text;
            float height = Mathf.Clamp(24 + Text.Split('\n').Length * 25, 70, 180);
            ((RectTransform)Popup.transform).sizeDelta = new Vector2(280, height);
            Detail.rectTransform.sizeDelta = new Vector2(256, height - 20);
            Popup.SetActive(true);
        }
        public void OnPointerExit(PointerEventData data) => Popup.SetActive(false);
        private void OnDisable()
        {
            if (Popup != null)
                Popup.SetActive(false);
        }
    }

    internal sealed class BoomboxCompactBuffs
    {
        private GameObject canvas;
        private RectTransform root;
        private readonly BoomboxAuraStatus[] current = new BoomboxAuraStatus[5];
        private readonly Image[] icons = new Image[5];
        private readonly TMP_Text[] values = new TMP_Text[5], counts = new TMP_Text[5];
        private readonly BoomboxBuffHover[] hovers = new BoomboxBuffHover[5];
        private TMP_Text detail;
        private TMP_Text heading;
        private float next;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly List<Graphic> mapGraphics = new List<Graphic>(), addonGraphics = new List<Graphic>();

        internal void Update(BoomboxPlugin plugin)
        {
            var player = Player.m_localPlayer;
            bool visible = player != null
                && !player.IsDead()
                && Hud.instance != null
                && Hud.instance.gameObject.activeInHierarchy
                && plugin.Settings.ShowBuffPanel.Value;
            if (!visible)
            {
                if (canvas != null)
                    canvas.SetActive(false);
                return;
            }
            if (InventoryGui.instance == null)
                return;
            if (canvas == null)
                Build();
            canvas.SetActive(true);
            float scale = Mathf.Clamp(Screen.height / 1080f, .65f, 2f) * plugin.Settings.BuffScale.Value;
            if (Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + .2f;
            System.Array.Clear(current, 0, current.Length);
            foreach (var effect in player.GetSEMan().GetStatusEffects())
            {
                if (effect is BoomboxAuraStatus aura && aura.BonusIndex >= 0 && aura.BonusIndex < 5)
                    current[aura.BonusIndex] = aura;
            }
            int shown = 0;
            for (int i = 0; i < 5; i++)
            {
                bool active = current[i] != null;
                hovers[i].gameObject.SetActive(active);
                if (!active)
                    continue;
                BoomboxNativeUI.Place((RectTransform)hovers[i].transform, 0, 26 + shown++ * 44, 190, 40);
                hovers[i].Text = current[i].GetTooltipString();
                string value = "+" + BoomboxAuraHud.Number(current[i].BonusValue, i);
                if (i > 0 && i < 4)
                    value += " / " + current[i].BonusPeriod.ToString("0.##", BoomboxText.Culture)
                        + BoomboxText.Get("text.s");
                values[i].text = value;
                counts[i].text = "×" + current[i].BonusSources;
            }
            heading.text = BoomboxText.Get("text.bonuses_2") + shown;
            root.gameObject.SetActive(shown > 0);
            Layout(plugin.Settings, scale, 26 + shown * 44);
        }

        private Rect MapBounds(float scale)
        {
            Rect map = new Rect(Screen.width - 220 * scale, 20 * scale, 200 * scale, 200 * scale);
            var rect = Minimap.instance?.m_mapImageSmall?.rectTransform;
            if (rect != null)
            {
                rect.GetWorldCorners(corners);
                var canvas = rect.GetComponentInParent<Canvas>();
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var bottom = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                var top = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                map = new Rect(bottom.x, Screen.height - top.y, top.x - bottom.x, top.y - bottom.y);
            }
            return map;
        }

        private float MapSafeBottom(Rect map)
        {
            float safeBottom = map.yMax;
            var small = Minimap.instance?.m_smallRoot;
            mapGraphics.Clear();
            if (small != null)
                small.GetComponentsInChildren(false, mapGraphics);
            var info = Hud.instance?.m_rootObject?.transform.Find("MyLittleUI_Parent");
            if (info != null)
            {
                addonGraphics.Clear();
                info.GetComponentsInChildren(false, addonGraphics);
                mapGraphics.AddRange(addonGraphics);
            }
            if (mapGraphics.Count > 0)
            {
                foreach (var graphic in mapGraphics)
                {
                    if (!graphic.isActiveAndEnabled)
                        continue;
                    var c = graphic.canvas;
                    var view = c == null || c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
                    graphic.rectTransform.GetWorldCorners(corners);
                    float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                    foreach (var corner in corners)
                    {
                        var p = RectTransformUtility.WorldToScreenPoint(view, corner);
                        minX = Mathf.Min(minX, p.x);
                        maxX = Mathf.Max(maxX, p.x);
                        minY = Mathf.Min(minY, Screen.height - p.y);
                        maxY = Mathf.Max(maxY, Screen.height - p.y);
                    }
                    if (maxX >= map.xMin
                        && minX <= map.xMax
                        && minY >= map.yMin - 8
                        && maxY <= Screen.height
                        && maxY - minY <= map.height * 2
                        && maxX - minX <= map.width * 2)
                        safeBottom = Mathf.Max(safeBottom, maxY);
                }
            }
            return safeBottom;
        }

        private void Layout(BoomboxSettings settings, float scale, float height)
        {
            Rect map = MapBounds(scale);
            float safeBottom = MapSafeBottom(map);
            float width = 190 * scale, x = map.xMax - width, y = map.yMax + 12 * scale;
            if (settings.BuffAnchor.Value == BoomboxBuffAnchor.LeftOfMap)
            {
                x = map.xMin - width - 12 * scale;
                y = map.yMin;
            }
            if (settings.BuffAnchor.Value == BoomboxBuffAnchor.TopLeft)
            {
                x = 16 * scale;
                y = 120 * scale;
            }
            if (settings.BuffAnchor.Value == BoomboxBuffAnchor.BottomRight)
            {
                x = Screen.width - width - 16 * scale;
                y = Screen.height - height * scale - 80 * scale;
            }
            x += settings.BuffOffset.Value.x * scale;
            y += settings.BuffOffset.Value.y * scale;
            if (settings.BuffAnchor.Value == BoomboxBuffAnchor.BelowMap)
                y = Mathf.Max(y, safeBottom + 12 * scale);
            x = Mathf.Clamp(x, 8, Mathf.Max(8, Screen.width - width - 8));
            y = Mathf.Clamp(y, 8, Mathf.Max(8, Screen.height - height * scale - 8));
            BoomboxNativeUI.Place(root, x, y, 190, height);
            root.localScale = Vector3.one * scale;
            var popup = (RectTransform)detail.transform.parent;
            BoomboxNativeUI.Place(popup, -90, height + 8, 280, popup.sizeDelta.y);
            if (y + (height + 8 + popup.sizeDelta.y) * scale > Screen.height)
                popup.anchoredPosition = new Vector2(-90, popup.sizeDelta.y + 8);
        }

        private void Build()
        {
            canvas = new GameObject(
                "BoomboxCompactBuffCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster)
            );
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 90;
            root = BoomboxNativeUI.Rect("BoomboxCompactBuffs", canvas.transform);
            heading = BoomboxNativeUI.FixedText("BuffHeading", root, 17, true);
            BoomboxNativeUI.Place(heading.rectTransform, 0, 0, 190, 24);
            var popup = BoomboxNativeUI.Image("BuffTooltip", root, BoomboxNativeUI.Wood, new Color(.55f, .6f, .65f));
            BoomboxNativeUI.Place(popup.rectTransform, 0, 64, 280, 100);
            popup.raycastTarget = false;
            detail = BoomboxNativeUI.FixedText("BuffDetails", popup.transform, 17);
            BoomboxNativeUI.Place(detail.rectTransform, 12, 10, 256, 80);
            popup.gameObject.SetActive(false);
            for (int index = 0; index < 5; index++)
            {
                var row = Object.Instantiate(Hud.instance.m_statusEffectTemplate, root, false);
                row.name = "BuffRow" + index;
                row.gameObject.SetActive(true);
                BoomboxNativeUI.Place(row, 0, 26 + index * 44, 190, 40);
                icons[index] = row.Find("Icon").GetComponent<Image>();
                icons[index].name = "BuffIcon";
                BoomboxNativeUI.KeepNative(row.gameObject, icons[index]);
                icons[index].sprite = BoomboxBonusLook.Icon(index);
                icons[index].color = Color.white;
                icons[index].raycastTarget = true;
                icons[index].type = Image.Type.Simple;
                icons[index].preserveAspect = true;
                BoomboxNativeUI.Place(icons[index].rectTransform, 0, 2, 36, 36);
                values[index] = BoomboxNativeUI.FixedText("BuffValue" + index, row, 18);
                BoomboxNativeUI.Place(values[index].rectTransform, 44, 8, 112, 28);
                counts[index] = BoomboxNativeUI.FixedText("BuffSources" + index, row, 15);
                BoomboxNativeUI.Place(counts[index].rectTransform, 158, 10, 32, 24);
                hovers[index] = row.gameObject.AddComponent<BoomboxBuffHover>();
                hovers[index].Detail = detail;
                hovers[index].Popup = popup.gameObject;
            }
            popup.transform.SetAsLastSibling();
        }

        internal void Dispose()
        {
            if (canvas != null)
                Object.Destroy(canvas);
        }
    }
}
