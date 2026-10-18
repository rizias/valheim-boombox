import { HOP_S } from '../music-params.ts';

export const ONSET_HIGHPASS_HZ = 400;
export const ONSET_BLOCK_S = 0.002;
export const ONSET_BEFORE_S = 0.04;
export const ONSET_AFTER_S = 0.02;
export const ONSET_MIN_RISE_DB = 3;

function blockDb(samples: Float32Array, first: number, length: number): number {
  let sum = 0;
  for (let index = first; index < first + length; index += 1) {
    const sample = samples[index] ?? 0;
    sum += sample * sample;
  }
  return 10 * Math.log10(Math.max(1e-12, sum / Math.max(1, length)));
}

function levels(samples: Float32Array, firstIndex: number, blocks: number, blockSamples: number): number[] {
  return Array.from({ length: blocks }, (_, block) => blockDb(samples, firstIndex + block * blockSamples, blockSamples));
}

function peakBlock(values: readonly number[], fromBlock: number): number {
  let peak = fromBlock;
  values.forEach((value, block) => {
    if (block >= fromBlock && value > (values[peak] ?? -Infinity)) {
      peak = block;
    }
  });
  return peak;
}

function floorBlock(values: readonly number[], peak: number): number {
  let floor = 0;
  for (let block = 1; block <= peak; block += 1) {
    if ((values[block] ?? Infinity) < (values[floor] ?? Infinity)) {
      floor = block;
    }
  }
  return floor;
}

function rising(values: readonly number[], fromBlock: number): number | null {
  const peak = peakBlock(values, fromBlock);
  const floor = floorBlock(values, peak);
  const floorDb = values[floor] ?? 0;
  const peakDb = values[peak] ?? 0;
  if (peakDb - floorDb < ONSET_MIN_RISE_DB) {
    return null;
  }
  const middle = floorDb + (peakDb - floorDb) / 2;
  const crossing = values.findIndex((value, block) => block >= floor && block <= peak && value >= middle);
  return crossing < 0 ? null : crossing;
}

export function onsetTime(samples: Float32Array, endS: number, sampleRate: number, hopEndS: number): number {
  const blockSamples = Math.max(1, Math.round(ONSET_BLOCK_S * sampleRate));
  const startS = hopEndS - ONSET_BEFORE_S;
  const firstIndex = samples.length - Math.round((endS - startS) * sampleRate);
  const blocks = Math.floor(((ONSET_BEFORE_S + ONSET_AFTER_S) * sampleRate) / blockSamples);
  if (firstIndex < 0 || firstIndex + blocks * blockSamples > samples.length) {
    return hopEndS - HOP_S;
  }
  const values = levels(samples, firstIndex, blocks, blockSamples);
  const block = rising(values, Math.round((ONSET_BEFORE_S - HOP_S) / ONSET_BLOCK_S));
  return block === null ? hopEndS - HOP_S : startS + (block * blockSamples) / sampleRate;
}
