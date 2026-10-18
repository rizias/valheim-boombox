using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;

namespace BoomboxQa
{
    public static class QaListeners
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr table,
            ref int size,
            bool ordered,
            int family,
            int kind,
            uint reserved
        );
        public static bool OwnedLoopback(int pid, int port)
        {
            int size = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, 5, 0);
            if (result != 122)
                throw new Win32Exception((int)result);
            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(table, ref size, true, 2, 5, 0);
                if (result != 0)
                    throw new Win32Exception((int)result);
                int count = Marshal.ReadInt32(table);
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = IntPtr.Add(table, 4 + i * 24);
                    int localPort = unchecked((ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt32(row, 8)));
                    var address = new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row, 4)));
                    if (Marshal.ReadInt32(row) == 2 && Marshal.ReadInt32(row, 20) == pid && localPort == port
                    && address.Equals(IPAddress.Loopback))
                        return true;
                }
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }
        }
    }
}
