using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal static class BoomboxChain
    {
        private const string Prefab = "Chain";
        private const string PartName = "ChainPart";
        private const float LowerLength = .08f;
        private const float UpperLength = .06f;
        private const int LowerLinks = 10;
        private const int UpperLinks = 8;
        private static BoomboxChainLinks links;

        internal static float Length(bool upper) => upper ? UpperLength : LowerLength;

        internal static Transform Create(Transform parent, Material material, bool upper)
        {
            var source = Links();
            int count = Count(upper);
            float scale = Scale(source, upper);
            var chain = new GameObject("Chain").transform;
            chain.SetParent(parent, false);
            for (int link = 0; link < count; link++)
            {
                var part = new GameObject(PartName);
                part.layer = parent.gameObject.layer;
                part.transform.SetParent(chain, false);
                part.transform.localPosition = Vector3.down * (source.Tops[0] - source.Tops[link]) * scale;
                part.transform.localScale = Vector3.one * scale;
                part.AddComponent<MeshFilter>().sharedMesh = source.Meshes[link];
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = material == null
                    ? source.Materials
                    : Enumerable.Repeat(material, source.Meshes[link].subMeshCount).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            return chain;
        }

        internal static float[] Reach(bool upper)
        {
            var source = Links();
            int count = Count(upper);
            float scale = Scale(source, upper);
            var reach = new float[count + 1];
            for (int point = 0; point <= count; point++)
                reach[point] = (source.Tops[0] - (point == count ? source.Bottoms[count - 1] : source.Tops[point])) * scale;
            return reach;
        }

        internal static float LinkRadius(bool upper) => Links().Width * .5f * Scale(Links(), upper);

        internal static void Clear()
        {
            if (links != null)
                foreach (var mesh in links.Meshes)
                    if (mesh)
                        UnityEngine.Object.Destroy(mesh);
            links = null;
        }

        private static int Count(bool upper) => upper ? UpperLinks : LowerLinks;

        private static float Scale(
            BoomboxChainLinks source,
            bool upper
        ) => Length(upper) / (source.Tops[0] - source.Bottoms[Count(upper) - 1]);

        private static BoomboxChainLinks Links()
        {
            if (links != null)
                return links;
            var prefab = ObjectDB.instance?.GetItemPrefab(Prefab);
            if (prefab == null)
                throw new InvalidOperationException("Native chain item is unavailable");
            links = BoomboxChainMesh.Build(prefab, LowerLinks);
            return links;
        }
    }
}
