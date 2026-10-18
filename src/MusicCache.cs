using System;
using System.IO;

namespace VikingBoombox
{
    public static class MusicCache
    {
        private static readonly object gate = new object();

        public static long Clean(string root)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(Path.Combine(root, "native-v1")));
            lock (gate)
            {
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    return 0;
                return Remove(directory);
            }
        }

        private static long Remove(DirectoryInfo directory)
        {
            long removed = 0;
            foreach (var child in directory.GetDirectories())
                if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                    removed += Remove(child);
            foreach (var file in directory.GetFiles())
            {
                try
                {
                    long length = file.Length;
                    file.Delete();
                    removed += length;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try
            {
                if (directory.GetFileSystemInfos().Length == 0)
                    directory.Delete();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return removed;
        }
    }
}
