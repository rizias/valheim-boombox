using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BoomboxQa
{
    public sealed class QaJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Basic
        {
            public long ProcessTime, JobTime;
            public uint Flags;
            public UIntPtr MinWorkingSet, MaxWorkingSet;
            public uint ActiveLimit;
            public UIntPtr Affinity;
            public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Io
        {
            public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Extended
        {
            public Basic Limits;
            public Io Counters;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true
        )]
        private static extern IntPtr CreateJobObject(
            IntPtr attributes,
            string name
        );
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int kind,
            IntPtr information,
            uint bytes
        );
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(
            IntPtr job,
            IntPtr process
        );
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsProcessInJob(
            IntPtr process,
            IntPtr job,
            out bool result
        );
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateJobObject(
            IntPtr job,
            uint code
        );
        [StructLayout(LayoutKind.Sequential)]
        private struct Accounting
        {
            public long User, Kernel, PeriodUser, PeriodKernel;
            public uint Faults, Total, Active, Terminated;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryInformationJobObject(
            IntPtr job,
            int kind,
            out Accounting info,
            uint bytes,
            out uint returned
        );
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private IntPtr handle;
        public QaJob()
        {
            handle = CreateJobObject(IntPtr.Zero, "Local\\BoomboxQa-" + Guid.NewGuid().ToString("N"));
            if (handle == IntPtr.Zero)
                throw new Win32Exception();
            var limits = new Extended { Limits = new Basic { Flags = 0x2000 } };
            int bytes = Marshal.SizeOf<Extended>();
            IntPtr memory = Marshal.AllocHGlobal(bytes);
            try
            {
                Marshal.StructureToPtr(limits, memory, false);
                if (!SetInformationJobObject(handle, 9, memory, (uint)bytes))
                    throw new Win32Exception();
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }
        public void Add(Process process)
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception();
            if (!Contains(process))
                throw new InvalidOperationException("QA process did not enter owned job");
        }
        public bool Contains(Process process)
        {
            if (!IsProcessInJob(process.Handle, handle, out bool inside))
                throw new Win32Exception();
            return inside;
        }
        public void Stop()
        {
            if (handle != IntPtr.Zero && !TerminateJobObject(handle, 1))
                throw new Win32Exception();
        }
        public void WaitEmpty(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
            while (DateTime.UtcNow < deadline)
            {
                if (!QueryInformationJobObject(handle, 1, out var info, (uint)Marshal.SizeOf<Accounting>(), out _))
                    throw new Win32Exception();
                if (info.Active == 0)
                    return;
                Thread.Sleep(50);
            }
            throw new TimeoutException("QA job still has active owned processes");
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
            }
        }
    }
}
