using System;
using System.IO;

namespace BoomboxQa
{
    public static class QaHistory
    {
        public static string Root(QaLedger ledger) => Path.Combine(ledger.Root, "history");
        public static void Copy(QaLedger ledger, string source, string target)
        {
            source = Path.GetFullPath(source);
            target = Path.GetFullPath(target);
            if (!target.StartsWith(
                Path.GetFullPath(Root(ledger)) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            ))
                throw new IOException("History path escaped");
            if (File.Exists(target))
            {
                if (QaFiles.Hash(source) != QaFiles.Hash(target))
                    throw new IOException("History exists with different content");
                return;
            }
            if (new FileInfo(source).Length > 64 * QaLedger.MiB)
                throw new IOException("Unexpected large history input");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            QaFiles.Copy(ledger, source, target);
            if (QaFiles.Hash(source) != QaFiles.Hash(target))
                throw new IOException("History copy hash mismatch");
        }
    }
}
