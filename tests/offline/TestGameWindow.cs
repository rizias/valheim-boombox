using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class TestGameWindow
{
    private static string Executable
    {
        get
        {
            string root = Environment.GetEnvironmentVariable("VALHEIM_QA_CLIENT");
            if (string.IsNullOrWhiteSpace(root) || !System.IO.Path.IsPathRooted(root))
                throw new InvalidOperationException("Set VALHEIM_QA_CLIENT explicitly");
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "valheim.exe"));
        }
    }
    private delegate bool Visitor(IntPtr window, IntPtr state);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left, top, right, bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x, y;
    }
    [DllImport("user32.dll")] private static extern bool EnumWindows(Visitor visitor, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr window,
        StringBuilder name,
        int count
    );
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint current, uint target, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint x,
        uint y,
        uint data,
        UIntPtr extra
    );
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public sealed class Area
    {
        public int X, Y, Width, Height;
        public bool Focused;
    }

    private static IntPtr Find(int pid)
    {
        using (var process = Process.GetProcessById(pid))
        {
            if (Environment.GetEnvironmentVariable("VALHEIM_QA_PROCESS_ID") != pid.ToString()
                ||
            Environment.GetEnvironmentVariable("VALHEIM_QA_PROCESS_START_TICKS")
                != process.StartTime.ToUniversalTime().Ticks.ToString())
                throw new InvalidOperationException(
                    "Explicit QA PID and start time are required; executable path alone cannot identify a test instance"
                );
            if (!String.Equals(process.MainModule.FileName, Executable, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only the isolated test executable is allowed");
        }
        IntPtr found = IntPtr.Zero;
        EnumWindows(
        (window, state) =>
            {
                GetWindowThreadProcessId(window, out uint owner);
                if (owner != pid)
                    return true;
                var name = new StringBuilder(128);
                GetClassName(window, name, 128);
                if (name.ToString() != "UnityWndClass")
                    return true;
                found = window;
                return false;
            },
        IntPtr.Zero
        );
        if (found == IntPtr.Zero)
            throw new InvalidOperationException("Test game window not found");
        return found;
    }
    public static Area Inspect(int pid)
    {
        var window = Find(pid);
        var old = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            if (!GetClientRect(window, out var rect))
                throw new InvalidOperationException("No client area");
            var point = new Point();
            if (!ClientToScreen(window, ref point))
                throw new InvalidOperationException("No client position");
            return new Area
            {
                X = point.x,
                Y = point.y,
                Width = rect.right - rect.left,
                Height = rect.bottom - rect.top,
                Focused = GetForegroundWindow() == window
            };
        }
        finally
        {
            SetThreadDpiAwarenessContext(old);
        }
    }
    public static void Focus(int pid)
    {
        if (Process.GetProcessesByName("dota2").Length != 0)
            throw new InvalidOperationException("Dota is running");
        var window = Find(pid);
        ShowWindow(window, 9);
        SetForegroundWindow(window);
        if (GetForegroundWindow() != window)
        {
            uint thread = GetWindowThreadProcessId(GetForegroundWindow(), out _), current = GetCurrentThreadId();
            bool attached = thread != current && AttachThreadInput(current, thread, true);
            try
            {
                BringWindowToTop(window);
                SetForegroundWindow(window);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(current, thread, false);
            }
        }
    }
    public static void Click(int pid, int x, int y)
    {
        var area = Inspect(pid);
        if (!area.Focused || x < 0 || y < 0 || x >= area.Width || y >= area.Height)
            throw new InvalidOperationException("Test window must be focused and click must be inside it");
        var old = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            SetCursorPos(area.X + x, area.Y + y);
            mouse_event(2, 0, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(80);
            mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        }
        finally
        {
            SetThreadDpiAwarenessContext(old);
        }
    }
    public static void Key(int pid, byte key)
    {
        if (!Inspect(pid).Focused)
            throw new InvalidOperationException("Test window is not focused");
        keybd_event(key, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(250);
        keybd_event(key, 0, 2, UIntPtr.Zero);
    }
}
