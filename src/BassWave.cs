using UnityEngine;

namespace VikingBoombox
{
    public sealed class BassWave : MonoBehaviour
    {
        public float Radius
        {
            get;
            private set;
        }
        public float MaximumRadius
        {
            get;
            private set;
        }
        public BoomboxWorldImpulse EnvironmentImpulse;
        private float speed;

        public static BassWave Emit(Vector3 origin, int tierLevel, float strength)
        {
            var root = new GameObject("Boombox musical impulse front");
            root.transform.position = origin;
            var wave = root.AddComponent<BassWave>();
            var settings = BoomboxPlugin.Instance.Settings;
            wave.MaximumRadius = BoomboxAuraNetwork.WaveRadius(tierLevel);
            wave.speed = BoomboxSettings.PositiveOr(settings.WaveSpeed.Value, 7f);
            wave.EnvironmentImpulse = root.AddComponent<BoomboxWorldImpulse>();
            wave.EnvironmentImpulse.Initialize(origin, wave.MaximumRadius, Mathf.Clamp01(strength));
            return wave;
        }

        private void Update()
        {
            if (Time.timeScale <= 0f || AudioListener.pause)
                return;
            float previous = Radius;
            Radius = Mathf.Min(MaximumRadius, Radius + speed * Time.deltaTime);
            if (BoomboxPlugin.Instance.Settings.LocalGameplayEnabled)
                EnvironmentImpulse.ApplyFront(previous, Radius);
            if (Radius >= MaximumRadius)
                Destroy(gameObject);
        }
    }
}
