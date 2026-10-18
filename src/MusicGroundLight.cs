using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal sealed class MusicGroundLight
    {
        public const float GrassSize = 9.5f;
        public const float NoGround = -10000f;
        private float Cell = .2f;
        private const float CrackHalf = .15f;
        public float Size
        {
            get;
            private set;
        }
        private readonly int Pixels;
        private readonly int Cells;
        private const int FreshCellsPerFrame = 1024;
        private const int RecheckedCellsPerFrame = 96;
        private static readonly string[] SurfaceLayers = {
            "terrain",
            "piece",
            "piece_nonsolid",
            "vehicle",
            "static_solid"
        };
        private readonly RenderTexture light;
        private readonly Texture2D ground;
        private readonly float[] heights;
        private readonly long[] owners;
        private readonly float[] window;
        private readonly Material glow;
        private readonly Material crack;
        private readonly Material ring;
        private readonly Material composite;
        private readonly GameObject box;
        private readonly Mesh strokes;
        private readonly Mesh cracks;
        private readonly Mesh plane;
        private readonly MaterialPropertyBlock ringLook = new MaterialPropertyBlock();
        private readonly CommandBuffer draw = new CommandBuffer { name = "Boombox music ground strokes" };
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector4> lights = new List<Vector4>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> indices = new List<int>();
        private readonly List<Vector3> crackVertices = new List<Vector3>();
        private readonly List<Vector4> crackShapes = new List<Vector4>();
        private readonly List<Vector4> crackLooks = new List<Vector4>();
        private readonly List<Vector4> crackTints = new List<Vector4>();
        private readonly List<int> crackIndices = new List<int>();
        private readonly int surfaceMask;
        private readonly int terrainLayer;
        private Vector2 origin;
        private int groundX;
        private int groundZ;
        private int recheck;
        private bool ringDrawn;
        private readonly MusicViewScene scene;

        public MusicGroundLight(float size, int pixels, int cells, MusicViewScene scene = null)
        {
            this.scene = scene;
            if (scene != null)
            {
                pixels = 512;
                cells = 32;
            }
            Size = size;
            Pixels = pixels;
            Cells = cells;
            if (scene != null)
                Cell = size / cells;
            heights = new float[cells * cells];
            owners = new long[cells * cells];
            window = new float[cells * cells];
            light = new RenderTexture(Pixels, Pixels, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "Boombox music ground light",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            light.Create();
            ground = new Texture2D(Cells, Cells, TextureFormat.RFloat, false, true)
            {
                name = "Boombox music ground height",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            for (int index = 0; index < owners.Length; index++)
                owners[index] = long.MinValue;
            glow = new Material(
                MusicEffectAssets.MapGlow
            )
            {
                name = "Boombox music map glow",
                mainTexture = Texture2D.whiteTexture,
                hideFlags = HideFlags.DontSave
            };
            crack = new Material(
                MusicEffectAssets.MapCrack
            )
            {
                name = "Boombox music map crack",
                hideFlags = HideFlags.DontSave
            };
            ring = new Material(
                MusicEffectAssets.MapRing
            )
            {
                name = "Boombox music map ring",
                hideFlags = HideFlags.DontSave
            };
            composite = new Material(
                MusicEffectAssets.GroundLight
            )
            {
                name = "Boombox music ground composite",
                hideFlags = HideFlags.DontSave
            };
            composite.SetTexture("_Light", light);
            composite.SetTexture("_Ground", ground);
            strokes = new Mesh { name = "Boombox music ground strokes", hideFlags = HideFlags.DontSave };
            strokes.MarkDynamic();
            cracks = new Mesh
            {
                name = "Boombox music ground cracks",
                hideFlags = HideFlags.DontSave,
                indexFormat = IndexFormat.UInt32
            };
            cracks.MarkDynamic();
            plane = new Mesh
            {
                name = "Boombox music ground plane",
                hideFlags = HideFlags.DontSave,
                vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f), Vector3.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Boombox music ground light";
            scene?.Own(box);
            Object.Destroy(box.GetComponent<Collider>());
            var renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = composite;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            box.SetActive(false);
            surfaceMask = LayerMask.GetMask(SurfaceLayers);
            terrainLayer = LayerMask.NameToLayer("terrain");
        }

        public void CoverRadius(float radius)
        {
            float size = Mathf.Max(GrassSize, radius * 2f + .4f);
            if (Mathf.Approximately(Size, size))
                return;
            Size = size;
            Cell = Mathf.Max(.2f, Size / Cells);
            ground.filterMode = FilterMode.Bilinear;
            for (int index = 0; index < owners.Length; index++)
                owners[index] = long.MinValue;
            recheck = 0;
        }

        public void Begin(Vector3 center)
        {
            var camera = scene?.Camera ?? Camera.main;
            if (camera != null && (camera.depthTextureMode & DepthTextureMode.Depth) == 0)
                camera.depthTextureMode |= DepthTextureMode.Depth;
            float texel = Size / Pixels;
            origin = new Vector2(
                Mathf.Floor((center.x - Size / 2f) / texel) * texel,
                Mathf.Floor((center.z - Size / 2f) / texel) * texel
            );
            vertices.Clear();
            lights.Clear();
            uvs.Clear();
            indices.Clear();
            crackVertices.Clear();
            crackShapes.Clear();
            crackLooks.Clear();
            crackTints.Clear();
            crackIndices.Clear();
            ringDrawn = false;
        }

        public Texture Heights => ground;

        public Vector4 HeightArea => new Vector4(groundX * Cell, groundZ * Cell, Cells * Cell, 0f);

        public void Ring(
            Vector3 center,
            float radius,
            float strength,
            float front,
            float phase,
            float brightness,
            Color color
        )
        {
            ringDrawn = true;
            ringLook.SetVector("_Ring", new Vector4(center.x, center.z, radius, strength));
            ringLook.SetFloat("_RingFront", front);
            ringLook.SetFloat("_RingPhase", phase);
            ringLook.SetFloat("_RingBrightness", brightness);
            ringLook.SetVector("_RingColor", new Vector4(color.r, color.g, color.b, 0f));
        }

        public void Crack(IReadOnlyList<MusicCrackPoint> line, Vector4 look, Color color)
        {
            int first = crackVertices.Count;
            for (int index = 0; index < line.Count; index++)
            {
                var before = line[Mathf.Max(0, index - 1)];
                var after = line[Mathf.Min(line.Count - 1, index + 1)];
                var normal = new Vector2(after.Z - before.Z, -(after.X - before.X));
                normal = normal.sqrMagnitude > 0f ? normal.normalized : Vector2.right;
                var point = line[index];
                for (int side = -1; side <= 1; side += 2)
                {
                    crackVertices.Add(new Vector3(
                            (point.X + normal.x * CrackHalf * side - origin.x) / Size,
                            (point.Z + normal.y * CrackHalf * side - origin.y) / Size,
                            0f
                        ));
                    crackShapes.Add(new Vector4(side, point.Radial, point.Width, 0f));
                    crackLooks.Add(look);
                    crackTints.Add(new Vector4(color.r, color.g, color.b, 0f));
                }
                if (index == 0)
                    continue;
                int a = first + (index - 1) * 2;
                crackIndices.Add(a);
                crackIndices.Add(a + 1);
                crackIndices.Add(a + 2);
                crackIndices.Add(a + 1);
                crackIndices.Add(a + 3);
                crackIndices.Add(a + 2);
            }
        }

        public float GroundAt(Vector3 point)
        {
            int cx = Mathf.FloorToInt(point.x / Cell), cz = Mathf.FloorToInt(point.z / Cell);
            int slot = Slot(cx, cz);
            return owners[slot] == Key(cx, cz) ? heights[slot] : Measure(cx, cz);
        }

        public void Band(Vector3 center, float radius, float width, float soft, Color color)
        {
            const int segments = 160;
            float[] radii = { radius - width / 2f - soft, radius - width / 2f, radius + width / 2f, radius + width / 2f + soft };
            float[] shares = { 0f, 1f, 1f, 0f };
            var middle = new Vector2((center.x - origin.x) / Size, (center.z - origin.y) / Size);
            int first = vertices.Count;
            for (int segment = 0; segment <= segments; segment++)
            {
                float angle = 2f * Mathf.PI * segment / segments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                for (int ring = 0; ring < radii.Length; ring++)
                {
                    var point = middle + direction * (Mathf.Max(0f, radii[ring]) / Size);
                    vertices.Add(new Vector3(point.x, point.y, 0f));
                    lights.Add(new Vector4(color.r * shares[ring], color.g * shares[ring], color.b * shares[ring], 0f));
                    uvs.Add(new Vector2(.5f, .5f));
                }
            }
            for (int segment = 0; segment < segments; segment++)
                for (int ring = 0; ring + 1 < radii.Length; ring++)
                {
                    int a = first + segment * radii.Length + ring, b = a + radii.Length;
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(a + 1);
                    indices.Add(a + 1);
                    indices.Add(b);
                    indices.Add(b + 1);
                }
        }

        public void End(Vector3 center, bool visible)
        {
            Refresh(center);
            visible &= indices.Count > 0 || crackIndices.Count > 0 || ringDrawn;
            box.SetActive(visible);
            if (!visible)
                return;
            strokes.Clear();
            strokes.SetVertices(vertices);
            strokes.SetUVs(0, uvs);
            strokes.SetUVs(1, lights);
            strokes.SetTriangles(indices, 0);
            cracks.Clear();
            cracks.SetVertices(crackVertices);
            cracks.SetUVs(0, crackShapes);
            cracks.SetUVs(1, crackLooks);
            cracks.SetUVs(2, crackTints);
            cracks.SetTriangles(crackIndices, 0);
            draw.Clear();
            draw.SetRenderTarget(light);
            draw.ClearRenderTarget(false, true, Color.clear);
            draw.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            if (indices.Count > 0)
                draw.DrawMesh(strokes, Matrix4x4.identity, glow, 0, 0);
            if (crackIndices.Count > 0)
                draw.DrawMesh(cracks, Matrix4x4.identity, crack, 0, 0);
            if (ringDrawn)
            {
                ringLook.SetVector("_MapArea", new Vector4(origin.x, origin.y, Size, 0f));
                draw.DrawMesh(plane, Matrix4x4.identity, ring, 0, 0, ringLook);
            }
            Graphics.ExecuteCommandBuffer(draw);
            composite.SetVector("_LightArea", new Vector4(origin.x, origin.y, Size, 0f));
            composite.SetVector("_GroundArea", HeightArea);
            composite.SetVector("_GrassArea", new Vector4(center.x, center.z, GrassSize / 2f, 0f));
            box.transform.SetPositionAndRotation(
                new Vector3(origin.x + Size / 2f, center.y, origin.y + Size / 2f),
                Quaternion.identity
            );
            box.transform.localScale = new Vector3(Size, 6f, Size);
        }

        public void Hide() => box.SetActive(false);

        public void Destroy()
        {
            Object.Destroy(box);
            Object.Destroy(strokes);
            Object.Destroy(cracks);
            Object.Destroy(crack);
            Object.Destroy(plane);
            Object.Destroy(ring);
            draw.Release();
            Object.Destroy(glow);
            Object.Destroy(composite);
            light.Release();
            Object.Destroy(light);
            Object.Destroy(ground);
        }

        private void Refresh(Vector3 center)
        {
            groundX = Mathf.FloorToInt((center.x - Cells * Cell / 2f) / Cell);
            groundZ = Mathf.FloorToInt((center.z - Cells * Cell / 2f) / Cell);
            int budget = FreshCellsPerFrame;
            for (int j = 0; j < Cells; j++)
                for (int i = 0; i < Cells; i++)
                {
                    int cx = groundX + i, cz = groundZ + j, slot = Slot(cx, cz);
                    if (owners[slot] != Key(cx, cz) && budget-- > 0)
                        Measure(cx, cz);
                    window[j * Cells + i] = owners[slot] == Key(cx, cz) ? heights[slot] : NoGround;
                }
            for (int step = 0; step < RecheckedCellsPerFrame; step++, recheck = (recheck + 1) % (Cells * Cells))
                Measure(groundX + recheck % Cells, groundZ + recheck / Cells);
            ground.SetPixelData(window, 0);
            ground.Apply(false);
        }

        private float Measure(int cx, int cz)
        {
            if (scene != null)
            {
                int sample = Slot(cx, cz);
                owners[sample] = Key(cx, cz);
                return heights[sample] = scene.GroundHeight;
            }
            var point = new Vector3((cx + .5f) * Cell, 0f, (cz + .5f) * Cell);
            float height = NoGround;
            if (Physics.Raycast(
                new Vector3(point.x, 5000f, point.z),
                Vector3.down,
                out var hit,
                10000f,
                surfaceMask,
                QueryTriggerInteraction.Ignore
            )
            && hit.collider.gameObject.layer == terrainLayer)
            {
                float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : float.NegativeInfinity;
                if (hit.point.y > water)
                    height = hit.point.y;
            }
            int slot = Slot(cx, cz);
            heights[slot] = height;
            owners[slot] = Key(cx, cz);
            return height;
        }

        private int Slot(int cx, int cz) => (cz % Cells + Cells) % Cells * Cells + (cx % Cells + Cells) % Cells;

        private static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    }
}
