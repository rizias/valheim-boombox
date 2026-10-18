using System;
using System.Collections.Generic;

namespace VikingBoombox
{
    public struct MusicSoundFrame
    {
        public double Time;
        public double RmsDb;
        public MusicRanges Smooth;
    }

    public sealed class MusicSoundAnalyzer
    {
        public static readonly double SettledSeconds = (MusicStrikePicker.LookaheadHops + 1)
            * MusicAnalysisParams.HopSeconds
            + MusicAnalysisParams.OnsetBeforeSeconds;
        public readonly double SampleRate;
        private readonly int hopSamples;
        private readonly MusicPowerSpectrum spectrum;
        private readonly float[] power;
        private readonly float[] ring;
        private readonly float[] frame;
        private readonly float[] bright;
        private readonly float[] brightFrame;
        private readonly MusicBiquad brightFilter;
        private readonly MusicOnsetFlux flux;
        private readonly MusicRangeSplitter splitter;
        private readonly MusicRangeMeters meters = new MusicRangeMeters();
        private readonly MusicStrikePicker picker = new MusicStrikePicker();
        private readonly List<MusicSoundFrame> frames = new List<MusicSoundFrame>();
        private readonly List<MusicStrike> strikes = new List<MusicStrike>();
        private readonly Func<double, double> refine;
        private int write;
        private int sinceHop;
        private double hopTime;

        public MusicSoundAnalyzer(double sampleRate)
        {
            SampleRate = sampleRate;
            refine = Refine;
            hopSamples = MusicAnalysisParams.Round(MusicAnalysisParams.HopSeconds * sampleRate);
            int length = MusicPowerSpectrum.WindowSamples(MusicAnalysisParams.OnsetWindowSeconds, sampleRate);
            int size = MusicPowerSpectrum.SizeFor(length);
            spectrum = new MusicPowerSpectrum(length, size);
            power = new float[size / 2 + 1];
            ring = new float[length];
            frame = new float[length];
            bright = new float[length];
            brightFrame = new float[length];
            brightFilter = new MusicBiquad(false, MusicAnalysisParams.OnsetHighpassHz, sampleRate);
            flux = new MusicOnsetFlux(
                MusicMelBands.Create(
                    sampleRate,
                    size,
                    MusicAnalysisParams.OnsetMelBands,
                    MusicAnalysisParams.OnsetMelTopHz
                ),
                MusicAnalysisParams.OnsetTopDb
            );
            splitter = new MusicRangeSplitter(sampleRate);
        }

        public double Threshold
        {
            get => picker.Threshold;
            set => picker.Threshold = value;
        }

        public void Reset()
        {
            Array.Clear(ring, 0, ring.Length);
            Array.Clear(bright, 0, bright.Length);
            brightFilter.Reset();
            write = 0;
            sinceHop = 0;
            flux.Reset();
            splitter.Reset();
            meters.Reset();
            picker.Reset();
            frames.Clear();
            strikes.Clear();
        }

        public void Push(double startTime, float[] samples, int count)
        {
            for (int index = 0; index < count; index++)
            {
                float sample = samples[index];
                ring[write] = sample;
                bright[write] = (float)brightFilter.Process(sample);
                write = (write + 1) % ring.Length;
                splitter.Add(sample);
                if (++sinceHop < hopSamples)
                    continue;
                sinceHop = 0;
                Hop(startTime + (index + 1) / SampleRate);
            }
        }

        public bool FrameAt(double time, out MusicSoundFrame found)
        {
            for (int index = frames.Count - 1; index >= 0; index--)
                if (frames[index].Time <= time)
                {
                    found = frames[index];
                    return true;
                }
            found = default;
            return false;
        }

        public void StrikesIn(double after, double until, List<MusicStrike> output)
        {
            foreach (var strike in strikes)
                if (strike.Time > after && strike.Time <= until)
                    output.Add(strike);
        }

        private float[] Linear(float[] source, float[] output)
        {
            Array.Copy(source, write, output, 0, source.Length - write);
            Array.Copy(source, 0, output, source.Length - write, write);
            return output;
        }

        private void Hop(double time)
        {
            spectrum.Compute(Linear(ring, frame), power);
            double onset = flux.Push(power);
            var rms = splitter.Take();
            var instant = meters.Push(rms, out var smooth);
            double rmsDb = 20 * Math.Log10(Math.Max(1e-12, rms.Whole));
            frames.Add(new MusicSoundFrame { Time = time, RmsDb = rmsDb, Smooth = smooth });
            frames.RemoveAll(kept => kept.Time < time - MusicAnalysisParams.KeepSeconds);
            hopTime = time;
            if (picker.Push(
                new MusicHopFeatures { Time = time, Flux = onset, RmsDb = rmsDb, Instant = instant },
                refine,
                out var strike
            ))
                strikes.Add(strike);
            strikes.RemoveAll(kept => kept.Time < time - MusicAnalysisParams.KeepSeconds);
        }

        private double Refine(
            double hopEnd
        ) => MusicOnsetTime.Find(Linear(bright, brightFrame), hopTime, SampleRate, hopEnd);
    }
}
