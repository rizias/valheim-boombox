import { HOP_S, ONSET_MEL_BANDS, ONSET_MEL_TOP_HZ, ONSET_TOP_DB, ONSET_WINDOW_S } from '../music-params.ts';
import { Biquad } from './biquad.ts';
import { fftSizeFor, PowerSpectrum, windowSamples } from './fft.ts';
import { melBands } from './mel.ts';
import { OnsetFlux } from './onset-flux.ts';
import { ONSET_HIGHPASS_HZ, onsetTime } from './onset-time.ts';
import { RangeSplitter, type Ranges } from './ranges.ts';
import { RangeMeters } from './relative.ts';
import { StrikePicker, type Strike } from './strike-picker.ts';

export interface SoundFrame {
  timeS: number;
  rmsDb: number;
  smooth: Ranges;
}

const KEEP_S = 3;

export class SoundAnalyzer {
  readonly sampleRate: number;
  readonly #hopSamples: number;
  readonly #spectrum: PowerSpectrum;
  readonly #power: Float32Array;
  readonly #ring: Float32Array;
  readonly #frame: Float32Array;
  readonly #bright: Float32Array;
  readonly #brightFrame: Float32Array;
  readonly #brightFilter: Biquad;
  readonly #flux: OnsetFlux;
  readonly #splitter: RangeSplitter;
  readonly #meters = new RangeMeters();
  readonly #picker = new StrikePicker();
  #write = 0;
  #sinceHop = 0;
  #frames: SoundFrame[] = [];
  #strikes: Strike[] = [];

  constructor(sampleRate: number) {
    this.sampleRate = sampleRate;
    this.#hopSamples = Math.round(HOP_S * sampleRate);
    const length = windowSamples(ONSET_WINDOW_S, sampleRate);
    const size = fftSizeFor(length);
    this.#spectrum = new PowerSpectrum(length, size);
    this.#power = new Float32Array(size / 2 + 1);
    this.#ring = new Float32Array(length);
    this.#frame = new Float32Array(length);
    this.#bright = new Float32Array(length);
    this.#brightFrame = new Float32Array(length);
    this.#brightFilter = new Biquad('highpass', ONSET_HIGHPASS_HZ, sampleRate);
    this.#flux = new OnsetFlux(melBands(sampleRate, size, ONSET_MEL_BANDS, ONSET_MEL_TOP_HZ), ONSET_TOP_DB);
    this.#splitter = new RangeSplitter(sampleRate);
  }

  set threshold(value: number) {
    this.#picker.threshold = value;
  }

  reset(): void {
    this.#ring.fill(0);
    this.#bright.fill(0);
    this.#brightFilter.reset();
    this.#write = 0;
    this.#sinceHop = 0;
    this.#flux.reset();
    this.#splitter.reset();
    this.#meters.reset();
    this.#picker.reset();
    this.#frames = [];
    this.#strikes = [];
  }

  push(startTimeS: number, samples: Float32Array): void {
    samples.forEach((sample, index) => {
      this.#ring[this.#write] = sample;
      this.#bright[this.#write] = this.#brightFilter.process(sample);
      this.#write = (this.#write + 1) % this.#ring.length;
      this.#splitter.add(sample);
      this.#sinceHop += 1;
      if (this.#sinceHop === this.#hopSamples) {
        this.#sinceHop = 0;
        this.#hop(startTimeS + (index + 1) / this.sampleRate);
      }
    });
  }

  frameAt(timeS: number): SoundFrame | null {
    for (let index = this.#frames.length - 1; index >= 0; index -= 1) {
      const frame = this.#frames[index];
      if (frame !== undefined && frame.timeS <= timeS) {
        return frame;
      }
    }
    return null;
  }

  strikesIn(afterS: number, untilS: number): Strike[] {
    return this.#strikes.filter((strike) => strike.timeS > afterS && strike.timeS <= untilS);
  }

  #linear(ring: Float32Array, out: Float32Array): Float32Array {
    out.set(ring.subarray(this.#write), 0);
    out.set(ring.subarray(0, this.#write), ring.length - this.#write);
    return out;
  }

  #hop(timeS: number): void {
    this.#spectrum.compute(this.#linear(this.#ring, this.#frame), this.#power);
    const flux = this.#flux.push(this.#power);
    const rms = this.#splitter.take();
    const relative = this.#meters.push(rms);
    const rmsDb = 20 * Math.log10(Math.max(1e-12, rms.whole));
    this.#frames.push({ timeS, rmsDb, smooth: relative.smooth });
    this.#frames = this.#frames.filter((kept) => kept.timeS >= timeS - KEEP_S);
    const refine = (hopEndS: number): number =>
      onsetTime(this.#linear(this.#bright, this.#brightFrame), timeS, this.sampleRate, hopEndS);
    const strike = this.#picker.push({ timeS, flux, rmsDb, instant: relative.instant }, refine);
    if (strike !== null) {
      this.#strikes.push(strike);
    }
    this.#strikes = this.#strikes.filter((kept) => kept.timeS >= timeS - KEEP_S);
  }
}
