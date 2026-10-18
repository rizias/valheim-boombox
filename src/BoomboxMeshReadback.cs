using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VikingBoombox
{
    internal static class BoomboxMeshReadback
    {
        internal static byte[] Read(GraphicsBuffer buffer, string error)
        {
            var request = AsyncGPUReadback.Request(buffer);
            request.WaitForCompletion();
            if (request.hasError)
                throw new InvalidOperationException(error);
            return request.GetData<byte>().ToArray();
        }

        internal static int Index(byte[] data, IndexFormat format, int index, int baseVertex)
        {
            int value = format == IndexFormat.UInt16
                ? BitConverter.ToUInt16(data, index * 2)
                : (int)BitConverter.ToUInt32(data, index * 4);
            return value + baseVertex;
        }

        internal static float Half(byte[] data, int offset)
        {
            ushort value = BitConverter.ToUInt16(data, offset);
            int sign = (value & 32768) == 0 ? 1 : -1;
            int exponent = value >> 10 & 31;
            int fraction = value & 1023;
            if (exponent == 31)
                return fraction == 0 ? sign * float.PositiveInfinity : float.NaN;
            double magnitude = exponent == 0
                ? Math.Pow(2, -14) * fraction / 1024d
                : Math.Pow(2, exponent - 15) * (1 + fraction / 1024d);
            return sign * (float)magnitude;
        }
    }
}
