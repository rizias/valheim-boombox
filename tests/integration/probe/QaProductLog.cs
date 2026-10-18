using System;
using BepInEx.Logging;

public sealed class QaProductLog : ILogListener
{
    public void LogEvent(object sender, LogEventArgs e)
    {
        if (e.Source.SourceName == "Valheim Boombox")
            System.Console.WriteLine("QA_PRODUCT_LOG " + e.Level + " " + e.Data);
    }
    public void Dispose()
    {
    }
}
