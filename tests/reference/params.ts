export const LOUDNESS_WINDOW_S = 0.02;
export const SILENCE_DB = -50;
export const LEVEL_WINDOW_S = 4;
export const QUIET_FRACTION = 0.1;
export const MIN_RANGE_DB = 6;

export const TIER_DAMAGE: readonly number[] = [1, 10, 16, 24, 34, 46, 62, 80];
export const TIER_RADIUS_M: readonly number[] = [3, 3.25, 3.5, 3.75, 4, 4.25, 4.5, 4.75];

export const RING_FRONT_WIDTH_M = 0.06;
export const RING_BRIGHTNESS = 1.4;
export const RING_GLOW_WIDTH_M = 0.3;
export const RING_GLOW_LEVEL = 0.2;
export const RING_WALL_HEIGHT_M = 1.2;
export const RING_WALL_BRIGHTNESS = 0.3;
export const RING_REFRACTION = 0.018;
export const RING_FRINGE = 0.35;
export const RIPPLE_WAVELENGTH_M = 0.45;
export const RIPPLE_JITTER_HZ = 6;
export const RIPPLE_FALLOFF_M = 0.9;
export const RIPPLE_SHARPNESS = 6;
export const RIPPLE_BRIGHTNESS = 0.14;
export const RIPPLE_REFRACTION = 0.004;
export const RING_COLOR: readonly [number, number, number] = [0.2, 0.8, 1];
export const RING_GROUND_OFFSET_M = 0.02;

export const PULSE_FALL_HALF_LIFE_S = 0.15;
export const PULSE_REARM_M = 0.1;

export const WAVE_RISE = 0.35;
export const WAVE_RISE_WINDOW_S = 0.06;
export const WAVE_MIN_GAP_S = 0.15;
export const WAVE_SPEED_MPS = 20;
export const WAVE_FADE_FRACTION = 0.2;
export const WAVE_PEAK_WINDOW_S = 0.06;

export const SCOPE_RADIUS_M = 1.2;
export const SCOPE_LINE_M = 0.04;
export const SCOPE_AMPLITUDE_M = 0.35;
export const SCOPE_WINDOW_S = 0.04;
export const SCOPE_PEAK_DECAY_S = 10;
export const SCOPE_TAPER_FRACTION = 0.05;
export const HAZE_BANDS = 8;
export const HAZE_LOW_HZ = 60;
export const HAZE_HIGH_HZ = 8000;
export const HAZE_RISE_DB = 6;
export const HAZE_FULL_RISE_DB = 12;

export const DUMMY_DISTANCES_M: readonly number[] = [1, 2, 3, 4, 5];
export const DUMMY_BODY_RADIUS_M = 0.25;
export const HIT_FLINCH_S = 0.15;
export const HIT_TILT_DEG = 12;
export const DISTANCE_RINGS_M: readonly number[] = [1, 2, 3, 4, 5];

export const DAMAGE_TEXT_DURATION_S = 0.8;
export const DAMAGE_TEXT_RISE_M = 0.55;
export const DAMAGE_TEXT_SOLID_FRACTION = 0.5;

export const CAMERA_DISTANCE_M = 4;
export const CAMERA_MAX_DISTANCE_M = 6;
export const CAMERA_FOV_DEG = 65;

export const WALK_SPEED_MPS = 4;
export const RUN_SPEED_MPS = 7;
export const WALK_SPEED_STEP_MPS = 0.4;
export const WALK_STEP_S = 0.02;
