using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxPendulum
    {
        private struct Bound
        {
            internal int Point;
            internal Vector3 Normal;
            internal float Offset;
        }

        private const float Relaxation = .3f;
        private const float ContactMargin = .005f;
        private readonly Transform holder;
        private readonly Transform[] links;
        private readonly Transform pivot;
        private readonly Quaternion facing;
        private readonly Vector3 hanging;
        private readonly float[] reach;
        private readonly Vector3[] points;
        private readonly Vector3[] previous;
        private readonly Vector3[] local;
        private readonly Vector3[] pressed;
        private readonly Vector3[] parted;
        private readonly List<Bound> bounds = new List<Bound>();
        private readonly List<Vector3> pushes = new List<Vector3>();
        private readonly float linkRadius;
        private readonly float radius;
        private Matrix4x4 placement;
        private Vector3 anchor;
        private Vector3 kick;
        private Vector3 carried;
        private Vector3 down;
        private float unit;
        private Quaternion attitude;

        internal BoomboxPendulum(
            Transform holder,
            Transform chain,
            Transform pivot,
            float[] chainReach,
            float linkRadius,
            Vector3 center,
            float radius
        )
        {
            this.holder = holder;
            this.pivot = pivot;
            this.linkRadius = linkRadius;
            this.radius = radius;
            facing = pivot.localRotation;
            hanging = facing * center;
            links = new Transform[chain.childCount];
            for (int link = 0; link < links.Length; link++)
                links[link] = chain.GetChild(link);
            reach = new float[chainReach.Length + 1];
            Array.Copy(chainReach, reach, chainReach.Length);
            reach[chainReach.Length] = chainReach[chainReach.Length - 1] + hanging.magnitude;
            points = new Vector3[reach.Length];
            previous = new Vector3[reach.Length];
            local = new Vector3[reach.Length];
            pressed = new Vector3[reach.Length];
            parted = new Vector3[reach.Length];
            Rest();
        }

        internal void Rest()
        {
            for (int point = 0; point < Last; point++)
                local[point] = Vector3.down * reach[point];
            local[Last] = local[End] + hanging;
            Hold();
        }

        internal void Hold()
        {
            anchor = holder.position;
            for (int point = 0; point < points.Length; point++)
                points[point] = previous[point] = holder.TransformVector(local[point]);
            unit = holder.TransformVector(Vector3.up).magnitude;
            down = holder.TransformDirection(Vector3.down);
            attitude = holder.rotation;
            kick = Vector3.zero;
            Apply();
        }

        internal void Push(Vector2 angular)
        {
            kick += holder.TransformDirection(new Vector3(angular.y, 0f, -angular.x)) * Mathf.Deg2Rad;
        }

        internal bool Begin(Transform model)
        {
            if ((holder.position - anchor).sqrMagnitude > 1f)
            {
                Rest();
                return false;
            }
            placement = model.worldToLocalMatrix * holder.localToWorldMatrix;
            return true;
        }

        internal void Integrate(Matrix4x4 model, float dt, float damping)
        {
            var frame = model * placement;
            Vector3 mount = frame.GetColumn(3);
            unit = frame.MultiplyVector(Vector3.up).magnitude;
            attitude = frame.rotation;
            down = attitude * Vector3.down;
            var shift = mount - anchor;
            carried = shift / dt;
            anchor = mount;
            float keep = Mathf.Exp(-damping * dt);
            for (int point = 1; point < points.Length; point++)
            {
                var velocity = (points[point] - previous[point]) / dt;
                velocity = carried + (velocity - carried) * keep + Physics.gravity * dt + kick * reach[point] * unit;
                previous[point] = points[point] - shift;
                points[point] += velocity * dt - shift;
            }
            points[0] = previous[0] = Vector3.zero;
            kick = Vector3.zero;
            bounds.Clear();
        }

        internal static void Separate(List<BoomboxPendulum> pendants, List<BoomboxPendantPair> pairs, float fraction)
        {
            pairs.Clear();
            for (int first = 0; first < pendants.Count; first++)
                for (int second = first + 1; second < pendants.Count; second++)
                {
                    var a = pendants[first];
                    var b = pendants[second];
                    float reach = a.Radius + b.Radius, distance = Vector3.Distance(a.Center, b.Center);
                    if (distance >= reach || distance <= 0f)
                        continue;
                    pairs.Add(
                        new BoomboxPendantPair
                        {
                            First = a,
                            Second = b,
                            Distance = distance + (reach - distance) * fraction
                        }
                    );
                }
        }

        internal static void Part(List<BoomboxPendantPair> pairs)
        {
            foreach (var pair in pairs)
            {
                var a = pair.First;
                var b = pair.Second;
                var between = b.anchor - a.anchor + (b.points[b.Last] - a.points[a.Last]);
                float distance = between.magnitude;
                if (distance >= pair.Distance || distance <= 0f)
                    continue;
                var shift = between * ((pair.Distance - distance) * .5f * Relaxation / distance);
                a.points[a.Last] -= shift;
                a.parted[a.Last] -= shift;
                b.points[b.Last] += shift;
                b.parted[b.Last] += shift;
            }
        }

        internal void Prepare(Matrix4x4 model, BoomboxContactShapes shapes, float fraction)
        {
            var inverse = model.inverse;
            Vector3 origin = placement.GetColumn(3);
            float ratio = unit / model.MultiplyVector(Vector3.up).magnitude;
            for (int point = 1; point < points.Length; point++)
            {
                shapes.Contacts(
                    origin + inverse.MultiplyVector(points[point]),
                    (point == Last ? radius : linkRadius) * ratio + ContactMargin,
                    pushes
                );
                foreach (var push in pushes)
                {
                    float reach = push.magnitude, depth = reach - ContactMargin;
                    if (reach <= 0f)
                        continue;
                    var world = model.MultiplyVector(push);
                    Bind(point, world.normalized, world.magnitude / reach * (depth > 0f ? depth * fraction : depth));
                }
            }
            Array.Clear(pressed, 0, pressed.Length);
            Array.Clear(parted, 0, parted.Length);
        }

        internal void Relax(float limit, float weight)
        {
            Contact();
            Reach();
            Stretch(weight);
            Bend(limit, false);
        }

        internal void Finish(float dt, float limit, float friction)
        {
            Bend(limit, true);
            Settle(dt, friction);
        }

        internal float Radius => radius * unit;

        internal Vector3 Center => anchor + points[Last];

        private int Last => points.Length - 1;

        private int End => points.Length - 2;

        private void Bind(
            int point,
            Vector3 normal,
            float depth
        ) => bounds.Add(new Bound { Point = point, Normal = normal, Offset = Vector3.Dot(points[point], normal) + depth });

        private void Contact()
        {
            foreach (var bound in bounds)
            {
                float depth = bound.Offset - Vector3.Dot(points[bound.Point], bound.Normal);
                if (depth <= 0f)
                    continue;
                points[bound.Point] += bound.Normal * depth * Relaxation;
                pressed[bound.Point] += bound.Normal * depth * Relaxation;
            }
        }

        private void Reach()
        {
            for (int point = 1; point < points.Length; point++)
            {
                var offset = points[point] - points[0];
                float allowed = reach[point] * unit;
                if (offset.sqrMagnitude > allowed * allowed)
                    points[point] = points[0] + offset.normalized * allowed;
            }
        }

        private void Stretch(float weight)
        {
            for (int segment = 0; segment < Last; segment++)
            {
                var offset = points[segment + 1] - points[segment];
                float length = offset.magnitude;
                if (length <= 0f)
                    continue;
                var correction = offset * ((length - (reach[segment + 1] - reach[segment]) * unit) / length);
                float share = segment == 0
                    ? 0f
                    : segment == End
                        ? weight / (weight + 1f)
                        : .5f;
                points[segment] += correction * share;
                points[segment + 1] -= correction * (1f - share);
            }
        }

        private void Bend(float limit, bool exact)
        {
            for (int segment = 0; segment < Last; segment++)
            {
                var offset = points[segment + 1] - points[segment];
                float length = offset.magnitude;
                if (length <= 0f)
                    continue;
                points[segment + 1] = points[segment] + Vector3.RotateTowards(down, offset / length, limit, 0f) * (exact
                    ? (reach[segment + 1] - reach[segment]) * unit
                    : length);
            }
        }

        private void Settle(float dt, float friction)
        {
            float keep = Mathf.Exp(-friction * dt);
            for (int point = 1; point < points.Length; point++)
            {
                if (pressed[point].sqrMagnitude <= 0f && parted[point].sqrMagnitude <= 0f)
                    continue;
                var relative = (points[point] - previous[point]) / dt - carried;
                relative = Across(Across(relative, pressed[point]), parted[point]) * keep;
                previous[point] = points[point] - (carried + relative) * dt;
            }
        }

        private static Vector3 Across(Vector3 velocity, Vector3 push)
        {
            float strength = push.sqrMagnitude;
            return strength <= 0f ? velocity : velocity - push * (Vector3.Dot(velocity, push) / strength);
        }

        private Quaternion Turn(
            int segment,
            Vector3 rest
        ) => Quaternion.FromToRotation(
            holder.TransformDirection(rest),
            holder.TransformVector(local[segment + 1] - local[segment])
        ) * holder.rotation;

        private static void Place(Transform target, Vector3 position, Quaternion rotation)
        {
            target.localPosition = position;
            target.rotation = rotation;
        }

        internal void Apply()
        {
            var carry = holder.rotation * Quaternion.Inverse(attitude);
            for (int point = 0; point < points.Length; point++)
                local[point] = holder.InverseTransformVector(carry * (points[point] - points[0]));
            for (int link = 0; link < links.Length; link++)
                Place(links[link], local[link], Turn(link, Vector3.down));
            Place(pivot, local[End], Turn(End, hanging) * facing);
        }
    }
}
