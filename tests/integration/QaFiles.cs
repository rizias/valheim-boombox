using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace BoomboxQa
{
    public static class QaInventory
    {
        public static IEnumerable<string> Files(string root)
        {
            if (!Directory.Exists(root))
                yield break;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked QA tree denied");
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    QaFileIdentity.AssertRegular(file);
                    yield return file;
                }
                foreach (string child in Directory.EnumerateDirectories(directory))
                    pending.Push(child);
            }
        }
    }
    public static class QaFileIdentity
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Info
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(
            SafeFileHandle handle,
            out Info info
        );
        public static void AssertRegular(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked QA file denied");
            using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            if (!GetFileInformationByHandle(file.SafeFileHandle, out var info))
                throw new IOException("QA file identity unavailable");
            if (info.Links != 1)
                throw new IOException("Hard-linked write target denied");
        }
    }
    public static class QaFiles
    {
        public static string ReadSharedText(string path)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        public static string Hash(string path)
        {
            using var input = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(input));
        }
        public static void Write(QaLedger ledger, string path, byte[] bytes)
        {
            path = ledger.Resolve(path);
            string partial = path + ".partial";
            if (File.Exists(path) && !ledger.State.Files.ContainsKey(path))
                throw new IOException("Unknown output target");
            ledger.ReserveFile(partial, bytes.LongLength);
            ledger.BeginReplace(partial, path, bytes);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes);
                    output.Flush(true);
                }
                ledger.CommitFile(partial);
                File.Move(partial, path, true);
                ledger.CompleteReplace();
            }
            catch
            {
                ledger.RecoveryRequired();
                throw;
            }
        }
        public static void Copy(
            QaLedger ledger,
            string source,
            string destination
        ) => Write(ledger, destination, File.ReadAllBytes(source));
    }
    public sealed class QaQuotaStream : Stream
    {
        private readonly QaLedger ledger;
        private readonly FileStream stream;
        private readonly string path, owner;
        private readonly long maximum;
        public QaQuotaStream(QaLedger value, string target, string role, long maximumBytes)
        {
            ledger = value;
            path = ledger.Resolve(target);
            owner = role;
            maximum = maximumBytes;
            if (File.Exists(path) && !ledger.State.Files.ContainsKey(path))
                throw new IOException("Unowned stream file");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ledger.AccountAppend(path, 0, owner);
            stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (stream.Length + count > maximum)
                throw new IOException("QA stream byte limit exceeded");
            ledger.AccountAppend(path, count, owner);
            try
            {
                stream.Write(buffer, offset, count);
            }
            catch
            {
                ledger.RecoveryRequired();
                throw;
            }
        }
        public override void Flush() => stream.Flush(true);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stream.Flush(true);
                stream.Dispose();
            }
            base.Dispose(disposing);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => stream.Length;
        public override long Position
        {
            get => stream.Position;
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
