using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxFitBox
    {
        public float[] min;
        public float[] max;
    }

    public sealed class BoomboxPendantFit
    {
        public BoomboxFitBox mainBody;
        public float size = 1f;
        public float[] hangPoint;
        public float[] rotation = { 0f, 0f, 0f };
        public float turn;
        private static readonly BoomboxPendantFit Automatic = new BoomboxPendantFit();
        private static Dictionary<string, BoomboxPendantFit> table;

        internal static BoomboxPendantFit For(
            string prefab
        ) => (table ?? (table = Load())).TryGetValue(prefab, out var fit)
            ? fit
            : Automatic;

        internal Bounds? MainBody
        {
            get
            {
                if (mainBody == null)
                    return null;
                var bounds = new Bounds();
                bounds.SetMinMax(
                    new Vector3(mainBody.min[0], mainBody.min[1], mainBody.min[2]),
                    new Vector3(mainBody.max[0], mainBody.max[1], mainBody.max[2])
                );
                return bounds;
            }
        }

        internal Vector2? HangPoint => hangPoint == null ? (Vector2?)null : new Vector2(hangPoint[0], hangPoint[1]);

        internal Quaternion Rotation => Quaternion.Euler(rotation[0], rotation[1], rotation[2]);

        internal Quaternion Turn => Quaternion.Euler(0f, turn, 0f);

        private static Dictionary<string, BoomboxPendantFit> Load()
        {
            using (var stream = typeof(BoomboxPendantFit).Assembly.GetManifestResourceStream(
                "ValheimBoombox.PendantFit.json"
            ))
            {
                if (stream == null)
                    throw new InvalidOperationException("Pendant fit resource is missing");
                using (var reader = new StreamReader(stream))
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, BoomboxPendantFit>>(
                        reader.ReadToEnd()
                    );
            }
        }
    }
}
