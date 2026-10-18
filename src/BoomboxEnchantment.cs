using System;
using UnityEngine;

namespace VikingBoombox
{
    [DefaultExecutionOrder(10001)]
    public sealed class BoomboxEnchantment : MonoBehaviour
    {
        private const int EventStride = 3;
        private AudioDeck deck;
        private BoomboxEnchantVisuals visuals;
        private BoomboxGhostEffects ghostEffects;
        private bool visualsFailed;
        private int beats;
        private bool carried;

        internal void Initialize(AudioDeck owner)
        {
            deck = owner;
            beats = owner.EmittedWaves / EventStride;
        }

        private void LateUpdate()
        {
            if (deck == null || deck.State == null)
                return;
            int step = deck.EmittedWaves / EventStride;
            bool beat = step != beats;
            beats = step;
            var player = Player.m_localPlayer;
            bool wasCarried = carried;
            carried = player != null
                && deck.Item != null
                && deck.State.onBack
                && deck.Item == BoomboxEquipment.Get(player);
            if (!carried)
            {
                if (wasCarried)
                    Stop();
                return;
            }
            var settings = BoomboxPlugin.Instance?.Settings;
            if (settings == null || !settings.LocalGameplayEnabled || !settings.MusicEnchantment.Value
            || deck.State.insertedCassette == null
            || !deck.State.playing
            || deck.StoragePaused
            || player.IsDead())
            {
                Stop();
                return;
            }
            BoomboxParticleAssets.Refresh();
            bool drawing = player.IsDrawingBow();
            bool blocking = player.IsBlocking() && BoomboxEnchantCharge.HoldsShield(player);
            var weapon = player.GetCurrentWeapon();
            bool arming = BoomboxEnchantWeapons.Charging(weapon);
            if (beat)
            {
                BoomboxEnchantCharge.Beat(drawing, blocking);
                BoomboxEnchantWeapons.Beat(weapon);
            }
            BoomboxEnchantCharge.Step(drawing, blocking, Time.deltaTime);
            BoomboxEnchantWeapons.Step(weapon, Time.deltaTime);
            var frame = new BoomboxEnchantFrame
            {
                LaunchHand = beat && (drawing || blocking),
                LaunchWeapon = beat && arming,
                HandCharge = BoomboxEnchantCharge.HoldsBow(player)
                    ? BoomboxEnchantCharge.Bow
                    : BoomboxEnchantCharge.HoldsShield(player)
                        ? BoomboxEnchantCharge.Shield
                        : 0f,
                WeaponCharge = BoomboxEnchantWeapons.Held(weapon),
                HitBurst = BoomboxEnchantWeapons.PendingBurst,
            };
            BoomboxEnchantWeapons.PendingBurst = null;
            if (settings.DrawEnchantment)
                Present(player, frame);
            else
            {
                visuals?.Clear();
                ghostEffects?.Clear();
            }
            BoomboxGhostStrikes.Update(settings.DrawEnchantment ? ghostEffects : null);
            BoomboxOdinSpears.Update(settings.DrawEnchantment ? ghostEffects : null);
            if (settings.DrawEnchantment)
                ghostEffects?.Present();
        }

        private void Present(Player player, BoomboxEnchantFrame frame)
        {
            if (visualsFailed)
                return;
            try
            {
                if (visuals == null)
                    visuals = new BoomboxEnchantVisuals();
                if (ghostEffects == null)
                    ghostEffects = new BoomboxGhostEffects();
                visuals.Present(deck, player, frame);
            }
            catch (Exception error)
            {
                visualsFailed = true;
                visuals?.Destroy();
                visuals = null;
                ghostEffects?.Destroy();
                ghostEffects = null;
                BoomboxPlugin.Instance?.LogError(
                    BoomboxText.Get("text.music_enchantment_particles_are_unavailable_charge") + error.Message
                );
            }
        }

        private void Stop()
        {
            ResetGameplay();
            visuals?.Clear();
            ghostEffects?.Clear();
        }

        internal static void ResetGameplay()
        {
            BoomboxEnchantCharge.Reset();
            BoomboxEnchantWeapons.Reset();
            BoomboxGhostStrikes.Reset();
            BoomboxOdinSpears.Reset();
        }

        private void OnDestroy()
        {
            if (carried)
                ResetGameplay();
            visuals?.Destroy();
            ghostEffects?.Destroy();
        }
    }
}
