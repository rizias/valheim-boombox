using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public enum BoomboxDecorationKind
    {
        Hanging, Figurine
    }
    public enum BoomboxDecorationBonus
    {
        Damage, Health, Stamina, Eitr, Speed
    }
    public sealed class BoomboxDecorationPlacement
    {
        public int slot;
        public string prefab;
        public string effect;
        public bool effectParticles = true;
        public string extraParticles = nameof(BoomboxExtraParticles.None);
    }
    public sealed class BoomboxDecorationSlot
    {
        public int Index;
        public int Number;
        public int MountIndex;
        public BoomboxDecorationKind Kind;
        public Vector3 Outward;
        public bool Upper;
    }
    public sealed class BoomboxDecorationVisualRecord
    {
        public GameObject Prefab;
        public Transform Attach;
        public Bounds Bounds;
        public bool HasGeometry;
        public bool HasMaterials;
        public bool UsesGeometryFallback;
        public bool UsesMaterialFallback;
        public bool Renderable;
        public bool Available => Attach != null && Renderable;
    }
    public sealed class BoomboxDecorationTotals
    {
        public int Damage, Health, Stamina, Eitr, Speed;
        public int Count(BoomboxDecorationBonus bonus)
        {
            switch (bonus)
            {
                case BoomboxDecorationBonus.Damage:
                    return Damage;
                case BoomboxDecorationBonus.Health:
                    return Health;
                case BoomboxDecorationBonus.Stamina:
                    return Stamina;
                case BoomboxDecorationBonus.Eitr:
                    return Eitr;
                default:
                    return Speed;
            }
        }
    }
    internal static partial class BoomboxDecorationCatalog
    {
        internal const string Mushroom = "MushroomYellow";
        private static readonly Dictionary<GameObject, BoomboxDecorationVisualRecord> artwork =
            new Dictionary<GameObject, BoomboxDecorationVisualRecord>();
        internal static readonly BoomboxDecorationSlot[] Slots = BuildSlots();
        private static readonly HashSet<string> standingHeads = new HashSet<string>(StringComparer.Ordinal)
        {
            "TrophyAsksvin", "TrophyBarka", "TrophyBjorn", "TrophyBjornUndead", "TrophyBoar",
            "TrophyCharredMelee", "TrophyCultist", "TrophyCultist_Hildir", "TrophyDeer", "TrophyDeerWhite",
            "TrophyDragonQueen", "TrophyDraugr", "TrophyDraugrElite", "TrophyDraugrFem", "TrophyEikthyr",
            "TrophyFader", "TrophyForestTroll", "TrophyFrostTroll",
            "TrophyGoblinBrute", "TrophyGoblinBruteBrosBrute", "TrophyGoblinBruteBrosShaman",
            "TrophyGreydwarf", "TrophyGreydwarfBrute", "TrophyGreydwarfShaman", "TrophyHatchling",
            "TrophyLox", "TrophyMoose", "TrophyNeck", "TrophySeal", "TrophySerpent",
            "TrophySkeleton", "TrophySkeletonHildir", "TrophySurtling", "TrophyWolf"
        };
        private static readonly HashSet<string> hangingOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            Mushroom,
            "TrophyBlob",
            "TrophyBlob_Frost",
            "TrophyBlob_Lava",
            "TrophyBlob_Morkhalla",
            "TrophyBonemawSerpent",
            "TrophyCharredMage",
            "TrophyDeathsquito",
            "TrophyDvergr",
            "TrophyElaking",
            "TrophyGjall",
            "TrophyGoblin",
            "TrophyGoblinKing",
            "TrophyGrowth",
            "TrophyJotunWarrior",
            "TrophyJotunWitch",
            "TrophyLeech",
            "TrophySeekerBrute",
            "TrophySeekerQueen",
            "TrophyVolture",
            BoomboxItemData.ArchivePrefab,
            BoomboxItemData.DevicePrefab,
            "TrophyTheElder",
            "TrophyGhost",
            "TrophyWraith",
            "TrophySkeletonPoison",
            "TrophySGolem",
            "Bronze",
            "Copper",
            "Tin",
            "Iron",
            "Silver",
            "BlackMetal",
            "Gold",
            "Flametal",
            "FlametalNew",
            "FrostCore",
            "ShieldCore",
            "BlackCore",
            "SurtlingCore",
            "MoltenCore",
            "Lingondricka",
            "RawMeat",
            "Raspberry",
            "Blueberries",
            "MushroomBzerker",
            "GreydwarfEye",
            "HealthUpgrade_Bonemass",
            "DragonEgg",
            "CryptKey",
            "DragonTear",
            "Ruby",
            "Resin"
        };
        private static ObjectDB itemsSource;
        private static int itemsCount;
        private static string itemsLanguage;
        private static readonly Dictionary<BoomboxDecorationKind, GameObject[]> itemsCache =
            new Dictionary<BoomboxDecorationKind, GameObject[]>();
        internal static readonly BoomboxDecorationBonus[] Effects = {
            BoomboxDecorationBonus.Damage,
            BoomboxDecorationBonus.Health,
            BoomboxDecorationBonus.Stamina,
            BoomboxDecorationBonus.Eitr,
            BoomboxDecorationBonus.Speed
        };
        private static readonly Dictionary<string, BoomboxDecorationBonus> bonuses =
            new Dictionary<string, BoomboxDecorationBonus>(
            StringComparer.Ordinal
        )
            {
            { Mushroom, BoomboxDecorationBonus.Stamina },
            { "TrophyDeer", BoomboxDecorationBonus.Speed },
            { "TrophyBoar", BoomboxDecorationBonus.Health },
            { "TrophyNeck", BoomboxDecorationBonus.Stamina },
            { "TrophyGreydwarf", BoomboxDecorationBonus.Health },
            { "TrophyGreydwarfShaman", BoomboxDecorationBonus.Health },
            { "TrophyGreydwarfBrute", BoomboxDecorationBonus.Damage },
            { "TrophySkeleton", BoomboxDecorationBonus.Damage },
            { "TrophySkeletonPoison", BoomboxDecorationBonus.Eitr },
            { "TrophyDraugr", BoomboxDecorationBonus.Damage },
            { "TrophyDraugrElite", BoomboxDecorationBonus.Damage },
            { "TrophyWraith", BoomboxDecorationBonus.Eitr },
            { "TrophyWolf", BoomboxDecorationBonus.Speed },
            { "TrophyFenring", BoomboxDecorationBonus.Speed },
            { "TrophyHatchling", BoomboxDecorationBonus.Eitr },
            { "TrophyGoblin", BoomboxDecorationBonus.Stamina },
            { "TrophyGoblinBrute", BoomboxDecorationBonus.Damage },
            { "TrophyGoblinShaman", BoomboxDecorationBonus.Eitr },
            { "TrophyDeathsquito", BoomboxDecorationBonus.Speed },
            { "TrophyLox", BoomboxDecorationBonus.Health },
            { "TrophySerpent", BoomboxDecorationBonus.Stamina },
            { "TrophyEikthyr", BoomboxDecorationBonus.Stamina },
            { "TrophyTheElder", BoomboxDecorationBonus.Health },
            { "TrophyBonemass", BoomboxDecorationBonus.Health },
            { "TrophyDragonQueen", BoomboxDecorationBonus.Eitr },
            { "TrophyGoblinKing", BoomboxDecorationBonus.Damage },
            { "TrophySeekerQueen", BoomboxDecorationBonus.Eitr },
            { "TrophyFader", BoomboxDecorationBonus.Damage }
            };
        internal static IEnumerable<GameObject> Items(BoomboxDecorationKind kind)
        {
            var database = ObjectDB.instance;
            if (database == null)
                return Enumerable.Empty<GameObject>();
            string language = Localization.instance?.GetSelectedLanguage();
            if (itemsSource != database || itemsCount != database.m_items.Count || itemsLanguage != language
            || piecesSource != ZNetScene.instance)
            {
                itemsCache.Clear();
                itemsSource = database;
                itemsCount = database.m_items.Count;
                itemsLanguage = language;
                piecesSource = ZNetScene.instance;
            }
            if (!itemsCache.TryGetValue(kind, out var items))
                itemsCache[kind] = items = database.m_items.Concat(Pieces(kind))
                    .Where(item => Eligible(item, kind))
                    .OrderBy(
                    Name,
                    StringComparer.CurrentCultureIgnoreCase
                )
                    .ToArray();
            return items;
        }
        internal static bool Eligible(GameObject prefab, BoomboxDecorationKind kind)
        {
            if (prefab == null)
                return false;
            bool piece = IsPiece(prefab);
            if (!piece && prefab.GetComponent<ItemDrop>() == null)
                return false;
            bool listed = piece
                ? kind == BoomboxDecorationKind.Hanging
                : standingHeads.Contains(prefab.name)
                    || kind == BoomboxDecorationKind.Hanging
                    && hangingOnly.Contains(prefab.name);
            return listed && TryVisual(prefab, out var visual) && visual.Available;
        }
        internal static bool DataEligible(string prefab, int slot)
        {
            if (string.IsNullOrEmpty(prefab) || slot < 0 || slot >= Slots.Length)
                return false;
            bool hanging = Slots[slot].Kind == BoomboxDecorationKind.Hanging;
            var native = Prefab(prefab);
            if (native == null)
                return false;
            if (hangingPieces.Contains(prefab))
                return hanging && IsPiece(native);
            return native.GetComponent<ItemDrop>() != null
                && (standingHeads.Contains(prefab) || hanging && hangingOnly.Contains(prefab));
        }
        internal static BoomboxDecorationBonus DataEffect(string prefab, string effect, string fallback)
        {
            if (Enum.TryParse(effect, true, out BoomboxDecorationBonus selected)
            && Enum.IsDefined(typeof(BoomboxDecorationBonus), selected))
                return selected;
            if (bonuses.TryGetValue(prefab, out var known))
                return known;
            return Enum.TryParse(fallback, true, out BoomboxDecorationBonus parsed)
            && Enum.IsDefined(typeof(BoomboxDecorationBonus), parsed)
                ? parsed
                : BoomboxDecorationBonus.Health;
        }
        internal static DeckState WithoutUnlisted(DeckState state)
        {
            var database = ObjectDB.instance;
            if (database == null || state?.decorationPlacements == null)
                return state;
            state.decorationPlacements.RemoveAll(
                p => p != null && p.slot >= 0 && p.slot < Slots.Length && Prefab(p.prefab) != null
                    && !(standingHeads.Contains(p.prefab)
                    || Slots[p.slot].Kind == BoomboxDecorationKind.Hanging
                    && (hangingOnly.Contains(p.prefab) || hangingPieces.Contains(p.prefab)))
            );
            return state;
        }
        internal static bool Found(GameObject prefab, DeckState state)
        {
            if (state.unlockedDecorationPrefabs.Contains(prefab.name))
                return true;
            var player = Player.m_localPlayer;
            if (IsPiece(prefab))
                return player != null && player.IsRecipeKnown(prefab.GetComponent<Piece>().m_name);
            return player != null && player.IsKnownMaterial(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
        }
        internal static BoomboxDecorationBonus Effect(BoomboxDecorationPlacement placement)
        => Enum.TryParse(placement.effect, true, out BoomboxDecorationBonus chosen) ? chosen : Bonus(placement.prefab);
        internal static string EffectName(BoomboxDecorationBonus effect)
        {
            switch (effect)
            {
                case BoomboxDecorationBonus.Damage:
                    return BoomboxText.Get("boombox_property_red");
                case BoomboxDecorationBonus.Health:
                    return BoomboxText.Get("text.health");
                case BoomboxDecorationBonus.Stamina:
                    return BoomboxText.Get("text.stamina");
                case BoomboxDecorationBonus.Eitr:
                    return BoomboxText.Get("text.eitr");
                default:
                    return BoomboxText.Get("config.Speed");
            }
        }
        internal static bool TryVisual(GameObject prefab, out BoomboxDecorationVisualRecord visual)
        {
            if (prefab == null)
            {
                visual = null;
                return false;
            }
            if (artwork.TryGetValue(prefab, out visual))
                return visual.Available;
            var attach = ItemStand.GetAttachPrefab(prefab);
            var source = attach != null
                ? ItemStand.GetAttachGameObject(attach)?.transform
                : IsPiece(prefab) || hangingOnly.Contains(prefab.name)
                    ? prefab.transform
                    : null;
            visual = new BoomboxDecorationVisualRecord { Prefab = prefab, Attach = source };
            if (source == null)
            {
                artwork[prefab] = visual;
                return false;
            }
            bool troll = prefab.name == "TrophyTroll"
                || source.GetComponentsInParent<Transform>(true).Any(t => t.name == "TrophyForestTroll");
            var renderers = source.GetComponentsInChildren<Renderer>(true).Where(
                r => BoomboxItemPrefabs.VisibleUnder(r.transform, source)
            ).ToArray();
            visual.HasGeometry = renderers.Any(HasGeometry);
            visual.HasMaterials = renderers.Any(HasMaterial);
            visual.Renderable = renderers.Any(r => HasGeometry(r) && HasMaterial(r));
            visual.UsesGeometryFallback = troll
                && !visual.HasGeometry
                && BoomboxTrophyVisuals.NativeTrollHead() != null;
            visual.UsesMaterialFallback = troll
                && !visual.HasMaterials
                && BoomboxTrophyVisuals.NativeTrollMaterialAvailable();
            visual.HasGeometry |= visual.UsesGeometryFallback;
            visual.HasMaterials |= visual.UsesMaterialFallback;
            visual.Renderable |= troll && renderers.Length > 0 && visual.HasGeometry && visual.HasMaterials;
            visual.Bounds = SourceBounds(
                source,
                renderers,
                visual.UsesGeometryFallback
                    ? BoomboxTrophyVisuals.NativeTrollHead()
                    : null
            );
            artwork[prefab] = visual;
            return visual.Available;
        }
        private static bool HasGeometry(Renderer renderer) => renderer is SkinnedMeshRenderer skin
            ? skin.sharedMesh != null && skin.sharedMesh.vertexCount > 0
            : renderer.GetComponent<MeshFilter>()?.sharedMesh is Mesh mesh && mesh.vertexCount > 0;
        private static bool HasMaterial(
            Renderer renderer
        ) => renderer.sharedMaterials.Any(
            material => material != null && material.shader != null && material.shader.isSupported
                && !material.shader.name.Contains("InternalErrorShader")
        );
        private static Bounds SourceBounds(Transform source, Renderer[] renderers, Mesh fallback)
        {
            var result = new Bounds();
            bool found = false;
            foreach (var renderer in renderers)
            {
                var bounds = renderer is SkinnedMeshRenderer skin
                    ? skin.sharedMesh?.bounds
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh?.bounds;
                if (!bounds.HasValue)
                    continue;
                var matrix = source.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(
                        bounds.Value.center + Vector3.Scale(
                            bounds.Value.extents,
                            new Vector3(
                                (i & 1) == 0
                                    ? -1
                                    : 1,
                                (i & 2) == 0
                                    ? -1
                                    : 1,
                                (i & 4) == 0
                                    ? -1
                                    : 1
                            )
                        )
                    );
                    if (!found)
                    {
                        result = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                        result.Encapsulate(point);
                }
            }
            if (!found && fallback != null)
                result = fallback.bounds;
            return result;
        }
        internal static string Name(GameObject prefab)
        {
            if (prefab == null)
                return BoomboxText.Get("text.item_unavailable");
            string key = IsPiece(prefab)
                ? prefab.GetComponent<Piece>().m_name
                : prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            string name = Localization.instance == null ? key : Localization.instance.Localize(key);
            if (string.IsNullOrEmpty(name) || name.StartsWith("$", StringComparison.Ordinal)
            || (name.StartsWith("[", StringComparison.Ordinal) && name.EndsWith("]", StringComparison.Ordinal)))
                return System.Text.RegularExpressions.Regex.Replace(prefab.name, "(?<=[a-z])(?=[A-Z])", " ");
            return name;
        }
        internal static Sprite Icon(string prefab) => IconOf(Prefab(prefab));
        internal static BoomboxDecorationBonus Bonus(string prefab)
        {
            if (bonuses.TryGetValue(prefab, out var bonus))
                return bonus;
            var configured = BoomboxPlugin.Instance?.Settings?.DecorationModFallback?.Value;
            return Enum.TryParse(configured, true, out BoomboxDecorationBonus parsed)
                ? parsed
                : BoomboxDecorationBonus.Health;
        }
        internal static BoomboxDecorationTotals Totals(DeckState state)
        {
            var result = new BoomboxDecorationTotals();
            foreach (var placement in state.decorationPlacements)
            {
                if (placement.slot < 0
                    || placement.slot >= Slots.Length
                    || !state.unlockedDecorationSlots.Contains(placement.slot)
                    || !state.unlockedDecorationPrefabs.Contains(placement.prefab)
                    || !Eligible(Prefab(placement.prefab), Slots[placement.slot].Kind))
                    continue;
                var bonus = DataEffect(
                    placement.prefab,
                    placement.effect,
                    BoomboxAuraNetwork.Balance?.Fallback ?? BoomboxPlugin.Instance.Settings.DecorationModFallback.Value
                );
                if (bonus == BoomboxDecorationBonus.Damage)
                    result.Damage++;
                else if (bonus == BoomboxDecorationBonus.Health)
                    result.Health++;
                else if (bonus == BoomboxDecorationBonus.Stamina)
                    result.Stamina++;
                else if (bonus == BoomboxDecorationBonus.Eitr)
                    result.Eitr++;
                else
                    result.Speed++;
            }
            return result;
        }
        internal static UpgradeRecipe SlotRecipe(int coins) => new UpgradeRecipe(
            "piece_workbench",
            1,
            coins <= 0
                ? Array.Empty<UpgradeIngredient>()
                : new[] { new UpgradeIngredient("Coins", coins) }
        );
        internal static UpgradeRecipe DesignRecipe(string prefab) => new UpgradeRecipe(
            "piece_workbench",
            1,
            prefab == BoomboxItemData.DevicePrefab || prefab == BoomboxItemData.ArchivePrefab
                ? Array.Empty<UpgradeIngredient>()
                : new[]
                {
                    new UpgradeIngredient(prefab, 1),
                    new UpgradeIngredient("Wood", 2),
                    new UpgradeIngredient("Resin", 1)
                }
        );
        private static BoomboxDecorationSlot[] BuildSlots()
        {
            int[] numbers = {
                9, 8, 7, 6, 5, 4, 3, 10, 11, 2, 1,
                17, 16, 15, 12, 13, 14,
                24, 23, 22, 21, 20, 19, 18
            };
            var slots = new List<BoomboxDecorationSlot>();
            for (int i = 0; i < 7; i++)
                slots.Add(new BoomboxDecorationSlot
                {
                    Index = slots.Count,
                    Kind = BoomboxDecorationKind.Hanging,
                    Outward = i == 0
                        ? new Vector3(-1, 0, 1).normalized
                        : i == 6
                            ? new Vector3(1, 0, 1).normalized
                            : Vector3.forward
                });
            foreach (float x in new[] { -.5f, .5f })
                for (int i = 0; i < 2; i++)
                    slots.Add(
                        new BoomboxDecorationSlot
                        {
                            Index = slots.Count,
                            Kind = BoomboxDecorationKind.Hanging,
                            Outward = x < 0
                                ? Vector3.left
                                : Vector3.right
                        }
                    );
            foreach (float x in new[] { -.5f, .5f })
                for (int i = 0; i < 3; i++)
                    slots.Add(new BoomboxDecorationSlot
                    {
                        Index = slots.Count,
                        Kind = BoomboxDecorationKind.Hanging,
                        Upper = true,
                        Outward = x < 0 ? Vector3.left : Vector3.right
                    });
            for (int i = 0; i < 7; i++)
                slots.Add(
                    new BoomboxDecorationSlot
                    {
                        Index = slots.Count,
                        Kind = BoomboxDecorationKind.Figurine,
                        MountIndex = i,
                        Outward = Vector3.up
                    }
                );
            foreach (var slot in slots)
                slot.Number = numbers[slot.Index];
            return slots.ToArray();
        }
    }
}
