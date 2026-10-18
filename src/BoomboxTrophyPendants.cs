using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class BoomboxTrophyVisuals
    {
        private const float PendantBodySize = .10f;
        private const float SwingStep = 1f / 60f;
        private Vector3 carrierPosition;
        private Quaternion carrierRotation = Quaternion.identity;
        private const int SwingIterations = 40;
        private float swingBacklog;
        private readonly List<BoomboxPendantPair> pairs = new List<BoomboxPendantPair>();

        private void Swing(float dt)
        {
            var carrier = visual.transform;
            var shapes = BoomboxContactShapes.Instance;
            float lag = swingBacklog, limit = PendantMaximumAngle * Mathf.Deg2Rad;
            swingBacklog = Mathf.Min(swingBacklog + dt, dt + SwingStep);
            for (int index = 1; swingBacklog >= SwingStep; index++)
            {
                swingBacklog -= SwingStep;
                float along = Mathf.Clamp01((index * SwingStep - lag) / dt);
                var model = Matrix4x4.TRS(
                    Vector3.Lerp(carrierPosition, carrier.position, along),
                    Quaternion.Slerp(carrierRotation, carrier.rotation, along),
                    carrier.lossyScale
                );
                foreach (var pendulum in swinging)
                    pendulum.Integrate(model, SwingStep, PendantDamping);
                BoomboxPendulum.Separate(swinging, pairs, ContactPushFraction);
                foreach (var pendulum in swinging)
                    pendulum.Prepare(model, shapes, ContactPushFraction);
                for (int iteration = 0; iteration < SwingIterations; iteration++)
                {
                    BoomboxPendulum.Part(pairs);
                    foreach (var pendulum in swinging)
                        pendulum.Relax(limit, PendantWeight);
                }
                foreach (var pendulum in swinging)
                    pendulum.Finish(SwingStep, limit, ContactFriction);
            }
            foreach (var pendulum in swinging)
                pendulum.Apply();
        }

        private void CreatePendant(
            Transform holder,
            Transform chain,
            BoomboxDecorationSlot slot,
            string prefab,
            BoomboxDecorationVisualRecord record
        )
        {
            var pivot = new GameObject("PendantPivot").transform;
            pivot.SetParent(holder, false);
            pivot.localPosition = Vector3.down * BoomboxChain.Length(slot.Upper);
            pivot.localRotation = Quaternion.LookRotation(new Vector3(slot.Outward.x, 0f, slot.Outward.z));
            var item = CloneVisual(record.Attach, pivot);
            if (prefab == BoomboxDecorationCatalog.Mushroom)
                item.gameObject.AddComponent<BoomboxMushroomGlow>().Initialize();
            BoomboxPendantPlacement.Orient(item, BoomboxDecorationCatalog.Prefab(prefab), Vector3.forward);
            var fit = BoomboxPendantFit.For(prefab);
            item.localRotation = fit.Rotation * item.localRotation;
            var bounds = BoundsOf(item, pivot);
            if (bounds.size.sqrMagnitude <= 0f)
                bounds = record.Bounds;
            var main = fit.MainBody ?? bounds;
            float size = (slot.Outward.x != 0f ? ShortSidePendantSize : PendantBodySize) * fit.size;
            float scale = size / Mathf.Max(.001f, Mathf.Max(main.size.x, main.size.y, main.size.z));
            if (PendantWholeLimit > 0f)
                scale = Mathf.Min(
                    scale,
                    PendantWholeLimit / Mathf.Max(.001f, BoomboxPendantPlacement.Extent(item, pivot))
                );
            item.localScale = Vector3.one * scale;
            var hangPoint = fit.HangPoint ?? new Vector2(main.center.x, main.center.z);
            item.localPosition = fit.Turn * new Vector3(-hangPoint.x, -main.max.y, -hangPoint.y) * scale;
            item.localRotation = fit.Turn * item.localRotation;
            if (fit.turn != 0f)
            {
                float grow = size / Mathf.Max(.001f, BoomboxPendantPlacement.Extent(item, pivot));
                item.localScale *= grow;
                item.localPosition *= grow;
                scale *= grow;
            }
            float linkRadius = BoomboxChain.LinkRadius(slot.Upper);
            float hang = BoomboxPendantPlacement.HangHeight(
                item,
                pivot,
                linkRadius,
                linkRadius,
                main.size.y * scale + linkRadius
            );
            if (!float.IsNegativeInfinity(hang))
                item.localPosition += Vector3.down * hang;
            var center = item.localPosition + fit.Turn * main.center * scale;
            BoomboxPendantParticleAnchor.Mark(pivot, slot.Index, center, size);
            if (prefab == BoomboxItemData.DevicePrefab)
                Speakers(item);
            motions.Add(
                new Motion
                {
                    pivot = pivot,
                    pendulum = new BoomboxPendulum(
                    holder,
                    chain,
                    pivot,
                    BoomboxChain.Reach(slot.Upper),
                    linkRadius,
                    center,
                    size * .5f
                )
                }
            );
        }
    }
}
