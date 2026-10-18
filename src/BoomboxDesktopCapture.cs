using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VikingBoombox
{
    internal sealed class BoomboxDesktopCapture : IDisposable
    {
        private readonly Thread worker;
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private readonly object stopGate = new object();
        private bool stopClosed;
        private readonly BoomboxMusicTap tap;
        internal volatile string Error;
        internal volatile bool Ready;
        internal bool Alive => worker.IsAlive;

        internal BoomboxDesktopCapture(BoomboxMusicTap target)
        {
            tap = target;
            worker = new Thread(Run) { IsBackground = true, Name = "Boombox Windows audio" };
            try
            {
                worker.SetApartmentState(ApartmentState.MTA);
                worker.Start();
            }
            catch
            {
                CloseStop();
                throw;
            }
        }

        private void Run()
        {
            object enumerator = null, device = null, clientObject = null, captureObject = null;
            IntPtr format = IntPtr.Zero;
            IAudioClient client = null;
            bool initialized = false;
            try
            {
                Check(CoInitializeEx(IntPtr.Zero, 0));
                initialized = true;
                Guid enumeratorClass = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),
                    enumeratorId = typeof(IMMDeviceEnumerator).GUID;
                Check(
                    CoCreateInstance(
                        ref enumeratorClass,
                        IntPtr.Zero,
                        23,
                        ref enumeratorId,
                        out IntPtr enumeratorPointer
                    )
                );
                try
                {
                    enumerator = Marshal.GetObjectForIUnknown(enumeratorPointer);
                }
                finally
                {
                    Marshal.Release(enumeratorPointer);
                }
                Check(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
                Guid clientId = typeof(IAudioClient).GUID;
                Check(((IMMDevice)device).Activate(ref clientId, 23, IntPtr.Zero, out clientObject));
                client = (IAudioClient)clientObject;
                Check(client.GetMixFormat(out format));
                int tag = (ushort)Marshal.ReadInt16(format), channels = (ushort)Marshal.ReadInt16(format, 2);
                int rate = Marshal.ReadInt32(format, 4),
                    align = (ushort)Marshal.ReadInt16(format, 12),
                    bits = (ushort)Marshal.ReadInt16(format, 14);
                if (tag == 65534)
                    tag = Marshal.ReadInt32(format, 24);
                bool floating = tag == 3 && bits == 32;
                if (channels < 1 || channels > 32 || rate < 8000 || rate > 384000 || align != channels * (bits / 8)
                || (!floating && (tag != 1 || bits != 16 && bits != 24 && bits != 32)))
                    throw new InvalidOperationException(BoomboxText.Get("error.unsupported_windows_output_format"));
                Check(client.Initialize(0, 0x20000, 1000000, 0, format, IntPtr.Zero));
                Guid captureId = typeof(IAudioCaptureClient).GUID;
                Check(client.GetService(ref captureId, out captureObject));
                var capture = (IAudioCaptureClient)captureObject;
                var bytes = new byte[8192 * align];
                var mono = new float[8192];
                var idle = System.Diagnostics.Stopwatch.StartNew();
                Check(client.Start());
                Ready = true;
                while (!stop.WaitOne(10))
                {
                    Check(capture.GetNextPacketSize(out uint packet));
                    while (packet > 0 && !stop.WaitOne(0))
                    {
                        Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                        try
                        {
                            for (int begin = 0; begin < frames; begin += mono.Length)
                            {
                                int count = (int)Math.Min(mono.Length, frames - begin);
                                if ((flags & 2) == 0)
                                    Marshal.Copy(IntPtr.Add(data, begin * align), bytes, 0, count * align);
                                DecodeMono(bytes, mono, count, channels, align, bits, floating, (flags & 2) != 0);
                                tap.PushExternal(mono, count, rate);
                            }
                        }
                        finally
                        {
                            Check(capture.ReleaseBuffer(frames));
                        }
                        idle.Restart();
                        Check(capture.GetNextPacketSize(out packet));
                    }
                    // Windows may supply no packets when the endpoint is silent.
                    // Advance the analyzer with silence instead of holding a peak.
                    if (idle.ElapsedMilliseconds >= 20)
                    {
                        int quiet = Math.Min(mono.Length, rate / 50);
                        Array.Clear(mono, 0, quiet);
                        tap.PushExternal(mono, quiet, rate);
                        idle.Restart();
                    }
                }
            }
            catch (Exception error)
            {
                Error = error.Message;
            }
            finally
            {
                Ready = false;
                try
                {
                    if (client != null)
                        client.Stop();
                    if (format != IntPtr.Zero)
                        Marshal.FreeCoTaskMem(format);
                    foreach (object value in new[] { captureObject, clientObject, device, enumerator })
                        if (value != null && Marshal.IsComObject(value))
                            Marshal.ReleaseComObject(value);
                }
                finally
                {
                    try
                    {
                        if (initialized)
                            CoUninitialize();
                    }
                    finally
                    {
                        CloseStop();
                    }
                }
            }
        }

        private static void DecodeMono(
            byte[] bytes, float[] mono, int count, int channels, int align, int bits, bool floating, bool silent)
        {
            for (int frame = 0; frame < count; frame++)
            {
                double sum = 0;
                if (!silent)
                {
                    for (int channel = 0; channel < channels; channel++)
                    {
                        int offset = frame * align + channel * (bits / 8);
                        float value = ReadSample(bytes, offset, bits, floating);
                        if (!float.IsNaN(value) && !float.IsInfinity(value))
                            sum += value;
                    }
                }
                mono[frame] = (float)(sum / channels);
            }
        }

        private static float ReadSample(byte[] bytes, int offset, int bits, bool floating)
        {
            if (floating)
                return BitConverter.ToSingle(bytes, offset);

            switch (bits)
            {
                case 16:
                    return BitConverter.ToInt16(bytes, offset) / 32768f;
                case 24:
                    int signed = (bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16) << 8 >> 8;
                    return signed / 8388608f;
                default:
                    return BitConverter.ToInt32(bytes, offset) / 2147483648f;
            }
        }

        internal void Stop()
        {
            lock (stopGate)
            {
                if (!stopClosed)
                    stop.Set();
            }
        }
        private void CloseStop()
        {
            lock (stopGate)
            {
                if (stopClosed)
                    return;
                stopClosed = true;
                stop.Dispose();
            }
        }
        public void Dispose()
        {
            Stop();
            if (worker.Join(1000))
                CloseStop();
        }
        private static void Check(int result)
        {
            if (result < 0)
                Marshal.ThrowExceptionForHR(result);
        }
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(
            ref Guid clsid,
            IntPtr outer,
            uint context,
            ref Guid iid,
            out IntPtr instance
        );
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(
                int flow,
                int role,
                [MarshalAs(UnmanagedType.Interface)] out object device
            );
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(
                ref Guid iid,
                uint context,
                IntPtr activation,
                [MarshalAs(UnmanagedType.IUnknown)] out object result
            );
        }
        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig]
            int Initialize(
                int mode,
                uint flags,
                long duration,
                long period,
                IntPtr format,
                IntPtr session
            );
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint frames);
            [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig]
            int GetBuffer(
                out IntPtr data,
                out uint frames,
                out uint flags,
                out ulong devicePosition,
                out ulong qpcPosition
            );
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
    }
}
