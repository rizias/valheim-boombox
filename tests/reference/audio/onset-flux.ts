import { applyMel, type MelBand } from './mel.ts';

const POWER_FLOOR = 1e-10;

function neighbourMax(values: Float32Array, index: number): number {
  const last = values.length - 1;
  const left = values[Math.max(0, index - 1)] ?? -Infinity;
  const right = values[Math.min(last, index + 1)] ?? -Infinity;
  return Math.max(left, values[index] ?? -Infinity, right);
}

export class OnsetFlux {
  readonly #bands: readonly MelBand[];
  readonly #topDb: number;
  readonly #mel: Float32Array;
  #current: Float32Array;
  #previous: Float32Array;
  #hasPrevious = false;
  #loudestDb = -Infinity;

  constructor(bands: readonly MelBand[], topDb: number) {
    this.#bands = bands;
    this.#topDb = topDb;
    this.#mel = new Float32Array(bands.length);
    this.#current = new Float32Array(bands.length);
    this.#previous = new Float32Array(bands.length);
  }

  reset(): void {
    this.#hasPrevious = false;
    this.#loudestDb = -Infinity;
  }

  push(power: Float32Array): number {
    this.#toDecibels(power);
    const floor = this.#loudestDb - this.#topDb;
    let sum = 0;
    if (this.#hasPrevious) {
      for (let band = 0; band < this.#current.length; band += 1) {
        const now = Math.max(this.#current[band] ?? floor, floor);
        const before = Math.max(neighbourMax(this.#previous, band), floor);
        sum += Math.max(0, now - before);
      }
    }
    this.#hasPrevious = true;
    const previous = this.#previous;
    this.#previous = this.#current;
    this.#current = previous;
    return sum / Math.max(1, this.#current.length);
  }

  #toDecibels(power: Float32Array): void {
    applyMel(power, this.#bands, this.#mel);
    this.#mel.forEach((value, band) => {
      const db = 10 * Math.log10(Math.max(POWER_FLOOR, value));
      this.#current[band] = db;
      this.#loudestDb = Math.max(this.#loudestDb, db);
    });
  }
}
