using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BoomboxQa
{
    public static class QaCrackOriginChecks
    {
        public static void Run(QaLedger ledger)
        {
            string managed = QaEnvironment.Managed;
            AppDomain.CurrentDomain.AssemblyResolve += (_, a) =>
            {
                string p = Path.Combine(managed, new AssemblyName(a.Name).Name + ".dll");
                return File.Exists(p) ? Assembly.LoadFrom(p) : null;
            };
            string dll = Path.Combine(QaEnvironment.CandidateRoot, "ValheimBoombox.dll");
            var product = Assembly.LoadFrom(dll);
            var type = product.GetType("VikingBoombox.MusicCracks")!;
            var unity = Assembly.LoadFrom(Path.Combine(managed, "UnityEngine.CoreModule.dll"));
            var v2 = unity.GetType("UnityEngine.Vector2")!;
            var v3 = unity.GetType("UnityEngine.Vector3")!;
            var build = type.GetMethod("Build")!;
            var shift = type.GetMethod("Shift")!;
            int checks = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (float reach in new[] { .3f, 6f })
                for (int direction = 0; direction < 8; direction++)
                    for (int seed = 0; seed < 4; seed++)
                    {
                        double angle = direction * Math.PI / 4;
                        var aim = Activator.CreateInstance(
                            v2,
                            (float)Math.Cos(angle) * reach,
                            (float)Math.Sin(angle) * reach
                        )!;
                        var crack = build.Invoke(null, new object[] { aim, false, 1f, 1f, 0d, new Random(seed) })!;
                        var lines = (IList)crack.GetType().GetField("Lines")!.GetValue(crack)!;
                        var main = (IList)lines[0]!;
                        if (main.Count < 2)
                            throw new IOException("Crack path missing");
                        float Read(
                            object point,
                            string field
                        ) => (float)point.GetType().GetField(field)!.GetValue(point)!;
                        object first = main[0]!;
                        if (Read(first, "X") != 0f || Read(first, "Z") != 0f || Read(first, "Radial") != 0f)
                            throw new IOException("Crack starts on a ring instead of the event origin");
                        shift.Invoke(null, new[] { crack, Activator.CreateInstance(v3, 12f, 5f, -7f)! });
                        first = main[0]!;
                        if (Read(first, "X") != 12f || Read(first, "Z") != -7f)
                            throw new IOException("World origin not preserved");
                        checks++;
                    }
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R12/crack-origin.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        checks,
                        seconds = clock.Elapsed.TotalSeconds,
                        candidate = QaFiles.Hash(dll),
                        scope = "Actual DLL generated paths start at event center before and after world translation; not a rendered game proof"
                    }
                )
            );
            Console.WriteLine(
                "QA_CRACK_ORIGIN_PASS " + checks + " cases " + clock.Elapsed.TotalSeconds.ToString("0.000") + "s"
            );
        }
    }
}
