using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxRayMesh
    {
        internal static float Nearest(Vector3 origin, Vector3 direction, Vector3[] vertices, int[] triangles)
        {
            float nearest = float.PositiveInfinity;
            for (int corner = 0; corner < triangles.Length; corner += 3)
            {
                var a = vertices[triangles[corner]];
                var ab = vertices[triangles[corner + 1]] - a;
                var ac = vertices[triangles[corner + 2]] - a;
                var cross = Vector3.Cross(direction, ac);
                float determinant = Vector3.Dot(ab, cross);
                if (Mathf.Abs(determinant) < 1e-12f)
                    continue;
                var offset = origin - a;
                float u = Vector3.Dot(offset, cross) / determinant;
                if (u < 0f || u > 1f)
                    continue;
                var turn = Vector3.Cross(offset, ab);
                float v = Vector3.Dot(direction, turn) / determinant;
                if (v < 0f || u + v > 1f)
                    continue;
                float distance = Vector3.Dot(ac, turn) / determinant;
                if (distance > 0f && distance < nearest)
                    nearest = distance;
            }
            return nearest;
        }
    }
}
