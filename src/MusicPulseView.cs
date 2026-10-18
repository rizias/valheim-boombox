using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal sealed class MusicPulseView : IMusicEffectView
    {
        private const float FrontWidth = .06f;
        private const float WallHeight = 1.2f;
        private const float Refraction = .018f;
        private const float Fringe = .35f;
        private const double RippleHz = 6;
        private readonly MusicPulse pulse;
        private readonly MusicGroundLight ground;
        private readonly MusicViewScene scene;
        private readonly MusicGroundRipple ripple;
        private readonly Mesh wallMesh = MusicStrikeView.WallMesh();
        private readonly Material lightMaterial;
        private readonly Material shiftMaterial;
        private readonly GameObject lightWall;
        private readonly GameObject shiftWall;

        public MusicPulseView(MusicPulse pulse, MusicViewScene scene = null)
        {
            this.scene = scene;
            ground = new MusicGroundLight(MusicGroundLight.GrassSize, 1024, 64, scene);
            this.pulse = pulse;
            ripple = new MusicGroundRipple(ground, Fringe, scene);
            lightMaterial = new Material(
                MusicEffectAssets.WallLight
            )
            {
                name = "Boombox music pulse wall",
                hideFlags = HideFlags.DontSave
            };
            lightMaterial.SetFloat("_WallHeight", WallHeight);
            shiftMaterial = new Material(
                MusicEffectAssets.AirShift
            )
            {
                name = "Boombox music pulse air",
                hideFlags = HideFlags.DontSave
            };
            shiftMaterial.SetFloat("_Refraction", Refraction);
            shiftMaterial.SetFloat("_WallHeight", WallHeight);
            shiftMaterial.SetFloat("_Fringe", Fringe);
            lightWall = Wall("Boombox music pulse wall", lightMaterial);
            shiftWall = Wall("Boombox music pulse air", shiftMaterial);
        }

        public void Update(MusicEffectFrame frame)
        {
            float radius = pulse.Radius, strength = Mathf.Clamp01(pulse.Strength);
            bool shown = radius > 0f && strength > 0f;
            float phase = (float)(Time.unscaledTimeAsDouble * RippleHz % 1.0);
            ground.CoverRadius(frame.Radius);
            ground.Begin(frame.Center);
            if (shown)
                ground.Ring(
                    frame.Center,
                    radius,
                    strength,
                    FrontWidth,
                    phase,
                    frame.Brightness * strength,
                    frame.Color
                );
            ground.End(frame.Center, shown);
            if (shown)
                ripple.Show(frame.Center, radius, strength, phase);
            else
                ripple.Hide();
            Place(lightWall, shown, frame.Center, radius);
            Place(shiftWall, shown, frame.Center, radius);
            lightMaterial.SetVector("_WallColor", new Vector4(frame.Color.r, frame.Color.g, frame.Color.b, 0f));
            lightMaterial.SetFloat("_Strength", strength);
            shiftMaterial.SetFloat("_Strength", strength);
        }

        public void Hide()
        {
            ground.Hide();
            ripple.Hide();
            lightWall.SetActive(false);
            shiftWall.SetActive(false);
        }

        public void Destroy()
        {
            ground.Destroy();
            ripple.Destroy();
            Object.Destroy(lightWall);
            Object.Destroy(shiftWall);
            Object.Destroy(lightMaterial);
            Object.Destroy(shiftMaterial);
            Object.Destroy(wallMesh);
        }

        private GameObject Wall(string name, Material material)
        {
            var holder = new GameObject(name);
            scene?.Own(holder);
            holder.AddComponent<MeshFilter>().sharedMesh = wallMesh;
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            holder.SetActive(false);
            return holder;
        }

        private static void Place(GameObject wall, bool shown, Vector3 center, float radius)
        {
            wall.SetActive(shown);
            if (!shown)
                return;
            wall.transform.SetPositionAndRotation(center, Quaternion.identity);
            wall.transform.localScale = new Vector3(radius, 1f, radius);
        }
    }
}
