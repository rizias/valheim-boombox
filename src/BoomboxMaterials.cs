using System;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxMaterials
    {
        public static Material Surface(GameObject model, Color color, string preferredName = null)
        {
            var candidates = model.GetComponentsInChildren<Renderer>()
            .SelectMany(r => r.sharedMaterials)
            .Where(m => m != null && m.shader != null && m.shader.isSupported)
            .ToArray();
            var source = candidates.FirstOrDefault(m => preferredName != null
                && m.name == preferredName)
                ?? candidates.FirstOrDefault();
            if (source == null)
                throw new InvalidOperationException(BoomboxText.Get("text.the_body_has_no_supported_material"));
            var material = new Material(source);
            VikingBoomboxVisualState.Color(material, color);
            return material;
        }
    }
}
