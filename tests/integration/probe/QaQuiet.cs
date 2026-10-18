using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

internal sealed class QaQuiet : IDisposable
{
    private OwnAudioSessionMute audio;
    private float next;
    public bool Muted => audio != null;
    public bool Offscreen
    {
        get;
        private set;
    }
    private delegate bool WindowVisitor(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr after,
        int x,
        int y,
        int width,
        int height,
        uint flags
    );
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out WindowRect rect);
    public void Tick()
    {
        if (Time.realtimeSinceStartup < next)
            return;
        next = Time.realtimeSinceStartup + .25f;
        if (audio == null)
        {
            try
            {
                audio = new OwnAudioSessionMute(
                    true,
                    null,
                    Environment.GetEnvironmentVariable("BOOMBOX_QA_RESTORE_AUDIO_UNMUTED") == "1"
                        ? false
                        : (bool?)null
                );
            }
            catch (InvalidOperationException) { }
            catch (COMException) { }
        }
        if (Environment.GetEnvironmentVariable("BOOMBOX_QA_WINDOW_CAPTURE") == "1")
        {
            Offscreen = false;
            return;
        }
        uint own = (uint)Process.GetCurrentProcess().Id;
        Offscreen = false;
        IntPtr gameWindow = IntPtr.Zero;
        long largest = 0;
        EnumWindows(
        (window, state) =>
            {
                GetWindowThreadProcessId(window, out uint pid);
                if (pid == own && GetClientRect(window, out var rect))
                {
                    long area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                    if (area > largest)
                    {
                        largest = area;
                        gameWindow = window;
                    }
                }
                return true;
            },
        IntPtr.Zero
        );
        if (gameWindow != IntPtr.Zero
            && SetWindowPos(
            gameWindow,
            IntPtr.Zero,
            -32000,
            -32000,
            0,
            0,
            0x0001 | 0x0004 | 0x0010
        ))
            Offscreen = GetWindowRect(gameWindow, out var actual) && actual.Right < 0 && actual.Bottom < 0;
    }
    public void Dispose()
    {
        audio?.Dispose();
        audio = null;
    }
}
