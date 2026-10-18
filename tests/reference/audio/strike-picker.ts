import { SILENCE_DB } from '../params.ts';
import {
  HOP_S,
  ONSET_WINDOW_S,
  PEAK_AFTER_HOPS,
  PEAK_BEFORE_HOPS,
  PEAK_MEAN_BEFORE_HOPS,
  PEAK_WAIT_HOPS,
  STRIKE_CONTEXT_MIN_PEAKS,
  STRIKE_CONTEXT_PERCENTILE,
  STRIKE_CONTEXT_S,
  STRIKE_FULL_LOUDNESS,
  STRIKE_LOUDNESS_WINDOW_S,
  STRIKE_MIN_GAP_S,
  STRIKE_MIN_RISE_DB,
  STRIKE_THRESHOLD,
} from '../music-params.ts';
import { RANGES, type Range, type Ranges } from './ranges.ts';

export interface HopFeatures {
  timeS: number;
  flux: number;
  rmsDb: number;
  instant: Ranges;
}

export interface Strike {
  timeS: number;
  strength: number;
  loudest: Range;
}

interface PeakRecord {
  hop: number;
  flux: number;
}

const CONTEXT_HOPS = Math.round(STRIKE_CONTEXT_S / HOP_S);
const LOUDNESS_HOPS = Math.round(STRIKE_LOUDNESS_WINDOW_S / HOP_S);
const LOOKAHEAD_HOPS = Math.max(PEAK_AFTER_HOPS, LOUDNESS_HOPS - 1);
const KEEP_HOPS = PEAK_MEAN_BEFORE_HOPS + LOOKAHEAD_HOPS + PEAK_BEFORE_HOPS + 8;
const WINDOW_HOPS = Math.ceil(ONSET_WINDOW_S / HOP_S);

export function percentile(values: readonly number[], share: number): number {
  const sorted = [...values].sort((a, b) => a - b);
  const position = (sorted.length - 1) * (share / 100);
  const lower = Math.floor(position);
  const low = sorted[lower] ?? 0;
  const high = sorted[Math.min(sorted.length - 1, lower + 1)] ?? low;
  return low + (high - low) * (position - lower);
}

export type Refine = (hopEndS: number) => number;

const HOP_START: Refine = (hopEndS) => hopEndS - HOP_S;

export class StrikePicker {
  threshold = STRIKE_THRESHOLD;
  #hops: HopFeatures[] = [];
  #firstHop = 0;
  #peaks: PeakRecord[] = [];
  #lastPeak = -Infinity;
  #lastStrikeS = -Infinity;

  reset(): void {
    this.#hops = [];
    this.#firstHop = 0;
    this.#peaks = [];
    this.#lastPeak = -Infinity;
    this.#lastStrikeS = -Infinity;
  }

  push(hop: HopFeatures, refine: Refine = HOP_START): Strike | null {
    this.#hops.push(hop);
    if (this.#hops.length > KEEP_HOPS) {
      this.#hops.shift();
      this.#firstHop += 1;
    }
    const candidate = this.#firstHop + this.#hops.length - 1 - LOOKAHEAD_HOPS;
    return candidate >= 0 && this.#isPeak(candidate) ? this.#decide(candidate, refine) : null;
  }

  #flux(hop: number): number | undefined {
    return this.#hops[hop - this.#firstHop]?.flux;
  }

  #isPeak(hop: number): boolean {
    const flux = this.#flux(hop) ?? 0;
    if (flux <= 0 || hop <= this.#lastPeak + PEAK_WAIT_HOPS) {
      return false;
    }
    for (let other = hop - PEAK_BEFORE_HOPS; other <= hop + PEAK_AFTER_HOPS; other += 1) {
      if ((this.#flux(other) ?? -Infinity) > flux) {
        return false;
      }
    }
    return flux >= this.#meanAround(hop);
  }

  #meanAround(hop: number): number {
    let sum = 0;
    let count = 0;
    for (let other = Math.max(this.#firstHop, hop - PEAK_MEAN_BEFORE_HOPS); other <= hop + PEAK_AFTER_HOPS; other += 1) {
      if (!this.#afterSilence(other)) {
        sum += this.#flux(other) ?? 0;
        count += 1;
      }
    }
    return count > 0 ? sum / count : 0;
  }

  #decide(hop: number, refine: Refine): Strike | null {
    this.#lastPeak = hop;
    const flux = this.#flux(hop) ?? 0;
    const reference = this.#reference(hop, flux);
    const following = this.#following(hop);
    const audible = (following[0]?.rmsDb ?? -Infinity) > SILENCE_DB;
    if (flux < STRIKE_MIN_RISE_DB || flux < this.threshold * reference || !audible) {
      return null;
    }
    const timeS = refine(this.#hops[hop - this.#firstHop]?.timeS ?? 0);
    if (timeS - this.#lastStrikeS < STRIKE_MIN_GAP_S - 1e-9) {
      return null;
    }
    this.#lastStrikeS = timeS;
    return this.#strike(timeS, following);
  }

  #reference(hop: number, flux: number): number {
    this.#peaks = this.#peaks.filter((peak) => peak.hop >= hop - CONTEXT_HOPS);
    const context = this.#peaks.map((peak) => peak.flux);
    if (!this.#afterSilence(hop)) {
      this.#peaks.push({ hop, flux });
    }
    return context.length >= STRIKE_CONTEXT_MIN_PEAKS ? percentile(context, STRIKE_CONTEXT_PERCENTILE) : flux;
  }

  #afterSilence(hop: number): boolean {
    for (let other = hop - WINDOW_HOPS; other < hop; other += 1) {
      if (other < 0 || (this.#hops[other - this.#firstHop]?.rmsDb ?? SILENCE_DB) < SILENCE_DB) {
        return true;
      }
    }
    return false;
  }

  #following(hop: number): HopFeatures[] {
    const start = hop - this.#firstHop;
    return this.#hops.slice(start, start + LOUDNESS_HOPS);
  }

  #strike(timeS: number, following: readonly HopFeatures[]): Strike {
    const peak = (key: keyof Ranges): number => Math.max(0, ...following.map((features) => features.instant[key]));
    const loudest = RANGES.reduce((best, range) => (peak(range) > peak(best) ? range : best), RANGES[0] ?? 'low');
    return { timeS, strength: Math.min(1, peak('whole') / STRIKE_FULL_LOUDNESS), loudest };
  }
}
