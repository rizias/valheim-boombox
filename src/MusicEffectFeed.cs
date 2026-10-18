using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class MusicMoment
    {
        public double Time;
        public bool Silent = true;
        public float Level;
        public MusicRanges Smooth;
        public readonly List<MusicStrike> Fresh = new List<MusicStrike>();
    }

    internal sealed class MusicEffectFeed
    {
        private const int ChunkFrames = 960;
        private static readonly MusicRanges Normal = new MusicRanges { Whole = 1, Low = 1, Mid = 1, High = 1 };
        private readonly float[] interleaved = new float[ChunkFrames * MusicLiveStream.Channels];
        private readonly float[] mono = new float[ChunkFrames];
        private readonly float[] heardMono = new float[MusicLevelMeter.LoudnessSamples(MusicLiveStream.Frequency)];
        private readonly float[] heardInterleaved = new float[MusicLevelMeter.LoudnessSamples(MusicLiveStream.Frequency)
            * MusicLiveStream.Channels];
        private readonly MusicLevelMeter meter = new MusicLevelMeter();
        private MusicSoundAnalyzer analyzer;
        private MusicLoudnessWindow tapLoudness;
        private BoomboxMusicTap tapped;
        private long analyzedFrame = -1;
        private double tapTime;
        private double emittedUntil = double.NegativeInfinity;
        public readonly MusicMoment Moment = new MusicMoment();

        public void Reset()
        {
            analyzer?.Reset();
            meter.Reset();
            tapped = null;
            analyzedFrame = -1;
            tapTime = 0;
            emittedUntil = double.NegativeInfinity;
            Rest();
        }

        public void Rest()
        {
            Moment.Silent = true;
            Moment.Level = 0f;
            Moment.Smooth = default;
            Moment.Fresh.Clear();
        }

        public void Track(MusicLiveStream stream, long playedFrame, double threshold)
        {
            Prepare(MusicLiveStream.Frequency, threshold);
            double heard = playedFrame / (double)MusicLiveStream.Frequency - OutputLatency();
            long target = (long)Math.Floor((heard + MusicAnalysisParams.LookAheadSeconds) * MusicLiveStream.Frequency);
            long heardFrame = Math.Max(0, (long)Math.Floor(heard * MusicLiveStream.Frequency));
            if (analyzedFrame < 0 || analyzedFrame < heardFrame - MusicLiveStream.Frequency
            || analyzedFrame > target + MusicLiveStream.Frequency)
            {
                analyzer.Reset();
                analyzedFrame = heardFrame;
                emittedUntil = heard;
            }
            long end = stream.EofFrame >= 0 ? Math.Min(target, stream.EofFrame) : target;
            while (analyzedFrame < end)
            {
                int count = (int)Math.Min(ChunkFrames, end - analyzedFrame);
                if (count < ChunkFrames && stream.EofFrame < 0)
                    break;
                if (!Read(stream, analyzedFrame, count))
                    break;
                analyzer.Push(analyzedFrame / (double)MusicLiveStream.Frequency, mono, count);
                analyzedFrame += count;
            }
            Emit(heard);
            Moment.Level = meter.Push(heard, HeardDb(stream, heard));
        }

        public void Tap(BoomboxMusicTap tap, double threshold)
        {
            Prepare(tap.SampleRate, threshold);
            if (tap != tapped)
            {
                analyzer.Reset();
                meter.Reset();
                tapLoudness = new MusicLoudnessWindow(tap.SampleRate);
                tapped = tap;
                tapTime = 0;
                emittedUntil = double.NegativeInfinity;
            }
            tapTime = tap.Drain(analyzer, tapLoudness, tapTime);
            analyzer.StrikesIn(emittedUntil, double.PositiveInfinity, Moment.Fresh);
            for (int index = 0; index < Moment.Fresh.Count; index++)
            {
                var strike = Moment.Fresh[index];
                emittedUntil = Math.Max(emittedUntil, strike.Time);
                strike.Time = tapTime;
                Moment.Fresh[index] = strike;
            }
            Describe(tapTime);
            Moment.Level = meter.Push(tapTime, tapLoudness.Db());
        }

        public void Beat(double time, float strength)
        {
            Moment.Time = time;
            Moment.Silent = false;
            Moment.Level = strength;
            Moment.Smooth = Normal;
            Moment.Fresh.Add(new MusicStrike { Time = time, Strength = strength, Loudest = MusicRange.Low });
        }

        public void Steady(double time)
        {
            Moment.Time = time;
            Moment.Silent = false;
            Moment.Level = 0f;
            Moment.Smooth = Normal;
            Moment.Fresh.Clear();
        }

        private void Prepare(int sampleRate, double threshold)
        {
            if (analyzer == null || analyzer.SampleRate != sampleRate)
            {
                analyzer = new MusicSoundAnalyzer(sampleRate);
                analyzedFrame = -1;
                tapTime = 0;
                emittedUntil = double.NegativeInfinity;
            }
            analyzer.Threshold = threshold;
            Moment.Fresh.Clear();
        }

        private void Emit(double heard)
        {
            if (heard > emittedUntil)
                analyzer.StrikesIn(emittedUntil, heard, Moment.Fresh);
            emittedUntil = Math.Max(emittedUntil, heard);
            Describe(heard);
        }

        private void Describe(double heard)
        {
            Moment.Time = heard;
            bool found = analyzer.FrameAt(heard, out var frame);
            Moment.Silent = !found || frame.RmsDb < MusicAnalysisParams.SilenceDb;
            Moment.Smooth = Moment.Silent ? default : frame.Smooth;
        }

        private bool Read(MusicLiveStream stream, long frame, int count)
        {
            var buffer = count == ChunkFrames ? interleaved : new float[count * MusicLiveStream.Channels];
            if (!stream.ReadAt(buffer, frame))
                return false;
            MixDown(buffer, mono, count);
            return true;
        }

        private double HeardDb(MusicLiveStream stream, double heard)
        {
            long end = (long)Math.Floor(heard * MusicLiveStream.Frequency);
            if (end < heardMono.Length || !stream.ReadAt(heardInterleaved, end - heardMono.Length))
                return double.NegativeInfinity;
            MixDown(heardInterleaved, heardMono, heardMono.Length);
            return MusicLevelMeter.RmsDb(heardMono, heardMono.Length);
        }

        private static void MixDown(float[] stereo, float[] output, int count)
        {
            for (int index = 0; index < count; index++)
            {
                float mixed = (float)(0 + stereo[index * 2] / 2.0);
                output[index] = (float)(mixed + stereo[index * 2 + 1] / 2.0);
            }
        }

        private static double OutputLatency()
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int bufferCount);
            return bufferLength * (double)bufferCount / Math.Max(1, AudioSettings.outputSampleRate);
        }
    }
}
