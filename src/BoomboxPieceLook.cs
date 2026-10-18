using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxPieceLook
    {
        private const string NoiseKeyword = "_VALUENOISEVERTEX_ON";

        internal static HashSet<ParticleSystem> Working(Transform copy)
        {
            var kept = new HashSet<ParticleSystem>();
            foreach (var wear in copy.GetComponentsInChildren<WearNTear>(true))
            {
                var fresh = Field(wear, "m_new");
                foreach (var name in new[] { "m_worn", "m_broken" })
                {
                    var other = Field(wear, name);
                    if (other != null && other != fresh)
                        other.SetActive(false);
                }
                if (fresh != null)
                {
                    fresh.SetActive(true);
                    if (wear.GetComponent<Smelter>() == null && wear.GetComponent<CookingStation>() == null)
                        kept.UnionWith(fresh.GetComponentsInChildren<ParticleSystem>(true));
                }
            }
            foreach (var smelter in copy.GetComponentsInChildren<Smelter>(true))
                On(Field(smelter, "m_enabledObject"), kept);
            foreach (var station in copy.GetComponentsInChildren<CookingStation>(true))
            {
                foreach (var name in new[] { "m_haveFireObject", "m_haveFuelObject", "m_cookingObject" })
                    On(Field(station, name), kept);
                foreach (var slot in station.m_slots.Where(s => s != null))
                    slot.gameObject.SetActive(false);
            }
            foreach (var fermenter in copy.GetComponentsInChildren<Fermenter>(true))
            {
                On(Field(fermenter, "m_fermentingObject"), kept);
                On(Field(fermenter, "m_topObject"), kept);
                Off(Field(fermenter, "m_readyObject"));
            }
            foreach (var hive in copy.GetComponentsInChildren<Beehive>(true))
                On(Field(hive, "m_beeEffect"), kept);
            foreach (var fire in copy.GetComponentsInChildren<Fireplace>(true))
            {
                On(Field(fire, "m_enabledObject"), kept);
                On(Field(fire, "m_enabledObjectHigh"), kept);
                Off(Field(fire, "m_enabledObjectLow"));
            }
            foreach (var effects in copy.GetComponentsInChildren<ShipEffects>(true))
                foreach (var name in new[] {
                    "m_speedWakeRoot",
                    "m_wakeSoundRoot",
                    "m_inWaterSoundRoot",
                    "m_splashEffects"
                })
                    Off(Field(effects, name));
            foreach (var ship in copy.GetComponentsInChildren<Ship>(true))
            {
                Off(Field(ship, "m_ashdamageEffects"));
                On(Field(ship, "m_sailObject"), kept);
            }
            foreach (var chest in copy.GetComponentsInChildren<Container>(true))
            {
                Off(Field(chest, "m_open"));
                var closed = Field(chest, "m_closed");
                if (closed != null)
                    closed.SetActive(true);
            }
            foreach (var cloth in copy.GetComponentsInChildren<Cloth>(true))
                Object.DestroyImmediate(cloth);
            foreach (var rope in copy.GetComponentsInChildren<LineRenderer>(true))
                rope.enabled = false;
            foreach (var mask in copy.GetComponentsInChildren<Transform>(true).Where(
                t => string.Equals(t.name, "watermask", System.StringComparison.OrdinalIgnoreCase)
            ))
                mask.gameObject.SetActive(false);
            foreach (var hidden in copy.GetComponentsInChildren<Renderer>(true).Where(
                r => !r.enabled && !(r is ParticleSystemRenderer) && r.transform.childCount == 0
            ))
                hidden.gameObject.SetActive(false);
            foreach (var particles in kept)
            {
                var main = particles.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return kept;
        }

        internal static void Freeze(Transform copy, GameObject owner)
        {
            var skins = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null).ToArray();
            if (skins.Length == 0)
                return;
            var owned = Owned(owner);
            foreach (var skin in skins)
            {
                var baked = new Mesh { name = skin.sharedMesh.name + " frozen" };
                skin.BakeMesh(baked);
                var scale = skin.transform.lossyScale;
                var inverse = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
                baked.vertices = baked.vertices.Select(v => Vector3.Scale(v, inverse)).ToArray();
                baked.RecalculateBounds();
                owned.Assets.Add(baked);
                var materials = skin.sharedMaterials;
                bool enabled = skin.enabled;
                var holder = skin.gameObject;
                Object.DestroyImmediate(skin);
                holder.AddComponent<MeshFilter>().sharedMesh = baked;
                var renderer = holder.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                renderer.enabled = enabled;
            }
        }

        internal static void Still(Transform copy, GameObject owner)
        {
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)
                .Where(r => !(r is ParticleSystemRenderer)))
            {
                var materials = renderer.sharedMaterials;
                if (!materials.Any(Noisy))
                    continue;
                var owned = Owned(owner);
                renderer.sharedMaterials = materials.Select(material =>
                {
                    if (!Noisy(material))
                        return material;
                    var still = new Material(material) { name = material.name + " still" };
                    still.DisableKeyword(NoiseKeyword);
                    foreach (var property in new[] { "_ValueNoiseVertex", "_RippleDistance", "_ValueNoise" })
                        if (still.HasProperty(property))
                            still.SetFloat(property, 0f);
                    owned.Assets.Add(still);
                    return still;
                }).ToArray();
            }
        }

        private static bool Noisy(Material material) => material != null
        && (material.shaderKeywords.Contains(NoiseKeyword)
        || material.HasProperty("_ValueNoiseVertex") && material.GetFloat("_ValueNoiseVertex") > 0f);

        private static BoomboxOwnedAssets Owned(
            GameObject owner
        ) => owner.GetComponent<BoomboxOwnedAssets>() ?? owner.AddComponent<BoomboxOwnedAssets>();

        internal static void NearestOnly(Transform copy)
        {
            foreach (var group in copy.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                if (lods.Length < 2)
                    continue;
                var nearest = new HashSet<Renderer>(lods[0].renderers.Where(r => r != null));
                foreach (var renderer in lods.Skip(1).SelectMany(l => l.renderers).Where(r => r != null
                    && !nearest.Contains(r)))
                    renderer.enabled = false;
            }
        }

        private static GameObject Field(
            object owner,
            string name
        ) => AccessTools.Field(owner.GetType(), name)?.GetValue(owner) as GameObject;

        private static void On(GameObject target, HashSet<ParticleSystem> kept)
        {
            if (target == null)
                return;
            target.SetActive(true);
            kept.UnionWith(target.GetComponentsInChildren<ParticleSystem>(true));
        }

        private static void Off(GameObject target)
        {
            if (target != null)
                target.SetActive(false);
        }
    }
}
