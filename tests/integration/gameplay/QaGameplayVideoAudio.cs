using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class QaVideoAudio : MonoBehaviour
{
    private readonly object gate = new object();
    private float[] samples;
    private int count, callbacks, rate, channels;
    private bool active;
    private double firstDsp;
    private float peak;
    public string Error
    {
        get;
        private set;
    }

    public void Begin()
    {
        rate = AudioSettings.outputSampleRate;
        samples = new float[checked(rate * 2 * 48)];
        lock (gate)
            active = true;
    }
    public void Stop()
    {
        lock (gate)
            active = false;
    }

    private void OnAudioFilterRead(float[] data, int channelCount)
    {
        lock (gate)
        {
            if (!active)
                return;
            if (channelCount != 2 || count + data.Length > samples.Length)
            {
                Error = "Source DSP channels/48-second memory bound";
                active = false;
                return;
            }
            if (count == 0)
            {
                firstDsp = AudioSettings.dspTime;
                channels = channelCount;
            }
            Array.Copy(data, 0, samples, count, data.Length);
            count += data.Length;
            callbacks++;
            foreach (float value in data)
                peak = Math.Max(peak, Math.Abs(value));
        }
    }

    public JObject Save(string path)
    {
        Stop();
        lock (gate)
        {
            if (Error != null || count == 0)
                throw new InvalidOperationException(Error ?? "No Source DSP callbacks");
            long bytes = 44L + count * 2L;
            if (bytes > 10L * 1024 * 1024)
                throw new InvalidOperationException("DSP WAV exceeds 10 MiB");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[] { 82, 73, 70, 70 });
                writer.Write(count * 2 + 36);
                writer.Write(new byte[] { 87, 65, 86, 69, 102, 109, 116, 32 });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(rate);
                writer.Write(rate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(new byte[] { 100, 97, 116, 97 });
                writer.Write(count * 2);
                var buffer = new byte[32768];
                for (int offset = 0; offset < count;)
                {
                    int taken = Math.Min(buffer.Length / 2, count - offset);
                    for (int i = 0; i < taken; i++)
                    {
                        short value = (short)(Math.Max(-1, Math.Min(1, samples[offset + i])) * 32767);
                        buffer[i * 2] = (byte)value;
                        buffer[i * 2 + 1] = (byte)(value >> 8);
                    }
                    writer.Write(buffer, 0, taken * 2);
                    offset += taken;
                }
            }
            return new JObject
            {
                ["file"] = path,
                ["rate"] = rate,
                ["channels"] = channels,
                ["samples"] = count,
                ["firstDsp"] = firstDsp,
                ["callbacks"] = callbacks,
                ["peak"] = peak,
                ["bytes"] = bytes
            };
        }
    }
    private void OnDestroy()
    {
        Stop();
        samples = null;
    }
}
