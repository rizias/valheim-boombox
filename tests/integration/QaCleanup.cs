using System;
using System.IO;
using System.Threading;

namespace BoomboxQa
{
    public static class QaCleanup
    {
        public static void Delete(string path)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (true)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (Exception error) when ((error is IOException || error is UnauthorizedAccessException)
                    && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
