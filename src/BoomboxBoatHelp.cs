using System;
using UnityEngine;

namespace VikingBoombox
{
    [DefaultExecutionOrder(10001)]
    public sealed class BoomboxBoatHelp : MonoBehaviour
    {
        internal const float DragShare = .203f;
        internal const int MaximumSteps = 25;
        internal const float MovingSpeed = .5f;
        private AudioDeck deck;
        private BoomboxBoatWave wave;
        private bool waveFailed;
        private int beats;

        internal void Initialize(AudioDeck owner)
        {
            deck = owner;
            beats = owner.EmittedWaves;
        }

        internal static bool TryPush(Ship ship)
        {
            if (ship == null || ship.GetSpeedSetting() == Ship.Speed.Stop)
                return false;
            var view = ship.GetComponent<ZNetView>();
            var body = ship.GetComponent<Rigidbody>();
            if (view == null || !view.IsValid() || !view.IsOwner() || body == null)
                return false;
            var velocity = body.linearVelocity;
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.magnitude < MovingSpeed)
                return false;
            body.AddForce(flat.normalized * (DragShare * BoomboxBoatDrag.Take(ship)), ForceMode.VelocityChange);
            return true;
        }

        private void LateUpdate()
        {
            if (deck == null || deck.State == null)
                return;
            bool beat = deck.EmittedWaves != beats;
            beats = deck.EmittedWaves;
            var ship = HelpedShip(out _);
            if (ship == null)
            {
                wave?.Clear();
                return;
            }
            bool pushed = beat && TryPush(ship);
            if (!BoomboxPlugin.Instance.Settings.ShowVisualEffects.Value)
            {
                wave?.Clear();
                return;
            }
            if (waveFailed || deck.Visual == null)
                return;
            try
            {
                if (wave == null)
                    wave = new BoomboxBoatWave();
                wave.Present(deck, ship, pushed);
            }
            catch (Exception error)
            {
                waveFailed = true;
                wave?.Destroy();
                wave = null;
                BoomboxPlugin.Instance?.LogError(BoomboxText.Get("text.boombox_sail_wave_is_not_shown_boat_impulses_still_work")
                    + error.Message);
            }
        }

        private void FixedUpdate()
        {
            var ship = HelpedShip(out bool worn);
            if (worn)
                BoomboxBoatDrag.Step(ship);
        }

        private Ship HelpedShip(out bool worn)
        {
            var player = Player.m_localPlayer;
            var settings = BoomboxPlugin.Instance?.Settings;
            worn = deck != null
                && deck.State != null
                && player != null
                && !player.IsDead()
                && deck.Item != null
                && deck.State.onBack
                && deck.Item == BoomboxEquipment.Get(player);
            return worn && settings != null && settings.LocalGameplayEnabled && settings.BoatHelp.Value
                ? player.GetControlledShip()
                : null;
        }

        private void OnDestroy() => wave?.Destroy();
    }
}
