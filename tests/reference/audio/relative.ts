import { HOP_S, LOUDNESS_AVERAGE_S, LOUDNESS_SMOOTH_S } from '../music-params.ts';
import { QUIET_RANGES, type Ranges } from './ranges.ts';

const AVERAGE_HOPS = Math.round(LOUDNESS_AVERAGE_S / HOP_S);
const SMOOTH_SPAN_HOPS = LOUDNESS_SMOOTH_S / HOP_S;
const SMOOTH_HOPS = Math.ceil(SMOOTH_SPAN_HOPS);

function meanOf(values: readonly number[]): number {
  return values.length === 0 ? 0 : values.reduce((sum, value) => sum + value, 0) / values.length;
}

export function meanOfLast(values: readonly number[], spanHops: number): number {
  let sum = 0;
  let weights = 0;
  for (let back = 0; back < values.length && back < spanHops; back += 1) {
    const weight = Math.min(1, spanHops - back);
    sum += weight * (values[values.length - 1 - back] ?? 0);
    weights += weight;
  }
  return weights > 0 ? sum / weights : 0;
}

function remember(values: number[], value: number, keep: number): void {
  values.push(value);
  if (values.length > keep) {
    values.shift();
  }
}

export interface Relative {
  instant: number;
  smooth: number;
}

export class RelativeMeter {
  #squares: number[] = [];
  #relativeSquares: number[] = [];

  push(rms: number): Relative {
    remember(this.#squares, rms * rms, AVERAGE_HOPS);
    const average = Math.sqrt(meanOf(this.#squares));
    const instant = average > 0 ? rms / average : 0;
    remember(this.#relativeSquares, instant * instant, SMOOTH_HOPS);
    return { instant, smooth: Math.sqrt(meanOfLast(this.#relativeSquares, SMOOTH_SPAN_HOPS)) };
  }

  reset(): void {
    this.#squares = [];
    this.#relativeSquares = [];
  }
}

export interface RelativeRanges {
  instant: Ranges;
  smooth: Ranges;
}

export class RangeMeters {
  readonly #meters = { whole: new RelativeMeter(), low: new RelativeMeter(), mid: new RelativeMeter(), high: new RelativeMeter() };

  push(rms: Ranges): RelativeRanges {
    const instant = { ...QUIET_RANGES };
    const smooth = { ...QUIET_RANGES };
    for (const key of ['whole', 'low', 'mid', 'high'] as const) {
      const value = this.#meters[key].push(rms[key]);
      instant[key] = value.instant;
      smooth[key] = value.smooth;
    }
    return { instant, smooth };
  }

  reset(): void {
    for (const meter of Object.values(this.#meters)) {
      meter.reset();
    }
  }
}
