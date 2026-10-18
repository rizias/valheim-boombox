namespace VikingBoombox
{
    internal class MusicHiddenView : IMusicEffectView
    {
        internal static IMusicEffectView Create(BoomboxMusicEffect effect)
        {
            switch (effect)
            {
                case BoomboxMusicEffect.Pulse:
                    return new MusicHiddenView();
                case BoomboxMusicEffect.Strike:
                    return new Strike(MusicReach.Front(.15, .3));
                case BoomboxMusicEffect.FixedCracks:
                    return new Strike(MusicReach.AnchoredFlight(.15, .15));
                default:
                    return new Strike(MusicReach.Flight(.15, .65));
            }
        }
        public void Update(MusicEffectFrame frame)
        {
        }
        public void Hide()
        {
        }
        public void Destroy()
        {
        }
        private sealed class Strike : MusicHiddenView, IMusicStrikeView
        {
            public MusicReach Reach
            {
                get;
            }
            internal Strike(MusicReach reach)
            {
                Reach = reach;
            }
        }
    }
}
