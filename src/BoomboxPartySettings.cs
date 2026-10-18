using BepInEx.Configuration;

namespace VikingBoombox
{
    public sealed class BoomboxPartySettings
    {
        public readonly ConfigEntry<float> Radius;
        public readonly ConfigEntry<float>[] Strength = new ConfigEntry<float>[5];
        private readonly ConfigFile configuration;

        internal BoomboxPartySettings(ConfigFile config)
        {
            configuration = config;
            Radius =
                config.Bind(
                "Party",
                "Radius",
                20f,
                BoomboxText.Description("text.shared_bonus_radius_in_metres_from_each")
            );
            string[] names = { "Damage", "Health", "Stamina", "Eitr", "Speed" };
            string[] descriptions = {
                "party.DamageMultiplier", "party.HealthMultiplier", "party.StaminaMultiplier",
                "party.EitrMultiplier", "party.SpeedMultiplier"
            };
            for (int index = 0; index < names.Length; index++)
                Strength[index] = config.Bind(
                    "Party",
                    names[index] + "Multiplier",
                    1f,
                    BoomboxText.Description(descriptions[index])
                );
        }

        public void Set(int index, float value)
        {
            if (!BoomboxAuraMath.Finite(value) || value < 0f || !BoomboxAuraNetwork.CanEditBalance)
                return;
            if (index < 0)
                Radius.Value = value;
            else
                Strength[index].Value = value;
            configuration.Save();
        }

        internal static BoomboxAuraBalance Capture(BoomboxSettings settings)
        {
            var result = new BoomboxAuraBalance
            {
                Radius = Safe(settings.Party.Radius.Value),
                MusicOnly = settings.MusicOnly.Value,
                Fallback = settings.DecorationModFallback.Value
            };
            float[] cassette = {
                settings.CassetteRedDamage.Value,
                settings.CassetteGreenHealth.Value,
                settings.CassetteYellowStamina.Value,
                settings.CassetteBlueEitr.Value,
                settings.CassettePurpleSpeed.Value
            };
            float[] decoration = {
                settings.DecorationDamage.Value,
                settings.DecorationHealth.Value,
                settings.DecorationStamina.Value,
                settings.DecorationEitr.Value,
                settings.DecorationSpeed.Value
            };
            for (int index = 0; index < 5; index++)
            {
                result.Strength[index] = Safe(settings.Party.Strength[index].Value);
                result.Cassette[index] = Safe(cassette[index]);
                result.Decoration[index] = Safe(decoration[index]);
            }
            var recovery = new[] { settings.Health, settings.Stamina, settings.Eitr };
            for (int tier = 0; tier < result.WaveRadius.Length; tier++)
                result.WaveRadius[tier] = Safe(settings.WaveRadius(tier));
            for (int index = 0; index < 3; index++)
            {
                result.Initial[index] = BoomboxAuraMath.Finite(recovery[index].Initial.Value)
                    ? recovery[index].Initial.Value
                    : 0f;
                result.PerRank[index] = BoomboxAuraMath.Finite(recovery[index].PerLevel.Value)
                    ? recovery[index].PerLevel.Value
                    : 0f;
                result.Period[index] = BoomboxAuraMath.Finite(recovery[index].Period) ? recovery[index].Period : 5f;
            }
            return result;
        }

        private static float Safe(float value) => BoomboxAuraMath.Finite(value)
            ? BoomboxSettings.Nonnegative(value)
            : 0f;
    }
}
