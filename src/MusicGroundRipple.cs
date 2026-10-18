using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal sealed class MusicGroundRipple
    {
        private readonly MusicGroundLight ground;
        private readonly Material material;
        private readonly GameObject box;

        public MusicGroundRipple(MusicGroundLight ground, float fringe, MusicViewScene scene = null)
        {
            this.ground = ground;
            material = new Material(
                MusicEffectAssets.GroundShift
            )
            {
                name = "Boombox music ground ripple",
                hideFlags = HideFlags.DontSave
            };
            material.SetFloat("_Fringe", fringe);
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Boombox music ground ripple";
            scene?.Own(box);
            Object.Destroy(box.GetComponent<Collider>());
            var renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            box.SetActive(false);
        }

        public void Show(Vector3 center, float radius, float strength, float phase)
        {
            box.SetActive(true);
            material.SetTexture("_Ground", ground.Heights);
            material.SetVector("_GroundArea", ground.HeightArea);
            material.SetVector("_Ring", new Vector4(center.x, center.z, radius, strength));
            material.SetFloat("_RingPhase", phase);
            box.transform.SetPositionAndRotation(center, Quaternion.identity);
            box.transform.localScale = new Vector3(radius * 2f, 6f, radius * 2f);
        }

        public void Hide() => box.SetActive(false);

        public void Destroy()
        {
            Object.Destroy(box);
            Object.Destroy(material);
        }
    }
}
