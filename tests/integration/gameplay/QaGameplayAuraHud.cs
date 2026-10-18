using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private JArray AuraHudRows()
    {
        var result = new JArray();
        var player = Player.m_localPlayer;
        if (player != null)
            foreach (var effect in player.GetSEMan().GetStatusEffects().OfType<BoomboxAuraStatus>())
                if (effect.m_icon != null)
                    auraHudKnownIcons.Add(effect.m_icon.GetInstanceID());
        void Add(Image image, Transform row, string surface)
        {
            if (image == null || image.sprite == null || !image.enabled || !image.gameObject.activeInHierarchy
            || !row.gameObject.activeInHierarchy
            || image.canvas == null
            || !image.canvas.isActiveAndEnabled)
                return;
            result.Add(
                new JObject
                {
                    ["active"] = true,
                    ["text"] = string.Join(
                    " ",
                    row.GetComponentsInChildren<TMP_Text>(false).Where(t => t.enabled).Select(t => t.text)
                ),
                    ["icon"] = image.sprite.name,
                    ["surface"] = surface
                }
            );
        }
        if (Hud.instance != null)
            foreach (var row in (List<RectTransform>)AccessTools.Field(typeof(Hud), "m_statusEffects")
                .GetValue(Hud.instance))
            {
                var image = row == null ? null : row.Find("Icon")?.GetComponent<Image>();
                if (image != null && image.sprite != null && auraHudKnownIcons.Contains(image.sprite.GetInstanceID()))
                    Add(image, row, "native");
            }
        var compact = GameObject.Find("BoomboxCompactBuffCanvas");
        if (compact != null)
            foreach (var image in compact.GetComponentsInChildren<Image>(true).Where(i => i.name == "BuffIcon"))
                Add(image, image.transform.parent, "compact");
        return result;
    }
}
