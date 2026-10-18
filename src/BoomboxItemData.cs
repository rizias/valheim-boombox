using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace VikingBoombox
{
    public sealed class RecordedCassette
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "";
        public string prefab;
        public List<string> tracks = new List<string>();
        public Dictionary<string, int> properties = new Dictionary<string, int>();

        public int Rank(string property) => properties.TryGetValue(property, out int rank) ? rank : 0;

    }

    public sealed class BoomboxItemPayload
    {
        public int version = 1;
        public DeckState deck;
        public RecordedCassette cassette;
    }

    public static class BoomboxItemData
    {
        public const string Key = "local.valheim.boombox.item";
        public const string DevicePrefab = "ValheimBoombox_Item";
        public const string CassettePrefix = "ValheimBoombox_Cassette";
        public const string ArchivePrefab = CassettePrefix + "Archive";
        public const string UpgradedPrefab = CassettePrefix + "Upgraded";
        public static readonly string[] Properties = { "Red", "Yellow", "Green", "Blue", "Purple" };
        public static bool IsDevice(
            ItemDrop.ItemData item
        ) => item?.m_dropPrefab != null && item.m_dropPrefab.name == DevicePrefab;
        public static bool IsCassette(
            ItemDrop.ItemData item
        ) => item?.m_dropPrefab != null && item.m_dropPrefab.name.StartsWith(CassettePrefix, StringComparison.Ordinal);
        public static bool IsOurs(ItemDrop.ItemData item) => IsDevice(item) || IsCassette(item);

        public static BoomboxItemPayload Read(ItemDrop.ItemData item)
        {
            if (!IsOurs(item))
                throw new ArgumentException(BoomboxText.Get("text.this_item_does_not_belong_to_the_boombox_system"));
            if (item.m_customData.TryGetValue(Key, out string json))
            {
                var saved = JsonConvert.DeserializeObject<BoomboxItemPayload>(json);
                Validate(saved, IsDevice(item));
                if (saved.deck != null)
                    item.m_quality = saved.deck.upgradeTier;
                return saved;
            }
            var payload = new BoomboxItemPayload();
            if (IsDevice(item))
                payload.deck = new DeckState { upgradeTier = Math.Max(1, item.m_quality), onBack = false };
            else
            {
                payload.cassette = new RecordedCassette { prefab = item.m_dropPrefab.name };
                string property = Properties.FirstOrDefault(p => item.m_dropPrefab.name == CassettePrefix + p);
                if (property != null)
                    payload.cassette.properties.Add(property, 1);
            }
            if (!ReferenceEquals(item, item.m_dropPrefab.GetComponent<ItemDrop>().m_itemData))
                Write(item, payload);
            return payload;
        }

        public static void Write(ItemDrop.ItemData item, BoomboxItemPayload payload)
        {
            Validate(payload, IsDevice(item));
            item.m_customData[Key] = JsonConvert.SerializeObject(payload);
            item.m_quality = payload.deck == null ? 1 : payload.deck.upgradeTier;
        }

        private static void Validate(BoomboxItemPayload payload, bool device)
        {
            if (payload == null || payload.version != 1 || device != (payload.deck != null))
                throw new InvalidDataException(
                    BoomboxText.Get("text.unknown_boombox_item_format_its_data_remains_unchanged")
                );
            if (device
            && (string.IsNullOrEmpty(payload.deck.id) || payload.deck.tracks == null || payload.deck.upgradeTier < 0
            || payload.deck.upgradeTier > BoomboxProgression.MaxTier))
                throw new InvalidDataException(BoomboxText.Get("text.boombox_data_is_damaged"));
            if (device && payload.deck.unlockedDecorationSlots == null)
                payload.deck.unlockedDecorationSlots = new List<int> { 0 };
            if (device && payload.deck.unlockedDecorationPrefabs == null)
                payload.deck.unlockedDecorationPrefabs = new List<string>();
            if (device && payload.deck.decorationPlacements == null)
                payload.deck.decorationPlacements = new List<BoomboxDecorationPlacement>();
            if (device)
                foreach (var placement in payload.deck.decorationPlacements)
                    if (placement != null && string.IsNullOrEmpty(placement.effect))
                        placement.effect = BoomboxDecorationCatalog.Bonus(placement.prefab).ToString();
            if (device)
                foreach (var placement in payload.deck.decorationPlacements)
                    if (placement != null && string.IsNullOrEmpty(placement.extraParticles))
                        placement.extraParticles = nameof(BoomboxExtraParticles.None);
            if (device
            && payload.deck.decorationPlacements.Any(p => p != null
                && (!Enum.TryParse(p.effect, true, out BoomboxDecorationBonus _)
                || !Enum.TryParse(p.extraParticles, true, out BoomboxExtraParticles _))))
                throw new InvalidDataException(BoomboxText.Get("text.boombox_decoration_data_is_corrupt"));
            if (device
                && (!payload.deck.unlockedDecorationSlots.Contains(0)
                    || payload.deck.unlockedDecorationSlots.Distinct().Count()
                    != payload.deck.unlockedDecorationSlots.Count
                    || payload.deck.unlockedDecorationPrefabs.Distinct(StringComparer.Ordinal).Count()
                    != payload.deck.unlockedDecorationPrefabs.Count
                    || payload.deck.unlockedDecorationSlots.Any(slot => slot < 0
                    || slot >= BoomboxDecorationCatalog.Slots.Length)
                    || payload.deck.decorationPlacements.Any(
                p => p == null
                    || p.slot < 0
                    || p.slot >= BoomboxDecorationCatalog.Slots.Length
                    || string.IsNullOrEmpty(p.prefab)
            )
            || payload.deck.decorationPlacements.GroupBy(p => p.slot).Any(g => g.Count() > 1)))
                throw new InvalidDataException(BoomboxText.Get("text.boombox_decoration_data_is_corrupt"));
            var cassette = device ? payload.deck.insertedCassette : payload.cassette;
            if (!device && cassette == null)
                throw new InvalidDataException(BoomboxText.Get("text.cassette_data_is_missing"));
            if (cassette != null
            && (string.IsNullOrEmpty(cassette.id) || cassette.tracks == null || cassette.properties == null
            || cassette.properties.Any(p => !Properties.Contains(p.Key) || p.Value < 1)))
                throw new InvalidDataException(BoomboxText.Get("text.cassette_data_is_damaged"));
        }
    }
}
