using UnityEngine;

namespace VikingBoombox
{
    internal sealed class MusicViewScene
    {
        internal readonly Transform Root;
        internal readonly Camera Camera;
        internal readonly int Layer;
        internal float GroundHeight => Root.position.y;
        internal MusicViewScene(Transform root, Camera camera, int layer)
        {
            Root = root;
            Camera = camera;
            Layer = layer;
        }
        internal void Own(GameObject item)
        {
            item.transform.SetParent(Root, true);
            foreach (var child in item.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = Layer;
        }
    }

    internal static class MusicEffectViews
    {
        internal static IMusicEffectView Create(
            BoomboxMusicEffect variant,
            Transform parent,
            GameObject model,
            MusicPulse pulse,
            MusicViewScene scene = null
        )
        {
            switch (variant)
            {
                case BoomboxMusicEffect.Sparks:
                    return new MusicSparksView(parent, scene);
                case BoomboxMusicEffect.RunesAround:
                    return new MusicRunesAroundView(scene);
                case BoomboxMusicEffect.Strike:
                    return new MusicStrikeView(model, scene);
                case BoomboxMusicEffect.FixedCracks:
                    return new MusicFixedCracksView(parent, scene);
                case BoomboxMusicEffect.Pulse:
                    return new MusicPulseView(pulse, scene);
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(variant));
            }
        }
    }
}
