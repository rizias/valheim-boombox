import { HIGH_BOTTOM_HZ, LOW_TOP_HZ } from '../music-params.ts';
import { Biquad } from './biquad.ts';

export type Range = 'low' | 'mid' | 'high';

export const RANGES: readonly Range[] = ['low', 'mid', 'high'];

export interface Ranges {
  whole: number;
  low: number;
  mid: number;
  high: number;
}

export const QUIET_RANGES: Ranges = { whole: 0, low: 0, mid: 0, high: 0 };

export function loudestRange(values: Ranges): Range {
  return RANGES.reduce((best, range) => (values[range] > values[best] ? range : best), RANGES[0] ?? 'low');
}

export class RangeSplitter {
  readonly #low: Biquad;
  readonly #midFloor: Biquad;
  readonly #midCeiling: Biquad;
  readonly #high: Biquad;
  #sums: Ranges = { ...QUIET_RANGES };
  #count = 0;

  constructor(sampleRate: number) {
    this.#low = new Biquad('lowpass', LOW_TOP_HZ, sampleRate);
    this.#midFloor = new Biquad('highpass', LOW_TOP_HZ, sampleRate);
    this.#midCeiling = new Biquad('lowpass', HIGH_BOTTOM_HZ, sampleRate);
    this.#high = new Biquad('highpass', HIGH_BOTTOM_HZ, sampleRate);
  }

  add(sample: number): void {
    const low = this.#low.process(sample);
    const mid = this.#midCeiling.process(this.#midFloor.process(sample));
    const high = this.#high.process(sample);
    this.#sums.whole += sample * sample;
    this.#sums.low += low * low;
    this.#sums.mid += mid * mid;
    this.#sums.high += high * high;
    this.#count += 1;
  }

  take(): Ranges {
    const count = Math.max(1, this.#count);
    const rms: Ranges = {
      whole: Math.sqrt(this.#sums.whole / count),
      low: Math.sqrt(this.#sums.low / count),
      mid: Math.sqrt(this.#sums.mid / count),
      high: Math.sqrt(this.#sums.high / count),
    };
    this.#sums = { ...QUIET_RANGES };
    this.#count = 0;
    return rms;
  }

  reset(): void {
    for (const filter of [this.#low, this.#midFloor, this.#midCeiling, this.#high]) {
      filter.reset();
    }
    this.#sums = { ...QUIET_RANGES };
    this.#count = 0;
  }
}
