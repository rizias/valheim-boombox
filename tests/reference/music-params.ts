export const OUTPUT_DELAY_S = 0.2;
export const HOP_S = 0.02;

export const ONSET_WINDOW_S = 2048 / 22050;
export const ONSET_MEL_BANDS = 128;
export const ONSET_MEL_TOP_HZ = 11025;
export const ONSET_TOP_DB = 80;
export const PEAK_BEFORE_HOPS = 3;
export const PEAK_AFTER_HOPS = 2;
export const PEAK_MEAN_BEFORE_HOPS = 50;
export const PEAK_WAIT_HOPS = 3;

export const STRIKE_CONTEXT_S = 4;
export const STRIKE_CONTEXT_PERCENTILE = 90;
export const STRIKE_CONTEXT_MIN_PEAKS = 4;
export const STRIKE_THRESHOLD = 0.6;
export const STRIKE_MIN_GAP_S = 0.24;
export const STRIKE_MIN_RISE_DB = 0.5;
export const STRIKE_LOUDNESS_WINDOW_S = 0.06;
export const STRIKE_FULL_LOUDNESS = 1.3;

export const LOUDNESS_AVERAGE_S = 4;
export const LOUDNESS_SMOOTH_S = 0.25;
export const LOUDNESS_QUIET = 0.7;
export const LOUDNESS_LOUD = 1.3;
export const LOW_TOP_HZ = 250;
export const HIGH_BOTTOM_HZ = 4000;

export const MEMBRANE_INTERVAL_S = 0.02;
export const MEMBRANE_SAMPLES = 1024;
export const MEMBRANE_BASS_HZ = 180;
export const MEMBRANE_PEAK_S = 3;
export const MEMBRANE_SILENCE = 0.01;
export const MEMBRANE_ATTACK_S = 0.015;
export const MEMBRANE_RELEASE_S = 0.065;
export const MEMBRANE_MOTION = 0.65;
export const MEMBRANE_TRAVEL_M = 0.075;
export const MEMBRANE_SWELL = 0.08;
export const MEMBRANE_PUSH_S = 0.15;
