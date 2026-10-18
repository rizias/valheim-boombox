using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxRemoteState
    {
        public string Id, Epoch;
        public long Revision, Peer, Owner;
        public ZDOID Character, WorldItem;
        public bool Worn, Active, Silent;
        public Vector3 Position;
        public Quaternion Rotation;
        public DeckState Appearance;
        public float Motion, Glow, Level, Radius;
        public double Time;
        public MusicRanges Smooth;
        public BoomboxMusicEffect Variant;
        public readonly List<MusicStrike> Strikes = new List<MusicStrike>();
        internal float Received;

        internal static DeckState VisualState(DeckState source)
        {
            var result = new DeckState
            {
                id = source.id,
                owner = source.owner,
                onBack = source.onBack,
                upgradeTier = source.upgradeTier
            };
            result.unlockedDecorationSlots = new List<int>(source.unlockedDecorationSlots);
            result.unlockedDecorationPrefabs = new List<string>(source.unlockedDecorationPrefabs);
            result.decorationPlacements = source.decorationPlacements.Where(p => p != null).Select(
                p => new BoomboxDecorationPlacement
                {
                    slot = p.slot,
                    prefab = p.prefab,
                    effect = p.effect,
                    effectParticles = p.effectParticles,
                    extraParticles = p.extraParticles
                }
            ).ToList();
            if (source.insertedCassette != null)
                result.insertedCassette = new RecordedCassette
                {
                    prefab = source.insertedCassette.prefab,
                    properties = new Dictionary<string, int>(source.insertedCassette.properties)
                };
            return result;
        }

        internal static BoomboxRemoteState Capture(AudioDeck deck, string epoch, long revision)
        {
            var moment = deck.Moment;
            var settings = BoomboxPlugin.Instance.Settings;
            return new BoomboxRemoteState
            {
                Id = deck.State.id,
                Epoch = epoch,
                Revision = revision,
                Peer = ZNet.GetUID(),
                Owner = deck.State.owner,
                Character = Player.m_localPlayer.GetZDOID(),
                WorldItem = deck.WorldDrop?.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None,
                Worn = deck.State.onBack,
                Active = !Player.m_localPlayer.IsDead() && !Player.m_localPlayer.IsTeleporting()
                && (deck.IsActivePlayback || deck.IsSyntheticActive),
                Silent = moment.Silent,
                Position = deck.transform.position,
                Rotation = deck.transform.rotation,
                Appearance = VisualState(deck.State),
                Motion = deck.VisualStrength,
                Glow = deck.GlowStrength,
                Level = moment.Level,
                Time = moment.Time,
                Smooth = moment.Smooth,
                Radius = settings.WaveRadius(deck.State.upgradeTier),
                Variant = settings.Effect.Variant.Value
            };
        }

        internal string AppearanceKey() => Appearance.upgradeTier + ":" + Appearance.insertedCassette?.prefab + ":"
            + string.Join(
            "|",
            Appearance.insertedCassette?.properties.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value)
                ?? Enumerable.Empty<string>()
        )
            + ":"
            + string.Join(
            "|",
            Appearance.decorationPlacements.OrderBy(p => p.slot).Select(
                p => p.slot + "=" + p.prefab + ":" + p.effect + ":" + p.effectParticles + ":" + p.extraParticles
            )
        );
    }
}
