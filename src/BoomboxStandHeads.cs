using UnityEngine;

namespace VikingBoombox
{
    public sealed partial class BoomboxTrophyVisuals
    {
        private const float StandCentreHeight = .85f;
        private const float StandPoleInsert = .02f;
        private const float StandBaseHeight = .09f;
        private const float StandPoleFoot = .07f;
        private const float StandFace = .09f;
        private const float StandWidthLimit = .16f;
        private const float StandHeightLimit = .18f;
        public float HeadBeatAngle = 20f;
        public float HeadBeatSeconds = .35f;

        private void CreateStandingHead(Transform holder, Transform attach, float size, string prefab, int slot)
        {
            Shape(
                holder,
                "Base",
                Cylinder(),
                wood,
                new Vector3(size * .6f, size * StandBaseHeight, size * .6f),
                Vector3.up * size * StandBaseHeight * .5f
            );
            var pivot = new GameObject("HeadPivot").transform;
            pivot.SetParent(holder, false);
            var head = CloneVisual(attach, pivot);
            head.name = "Head";
            foreach (var animator in head.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }
                animator.enabled = false;
            }
            foreach (var animation in head.GetComponentsInChildren<Animation>(true))
                animation.enabled = false;
            BoomboxPendantPlacement.Orient(head, BoomboxDecorationCatalog.Prefab(prefab), Vector3.forward);
            var fit = BoomboxPendantFit.For(prefab);
            head.localRotation = fit.Rotation * head.localRotation;
            var whole = StandingBounds(head, pivot);
            var main = fit.MainBody ?? whole;
            float face = main.size.x;
            if (face <= .00001f || whole.size.x <= .00001f || float.IsNaN(face) || float.IsInfinity(face))
            {
                Destroy(holder.gameObject);
                return;
            }
            float scale = Mathf.Min(StandFace / face, StandWidthLimit / whole.size.x, StandHeightLimit / whole.size.y);
            head.localScale = Vector3.one * scale;
            var centre = Vector3.up
                * Mathf.Max(
                size * StandCentreHeight,
                (main.center.y - whole.min.y) * scale + size * .15f
            );
            head.localPosition = centre - main.center * scale;
            float foot = size * StandPoleFoot;
            float underside = BoomboxPendantPlacement.RiseHeight(head, pivot, foot, centre.y);
            float top = float.IsNaN(underside) ? centre.y : Mathf.Min(centre.y, underside + size * StandPoleInsert);
            Shape(
                holder,
                "Stem",
                Cylinder(),
                metal,
                new Vector3(size * .06f, top - foot, size * .06f),
                Vector3.up * (top + foot) * .5f
            );
            pivot.localPosition = Vector3.up * top;
            head.localPosition -= pivot.localPosition;
            BoomboxPendantParticleAnchor.Mark(pivot, slot, centre - pivot.localPosition, size);
            motions.Add(
                new Motion { pivot = pivot, previousPosition = pivot.position, restRotation = pivot.localRotation }
            );
        }

        private void Beat(Motion motion)
        {
            float half = HeadBeatSeconds * .5f;
            if (motion.bob < 0f || motion.bob >= HeadBeatSeconds)
                motion.bob = 0f;
            else if (motion.bob >= half)
                motion.bob = HeadBeatSeconds - motion.bob;
        }

        private float Bob(Motion motion, float dt)
        {
            if (motion.bob < 0f)
                return 0f;
            motion.bob += dt;
            if (motion.bob >= HeadBeatSeconds)
            {
                motion.bob = -1f;
                return 0f;
            }
            float strength = Mathf.Clamp(BoomboxPlugin.Instance.Settings.HeadBeatBob.Value, 0f, 3f);
            return HeadBeatAngle * strength * Mathf.Sin(Mathf.PI * motion.bob / HeadBeatSeconds);
        }
    }
}
