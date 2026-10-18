using System.IO;
using UnityEngine;

namespace VikingBoombox
{
    internal static class BoomboxBonusArt
    {
        private static readonly Sprite[] icons = new Sprite[4];
        internal static void Release()
        {
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i] != null)
                {
                    Object.Destroy(icons[i].texture);
                    Object.Destroy(icons[i]);
                }
                icons[i] = null;
            }
        }
        internal static Sprite Get(int index)
        {
            if (index < 0 || index >= 4)
                return null;
            if (icons[index] != null)
                return icons[index];
            string name = new[] { "wave", "health", "stamina", "eitr" }[index];
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            using (var input = typeof(BoomboxBonusArt).Assembly.GetManifestResourceStream("ValheimBoombox.Bonus." + name
                + ".png"))
            using (var bytes = new MemoryStream())
            {
                if (input == null)
                    throw new IOException("Missing approved bonus icon: " + name);
                input.CopyTo(bytes);
                if (!source.LoadImage(bytes.ToArray()))
                    throw new IOException("Invalid bonus icon: " + name);
            }
            var target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32);
            var active = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var texture = new Texture2D(
                    256,
                    256,
                    TextureFormat.RGBA32,
                    false
                )
                {
                    name = "Boombox approved " + name,
                    hideFlags = HideFlags.DontSave
                };
                texture.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                texture.Apply(false, true);
                icons[index] = Sprite.Create(texture, new Rect(0, 0, 256, 256), new Vector2(.5f, .5f), 100);
                icons[index].name = texture.name;
                return icons[index];
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(source);
            }
        }
    }
}
