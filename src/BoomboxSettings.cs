using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace VikingBoombox
{
    public enum BoomboxBuffAnchor
    {
        BelowMap, LeftOfMap, TopLeft, BottomRight
    }
    public sealed class BoomboxSettings
    {
        public readonly BoomboxPartySettings Party;
        public readonly ConfigEntry<bool> ShowPlayerPanel;
        public readonly ConfigEntry<bool> ShowBuffPanel;
        public readonly ConfigEntry<BoomboxBuffAnchor> BuffAnchor;
        public readonly ConfigEntry<Vector2> BuffOffset;
        public readonly ConfigEntry<float> BuffScale;
        public readonly ConfigEntry<Vector2> PlayerPanelPosition;
        public readonly ConfigEntry<float>[] Damage = new ConfigEntry<float>[BoomboxProgression.MaxTier + 1];
        public readonly ConfigEntry<float>[] Radius = new ConfigEntry<float>[BoomboxProgression.MaxTier + 1];
        public readonly ConfigEntry<float>[] Push = new ConfigEntry<float>[BoomboxProgression.MaxTier + 1];
        public readonly ConfigEntry<float> EnemyPushMultiplier;
        public readonly RecoverySettings Health;
        public readonly RecoverySettings Stamina;
        public readonly RecoverySettings Eitr;
        public readonly ConfigEntry<float> CassetteRedDamage;
        public readonly ConfigEntry<float> CassetteYellowStamina;
        public readonly ConfigEntry<float> CassetteGreenHealth;
        public readonly ConfigEntry<float> CassetteBlueEitr;
        public readonly ConfigEntry<float> CassettePurpleSpeed;
        public readonly ConfigEntry<Color> DisplayTextColor;
        public readonly ConfigEntry<Color> DisplayPausedColor;
        public readonly ConfigEntry<Color> DisplayLoadingColor;
        public readonly ConfigEntry<float> DisplayTextScale;
        public readonly ConfigEntry<float> DisplayScrollSpeed;
        public readonly ConfigEntry<float> DisplayScrollPause;
        public readonly ConfigEntry<bool> DisplayShowTime;
        public readonly ConfigEntry<float> WaveSpeed;
        public readonly MusicEffectSettings Effect;
        public readonly ConfigEntry<bool> ShowVisualEffects;
        public readonly ConfigEntry<bool> ShowMusicEffects;
        public readonly ConfigEntry<bool> ShowMembraneMotion;
        public readonly ConfigEntry<bool> ShowReelMotion;
        public readonly ConfigEntry<bool> ShowEnchantmentVisuals;
        public readonly ConfigEntry<bool> ShowDecorationGlow;
        public bool DrawEnchantment => ShowVisualEffects.Value && ShowEnchantmentVisuals.Value;
        public readonly ConfigEntry<bool> ShowOthersWaves;
        public readonly ConfigEntry<bool> ShowSpeakerEffects;
        public readonly ConfigEntry<bool> ShowBodyGlow;
        public readonly ConfigEntry<bool> ShowSigurdGlow;
        public readonly ConfigEntry<float> SpeakerIntensity;
        public readonly ConfigEntry<float> SpeakerRadius;
        public readonly ConfigEntry<float> SpeakerTravel;
        public readonly ConfigEntry<float> SpeakerDuration;
        public readonly ConfigEntry<float> MembraneMotion;
        public readonly ConfigEntry<float> BodyGlowIntensity;
        public readonly ConfigEntry<float> SigurdGlowIntensity;
        public readonly ConfigEntry<float> SideGlowReaction;
        public readonly ConfigEntry<bool> PushEnvironment;
        public readonly ConfigEntry<float> EnvironmentPressure;
        public readonly ConfigEntry<float> EnvironmentRadius;
        public readonly ConfigEntry<float> EnvironmentMaxMass;
        public readonly ConfigEntry<int> EnvironmentMaxBodies;
        public readonly ConfigEntry<float> EnvironmentMaxSpeedChange;
        public readonly ConfigEntry<float> EnvironmentPushCooldown;
        public readonly ConfigEntry<float> AudioBufferSeconds;
        public readonly ConfigEntry<float> BackScale;
        public readonly ConfigEntry<Vector3> BackOffset;
        public readonly ConfigEntry<Vector3> BackRotation;
        public readonly ConfigEntry<float> BackBodyDepth;
        public readonly ConfigEntry<float> BackClearance;
        public readonly ConfigEntry<float> BackEquipmentOffset;
        public readonly ConfigEntry<bool> AdaptiveBackClearance;
        public readonly ConfigEntry<float> BackSampleInterval;
        public readonly ConfigEntry<int> MaximumDeviceTier;
        public readonly ConfigEntry<int> MaximumCassetteRank;
        public readonly ConfigEntry<bool> MusicOnly;
        public readonly ConfigEntry<bool> PersonalMusicOnly;
        public bool LocalGameplayEnabled => !PersonalMusicOnly.Value
            && !MusicOnly.Value
            && BoomboxAuraNetwork.Balance?.MusicOnly != true;
        public readonly ConfigEntry<int> DecorationSlotPriceStart;
        public readonly ConfigEntry<int> DecorationSlotPriceStep;
        public readonly ConfigEntry<int> DecorationSlotPriceCap;
        public readonly ConfigEntry<bool> DecorationMovement;
        public readonly ConfigEntry<bool> DecorationMusicMovement;
        public readonly ConfigEntry<float> DecorationDamage;
        public readonly ConfigEntry<float> DecorationHealth;
        public readonly ConfigEntry<float> DecorationStamina;
        public readonly ConfigEntry<float> DecorationEitr;
        public readonly ConfigEntry<float> DecorationSpeed;
        public readonly ConfigEntry<string> DecorationModFallback;
        public readonly ConfigEntry<bool> DecorationParticles;
        public readonly ConfigEntry<float> YellowMushroomGlow;
        public readonly ConfigEntry<float> CoinInterval;
        public readonly ConfigEntry<float> CoinLifetime;
        public readonly ConfigEntry<bool> MusicEnchantment;
        public readonly ConfigEntry<bool> BoatHelp;
        public readonly ConfigEntry<float> ParticleBrightness;
        public readonly ConfigEntry<float> HeadBeatBob;
        public readonly ConfigEntry<float> ParticleAmount;
        public readonly ConfigEntry<Color> SpeakerColor;
        public readonly ConfigEntry<Color> BodyGlowTint;
        public readonly ConfigEntry<Color> EngravingGlowTint;
        public readonly ConfigEntry<Color> PatternColor;
        public readonly ConfigEntry<bool> PatternPulse;
        private readonly Dictionary<string, RecipeSettings> recipes = new Dictionary<string, RecipeSettings>();

        public BoomboxSettings(ConfigFile config)
        {
            Party = new BoomboxPartySettings(config);
            ShowPlayerPanel = config.Bind(
                "Interface",
                "ShowPlayerPanel",
                true,
                BoomboxText.Description("text.show_the_draggable_player_while_inventory_is")
            );
            ShowBuffPanel = config.Bind(
                "Interface",
                "ShowBuffPanel",
                true,
                BoomboxText.Description("text.show_compact_music_bonuses_with_their_actual")
            );
            BuffAnchor = config.Bind(
                "Interface",
                "BuffAnchor",
                BoomboxBuffAnchor.BelowMap,
                BoomboxText.Description("text.anchor_music_bonuses_to_the_minimap_or_a_screen_corner")
            );
            BuffOffset = config.Bind(
                "Interface",
                "BuffOffset",
                Vector2.zero,
                BoomboxText.Description("text.bonus_block_offset_right_and_down_in_ui_units")
            );
            BuffScale = config.Bind(
                "Interface",
                "BuffScale",
                1f,
                BoomboxText.Description("text.bonus_block_scale", new AcceptableValueRange<float>(.7f, 1.5f))
            );
            PlayerPanelPosition = config.Bind(
                "Interface",
                "PlayerPanelPosition",
                new Vector2(-1, -1),
                BoomboxText.Description("text.player_panel_position_in_screen_fractions_negative")
            );
            ShowOthersWaves =
                config.Bind(
                "Visual effects",
                "ShowOthersWaves",
                true,
                BoomboxText.Description("text.show_other_players_music_waves_and_dust")
            );
            YellowMushroomGlow =
                config.Bind(
                "Decorations",
                "YellowMushroomGlow",
                .25f,
                BoomboxText.Description("text.yellow_mushroom_glow_0_disables_it_1", new AcceptableValueRange<float>(0f, 2f))
            );
            DisplayTextColor = config.Bind(
                "Display",
                "TextColor",
                new Color(.3f, .85f, 1f, 1f),
                BoomboxText.Description("text.text_colour_while_playing")
            );
            DisplayPausedColor = config.Bind(
                "Display",
                "PausedTextColor",
                new Color(.45f, .6f, .69f, 1f),
                BoomboxText.Description("text.text_colour_while_paused")
            );
            DisplayLoadingColor = config.Bind(
                "Display",
                "LoadingTextColor",
                new Color(.7f, .9f, 1f, 1f),
                BoomboxText.Description("text.text_colour_while_preparing_a_track")
            );
            DisplayTextScale = config.Bind(
                "Display",
                "TextScale",
                1f,
                BoomboxText.Description("text.title_size_multiplier_1_uses_the_large")
            );
            DisplayScrollSpeed = config.Bind(
                "Display",
                "ScrollSpeed",
                .35f,
                BoomboxText.Description("text.long_title_scrolling_speed_in_display_widths")
            );
            DisplayScrollPause = config.Bind(
                "Display",
                "ScrollPause",
                1.2f,
                BoomboxText.Description("text.pause_in_seconds_at_each_end_of")
            );
            DisplayShowTime =
                config.Bind(
                "Display",
                "ShowTime",
                false,
                BoomboxText.Description("text.show_a_second_line_with_elapsed_and")
            );
            AudioBufferSeconds =
                config.Bind(
                "Audio",
                "BufferSeconds",
                2f,
                BoomboxText.Description("text.seconds_of_decoded_audio_prepared_ahead_in_2")
            );
            BackScale = config.Bind(
                "Back attachment",
                "Scale",
                0.8f,
                BoomboxText.Description("text.scale_while_equipped_0_uses_0_8")
            );
            BackOffset = config.Bind(
                "Back attachment",
                "Offset",
                Vector3.zero,
                BoomboxText.Description("text.local_offset_in_metres_sideways_upwards_away")
            );
            BackRotation = config.Bind(
                "Back attachment",
                "Rotation",
                Vector3.zero,
                BoomboxText.Description("text.additional_local_rotation_in_degrees_around_x_y_and_z")
            );
            BackBodyDepth =
                config.Bind(
                "Back attachment",
                "BodyDepth",
                0.13f,
                BoomboxText.Description("text.minimum_torso_depth_in_metres_when")
            );
            BackClearance = config.Bind(
                "Back attachment",
                "Clearance",
                0.035f,
                BoomboxText.Description("text.gap_between_the_back_surface_and_the_case_in_metres")
            );
            BackEquipmentOffset = config.Bind(
                "Back attachment",
                "EquipmentOffset",
                0f,
                BoomboxText.Description("text.additional_outward_offset_for_thick_armour_or")
            );
            AdaptiveBackClearance =
                config.Bind(
                "Back attachment",
                "AdaptiveClearance",
                false,
                BoomboxText.Description("text.allow_the_equipment_change_fit_closer_than")
            );
            BackSampleInterval =
                config.Bind(
                "Back attachment",
                "SampleInterval",
                0.1f,
                BoomboxText.Description("text.legacy_setting_retained_for_configuration")
            );
            MaximumDeviceTier =
                config.Bind(
                "Progression",
                "MaximumDeviceTier",
                8,
                BoomboxText.Description("text.maximum_craftable_device_tier_from_1_to")
            );
            MaximumCassetteRank = config.Bind(
                "Progression",
                "MaximumCassetteRank",
                7,
                BoomboxText.Description("text.maximum_rank_produced_by_combining_cassettes_at")
            );
            MusicOnly =
                config.Bind(
                "Gameplay",
                "MusicOnly",
                false,
                BoomboxText.Description("text.play_music_with_visual_effects_but_disable")
            );
            PersonalMusicOnly =
                config.Bind(
                "Gameplay",
                "PersonalMusicOnly",
                false,
                BoomboxText.Description("text.personal_audio_only_mode_your_boombox_emits")
            );
            var legacySlotPrice = config.Bind(
                "Decorations",
                "SlotPrice",
                50,
                BoomboxText.Description("text.deprecated_replaced_by_slotpricestart_slotpricestep")
            );
            config.Remove(legacySlotPrice.Definition);
            int slotPriceStepDefault = legacySlotPrice.Value != 50 ? legacySlotPrice.Value : 50;
            DecorationSlotPriceStart = config.Bind(
                "Decorations",
                "SlotPriceStart",
                0,
                BoomboxText.Description("text.price_of_the_first_purchased_decoration_slot")
            );
            DecorationSlotPriceStep = config.Bind(
                "Decorations",
                "SlotPriceStep",
                slotPriceStepDefault,
                BoomboxText.Description("text.price_increase_for_each_subsequent_decoration_slot")
            );
            DecorationSlotPriceCap = config.Bind(
                "Decorations",
                "SlotPriceCap",
                0,
                BoomboxText.Description("text.decoration_slot_price_cap_in_coins_0_means_no_cap")
            );
            DecorationMovement = config.Bind(
                "Decorations",
                "Movement",
                true,
                BoomboxText.Description("text.enable_inertial_decoration_movement_does_not_change")
            );
            DecorationMusicMovement = config.Bind(
                "Decorations",
                "MusicMovement",
                true,
                BoomboxText.Description("text.add_decoration_movement_on_music_events_does")
            );
            HeadBeatBob =
                config.Bind(
                "Decorations",
                "HeadBeatBob",
                1f,
                BoomboxText.Description("text.head_movement_on_stands_to_the_beat", new AcceptableValueRange<float>(0f, 3f))
            );
            DecorationDamage = config.Bind(
                "Decorations",
                "DamagePerDecoration",
                .0025f,
                BoomboxText.Description("text.additive_wave_damage_fraction_for_each_matching")
            );
            DecorationHealth = config.Bind(
                "Decorations",
                "HealthPerDecoration",
                .1f,
                BoomboxText.Description("text.additional_health_on_each_regular_recovery_tick")
            );
            DecorationStamina = config.Bind(
                "Decorations",
                "StaminaPerDecoration",
                .1f,
                BoomboxText.Description("text.additional_stamina_on_each_regular_recovery_tick")
            );
            DecorationEitr = config.Bind(
                "Decorations",
                "EitrPerDecoration",
                .1f,
                BoomboxText.Description("text.additional_eitr_on_each_regular_recovery_tick")
            );
            DecorationSpeed = config.Bind(
                "Decorations",
                "SpeedPerDecoration",
                .0025f,
                BoomboxText.Description("text.additive_movement_speed_fraction_for_each_matching")
            );
            DecorationModFallback =
                config.Bind(
                "Decorations",
                "ModTrophyFallback",
                "Health",
                BoomboxText.Description("text.bonus_for_registered_mod_trophies_absent_from")
            );
            DecorationParticles =
                config.Bind(
                "Decorations",
                "Particles",
                true,
                BoomboxText.Description("text.decoration_particles_effect_particles_and_extra")
            );
            CoinInterval = config.Bind(
                "Decorations",
                "CoinInterval",
                2f,
                BoomboxText.Description("text.coin_drop_interval_for_pendants_with_the")
            );
            CoinLifetime = config.Bind(
                "Decorations",
                "CoinLifetime",
                4f,
                BoomboxText.Description("text.coin_lifetime_from_appearance_to_disappearance_in")
            );
            MusicEnchantment =
                config.Bind(
                "Gameplay",
                "MusicEnchantment",
                true,
                BoomboxText.Description("text.music_enchantment_equipped_playing_boombox_beats")
            );
            BoatHelp =
                config.Bind(
                "Gameplay",
                "BoatHelp",
                true,
                BoomboxText.Description("text.boombox_boat_assistance_while_the_wearer_steers")
            );
            ParticleBrightness =
                config.Bind(
                "Decorations",
                "ParticleBrightness",
                1f,
                BoomboxText.Description("text.pendant_and_music_enchantment_particle_brightness", new AcceptableValueRange<float>(.25f, 3f))
            );
            ParticleAmount =
                config.Bind(
                "Decorations",
                "ParticleAmount",
                1f,
                BoomboxText.Description("text.pendant_and_music_enchantment_particle_amount", new AcceptableValueRange<float>(.25f, 3f))
            );
            SpeakerColor = config.Bind(
                "Speaker effects",
                "Color",
                new Color(.2f, .8f, 1f, 1f),
                BoomboxText.Description("text.colour_of_the_speaker_fronts_alpha_multiplies")
            );
            BodyGlowTint = config.Bind(
                "Body glow",
                "Tint",
                Color.white,
                BoomboxText.Description("text.colour_multiplier_for_the_original_rune_emission")
            );
            EngravingGlowTint = config.Bind(
                "Body glow",
                "EngravingTint",
                Color.white,
                BoomboxText.Description("text.colour_multiplier_for_the_original_engraving")
            );
            PatternColor = config.Bind(
                "Body pattern",
                "PatternColor",
                new Color(.72f, .035f, .018f, 1f),
                BoomboxText.Description("text.pattern_colour_on_the_upper_front_panel")
            );
            PatternPulse = config.Bind(
                "Body pattern",
                "PatternPulse",
                true,
                BoomboxText.Description("text.upper_pattern_pulsing_in_response_to_music_impulses")
            );
            for (int tier = 1; tier <= BoomboxProgression.MaxTier; tier++)
                BindRecipe(config, "Device " + tier, BoomboxProgression.GetTier(tier).Recipe);
            BindRecipe(config, "Cassette basic", BoomboxCassetteWorkshop.BaseRecipe);
            for (int rank = 1; rank <= BoomboxCassetteProgression.MaxRank; rank++)
                BindRecipe(config, "Cassette rank " + rank, BoomboxCassetteProgression.DefaultRecipe(rank));
            EnemyPushMultiplier =
                config.Bind(
                "Enemy push",
                "Multiplier",
                0.15f,
                BoomboxText.Description("text.overall_strength_of_gentle_enemy_impulses_0")
            );
            for (int level = 1; level <= BoomboxProgression.MaxTier; level++)
            {
                var tier = BoomboxProgression.GetTier(level);
                string section = "Wave level " + level;
                Damage[level] = config.Bind(
                    section,
                    "Damage",
                    tier.BluntDamage,
                    BoomboxText.Description("text.blunt_damage_of_each_music_pulse_independent_of_volume")
                );
                Radius[level] = config.Bind(section, "Radius", tier.Radius, BoomboxText.Description("text.music_pulse_radius_in_meters"));
                Push[level] = config.Bind(
                    section,
                    "Push",
                    tier.RawPushForce,
                    BoomboxText.Description("text.enemy_push_force_0_disables_pushing")
                );
            }
            WaveSpeed = config.Bind(
                "Wave appearance",
                "Speed",
                7f,
                BoomboxText.Description("text.front_speed_in_meters_per_second_0")
            );
            ShowVisualEffects =
                config.Bind(
                "Wave appearance",
                "ShowVisualEffects",
                true,
                BoomboxText.Description("text.master_switch_for_effects_glow_and_membrane")
            );
            ShowMusicEffects = config.Bind(
                "Visual effects",
                "ShowMusicEffects",
                true,
                BoomboxText.Description("text.selected_music_effect_in_the_world_independent")
            );
            ShowMembraneMotion = config.Bind(
                "Visual effects",
                "ShowMembraneMotion",
                true,
                BoomboxText.Description("text.speaker_movement_without_changing_the_sound")
            );
            ShowReelMotion = config.Bind(
                "Visual effects",
                "ShowReelMotion",
                true,
                BoomboxText.Description("text.cassette_reel_rotation_without_changing_playback")
            );
            ShowEnchantmentVisuals = config.Bind(
                "Visual effects",
                "ShowEnchantmentVisuals",
                true,
                BoomboxText.Description("text.hand_and_weapon_particles_ghost_models_and")
            );
            ShowDecorationGlow = config.Bind(
                "Visual effects",
                "ShowDecorationGlow",
                true,
                BoomboxText.Description("text.glow_of_luminous_mushrooms")
            );
            Effect = new MusicEffectSettings(config);
            ShowSpeakerEffects = config.Bind(
                "Speaker effects",
                "ShowSpeakerEffects",
                true,
                BoomboxText.Description("text.two_small_outward_fronts_in_front_of")
            );
            SpeakerIntensity = config.Bind(
                "Speaker effects",
                "Intensity",
                0.12f,
                BoomboxText.Description("text.speaker_front_brightness_multiplied_by_event_strength")
            );
            SpeakerRadius = config.Bind(
                "Speaker effects",
                "Radius",
                0.15f,
                BoomboxText.Description("text.maximum_speaker_front_radius_in_meters")
            );
            SpeakerTravel = config.Bind("Speaker effects", "Travel", 0.10f, BoomboxText.Description("text.outward_travel_from_each_speaker_in_meters"));
            SpeakerDuration = config.Bind(
                "Speaker effects",
                "Duration",
                0.28f,
                BoomboxText.Description("text.speaker_front_duration_in_seconds_0_uses_0_28")
            );
            MembraneMotion = config.Bind(
                "Speaker effects",
                "MembraneMotion",
                0.65f,
                BoomboxText.Description("text.membrane_travel_multiplier_full_travel_is_7")
            );
            ShowBodyGlow = config.Bind(
                "Body glow",
                "ShowBodyGlow",
                true,
                BoomboxText.Description("text.body_frame_and_rune_glow_independent_of_waves")
            );
            BodyGlowIntensity = config.Bind(
                "Body glow",
                "Intensity",
                0.18f,
                BoomboxText.Description("text.rune_emission_multiplier_preserves_body_colors")
            );
            ShowSigurdGlow = config.Bind("Body glow", "ShowSigurdGlow", true, BoomboxText.Description("text.glow_of_the_side_carving_outline"));
            SigurdGlowIntensity = config.Bind(
                "Body glow",
                "SigurdIntensity",
                1f,
                BoomboxText.Description("text.side_carving_outline_glow_brightness")
            );
            SideGlowReaction =
                config.Bind(
                "Body glow",
                "SideGlowReaction",
                1f,
                BoomboxText.Description("text.how_strongly_the_side_carving_glow_flashes", new AcceptableValueRange<float>(0f, 3f))
            );
            PushEnvironment = config.Bind(
                "World physics",
                "Enabled",
                true,
                BoomboxText.Description("text.physical_impulses_on_free_bodies_independent_of")
            );
            EnvironmentPressure =
                config.Bind(
                "World physics",
                "PressureImpulse",
                20f,
                BoomboxText.Description("text.pressure_impulse_in_n_s_m_multiplied")
            );
            EnvironmentRadius = config.Bind(
                "World physics",
                "RadiusMultiplier",
                1f,
                BoomboxText.Description("text.free_body_interaction_range_relative_to_the_wave_radius")
            );
            EnvironmentMaxBodies = config.Bind(
                "World physics",
                "MaximumBodies",
                0,
                BoomboxText.Description("text.maximum_nearest_free_bodies_per_wave_0_means_unlimited")
            );
            EnvironmentMaxMass = config.Bind(
                "World physics",
                "MaximumMass",
                0f,
                BoomboxText.Description("text.exclude_bodies_above_this_mass_0_means_unlimited")
            );
            EnvironmentMaxSpeedChange = config.Bind(
                "World physics",
                "MaxSpeedChange",
                2f,
                BoomboxText.Description("text.maximum_speed_change_of_a_dropped_item")
            );
            EnvironmentPushCooldown = config.Bind(
                "World physics",
                "PushCooldown",
                1f,
                BoomboxText.Description("text.minimum_interval_between_pushes_of_the_same")
            );
            Health = new RecoverySettings(config, "Recovery health");
            Stamina = new RecoverySettings(config, "Recovery stamina");
            Eitr = new RecoverySettings(config, "Recovery eitr");
            CassetteRedDamage = config.Bind(
                "Cassette properties",
                "RedDamagePerRank",
                0.05f,
                BoomboxText.Description("text.additional_wave_damage_fraction_per_red_rank")
            );
            CassetteYellowStamina = config.Bind(
                "Cassette properties",
                "YellowStaminaPerRank",
                1f,
                BoomboxText.Description("text.additional_stamina_per_yellow_rank_on_each")
            );
            CassetteGreenHealth = config.Bind(
                "Cassette properties",
                "GreenHealthPerRank",
                1f,
                BoomboxText.Description("text.additional_health_per_green_rank_on_each")
            );
            CassetteBlueEitr = config.Bind(
                "Cassette properties",
                "BlueEitrPerRank",
                1f,
                BoomboxText.Description("text.additional_eitr_per_blue_rank_on_each")
            );
            CassettePurpleSpeed =
                config.Bind(
                "Cassette properties",
                "PurpleSpeedPerRank",
                .05f,
                BoomboxText.Description("text.movement_speed_bonus_per_purple_rank_0")
            );
        }

        public float WaveDamage(
            int level
        ) => Nonnegative(Damage[Mathf.Clamp(level, 1, BoomboxProgression.MaxTier)].Value);
        private void BindRecipe(ConfigFile config, string key, UpgradeRecipe original)
        {
            recipes[key] = new RecipeSettings(config, key, original);
        }
        public UpgradeRecipe Recipe(string key, UpgradeRecipe original) => recipes.TryGetValue(key, out var value)
            ? value.Read()
            : original;
        public float WaveRadius(
            int level
        ) => Nonnegative(Radius[Mathf.Clamp(level, 1, BoomboxProgression.MaxTier)].Value);
        public float WavePush(int level) => Nonnegative(Push[Mathf.Clamp(level, 1, BoomboxProgression.MaxTier)].Value);
        public static float Nonnegative(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 0f
            : Mathf.Max(0f, value);
        public static float PositiveOr(float value, float fallback) => value > 0f && !float.IsInfinity(value)
            ? value
            : fallback;
    }

    internal sealed class RecipeSettings
    {
        private readonly ConfigEntry<string> station;
        private readonly ConfigEntry<int> level;
        private readonly ConfigEntry<string> cost;
        private readonly UpgradeRecipe original;
        private UpgradeRecipe cached;
        private string signature;
        public RecipeSettings(ConfigFile config, string key, UpgradeRecipe recipe)
        {
            original = recipe;
            station = config.Bind(
                "Recipe " + key,
                "Station",
                recipe.StationPrefab,
                BoomboxText.Description("text.crafting_station_prefab_name_applies_after")
            );
            level = config.Bind(
                "Recipe " + key,
                "StationLevel",
                recipe.StationLevel,
                BoomboxText.Description("text.required_station_level_at_least_1_applies")
            );
            cost =
                config.Bind(
                "Recipe " + key,
                "Materials",
                string.Join(",", recipe.Ingredients.Select(i => i.Prefab + ":" + i.Amount)),
                BoomboxText.Description("text.comma_separated_prefab_amount_pairs_for_example")
            );
        }
        public UpgradeRecipe Read()
        {
            string next = station.Value + "\n" + level.Value + "\n" + cost.Value;
            if (signature == next)
                return cached;
            signature = next;
            try
            {
                if (string.IsNullOrWhiteSpace(station.Value) || level.Value < 1)
                    throw new FormatException("Station and positive station level are required");
                var ingredients = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string entry in cost.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = entry.Split(':');
                    if (parts.Length != 2
                        || string.IsNullOrWhiteSpace(parts[0])
                        || !int.TryParse(
                        parts[1].Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int amount
                    )
                        || amount < 1)
                        throw new FormatException("Expected prefab:positiveAmount: " + entry);
                    string prefab = parts[0].Trim();
                    ingredients.TryGetValue(prefab, out int previous);
                    ingredients[prefab] = checked(previous + amount);
                }
                cached = new UpgradeRecipe(
                    station.Value.Trim(),
                    level.Value,
                    ingredients.Select(p => new UpgradeIngredient(p.Key, p.Value)).ToArray()
                );
            }
            catch (Exception error) when (error is FormatException || error is OverflowException)
            {
                cached = original;
                BoomboxPlugin.Instance?.LogError(station.Definition.Section + ": " + error.Message
                    + "; using the default recipe");
            }
            return cached;
        }
    }

    public sealed class RecoverySettings
    {
        public readonly ConfigEntry<float> Initial;
        public readonly ConfigEntry<float> PerLevel;
        public readonly ConfigEntry<float> Interval;
        internal RecoverySettings(ConfigFile config, string section)
        {
            Initial =
                config.Bind(
                section,
                "Initial",
                1f,
                BoomboxText.Description("text.recovery_per_tick_at_the_first_rank")
            );
            PerLevel = config.Bind(
                section,
                "PerLevel",
                1f,
                BoomboxText.Description("text.additional_recovery_for_each_subsequent_rank_of")
            );
            Interval = config.Bind(
                section,
                "Interval",
                5f,
                BoomboxText.Description("text.independent_period_in_seconds_0_means_5")
            );
        }
        public float Amount(
            int level
        ) => BoomboxSettings.Nonnegative(Initial.Value + Math.Max(0, level - 1) * PerLevel.Value);
        public float Period => BoomboxSettings.PositiveOr(Interval.Value, 5f);
    }
}
