using UnityEngine;
using ValheimBoombox.Visuals;

namespace VikingBoombox
{
    public sealed class BoomboxSpeakerPulse : MonoBehaviour
    {
        private Vector3 origin;
        private Vector3 direction;
        private float elapsed;
        private float duration;
        private float radius;
        private float travel;
        private float strength;
        private BassShockwaveVisual visual;

        public static void Emit(VikingBoomboxVisualState model, float strength)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            if (!settings.ShowVisualEffects.Value
                || !settings.ShowSpeakerEffects.Value
                || settings.SpeakerIntensity.Value <= 0f)
                return;
            if (!BassShockwaveVisual.LoadEmbeddedAssets(typeof(BassWave).Assembly))
                return;
            foreach (var membrane in new[] { model.Left, model.Right })
            {
                var root = new GameObject("Boombox speaker pressure front");
                try
                {
                    var pulse = root.AddComponent<BoomboxSpeakerPulse>();
                    pulse.strength = Mathf.Clamp01(strength);
                    pulse.direction = model.transform.forward;
                    pulse.origin = membrane.position + pulse.direction * 0.018f;
                    root.transform.position = pulse.origin;
                    pulse.duration = BoomboxSettings.PositiveOr(settings.SpeakerDuration.Value, 0.28f);
                    pulse.radius = BoomboxSettings.Nonnegative(settings.SpeakerRadius.Value);
                    pulse.travel = BoomboxSettings.Nonnegative(settings.SpeakerTravel.Value);
                    pulse.visual = root.AddComponent<BassShockwaveVisual>();
                    pulse.visual.Intensity = BoomboxSettings.Nonnegative(settings.SpeakerIntensity.Value) * 4f;
                    pulse.visual.FrontRotation = Quaternion.FromToRotation(Vector3.up, pulse.direction);
                    var color = settings.SpeakerColor.Value;
                    color.a *= Mathf.Clamp01(strength);
                    pulse.visual.Init(color, BoomboxWaveMaterials.Alpha);
                }
                catch
                {
                    Destroy(root);
                    throw;
                }
            }
        }

        private void Update()
        {
            var settings = BoomboxPlugin.Instance.Settings;
            if (!settings.ShowVisualEffects.Value || !settings.ShowSpeakerEffects.Value)
            {
                Destroy(gameObject);
                return;
            }
            var color = settings.SpeakerColor.Value;
            color.a *= strength;
            visual.SetColor(color);
            elapsed += Time.timeScale <= 0f || AudioListener.pause ? 0f : Time.deltaTime;
            float age = Mathf.Clamp01(elapsed / duration);
            transform.position = origin + direction * (travel * age);
            visual.Intensity = BoomboxSettings.Nonnegative(settings.SpeakerIntensity.Value) * 4f;
            visual.UpdateRadius(radius * age, radius, age);
            if (age >= 1f)
                Destroy(gameObject);
        }
    }
}
