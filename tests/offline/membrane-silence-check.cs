using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

internal static class MembraneSilenceCheck
{
    public static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string folder in new[] { args[1], args[2] })
            {
                string path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(Path.GetFullPath(path));
            }
            return null;
        };
        var product = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[0]));
        var type = product.GetType("VikingBoombox.BoomboxMembraneMeter", true);
        object meter = Activator.CreateInstance(type, true);
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = (float[])type.GetField("window", privateInstance).GetValue(meter);
        for (int index = 0; index < window.Length; index++)
            window[index] = (float)Math.Sin(index * .01);
        type.GetMethod("MeasureWindow", privateInstance).Invoke(meter, new object[] { 1f, 48000, 1f });
        float signal = (float)type.GetProperty("Level").GetValue(meter);
        float travel = (float)type.GetProperty("Travel").GetValue(meter);
        type.GetMethod("MeasureGameMusic").Invoke(meter, new object[] { 2f, 48000 });
        float silence = (float)type.GetProperty("Level").GetValue(meter);
        float rest = (float)type.GetProperty("Travel").GetValue(meter);
        bool passed = signal > 0 && travel > 0 && silence == 0 && rest == 0;
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    passed,
                    productSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),
                    signal,
                    travel,
                    silence,
                    rest,
                    scope = "Actual candidate DLL; deterministic PCM followed by no tracked game sources. No native audio output acceptance."
                }
            )
        );
        return passed ? 0 : 1;
    }
}
