using UnityEngine;

namespace VikingBoombox
{
    internal struct BoomboxEnchantRoute
    {
        private const float BackBehind = .5f;
        private const float BackAbove = .15f;
        private const float BackAside = .1f;
        private const float ShoulderAbove = .2f;
        private const float ShoulderAside = .2f;
        private const float FallbackChestHeight = 1.35f;
        private const float FallbackShoulderAbove = .1f;
        private const float FallbackShoulderAside = .2f;
        private const float MinimumAcross = .0001f;

        internal Vector3 Back;
        internal Vector3 Shoulder;

        internal static BoomboxEnchantRoute For(BoomboxEnchantSkeleton skeleton, Player player, bool right)
        {
            var body = player.transform;
            var up = body.up;
            var across = skeleton.LeftArm != null && skeleton.RightArm != null
                ? Vector3.ProjectOnPlane(skeleton.RightArm.position - skeleton.LeftArm.position, up)
                : Vector3.zero;
            var rightward = across.sqrMagnitude > MinimumAcross ? across.normalized : body.right;
            var backward = Vector3.Cross(up, rightward);
            var aside = right ? rightward : -rightward;
            var chest = skeleton.Chest != null ? skeleton.Chest.position : body.position + up * FallbackChestHeight;
            var arm = right ? skeleton.RightArm : skeleton.LeftArm;
            var shoulder = arm != null
                ? arm.position
                : chest + up * FallbackShoulderAbove + aside * FallbackShoulderAside;
            return new BoomboxEnchantRoute
            {
                Back = chest + backward * BackBehind + up * BackAbove + aside * BackAside,
                Shoulder = shoulder + up * ShoulderAbove + aside * ShoulderAside,
            };
        }

        internal static Vector3 Point(Vector3 from, Vector3 back, Vector3 shoulder, Vector3 to, float progress)
        {
            float scaled = Mathf.Clamp01(progress) * 3f;
            int segment = Mathf.Min(2, Mathf.FloorToInt(scaled));
            float t = scaled - segment;
            var start = from + (from - back);
            var end = to + (to - shoulder);
            if (segment == 0)
                return CatmullRom(start, from, back, shoulder, t);
            if (segment == 1)
                return CatmullRom(from, back, shoulder, to, t);
            return CatmullRom(back, shoulder, to, end, t);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return .5f
                * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }

    internal sealed class BoomboxEnchantSkeleton
    {
        private Player owner;
        private Animator animator;

        internal Transform Chest
        {
            get;
            private set;
        }

        internal Transform LeftArm
        {
            get;
            private set;
        }

        internal Transform RightArm
        {
            get;
            private set;
        }

        internal BoomboxEnchantSkeleton Resolve(Player player)
        {
            if (player == owner && animator != null)
                return this;
            owner = player;
            animator = player.GetComponentInChildren<Animator>();
            bool human = animator != null && animator.isHuman;
            Chest = human
                ? animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                : null;
            LeftArm = human ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
            RightArm = human ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            return this;
        }
    }
}
