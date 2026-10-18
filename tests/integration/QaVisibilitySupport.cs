using System.IO;
namespace BoomboxQa
{
    internal static class QaVisibilitySupport
    {
        internal static void Prepare(QaLedger ledger)
        {
            string gale = Path.Combine(QaEnvironment.FixturesRoot, "visibility/BepInEx");
            string profile = Path.Combine(ledger.Root, "profiles/client-a/BepInEx");
            foreach (var pair in new[] {
                ("plugins/shudnal-MyLittleUI/MyLittleUI.dll", "plugins/QA-MyLittleUI/MyLittleUI.dll"),
                ("config/shudnal.MyLittleUI.cfg", "config/shudnal.MyLittleUI.cfg"),
                ("plugins/shudnal-ConditionalConfigSync/ConditionalConfigSync.dll", "plugins/QA-MyLittleUI/ConditionalConfigSync.dll"),
                ("plugins/shudnal-ConditionalConfigSync/ConditionalConfigSync.Plugin.dll", "plugins/QA-MyLittleUI/ConditionalConfigSync.Plugin.dll")
            })
            {
                string target = Path.Combine(profile, pair.Item2);
                if (File.Exists(target))
                    throw new IOException("Visibility fixture already present; inspect before replacing");
                QaFiles.Copy(ledger, Path.Combine(gale, pair.Item1), target);
            }
        }
        internal static void Clear(QaLedger ledger)
        {
            foreach (string name in new[] {
                "plugins/QA-MyLittleUI/MyLittleUI.dll",
                "config/shudnal.MyLittleUI.cfg",
                "plugins/QA-MyLittleUI/ConditionalConfigSync.dll",
                "plugins/QA-MyLittleUI/ConditionalConfigSync.Plugin.dll"
            })
            {
                string path = ledger.Resolve(Path.Combine(ledger.Root, "profiles/client-a/BepInEx", name));
                if (File.Exists(path))
                    ledger.RemoveOwned(path);
            }
        }
    }
}
