using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using VikingBoombox;

public static partial class NativeMusicChecks
{
    private static object Membranes(string ffmpeg, string ffprobe, string run)
    {
        string wav = Path.Combine(run, "membranes.wav");
        const int rate = 48000, length = rate * 11;
        double beta = Math.Exp(-2 * Math.PI * 180 / rate), omega = 2 * Math.PI / 512;
        double gain = (1 - beta) / Math.Sqrt(1 + beta * beta - 2 * beta * Math.Cos(omega)) / Math.Sqrt(2);
        double Amplitude(int frame) => frame < rate / 2
            ? .8
            : frame < rate * 8
                ? .1
                : frame < rate * 19 / 2
                    ? .0115 / gain
                    : .0095 / gain;
        using (var writer = new BinaryWriter(File.Create(wav)))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + length * 4);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(rate);
            writer.Write(rate * 4);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(length * 4);
            for (int frame = 0; frame < length; frame++)
            {
                short value = (short)(Math.Sin(omega * frame) * Amplitude(frame) * 32767);
                writer.Write(value);
                writer.Write(value);
            }
        }
        float[] reference = Decode(ffmpeg, wav);
        var oracle = new MembranePcmOracle(reference);
        var meterType = typeof(MusicLiveStream).Assembly.GetType("VikingBoombox.BoomboxMembraneMeter");
        var live = MusicLiveSource.Locate(
            new MusicTrack { id = "membranes", source = "file://" + wav, title = "membranes" },
            ffmpeg,
            ffprobe,
            CancellationToken.None
        );
        var results = new List<object>();
        var common = new Dictionary<int, float>();
        foreach (int fps in new[] { 20, 60, 144 })
        {
            using var stream = new MusicLiveStream(live, 2, 2048);
            stream.Begin(0);
            WaitReady(stream);
            var meter = Activator.CreateInstance(meterType);
            meterType.GetMethod("Forget").Invoke(meter, null);
            var measure = meterType.GetMethod("Measure");
            var measureWave = meterType.GetMethod("MeasureWave");
            var travel = meterType.GetProperty("Travel");
            measureWave.Invoke(meter, new object[] { stream, 0L });
            int played = 0;
            double worst = 0, fpsDifference = 0, worstLevel = 0;
            for (int tick = 1; tick <= fps * 43 / 4; tick++)
            {
                int frame = (int)Math.Round(tick * rate / (double)fps);
                while (played < frame)
                {
                    int count = Math.Min(2048, frame - played);
                    stream.Read(new float[count * 2]);
                    played += count;
                }
                WaitReady(stream);
                measure.Invoke(meter, new object[] { stream, (long)frame, 2, rate, stream.Length });
                measureWave.Invoke(meter, new object[] { stream, (long)frame });
                float actual = (float)travel.GetValue(meter);
                double expected = oracle.At(frame);
                worst = Math.Max(worst, Math.Abs(actual - expected));
                worstLevel = Math.Max(
                    worstLevel,
                    Math.Abs(
                        (float)meterType.GetProperty("Level").GetValue(meter) - oracle.LevelAt(frame, stream.Length)
                    )
                );
                if (frame % (rate / 4) == 0)
                {
                    if (common.TryGetValue(frame, out float previous))
                        fpsDifference = Math.Max(fpsDifference, Math.Abs(actual - previous));
                    else
                        common[frame] = actual;
                }
            }
            Check(
                worst < 3e-6 && fpsDifference < 3e-6,
                "Causal waveform Travel agrees with independent quantized PCM oracle at " + fps + "fps, worst " + worst
                    + ", common-frame difference "
                    + fpsDifference
            );
            Check(
                worstLevel < 3e-6,
                "Unchanged glow Level matches independent forward-window PCM RMS at " + fps + "fps, worst " + worstLevel
            );
            meterType.GetMethod("Forget").Invoke(meter, null);
            Check((float)travel.GetValue(meter) == 0, "Forget clears Travel/history");
            results.Add(new
            {
                fps,
                worst,
                fpsDifference,
                worstLevel
            });
        }
        Check(
            oracle.Rms(8 * rate + rate / 4) > .01 && oracle.Rms(10 * rate) < .01,
            "Independent PCM threshold fixture straddles RMS silence boundary"
        );
        return results;
    }
    private sealed class MembranePcmOracle
    {
        private readonly double[][] prefix;
        private readonly double[] powers, peaks;
        public MembranePcmOracle(float[] pcm)
        {
            int count = pcm.Length / 2;
            prefix = new[] { new double[count + 1], new double[count + 1] };
            powers = new double[1473];
            powers[0] = 1;
            double beta = Math.Exp(-2 * Math.PI * 180 / 48000);
            for (int index = 1; index < powers.Length; index++)
                powers[index] = powers[index - 1] * beta;
            for (int n = 0; n < count; n++)
                for (int c = 0; c < 2; c++)
                    prefix[c][n + 1] = beta * prefix[c][n] + (1 - beta) * pcm[n * 2 + c];
            peaks = new double[(count + 959) / 960];
            for (int bin = 0; bin < peaks.Length; bin++)
                peaks[bin] = Peak(bin, Math.Min(count - 1, (bin + 1) * 960 - 1));
        }
        private double Window(
            int channel,
            int first,
            int frame
        ) => prefix[channel][frame + 1] - powers[frame - first + 1] * prefix[channel][first];
        private double Peak(int bin, int end)
        {
            int begin = bin * 960, first = Math.Max(0, begin - 512);
            double peak = 0;
            for (int frame = begin; frame <= end; frame++)
                peak = Math.Max(peak, Math.Abs((Window(0, first, frame) + Window(1, first, frame)) / 2));
            return peak;
        }
        public double Rms(int frame)
        {
            if (frame < 511)
                return 0;
            int first = frame - 511;
            double energy = 0;
            for (int n = first; n <= frame; n++)
                for (int c = 0; c < 2; c++)
                {
                    double value = Window(c, first, n);
                    energy += value * value;
                }
            return Math.Sqrt(energy / 1024);
        }
        public double LevelAt(int frame, long length)
        {
            long offset = Math.Min((long)(frame / 960) * 960, Math.Max(0, length - 512));
            if (offset < 0 || offset + 512 >= prefix[0].Length)
                return 0;
            int first = (int)offset;
            double energy = 0;
            for (int n = first; n < first + 512; n++)
                for (int c = 0; c < 2; c++)
                {
                    double value = Window(c, first, n);
                    energy += value * value;
                }
            return Math.Sqrt(energy / 1024);
        }
        public double At(int frame)
        {
            if (frame < 511 || Rms(frame) < .01)
                return 0;
            int bin = frame / 960;
            double peak = Peak(bin, frame);
            for (int previous = Math.Max(0, bin - 149); previous < bin; previous++)
                peak = Math.Max(peak, peaks[previous]);
            double last = (Window(0, frame - 511, frame) + Window(1, frame - 511, frame)) / 2;
            return peak <= 0 ? 0 : Math.Min(1, Math.Abs(last) / peak);
        }
    }
}
