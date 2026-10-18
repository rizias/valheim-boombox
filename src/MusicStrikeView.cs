using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal sealed class MusicStrikeView : IMusicStrikeView
    {
        private const float Travel = .15f;
        private const float Fade = .15f;
        private const float FrontWidth = .06f;
        private const float Soft = .012f;
        private const float WallHeight = 1.2f;
        private const float Refraction = .018f;
        private const float DustPerStrength = 40f;
        private const float DustRise = .3f;
        private const float DustSettle = .6f;
        private const float GrainSize = .03f;
        private const int DustCapacity = 600;
        private static readonly Color DustColor = new Color(.64f, .58f, .48f, 1f);

        private struct Grain
        {
            public Vector3 Point;
            public double Launch;
            public float Peak;
        }

        private readonly MusicGroundLight ground;
        private readonly MusicViewScene scene;
        private readonly List<GameObject> walls = new List<GameObject>();
        private readonly List<Material> wallMaterials = new List<Material>();
        private readonly Mesh wallMesh = WallMesh();
        private readonly Mesh grainMesh;
        private readonly Material grainMaterial;
        private readonly List<Grain> grains = new List<Grain>();
        private readonly Matrix4x4[] matrices = new Matrix4x4[DustCapacity];
        private readonly System.Random random = new System.Random(11);

        public MusicStrikeView(GameObject model, MusicViewScene scene = null)
        {
            this.scene = scene;
            ground = new MusicGroundLight(MusicGroundLight.GrassSize, 1024, 64, scene);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            grainMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(sphere);
            grainMaterial = BoomboxMaterials.Surface(model, DustColor, "Boombox music dust");
            grainMaterial.enableInstancing = true;
        }

        public MusicReach Reach
        {
            get;
        } = MusicReach.Front(Travel, Travel + Fade);

        public void Update(MusicEffectFrame frame)
        {
            if (scene != null)
                ground.CoverRadius(frame.Radius);
            bool visible = !frame.Moment.Silent;
            foreach (var strike in frame.Fresh)
                Spawn(strike, frame);
            ground.Begin(frame.Center);
            int wall = 0;
            foreach (var strike in frame.Strikes)
            {
                double age = frame.Time - strike.Time;
                if (age < 0)
                    continue;
                float radius = frame.Radius * (1f - Mathf.Pow(1f - Mathf.Min(1f, (float)age / Travel), 3f));
                float fade = age <= Travel ? 1f : Mathf.Max(0f, 1f - ((float)age - Travel) / Fade);
                float strength = strike.Strength * fade;
                if (radius <= 0f || strength <= 0f)
                    continue;
                ground.Band(frame.Center, radius, FrontWidth, Soft, frame.Color * (frame.Brightness * strength));
                Wall(wall++, frame.Center, radius, strength, visible);
            }
            for (int index = wall; index < walls.Count; index++)
                walls[index].SetActive(false);
            ground.End(frame.Center, visible);
            DrawDust(frame.Time, visible);
        }

        public void Hide()
        {
            ground.Hide();
            foreach (var wall in walls)
                wall.SetActive(false);
            grains.Clear();
        }

        public void Destroy()
        {
            ground.Destroy();
            foreach (var wall in walls)
                if (wall != null)
                    UnityEngine.Object.Destroy(wall);
            foreach (var material in wallMaterials)
                if (material != null)
                    UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(wallMesh);
            UnityEngine.Object.Destroy(grainMaterial);
        }

        private void Spawn(MusicLiveStrike strike, MusicEffectFrame frame)
        {
            int count = MusicAnalysisParams.Round(DustPerStrength * strike.Strength * frame.Amount);
            for (int index = 0; index < count && grains.Count < DustCapacity; index++)
            {
                float distance = (float)random.NextDouble() * frame.Radius;
                float angle = (float)random.NextDouble() * 2f * Mathf.PI;
                var point = frame.Center + new Vector3(Mathf.Sin(angle) * distance, 0f, Mathf.Cos(angle) * distance);
                float height = ground.GroundAt(point);
                float peak = DustRise * (.5f + .5f * (float)random.NextDouble());
                if (height <= MusicGroundLight.NoGround)
                    continue;
                point.y = height;
                grains.Add(
                    new Grain
                    {
                        Point = point,
                        Launch = strike.Time + Reach.Arrival(distance, frame.Radius),
                        Peak = peak
                    }
                );
            }
        }

        private void DrawDust(double time, bool visible)
        {
            grains.RemoveAll(grain => time - grain.Launch > DustSettle);
            if (!visible)
                return;
            int shown = 0;
            foreach (var grain in grains)
            {
                double age = time - grain.Launch;
                if (age < 0)
                    continue;
                float share = (float)age / DustSettle;
                float height = 4f * grain.Peak * share * (1f - share);
                matrices[shown++] = Matrix4x4.TRS(
                    grain.Point + Vector3.up * (GrainSize / 2f + height),
                    Quaternion.identity,
                    Vector3.one * GrainSize
                );
            }
            if (shown > 0)
                Graphics.DrawMeshInstanced(
                    grainMesh,
                    0,
                    grainMaterial,
                    matrices,
                    shown,
                    null,
                    ShadowCastingMode.On,
                    true,
                    scene?.Layer ?? 0,
                    scene?.Camera,
                    LightProbeUsage.Off
                );
        }

        private void Wall(int index, Vector3 center, float radius, float strength, bool visible)
        {
            while (walls.Count <= index)
            {
                var holder = new GameObject("Boombox music air front");
                scene?.Own(holder);
                holder.AddComponent<MeshFilter>().sharedMesh = wallMesh;
                var renderer = holder.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = new Material(
                    MusicEffectAssets.AirShift
                )
                {
                    name = "Boombox music air front",
                    hideFlags = HideFlags.DontSave
                };
                wallMaterials.Add(renderer.sharedMaterial);
                renderer.sharedMaterial.SetFloat("_Refraction", Refraction);
                renderer.sharedMaterial.SetFloat("_WallHeight", WallHeight);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                walls.Add(holder);
            }
            var wall = walls[index];
            wall.SetActive(visible);
            wall.transform.SetPositionAndRotation(center, Quaternion.identity);
            wall.transform.localScale = new Vector3(radius, 1f, radius);
            wallMaterials[index].SetFloat("_Strength", strength);
        }

        internal static Mesh WallMesh()
        {
            const int segments = 180;
            var vertices = new Vector3[(segments + 1) * 2];
            var triangles = new int[segments * 6];
            for (int segment = 0; segment <= segments; segment++)
            {
                float angle = 2f * Mathf.PI * segment / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[segment * 2] = direction;
                vertices[segment * 2 + 1] = direction + Vector3.up * WallHeight;
                if (segment == segments)
                    continue;
                int a = segment * 2, t = segment * 6;
                triangles[t] = a;
                triangles[t + 1] = a + 1;
                triangles[t + 2] = a + 2;
                triangles[t + 3] = a + 1;
                triangles[t + 4] = a + 3;
                triangles[t + 5] = a + 2;
            }
            var mesh = new Mesh
            {
                name = "Boombox music air front",
                vertices = vertices,
                triangles = triangles,
                hideFlags = HideFlags.DontSave
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
