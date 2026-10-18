using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VikingBoombox;

internal static class AudioOracleChecks
{
    private static readonly List<string> Checks = new();
    private static void Check(bool passed, string behavior)
    {
        if (!passed)
            throw new InvalidOperationException(behavior);
        Checks.Add(behavior);
    }

    private static MusicStrike Strike(double time, float strength = .5f) => new() { Time = time, Strength = strength };

    public static int Main(string[] args)
    {
        var stand = new[] {
            (Time: 1d, Strength: .5d, Loudest: Strike(1).Loudest.ToString()),
            (Time: 2d, Strength: .5d, Loudest: Strike(2).Loudest.ToString())
        };
        var paired = StrikeChecks.CompareStrikes(stand, new[] { Strike(1.01), Strike(2.019) });
        Check(paired.Passed && paired.Matched == 2, "Two corresponding events within 20 ms pass.");
        var duplicated = StrikeChecks.CompareStrikes(stand, new[] { Strike(1), Strike(1), Strike(2) });
        Check(!duplicated.Passed && duplicated.Matched == 2, "A duplicated mod event cannot reuse a stand event.");
        var missing = StrikeChecks.CompareStrikes(stand, new[] { Strike(1) });
        Check(!missing.Passed && missing.Matched == 1, "A missing mod event fails.");
        Check(
            !StrikeChecks.CompareStrikes(stand, new[] { Strike(1), Strike(2.021) }).Passed,
            "A 21 ms timing error fails."
        );
        var closeStand = new[] {
            (Time: 1d, Strength: .5d, Loudest: stand[0].Loudest),
            (Time: 1.01, Strength: .5d, Loudest: stand[0].Loudest)
        };
        Check(
            !StrikeChecks.CompareStrikes(closeStand, new[] { Strike(1.005) }).Passed,
            "One mod event cannot satisfy two nearby expected events."
        );
        var diagnostics = StrikeChecks.CompareStrikes(stand, new[] { Strike(1, .25f), Strike(2, .75f) });
        Check(diagnostics.Passed && diagnostics.WorstStrength == .25 && diagnostics.SameLoudest == 2,
            "Strength and band diagnostics use paired events without adding a strength acceptance threshold.");
        Check(!StrikeChecks.CompareStrikes(Array.Empty<(double, double, string)>(), Array.Empty<MusicStrike>()).Passed,
            "An empty expected strike list remains a failed oracle.");

        string root = args[0], run = Path.Combine(root, "run"), temp = Path.Combine(root, "temp");
        Directory.CreateDirectory(run);
        Directory.CreateDirectory(temp);
        string watched = Path.Combine(run, "already-there.bin");
        File.WriteAllText(watched, "before");
        var before = NativeMusicChecks.Written(run, temp);
        Check(NativeMusicChecks.Written(run, temp).SequenceEqual(before), "An unchanged watched tree passes.");
        DateTime oldTime = File.GetLastWriteTimeUtc(watched);
        File.WriteAllText(watched, "after!");
        File.SetLastWriteTimeUtc(watched, oldTime);
        Check(
            !NativeMusicChecks.Written(run, temp).SequenceEqual(before),
            "A same-path, same-length rewrite with restored mtime is detected by content hash."
        );
        var afterRewrite = NativeMusicChecks.Written(run, temp);
        File.SetLastWriteTimeUtc(watched, oldTime.AddSeconds(2));
        Check(
            !NativeMusicChecks.Written(run, temp).SequenceEqual(afterRewrite),
            "A write-time change with unchanged content is detected."
        );

        string owned = Path.Combine(temp, "ValheimBoombox-fixture", "nested");
        Directory.CreateDirectory(owned);
        string nested = Path.Combine(owned, "cache.bin");
        File.WriteAllText(nested, "first");
        var withNested = NativeMusicChecks.Written(run, temp);
        DateTime nestedTime = File.GetLastWriteTimeUtc(nested);
        File.WriteAllText(nested, "other");
        File.SetLastWriteTimeUtc(nested, nestedTime);
        Check(
            !NativeMusicChecks.Written(run, temp).SequenceEqual(withNested),
            "A nested existing file in an owned ValheimBoombox temp directory is fingerprinted."
        );
        var beforeUnrelated = NativeMusicChecks.Written(run, temp);
        string unrelated = Path.Combine(temp, "unrelated-application");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "private.bin"), "outside the watched roots");
        Check(
            NativeMusicChecks.Written(run, temp).SequenceEqual(beforeUnrelated),
            "Unrelated temp folders are not observed."
        );
        var beforeEmptyDirectory = NativeMusicChecks.Written(run, temp);
        Directory.CreateDirectory(Path.Combine(temp, "ValheimBoombox-empty"));
        Check(
            !NativeMusicChecks.Written(run, temp).SequenceEqual(beforeEmptyDirectory),
            "Creation of an empty owned temp directory is detected."
        );
        string result = JsonSerializer.Serialize(
            new
            {
                passed = true,
                checks = Checks,
                fixture = root
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        File.WriteAllText(Path.Combine(root, "result.json"), result);
        Console.WriteLine(result);
        return 0;
    }
}
