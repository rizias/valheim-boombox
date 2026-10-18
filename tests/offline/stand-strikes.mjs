import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { SoundAnalyzer } from '../reference/audio/sound-analyzer.ts';

const CHUNK_FRAMES = 1024;
const SETTLED_S = 0.2;
const [mode, file, rateText] = process.argv.slice(2);

if (mode === 'tracks') {
  process.stdout.write(readFileSync(join(process.env.VALHEIM_QA_ROOT ?? (() => { throw new Error('Set VALHEIM_QA_ROOT'); })(), 'fixtures', 'tracks.json'), 'utf8'));
} else {
  const rate = Number(rateText);
  const bytes = readFileSync(file);
  const samples = new Float32Array(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength));
  const analyzer = new SoundAnalyzer(rate);
  const strikes = [];
  let untilS = -Infinity;
  for (let start = 0; start < samples.length; start += CHUNK_FRAMES) {
    const chunk = samples.subarray(start, Math.min(samples.length, start + CHUNK_FRAMES));
    analyzer.push(start / rate, chunk);
    const settledS = (start + chunk.length) / rate - SETTLED_S;
    strikes.push(...analyzer.strikesIn(untilS, settledS));
    untilS = Math.max(untilS, settledS);
  }
  strikes.push(...analyzer.strikesIn(untilS, Infinity));
  process.stdout.write(JSON.stringify(strikes));
}
