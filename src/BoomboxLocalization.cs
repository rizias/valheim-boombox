using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace VikingBoombox
{
    public static class BoomboxLocalization
    {
        private static Player tutorialPlayer;
        private static readonly HashSet<string> queuedTopics = new HashSet<string>();

        public static void Register(Localization localization = null, string language = null)
        {
            localization = localization ?? Localization.instance;
            if (localization == null)
                return;
            BoomboxText.LoadTranslations(
                language ?? localization.GetSelectedLanguage(),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Translations"),
                Path.Combine(Paths.ConfigPath, "ValheimBoombox", "Translations")
            );
            var addWord = AccessTools.Method(typeof(Localization), "AddWord");
            foreach (string key in BoomboxText.Keys)
                if (key.StartsWith("boombox_", StringComparison.Ordinal))
                    addWord.Invoke(localization, new object[] { key, BoomboxText.Get(key) });
            BoomboxText.RefreshConfig();
        }

        public static void Tutorial(string topic)
        {
            var player = Player.m_localPlayer;
            if (player == null || global::Tutorial.instance == null)
                return;
            if (tutorialPlayer != player)
            {
                tutorialPlayer = player;
                queuedTopics.Clear();
            }
            string name = "boombox_" + topic;
            if (player.HaveSeenTutorial(name) || !queuedTopics.Add(name))
                return;
            var tutorial = global::Tutorial.instance;
            if (!tutorial.m_texts.Any(t => t.m_name == name))
                tutorial.m_texts.Add(new global::Tutorial.TutorialText
                {
                    m_name = name,
                    m_topic = "$boombox_tutorial_" + topic + "_topic",
                    m_text = "$boombox_tutorial_" + topic + "_text",
                    m_label = "$boombox_tutorial_" + topic + "_topic",
                    m_globalKeyTrigger = "",
                    m_tutorialTrigger = "",
                    m_isMunin = false
                });
            player.ShowTutorial(name, false);
        }

        internal static void RestoreReadTutorials(Player player)
        {
            foreach (var topic in new[] { "assembly", "equipment" })
                if (player.HaveSeenTutorial("boombox_" + topic))
                    player.AddKnownText(
                        "$boombox_tutorial_" + topic + "_topic",
                        "$boombox_tutorial_" + topic + "_text"
                    );
        }
    }

    [HarmonyPatch(typeof(Localization), "SetupLanguage")]
    internal static class BoomboxLanguageChanged
    {
        private static void Postfix(
            Localization __instance,
            string __0
        ) => BoomboxLocalization.Register(__instance, __0);
    }

    [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
    internal static class BoomboxCassetteRightClick
    {
        private static bool Prefix(InventoryGrid __0, ItemDrop.ItemData __1)
        {
            var player = Player.m_localPlayer;
            var plugin = BoomboxPlugin.Instance;
            if (player == null
                || plugin == null
                || !BoomboxItemData.IsCassette(__1)
                || __0.GetInventory() != player.GetInventory())
                return true;
            plugin.Notice = "";
            var deck = plugin.Items.Carried;
            if (deck == null)
                plugin.Notify("notice.boombox_missing_from_backpack");
            else if (!plugin.Items.Insert(deck, __1) && plugin.Notice.Length == 0)
                plugin.Notify("notice.cassette_insert_failed");
            player.Message(MessageHud.MessageType.Center, plugin.Notice);
            return false;
        }
    }

    [HarmonyPatch(
        typeof(ItemDrop.ItemData),
        "GetTooltip",
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) }
    )]
    internal static class BoomboxItemTooltip
    {
        private static void Postfix(ItemDrop.ItemData __0, ref string __result)
        {
            if (!BoomboxItemData.IsOurs(__0))
                return;
            var payload = BoomboxItemData.Read(__0);
            var cassette = payload.deck?.insertedCassette ?? payload.cassette;
            if (payload.deck != null)
            {
                if (cassette != null)
                    __result = __result.Replace("$boombox_description", "$boombox_description_ready").Replace(
                        Localization.instance.Localize("$boombox_description"),
                        Localization.instance.Localize("$boombox_description_ready")
                    );
                __result += "\n" + BoomboxText.Format("tooltip.boombox_level", payload.deck.upgradeTier, BoomboxProgression.MaxTier);
                if (cassette != null)
                    __result += "\n\n" + BoomboxCassetteWorkshop.Name(cassette);
            }
            if (cassette != null)
                __result += "\n" + BoomboxCassetteWorkshop.Statistics(cassette);
        }
    }
}
