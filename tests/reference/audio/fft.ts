function bitReversal(size: number): Uint32Array {
  const bits = Math.round(Math.log2(size));
  const table = new Uint32Array(size);
  for (let index = 0; index < size; index += 1) {
    let reversed = 0;
    for (let bit = 0; bit < bits; bit += 1) {
      reversed = (reversed << 1) | ((index >> bit) & 1);
    }
    table[index] = reversed;
  }
  return table;
}

export function windowSamples(windowS: number, sampleRate: number): number {
  return Math.round(windowS * sampleRate);
}

export function fftSizeFor(samples: number): number {
  return 2 ** Math.ceil(Math.log2(Math.max(2, samples)));
}

export class PowerSpectrum {
  readonly size: number;
  readonly windowLength: number;
  readonly #window: Float64Array;
  readonly #reversed: Uint32Array;
  readonly #cos: Float64Array;
  readonly #sin: Float64Array;
  readonly #re: Float64Array;
  readonly #im: Float64Array;

  constructor(windowLength: number, size: number) {
    if (!Number.isInteger(Math.log2(size)) || size < 2 || windowLength > size) {
      throw new Error(`окно ${windowLength} не помещается в преобразование ${size}`);
    }
    this.size = size;
    this.windowLength = windowLength;
    this.#window = Float64Array.from({ length: windowLength }, (_, index) => 0.5 - 0.5 * Math.cos((2 * Math.PI * index) / windowLength));
    this.#reversed = bitReversal(size);
    this.#cos = Float64Array.from({ length: size / 2 }, (_, index) => Math.cos((-2 * Math.PI * index) / size));
    this.#sin = Float64Array.from({ length: size / 2 }, (_, index) => Math.sin((-2 * Math.PI * index) / size));
    this.#re = new Float64Array(size);
    this.#im = new Float64Array(size);
  }

  compute(frame: ArrayLike<number>, out: Float32Array): void {
    this.#load(frame);
    this.#transform();
    for (let bin = 0; bin <= this.size / 2; bin += 1) {
      const re = this.#re[bin] ?? 0;
      const im = this.#im[bin] ?? 0;
      out[bin] = re * re + im * im;
    }
  }

  #load(frame: ArrayLike<number>): void {
    for (let index = 0; index < this.size; index += 1) {
      const target = this.#reversed[index] ?? 0;
      this.#re[target] = index < this.windowLength ? (frame[index] ?? 0) * (this.#window[index] ?? 0) : 0;
      this.#im[target] = 0;
    }
  }

  #transform(): void {
    for (let span = 2; span <= this.size; span *= 2) {
      const half = span / 2;
      const step = this.size / span;
      for (let start = 0; start < this.size; start += span) {
        this.#butterflies(start, half, step);
      }
    }
  }

  #butterflies(start: number, half: number, step: number): void {
    const re = this.#re;
    const im = this.#im;
    for (let offset = 0; offset < half; offset += 1) {
      const cos = this.#cos[offset * step] ?? 1;
      const sin = this.#sin[offset * step] ?? 0;
      const top = start + offset;
      const bottom = top + half;
      const bottomRe = re[bottom] ?? 0;
      const bottomIm = im[bottom] ?? 0;
      const turnedRe = bottomRe * cos - bottomIm * sin;
      const turnedIm = bottomRe * sin + bottomIm * cos;
      const topRe = re[top] ?? 0;
      const topIm = im[top] ?? 0;
      re[bottom] = topRe - turnedRe;
      im[bottom] = topIm - turnedIm;
      re[top] = topRe + turnedRe;
      im[top] = topIm + turnedIm;
    }
  }
}
