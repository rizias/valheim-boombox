using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

public sealed class OwnAudioSessionMute : IDisposable
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class Enumerator
    {
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevices
    {
        [PreserveSig] int Enumerate(int flow, uint state, out IntPtr devices);
        [PreserveSig] int Default(int flow, int role, out IDevice device);
        [PreserveSig] int Device([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
        [PreserveSig] int Register(IntPtr callback);
        [PreserveSig] int Unregister(IntPtr callback);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            int context,
            IntPtr parameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object result
        );
        [PreserveSig] int Store(int access, out IntPtr store);
        [PreserveSig] int Id(out IntPtr id);
        [PreserveSig] int State(out uint state);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IManager
    {
        [PreserveSig] int Control(ref Guid id, uint flags, out IntPtr control);
        [PreserveSig] int Volume(ref Guid id, uint flags, out IntPtr volume);
        [PreserveSig] int Sessions(out ISessions sessions);
        [PreserveSig] int Register(IntPtr callback);
        [PreserveSig] int Unregister(IntPtr callback);
        [PreserveSig] int Duck([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr callback);
        [PreserveSig] int Unduck(IntPtr callback);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessions
    {
        [PreserveSig] int Count(out int count);
        [PreserveSig] int Get(int index, out IControl control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IControl
    {
        [PreserveSig] int State(out int state);
        [PreserveSig] int Name(out IntPtr name);
        [PreserveSig] int SetName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int Icon(out IntPtr path);
        [PreserveSig] int SetIcon([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int Group(out Guid group);
        [PreserveSig] int SetGroup(ref Guid group, ref Guid context);
        [PreserveSig] int Register(IntPtr callback);
        [PreserveSig] int Unregister(IntPtr callback);
        [PreserveSig] int SessionId(out IntPtr id);
        [PreserveSig] int InstanceId(out IntPtr id);
        [PreserveSig] int ProcessId(out uint pid);
        [PreserveSig] int SystemSounds();
        [PreserveSig] int DuckPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVolume
    {
        [PreserveSig] int SetVolume(float volume, ref Guid context);
        [PreserveSig] int GetVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    private readonly List<(IVolume Volume, bool Mute, float Level)> changed = new List<(IVolume, bool, float)>();

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid clsid,
        IntPtr outer,
        uint context,
        ref Guid iid,
        out IntPtr instance
    );
    public OwnAudioSessionMute(bool muted = true, float? testVolume = null, bool? restoreMute = null)
    {
        Guid clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), iid = typeof(IDevices).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 23, ref iid, out IntPtr pointer));
        IDevices devices;
        try
        {
            devices = (IDevices)Marshal.GetObjectForIUnknown(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
        IDevice device = null;
        object managerObject = null;
        ISessions sessions = null;
        try
        {
            Marshal.ThrowExceptionForHR(devices.Default(0, 0, out device));
            var guid = typeof(IManager).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref guid, 23, IntPtr.Zero, out managerObject));
            Marshal.ThrowExceptionForHR(((IManager)managerObject).Sessions(out sessions));
            Marshal.ThrowExceptionForHR(sessions.Count(out int count));
            uint own = (uint)Process.GetCurrentProcess().Id;
            for (int index = 0; index < count; index++)
            {
                Marshal.ThrowExceptionForHR(sessions.Get(index, out var control));
                Marshal.ThrowExceptionForHR(control.ProcessId(out uint pid));
                if (pid != own)
                {
                    Marshal.ReleaseComObject(control);
                    continue;
                }
                var volume = (IVolume)control;
                Marshal.ThrowExceptionForHR(volume.GetMute(out bool previous));
                Marshal.ThrowExceptionForHR(volume.GetVolume(out float previousLevel));
                changed.Add((volume, restoreMute ?? previous, previousLevel));
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(volume.SetMute(muted, ref context));
                if (testVolume.HasValue)
                    Marshal.ThrowExceptionForHR(volume.SetVolume(testVolume.Value, ref context));
            }
            if (changed.Count == 0)
                throw new InvalidOperationException("Current test process audio session not found");
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (sessions != null)
                Marshal.ReleaseComObject(sessions);
            if (managerObject != null)
                Marshal.ReleaseComObject(managerObject);
            if (device != null)
                Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(devices);
        }
    }

    public void Dispose()
    {
        foreach (var item in changed)
        {
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(item.Volume.SetMute(item.Mute, ref context));
            Marshal.ThrowExceptionForHR(item.Volume.SetVolume(item.Level, ref context));
            Marshal.ReleaseComObject(item.Volume);
        }
        changed.Clear();
    }
}
