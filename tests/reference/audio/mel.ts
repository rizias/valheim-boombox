const LINEAR_STEP_HZ = 200 / 3;
const LOG_START_HZ = 1000;
const LOG_START_MEL = LOG_START_HZ / LINEAR_STEP_HZ;
const LOG_STEP = Math.log(6.4) / 27;

export interface MelBand {
  firstBin: number;
  weights: Float32Array;
}

export function hzToMel(hz: number): number {
  return hz < LOG_START_HZ ? hz / LINEAR_STEP_HZ : LOG_START_MEL + Math.log(hz / LOG_START_HZ) / LOG_STEP;
}

export function melToHz(mel: number): number {
  return mel < LOG_START_MEL ? mel * LINEAR_STEP_HZ : LOG_START_HZ * Math.exp(LOG_STEP * (mel - LOG_START_MEL));
}

function bandWeights(lowerHz: number, centerHz: number, upperHz: number, binHz: number, bins: number): MelBand {
  const scale = 2 / (upperHz - lowerHz);
  const firstBin = Math.max(0, Math.ceil(lowerHz / binHz));
  const lastBin = Math.min(bins - 1, Math.floor(upperHz / binHz));
  const weights = new Float32Array(Math.max(0, lastBin - firstBin + 1));
  for (let bin = firstBin; bin <= lastBin; bin += 1) {
    const hz = bin * binHz;
    const rising = (hz - lowerHz) / (centerHz - lowerHz);
    const falling = (upperHz - hz) / (upperHz - centerHz);
    weights[bin - firstBin] = Math.max(0, Math.min(rising, falling)) * scale;
  }
  return { firstBin, weights };
}

export function melBands(sampleRate: number, fftSize: number, bandCount: number, topHz: number): MelBand[] {
  const topMel = hzToMel(Math.min(topHz, sampleRate / 2));
  const edges = Array.from({ length: bandCount + 2 }, (_, index) => melToHz((topMel * index) / (bandCount + 1)));
  const binHz = sampleRate / fftSize;
  const bins = fftSize / 2 + 1;
  return Array.from({ length: bandCount }, (_, band) =>
    bandWeights(edges[band] ?? 0, edges[band + 1] ?? 0, edges[band + 2] ?? 0, binHz, bins),
  );
}

export function applyMel(power: Float32Array, bands: readonly MelBand[], out: Float32Array): void {
  bands.forEach((band, index) => {
    let sum = 0;
    band.weights.forEach((weight, offset) => {
      sum += weight * (power[band.firstBin + offset] ?? 0);
    });
    out[index] = sum;
  });
}
