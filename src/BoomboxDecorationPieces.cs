using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static partial class BoomboxDecorationCatalog
    {
        private static readonly HashSet<string> hangingPieces = new HashSet<string>(StringComparer.Ordinal)
        {
            "piece_Lavalantern",
            "piece_brazierceiling01",
            "smelter",
            "blastfurnace",
            "charcoal_kiln",
            "eitrrefinery",
            "piece_FrostKiln",
            "windmill",
            "piece_spinningwheel",
            "fermenter",
            "piece_oven",
            "piece_FrostFoundry",
            "piece_beehive",
            "piece_birdnest",
            "Raft",
            "Karve",
            "VikingShip",
            "VikingShip_Ashlands"
        };
        private static readonly Dictionary<string, string> products = new Dictionary<string, string>(
            StringComparer.Ordinal
        )
        {
            { "smelter", "Copper" },
            { "blastfurnace", "BlackMetal" },
            { "charcoal_kiln", "Coal" },
            { "eitrrefinery", "Eitr" },
            { "piece_FrostKiln", "FrozenFuel" },
            { "windmill", "BarleyFlour" },
            { "piece_spinningwheel", "LinenThread" },
            { "fermenter", "MeadHealthMinor" },
            { "piece_oven", "Bread" },
            { "piece_beehive", "Honey" },
            { "piece_birdnest", "Feathers" }
        };
        private static readonly HashSet<string> craftedProducts = new HashSet<string>(
            StringComparer.Ordinal
        ) {
            "piece_FrostFoundry"
        };
        private static readonly Dictionary<string, float> productSizes = new Dictionary<string, float>(
            StringComparer.Ordinal
        ) {
            { "Eitr", .5f }
        };
        private static readonly Dictionary<string, string[]> droppable = new Dictionary<string, string[]>(
            StringComparer.Ordinal
        );
        private static ObjectDB droppableSource;
        private static ZNetScene piecesSource;

        internal static bool IsPiece(
            GameObject prefab
        ) => prefab != null && hangingPieces.Contains(prefab.name) && prefab.GetComponent<Piece>() != null
        && prefab.GetComponent<ItemDrop>() == null;

        internal static bool IsPiece(string prefab) => IsPiece(Prefab(prefab));

        internal static GameObject Prefab(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var item = ObjectDB.instance?.GetItemPrefab(name);
            if (item != null)
                return item;
            return hangingPieces.Contains(name) ? ZNetScene.instance?.GetPrefab(name) : null;
        }

        private static IEnumerable<GameObject> Pieces(BoomboxDecorationKind kind)
        {
            if (kind != BoomboxDecorationKind.Hanging || ZNetScene.instance == null)
                return Enumerable.Empty<GameObject>();
            return hangingPieces.Select(name => ZNetScene.instance.GetPrefab(name)).Where(prefab => prefab != null);
        }

        internal static Sprite IconOf(GameObject prefab)
        {
            if (prefab == null)
                return null;
            if (IsPiece(prefab))
                return prefab.GetComponent<Piece>().m_icon;
            return prefab.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
        }

        internal static string[] Products(GameObject prefab)
        {
            if (!IsPiece(prefab))
                return null;
            if (droppableSource != ObjectDB.instance)
            {
                droppable.Clear();
                droppableSource = ObjectDB.instance;
            }
            if (droppable.TryGetValue(prefab.name, out var cached))
                return cached;
            string[] found = null;
            if (craftedProducts.Contains(prefab.name))
                found = Outputs(prefab).Distinct().Where(BoomboxParticleProducts.Droppable).ToArray();
            else if (products.TryGetValue(prefab.name, out var product) && Outputs(prefab).Contains(product))
                found = new[] { product };
            if (found != null && found.Length == 0)
                found = null;
            droppable[prefab.name] = found;
            return found;
        }

        internal static float ProductSize(
            string product
        ) => product != null && productSizes.TryGetValue(product, out var size)
            ? size
            : 1f;

        private static IEnumerable<string> Outputs(GameObject prefab)
        {
            var smelter = prefab.GetComponent<Smelter>();
            if (smelter != null)
                foreach (var c in smelter.m_conversion)
                    if (c.m_to != null)
                        yield return c.m_to.name;
            var fermenter = prefab.GetComponent<Fermenter>();
            if (fermenter != null)
                foreach (var c in fermenter.m_conversion)
                    if (c.m_to != null)
                        yield return c.m_to.name;
            var cooking = prefab.GetComponent<CookingStation>();
            if (cooking != null)
                foreach (var c in cooking.m_conversion)
                    if (c.m_to != null)
                        yield return c.m_to.name;
            var hive = prefab.GetComponent<Beehive>();
            if (hive != null && hive.m_honeyItem != null)
                yield return hive.m_honeyItem.name;
        }

    }
}
