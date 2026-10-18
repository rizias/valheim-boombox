using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxMountGroup
    {
        public int[] slots;
        public float[] normal;
        public float[] legs;
        public float[][] aims;
        public float? top;
        public float? bottom;
        public bool common;

        internal Vector3 Normal => new Vector3(normal[0], normal[1], normal[2]).normalized;

        internal Vector3 Legs => new Vector3(legs[0], legs[1], legs[2]).normalized;

        internal Vector3 Aim(int index) => new Vector3(aims[index][0], aims[index][1], aims[index][2]);
    }

    internal struct BoomboxPendantMount
    {
        internal Vector3 Loop;
        internal Vector3 Normal;
        internal Vector3 Along;
    }
}
