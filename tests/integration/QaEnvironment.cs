using System;
using System.IO;

namespace BoomboxQa
{
    public static class QaEnvironment
    {
        private static string Required(string name, string marker)
        {
            string value = Environment.GetEnvironmentVariable(name) ?? "";
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                throw new IOException("Set " + name
                    + " to an absolute existing path; see QA.md. No legacy stand fallback.");
            string path = Path.GetFullPath(value);
            if (!File.Exists(Path.Combine(path, marker)))
                throw new IOException(name + " is missing " + marker);
            return path;
        }
        public static string ClientRoot => Required("VALHEIM_QA_CLIENT", "valheim.exe");
        public static string ServerRoot => Required("VALHEIM_QA_SERVER", "valheim_server.exe");
        public static string Core => Required("VALHEIM_BEPINEX_CORE", "BepInEx.dll");
        private static string RequiredDirectory(string name)
        {
            string value = Environment.GetEnvironmentVariable(name) ?? "";
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value) || !Directory.Exists(value))
                throw new IOException("Set " + name + " to an absolute existing directory; no implicit fallback.");
            return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
        }
        public static string SourceRoot(string project)
        {
            string root = Path.GetFullPath(project);
            if (!File.Exists(Path.Combine(root, "src/BoomboxPlugin.cs"))
            || !File.Exists(Path.Combine(root, "tests/integration/QaController.cs")))
                throw new IOException(
                    "Project source markers missing: src/BoomboxPlugin.cs and tests/integration/QaController.cs"
                );
            string runtime = QaRoot;
            if (runtime.Equals(root, StringComparison.OrdinalIgnoreCase)
            || runtime.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("VALHEIM_QA_ROOT must be outside the source repository.");
            return Path.Combine(root, "tests/integration");
        }
        public static string QaRoot => RequiredDirectory("VALHEIM_QA_ROOT");
        public static string BinRoot => Path.Combine(QaRoot, "bin");
        public static string CandidateRoot => Path.Combine(QaRoot, "stage");
        public static string FixturesRoot => Path.Combine(QaRoot, "fixtures");
        public static string FFmpegRoot => Required("VALHEIM_FFMPEG_ROOT", "bin/ffmpeg.exe");
        public static string Encoder => Path.Combine(FFmpegRoot, "bin/ffmpeg.exe");
        public static string MediaProbe => Path.Combine(Path.GetDirectoryName(Encoder)!, "ffprobe.exe");
        public static string Managed => Path.Combine(ClientRoot, "valheim_Data", "Managed");
        public static string ClientExe => Path.Combine(ClientRoot, "valheim.exe");
        public static string ServerExe => Path.Combine(ServerRoot, "valheim_server.exe");
        public static string RuntimeRoot(string role) => role == "server" ? ServerRoot : ClientRoot;
        public static string Compiler => Environment.GetEnvironmentVariable("VALHEIM_QA_COMPILER")
            ?? throw new IOException("Use qa.ps1 to select installed .NET tools.");
        public static string References => Environment.GetEnvironmentVariable("VALHEIM_QA_REFERENCES")
            ?? throw new IOException("Use qa.ps1 to select installed .NET tools.");
        public static string PowerShell => Environment.GetEnvironmentVariable("VALHEIM_QA_POWERSHELL")
            ?? throw new IOException("Use qa.ps1 from PowerShell 7.");
        public static bool IsGameExecutable(string executable)
        {
            foreach (var pair in new[] {
                ("VALHEIM_QA_CLIENT", "valheim.exe"),
                ("VALHEIM_QA_SERVER", "valheim_server.exe")
            })
            {
                string root = Environment.GetEnvironmentVariable(pair.Item1) ?? "";
                if (!string.IsNullOrWhiteSpace(root) && Path.IsPathRooted(root) &&
                string.Equals(
                    Path.GetFullPath(executable),
                    Path.GetFullPath(Path.Combine(root, pair.Item2)),
                    StringComparison.OrdinalIgnoreCase
                ))
                    return true;
            }
            return false;
        }
        public static void ValidateGameLaunch(string project, string executable, string[] arguments)
        {
            if (!IsGameExecutable(executable))
                throw new IOException("Not the explicitly configured game executable");
            string Value(string key)
            {
                int count = 0;
                foreach (string arg in arguments)
                    if (arg == key)
                        count++;
                if (count != 1)
                    throw new IOException("Required unique QA argument: " + key);
                int index = Array.IndexOf(arguments, key);
                return index + 1 < arguments.Length ? arguments[index + 1] : "";
            }
            string save = Value("-savedir"), preloader = Value("--doorstop-target-assembly");
            bool profile = false;
            foreach (string role in new[] { "client-a", "client-b", "server" })
            {
                string root = Path.GetFullPath(Path.Combine(QaEnvironment.QaRoot, "profiles", role));
                if (string.Equals(save, Path.Combine(root, "saves"), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    preloader,
                    Path.Combine(root, "BepInEx/core/BepInEx.Preloader.dll"),
                    StringComparison.OrdinalIgnoreCase
                ))
                    profile = true;
            }
            if (!profile || Value("--doorstop-enabled") != "true" || Value("-logFile") != "-")
                throw new IOException(
                    "Game launch requires matching isolated QA saves/preloader and redirected log; personal profile refused"
                );
        }
    }
}
