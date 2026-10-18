using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

internal static class QaDemoPose
{
    internal static JObject Apply(AudioDeck deck, Camera camera, double time, double seconds)
    {
        Transform frame = deck.Visual.transform;
        string view;
        float angle, distance, fov;
        Vector3 point;
        if (seconds == 2)
        {
            view = "context-quarter";
            angle = 45f + (float)time * 4;
            distance = 1.05f;
            fov = 48;
            point = frame.TransformPoint(deck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds.center);
        }
        else if (time < 12)
        {
            view = time < 4
                ? "main-front"
                : time < 8
                    ? "main-quarter"
                    : "main-side";
            angle = (float)(time / 12 * 90);
            distance = .85f;
            fov = 48;
            point = frame.TransformPoint(deck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds.center);
        }
        else if (time < 28)
        {
            int slot = time < 20 ? 12 : 15;
            double phase = time - (slot == 12 ? 12 : 20);
            view = "mini" + slot + (phase < 4 ? "-front" : "-side");
            frame = frame.Find("BoomboxDecorations/DecorationSlot_" + slot + "/PendantPivot/NativeDecoration").GetChild(0);
            var selected = frame.GetComponentsInChildren<Renderer>(true).Where(
                r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
            ).ToArray();
            if (selected.Length == 0)
                throw new InvalidOperationException("Actual mini renderers missing");
            var bounds = selected[0].bounds;
            foreach (var renderer in selected.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            point = bounds.center;
            angle = (float)(phase / 8 * 90);
            distance = .5f;
            fov = 16;
        }
        else
        {
            view = "context-quarter";
            angle = 45f + (float)(time - 28) * 4;
            distance = 1.05f;
            fov = 48;
            point = frame.TransformPoint(deck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds.center);
        }
        var outward = (frame.forward * Mathf.Cos(angle * Mathf.Deg2Rad) + frame.right * Mathf.Sin(angle * Mathf.Deg2Rad)).normalized;
        camera.fieldOfView = fov;
        if (!view.StartsWith("mini", StringComparison.Ordinal))
        {
            var selected = deck.Visual.GetComponentsInChildren<Renderer>(true).Where(
                r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)
            ).ToArray();
            var whole = selected[0].bounds;
            foreach (var renderer in selected.Skip(1))
                whole.Encapsulate(renderer.bounds);
            point = whole.center;
            var rotation = Quaternion.LookRotation(-outward, frame.up);
            float vertical = Mathf.Tan(fov * Mathf.Deg2Rad * .5f) * .88f, horizontal = vertical * camera.aspect;
            foreach (var renderer in selected)
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = renderer.bounds.center + Vector3.Scale(
                        renderer.bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)
                    );
                    var v = Quaternion.Inverse(rotation) * (p - point);
                    distance = Mathf.Max(distance, Mathf.Abs(v.x) / horizontal - v.z, Mathf.Abs(v.y) / vertical - v.z);
                }
        }
        camera.transform.position = point + outward * distance;
        camera.transform.LookAt(point, frame.up);
        return new JObject
        {
            ["name"] = view,
            ["angle"] = angle,
            ["distance"] = distance,
            ["forwardDot"] = Vector3.Dot(outward, frame.forward),
            ["rightDot"] = Vector3.Dot(outward, frame.right),
            ["sourceTimeSamples"] = deck.Source.timeSamples
        };
    }
}
