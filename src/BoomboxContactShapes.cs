using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxContactBox
    {
        public string name;
        public float[] min;
        public float[] max;
        internal Vector3 Min => new Vector3(min[0], min[1], min[2]);
        internal Vector3 Max => new Vector3(max[0], max[1], max[2]);
        internal bool Holds(
            Vector3 point
        ) => point.x >= min[0] && point.x <= max[0] && point.y >= min[1] && point.y <= max[1] && point.z >= min[2]
        && point.z <= max[2];
        internal bool Encloses(
            Vector3 point
        ) => point.x > min[0]
            && point.x < max[0]
            && point.y > min[1]
            && point.y < max[1]
            && point.z > min[2]
            && point.z < max[2];
    }

    public sealed class BoomboxContactCapsule
    {
        public float[] a;
        public float[] b;
        public float radius;
    }

    public sealed class BoomboxContactShapes
    {
        public BoomboxContactBox[] boxes;
        public BoomboxContactCapsule[] capsules;
        public string[] mounts;
        public Dictionary<string, BoomboxMountGroup[]> mountLayouts;
        private const float SurfaceGap = .0001f;
        private static BoomboxContactShapes instance;

        internal static BoomboxContactShapes Instance => instance ?? (instance = Load());

        internal IEnumerable<BoomboxMountGroup> ActiveMounts => mounts.SelectMany(
            name => mountLayouts.TryGetValue(name, out var groups)
                ? groups
                : throw new InvalidOperationException("Unknown pendant mount layout: " + name)
        );

        internal void Contacts(Vector3 point, float radius, List<Vector3> pushes)
        {
            pushes.Clear();
            if (Exit(point, radius, pushes))
                return;
            foreach (var box in boxes)
            {
                var offset = point - Vector3.Max(box.Min, Vector3.Min(box.Max, point));
                float distance = offset.magnitude;
                if (distance <= 0f || distance >= radius)
                    continue;
                var push = offset / distance * (radius - distance);
                if (!Enclosed(point + push))
                    pushes.Add(push);
            }
            foreach (var capsule in capsules)
            {
                var start = new Vector3(capsule.a[0], capsule.a[1], capsule.a[2]);
                var segment = new Vector3(capsule.b[0], capsule.b[1], capsule.b[2]) - start;
                var offset = point
                    - (start + segment * Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude));
                float distance = offset.magnitude, reach = radius + capsule.radius;
                if (distance <= 0f || distance >= reach)
                    continue;
                var push = offset / distance * (reach - distance);
                var around = -offset / distance * (reach + distance);
                if (!Enclosed(point + push))
                    pushes.Add(push);
                else if (!Enclosed(point + around))
                    pushes.Add(around);
            }
        }

        private bool Exit(Vector3 point, float radius, List<Vector3> pushes)
        {
            bool inside = false;
            float nearest = float.PositiveInfinity;
            var exit = Vector3.zero;
            foreach (var box in boxes)
            {
                if (!box.Holds(point))
                    continue;
                inside = true;
                for (int axis = 0; axis < 3; axis++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float plane = side < 0 ? box.min[axis] : box.max[axis];
                        float distance = Mathf.Abs(plane - point[axis]);
                        if (distance >= nearest)
                            continue;
                        var target = point;
                        target[axis] = plane + side * SurfaceGap;
                        if (Enclosed(target))
                            continue;
                        nearest = distance;
                        exit = Vector3.zero;
                        exit[axis] = side * (distance + radius);
                    }
            }
            if (inside && nearest < float.PositiveInfinity)
                pushes.Add(exit);
            return inside;
        }

        private bool Enclosed(Vector3 point)
        {
            foreach (var box in boxes)
                if (box.Encloses(point))
                    return true;
            return false;
        }

        private static BoomboxContactShapes Load()
        {
            using (var stream = typeof(BoomboxContactShapes).Assembly.GetManifestResourceStream(
                "ValheimBoombox.ContactShapes.json"
            ))
            {
                if (stream == null)
                    throw new InvalidOperationException("Contact shapes resource is missing");
                using (var reader = new StreamReader(stream))
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<BoomboxContactShapes>(reader.ReadToEnd());
            }
        }
    }
}
