using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    public static class BoomboxCreatureFigurine
    {
        public static Vector3 MaximumSize(
            Bounds body
        ) => new Vector3(body.size.x * .135f, body.size.x * .17f, body.size.z * .9f);

        public sealed class Result
        {
            public Transform Model;
            public Transform HeadPivot;
            public Transform HeadBone;
            public string Pose;
            public Bounds Bounds;
        }

        public static bool CanCreate(GameObject source)
        {
            var animator = SourceAnimator(source);
            return animator != null
                && Idle(animator) != null
                && (animator.isHuman || BoneHead(animator.transform) != null);
        }

        public static Result Create(GameObject source, Transform parent, Vector3 maximumSize)
        {
            var sourceAnimator = SourceAnimator(source);
            var clip = sourceAnimator == null ? null : Idle(sourceAnimator);
            if (clip == null)
                throw new InvalidOperationException("Creature has no native idle animation: " + source?.name);
            var host = new GameObject("NativeFigurine_" + source.name) { hideFlags = HideFlags.DontSave };
            host.SetActive(false);
            host.transform.position = Vector3.zero;
            try
            {
                var model = UnityEngine.Object.Instantiate(sourceAnimator.gameObject, host.transform, false);
                Strip(model);
                host.SetActive(true);
                var animator = model.GetComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.enabled = true;
                animator.Rebind();
                var graph = PlayableGraph.Create("Boombox native idle pose");
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    playable.SetApplyPlayableIK(false);
                    playable.SetTime(Mathf.Min(.15f, clip.length * .1f));
                    playable.SetSpeed(0);
                    output.SetSourcePlayable(playable);
                    graph.Evaluate(0f);
                    var transforms = model.GetComponentsInChildren<Transform>(true);
                    var positions = transforms.Select(t => t.localPosition).ToArray();
                    var rotations = transforms.Select(t => t.localRotation).ToArray();
                    var scales = transforms.Select(t => t.localScale).ToArray();
                    animator.enabled = false;
                    graph.Destroy();
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        transforms[i].localPosition = positions[i];
                        transforms[i].localRotation = rotations[i];
                        transforms[i].localScale = scales[i];
                    }
                }
                finally
                {
                    if (graph.IsValid())
                        graph.Destroy();
                }
                var head = animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Head)
                    : BoneHead(model.transform);
                if (head == null)
                    throw new InvalidOperationException("Creature has no head bone: " + source.name);
                var pivot = new GameObject("HeadPivot").transform;
                pivot.SetParent(head.parent, false);
                pivot.localPosition = head.localPosition;
                pivot.rotation = host.transform.rotation;
                head.SetParent(pivot, true);
                head.localScale *= 3f;
                host.transform.SetParent(parent, false);
                host.transform.localPosition = Vector3.zero;
                host.transform.localRotation = Quaternion.identity;
                host.transform.localScale = Vector3.one;
                var bounds = GeometryBounds(host.transform);
                if (bounds.size.sqrMagnitude < .000001f)
                    throw new InvalidOperationException("Creature has no visible geometry: " + source.name);
                float scale = Mathf.Min(
                    maximumSize.x / Mathf.Max(bounds.size.x, .001f),
                    maximumSize.y / Mathf.Max(bounds.size.y, .001f),
                    maximumSize.z / Mathf.Max(bounds.size.z, .001f)
                );
                host.transform.localScale = Vector3.one * scale;
                host.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                return new Result
                {
                    Model = host.transform,
                    HeadPivot = pivot,
                    HeadBone = head,
                    Pose = clip.name,
                    Bounds = bounds
                };
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(host);
                throw;
            }
        }

        private static Animator SourceAnimator(GameObject source) => source == null
            ? null
            : source.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.runtimeAnimatorController != null);
        private static AnimationClip Idle(Animator animator) => animator.runtimeAnimatorController.animationClips
        .Where(c => c != null && c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
        .OrderBy(c => string.Equals(c.name, "Idle", StringComparison.OrdinalIgnoreCase)
            ? 0
            : c.name.IndexOf("alert", StringComparison.OrdinalIgnoreCase) >= 0
                ? 2
                : 1)
        .ThenBy(c => c.name, StringComparer.Ordinal).FirstOrDefault();

        private static Transform BoneHead(Transform root)
        {
            return root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(t => t != null).Distinct()
            .Where(
            t =>
                {
                    string n = new string(t.name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                    return n == "head"
                        || n == "shamanhead"
                        || n == "bip01head"
                        || n == "mixamorighead"
                        || n.EndsWith("head");
                }
            )
            .OrderBy(t => t.name.Length).FirstOrDefault();
        }

        private static void Strip(GameObject model)
        {
            foreach (var audio in model.GetComponentsInChildren<AudioSource>(true))
                UnityEngine.Object.DestroyImmediate(audio);
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (var joint in model.GetComponentsInChildren<Joint>(true))
                UnityEngine.Object.DestroyImmediate(joint);
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(body);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && behaviour.GetType().FullName != "SoftReferenceableAssets.ReferenceHolder")
                    UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var light in model.GetComponentsInChildren<Light>(true))
                light.enabled = false;
            foreach (var particles in model.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var renderer = particles.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.enabled = false;
            }
            foreach (var group in model.GetComponentsInChildren<LODGroup>(true))
            {
                var levels = group.GetLODs();
                if (levels.Length > 0)
                {
                    var first = new HashSet<Renderer>(levels[0].renderers);
                    foreach (var renderer in levels.SelectMany(l => l.renderers).Where(r => r != null))
                        renderer.enabled = first.Contains(renderer);
                }
                UnityEngine.Object.DestroyImmediate(group);
            }
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Bounds GeometryBounds(Transform root)
        {
            Bounds result = default;
            bool found = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer)
                    continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    if (skin.sharedMesh == null)
                        continue;
                    skin.updateWhenOffscreen = true;
                    mesh = skin.sharedMesh;
                    if (!mesh.isReadable)
                    {
                        var baked = new Mesh();
                        try
                        {
                            skin.BakeMesh(baked, false);
                            var unscaled = root.worldToLocalMatrix
                                * Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                            foreach (var vertex in baked.vertices)
                            {
                                var p = unscaled.MultiplyPoint3x4(vertex);
                                if (!found)
                                {
                                    result = new Bounds(p, Vector3.zero);
                                    found = true;
                                }
                                else
                                    result.Encapsulate(p);
                            }
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(baked);
                        }
                        continue;
                    }
                    var vertices = mesh.vertices;
                    var weights = mesh.boneWeights;
                    var binds = mesh.bindposes;
                    var matrices = skin.bones.Select((bone, i) => root.worldToLocalMatrix * bone.localToWorldMatrix
                        * binds[i]).ToArray();
                    Bounds local = default;
                    bool localFound = false;
                    var toRenderer = skin.transform.worldToLocalMatrix * root.localToWorldMatrix;
                    foreach (int index in mesh.triangles.Distinct())
                    {
                        var w = weights[index];
                        var v = vertices[index];
                        var p = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0;
                        if (w.weight1 > 0)
                            p += matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1;
                        if (w.weight2 > 0)
                            p += matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2;
                        if (w.weight3 > 0)
                            p += matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                        if (!found)
                        {
                            result = new Bounds(p, Vector3.zero);
                            found = true;
                        }
                        else
                            result.Encapsulate(p);
                        var lp = toRenderer.MultiplyPoint3x4(p);
                        if (!localFound)
                        {
                            local = new Bounds(lp, Vector3.zero);
                            localFound = true;
                        }
                        else
                            local.Encapsulate(lp);
                    }
                    local.Expand(local.size * .3f);
                    skin.localBounds = local;
                    continue;
                }
                if (mesh == null)
                    continue;
                var matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                if (mesh.isReadable)
                {
                    var vertices = mesh.vertices;
                    foreach (int index in mesh.triangles.Distinct())
                    {
                        var p = matrix.MultiplyPoint3x4(vertices[index]);
                        if (!found)
                        {
                            result = new Bounds(p, Vector3.zero);
                            found = true;
                        }
                        else
                            result.Encapsulate(p);
                    }
                }
                else
                {
                    for (int i = 0; i < 8; i++)
                    {
                        var p = matrix.MultiplyPoint3x4(
                            mesh.bounds.center + Vector3.Scale(
                                mesh.bounds.extents,
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
                            result = new Bounds(p, Vector3.zero);
                            found = true;
                        }
                        else
                            result.Encapsulate(p);
                    }
                }
            }
            return result;
        }
    }
}
