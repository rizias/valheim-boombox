export type FilterKind = 'lowpass' | 'highpass';

export class Biquad {
  readonly #b0: number;
  readonly #b1: number;
  readonly #b2: number;
  readonly #a1: number;
  readonly #a2: number;
  #x1 = 0;
  #x2 = 0;
  #y1 = 0;
  #y2 = 0;

  constructor(kind: FilterKind, frequencyHz: number, sampleRate: number) {
    const angle = (2 * Math.PI * frequencyHz) / sampleRate;
    const cos = Math.cos(angle);
    const alpha = Math.sin(angle) / Math.SQRT2;
    const norm = 1 + alpha;
    const edge = kind === 'lowpass' ? (1 - cos) / 2 : (1 + cos) / 2;
    this.#b0 = edge / norm;
    this.#b1 = (kind === 'lowpass' ? 2 * edge : -2 * edge) / norm;
    this.#b2 = edge / norm;
    this.#a1 = (-2 * cos) / norm;
    this.#a2 = (1 - alpha) / norm;
  }

  process(input: number): number {
    const output = this.#b0 * input + this.#b1 * this.#x1 + this.#b2 * this.#x2 - this.#a1 * this.#y1 - this.#a2 * this.#y2;
    this.#x2 = this.#x1;
    this.#x1 = input;
    this.#y2 = this.#y1;
    this.#y1 = output;
    return output;
  }

  reset(): void {
    this.#x1 = 0;
    this.#x2 = 0;
    this.#y1 = 0;
    this.#y2 = 0;
  }
}
