using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace BoomboxQa
{
    public sealed class QaFile
    {
        public string Path = "";
        public long Bytes;
        public string Category = "runtime";
        public string Owner = "controller";
    }
    public sealed class QaReservation
    {
        public string Path = "";
        public long Maximum;
        public string Category = "runtime";
    }
    public sealed class QaNativeReservation
    {
        public string Id = "";
        public string Path = "";
        public string Role = "";
        public long PriorBytes;
        public long MaximumLength;
        public long ExpectedLength = -1;
        public string Category = "runtime";
        public string Source = "";
        public long Growth => Math.Max(0, MaximumLength - PriorBytes);
    }
    public sealed class QaState
    {
        public int Version = 1;
        public string Owner = "boombox-rework-qa-v1";
        public string Phase = "ready";
        public long Generation;
        public Dictionary<string, QaFile> Files = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, QaReservation> Reservations = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, QaNativeReservation> NativeReservations = new(StringComparer.OrdinalIgnoreCase);
        public List<QaConfigSnapshot> PendingConfigs = new();
        public QaReplace? Replace;
        public Dictionary<string, bool> External = new(StringComparer.OrdinalIgnoreCase);
    }
    public sealed class QaReplace
    {
        public string Partial = "", Target = "", Sha256 = "";
        public long Bytes;
    }
    public sealed partial class QaLedger : IDisposable
    {
        public const long MiB = 1024 * 1024, GiB = 1024 * MiB;
        private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
        private readonly object gate = new();
        public readonly string Project, Root, StatePath;
        public readonly QaState State;
        private readonly FileStream lease;
        public long TotalCap = 6 * GiB, RunCap = 256 * MiB, Reserve = 4 * GiB, RestoreMaximum = 64 * MiB;
        public long RuntimeCap = 3 * GiB, EvidenceCap = 1536 * MiB, StageCap = 1536 * MiB;
        private long runWritten;
        public bool RegistryOwned;
        public QaLedger(string project)
        {
            Project = Path.GetFullPath(project);
            _ = QaEnvironment.SourceRoot(Project);
            Root = QaEnvironment.QaRoot;
            StatePath = Path.Combine(Root, "control.json");
            Directory.CreateDirectory(Root);
            string lockPath = Path.Combine(Root, "control.lock");
            if (File.Exists(lockPath))
                QaFileIdentity.AssertRegular(lockPath);
            lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            State = File.Exists(StatePath)
                ? JsonSerializer.Deserialize<QaState>(File.ReadAllText(StatePath), Json)!
                : new QaState();
            State.NativeReservations ??= new Dictionary<string, QaNativeReservation>(StringComparer.OrdinalIgnoreCase);
            State.PendingConfigs ??= new List<QaConfigSnapshot>();
            if (State.Owner != "boombox-rework-qa-v1" || State.Version != 1)
                throw new IOException("Unowned QA state");
            State.Files[lockPath] = new QaFile { Path = lockPath, Owner = "controller-lock", Bytes = 0 };
            Save();
        }
        public string Resolve(string path, bool checkIdentity = true)
        {
            string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Project, path));
            string stage = QaEnvironment.CandidateRoot;
            if (!Inside(full, Root) && !Inside(full, stage) && !State.External.ContainsKey(full))
                throw new IOException("QA write outside allowed roots: " + full);
            string? parent = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            while (parent != null)
            {
                if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse directory rejected");
                if (parent.Equals(Root, StringComparison.OrdinalIgnoreCase))
                    break;
                parent = Path.GetDirectoryName(parent);
            }
            if (File.Exists(full) && checkIdentity)
                QaFileIdentity.AssertRegular(full);
            return full;
        }
        private static bool Inside(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        public string Category(string path) => Inside(path, QaEnvironment.CandidateRoot)
            ? "stage"
            : (Inside(path, Path.Combine(Root, "evidence")) || Inside(path, Path.Combine(Root, "history")))
                ? "evidence"
                : "runtime";
        public void AllowServerLoader(string path)
        {
            lock (gate)
            {
                path = Path.GetFullPath(path);
                string server = QaEnvironment.ServerRoot;
                if (path != Path.Combine(server, "winhttp.dll") && path != Path.Combine(server, "doorstop_config.ini"))
                    throw new IOException("Only exact server bootstrap files permitted");
                if (!State.External.ContainsKey(path))
                {
                    if (File.Exists(path))
                        throw new IOException("Existing server loader needs explicit baseline preservation");
                    State.External[path] = true;
                    State.External[path + ".partial"] = true;
                    Save();
                }
            }
        }
        public void RestoreServerLoader()
        {
            lock (gate)
            {
                foreach (string path in State.External.Keys.ToArray())
                {
                    if (State.Files.ContainsKey(path) || State.Reservations.ContainsKey(path))
                        RemoveOwned(path);
                    else if (File.Exists(path))
                        throw new IOException("Unowned server bootstrap file");
                }
                State.External.Clear();
                Save();
            }
        }
        public void Preflight(long maximumGrowth, string category = "runtime")
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                if (State.Phase == "recovery-required" || State.Reservations.Count != 0 || State.Replace != null)
                    throw new IOException("QA recovery required before new work");
                if (!RegistryOwned && File.Exists(Path.Combine(Root, "baseline/registry-active.json")))
                    throw new IOException("Preference transaction requires recovery");
                if (category != "runtime" && category != "stage" && category != "evidence")
                    throw new ArgumentException("Unknown quota category", nameof(category));
                ValidateBudget(null, maximumGrowth, category);
                FreeCheck(maximumGrowth);
            }
        }
        public void ValidateRunningBudget(long growth)
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                if (State.Phase == "recovery-required"
                || (!RegistryOwned && File.Exists(Path.Combine(Root, "baseline/registry-active.json"))))
                    throw new IOException("QA recovery required");
                ValidateBudget(null, growth, "runtime");
                FreeCheck(growth);
            }
        }
        private void FreeCheck(long growth)
        {
            long free = new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace;
            if (free < Reserve + RestoreMaximum + Math.Max(0, growth))
                throw new IOException("Insufficient reserve plus restore headroom");
        }
        private void ValidateBudget(string? replacing, long growth, string category)
        {
            long files = State.Files.Values.Sum(f => f.Bytes);
            long configRestore = State.PendingConfigs.Sum(p => p.Bytes);
            long reserved = State.Reservations.Where(p => p.Key != replacing).Sum(p => p.Value.Maximum);
            reserved += State.NativeReservations.Values.Sum(p => p.Growth);
            long owned = State.Files.Values.Where(f => f.Category == category).Sum(f => f.Bytes);
            long categoryReserved = State.Reservations.Where(p => p.Key != replacing
                && p.Value.Category == category).Sum(p => p.Value.Maximum);
            categoryReserved += State.NativeReservations.Values.Where(p => p.Category == category).Sum(p => p.Growth);
            long cap = category == "stage"
                ? StageCap
                : category == "evidence"
                    ? EvidenceCap
                    : RuntimeCap;
            long runReserved = State.Reservations.Where(p => p.Key != replacing).Sum(p => p.Value.Maximum);
            runReserved += State.NativeReservations.Values.Sum(p => p.Growth);
            if (growth < 0 || files + reserved + growth + configRestore > TotalCap
            || owned + categoryReserved + growth + (category == "runtime" ? configRestore : 0) > cap
            || runWritten + runReserved + growth > RunCap)
                throw new IOException("QA byte quota exceeded");
        }
        private void AssertNativePath(string role, string path)
        {
            string profile = Path.Combine(Root, "profiles", role), temp = Path.Combine(Root, "current", role, "temp");
            if (role != "server" && role != "client-a" && role != "client-b")
                throw new IOException("Unknown native role");
            if (!Inside(path, profile) && !Inside(path, temp))
                throw new IOException("Native write outside role-owned paths");
        }
        public void NativeReserve(string role, string id, string path, long maximumLength)
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                path = Resolve(path, false);
                AssertNativePath(role, path);
                if (maximumLength < 0 || State.Phase == "recovery-required")
                    throw new IOException("Invalid native reservation");
                if (State.NativeReservations.ContainsKey(id))
                    throw new IOException("Native reservation already exists");
                if (File.Exists(path) && !State.Files.ContainsKey(path))
                    throw new IOException("Unowned native overwrite");
                long prior = State.Files.TryGetValue(path, out var known) ? known.Bytes : 0;
                var reservation = new QaNativeReservation
                {
                    Id = id,
                    Path = path,
                    Role = role,
                    PriorBytes = prior,
                    MaximumLength = maximumLength,
                    Category = "runtime"
                };
                ValidateBudget(null, reservation.Growth, reservation.Category);
                FreeCheck(reservation.Growth);
                State.NativeReservations[id] = reservation;
                Save();
            }
        }
        public void NativeMoveReserve(string role, string id, string source, string target)
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                source = Resolve(source);
                target = Resolve(target);
                AssertNativePath(role, source);
                AssertNativePath(role, target);
                NativeCheck(role, source);
                if (State.Phase == "recovery-required" || !File.Exists(source) || File.Exists(target)
                || State.NativeReservations.ContainsKey(id))
                    throw new IOException("Invalid owned native move");
                long bytes = new FileInfo(source).Length;
                var reservation = new QaNativeReservation
                {
                    Id = id,
                    Path = target,
                    Source = source,
                    Role = role,
                    PriorBytes = 0,
                    MaximumLength = bytes,
                    ExpectedLength = bytes,
                    Category = "runtime"
                };
                ValidateBudget(null, reservation.Growth, reservation.Category);
                FreeCheck(reservation.Growth);
                State.NativeReservations[id] = reservation;
                Save();
            }
        }
        public void NativeCommit(string role, string id, string path, long actualLength)
        {
            lock (gate)
            {
                path = Resolve(path, false);
                AssertNativePath(role, path);
                if (!State.NativeReservations.TryGetValue(id, out var reservation)
                || !string.Equals(reservation.Path, path, StringComparison.OrdinalIgnoreCase)
                || reservation.Role != role)
                    throw new IOException("Unknown native reservation");
                if (actualLength < 0 || actualLength > reservation.MaximumLength)
                    throw new IOException("Native write exceeded reservation");
                long prior = State.Files.TryGetValue(path, out var known) ? known.Bytes : reservation.PriorBytes;
                if (File.Exists(path))
                    State.Files[path] = new QaFile
                    {
                        Path = path,
                        Bytes = actualLength,
                        Category = reservation.Category,
                        Owner = role
                    };
                else
                    State.Files.Remove(path);
                if (reservation.Source.Length > 0 && !File.Exists(reservation.Source))
                    State.Files.Remove(reservation.Source);
                State.NativeReservations.Remove(id);
                runWritten += Math.Max(0, actualLength - prior);
                Save();
            }
        }
        public void NativeMoveFinish(string role, string id, string source, string target, bool succeeded)
        {
            lock (gate)
            {
                source = Resolve(source);
                target = Resolve(target);
                if (!State.NativeReservations.TryGetValue(id, out var reservation) || reservation.Role != role
                || reservation.Source != source
                || reservation.Path != target
                || reservation.ExpectedLength < 0)
                    throw new IOException("Unknown native move transaction");
                if (succeeded
                    && (File.Exists(source)
                        || !File.Exists(target) || new FileInfo(target).Length != reservation.ExpectedLength))
                {
                    RecoveryRequired();
                    throw new IOException("Native move did not reach its complete post-state");
                }
                RecoverNativeReservation(reservation);
            }
        }
        public void NativeReplaceReserve(string role, string id, string source, string target, string? backup)
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                source = Resolve(source);
                target = Resolve(target);
                NativeCheck(role, source);
                NativeCheck(role, target);
                AssertNativePath(role, source);
                AssertNativePath(role, target);
                if (!File.Exists(source)
                    || !File.Exists(target)
                    || source.Equals(target, StringComparison.OrdinalIgnoreCase)
                    || State.Phase == "recovery-required")
                    throw new IOException("Invalid owned native replacement");
                if (backup != null)
                {
                    backup = Resolve(backup);
                    AssertNativePath(role, backup);
                    NativeCheck(role, backup);
                    if (!backup.Equals(target + ".bak", StringComparison.OrdinalIgnoreCase)
                    || backup.Equals(source, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Backup must be the owned target .bak");
                }
                string targetId = id + "-target", backupId = id + "-backup";
                if (State.NativeReservations.ContainsKey(targetId) || State.NativeReservations.ContainsKey(backupId))
                    throw new IOException("Replacement reservation already exists");
                long bytes = new FileInfo(source).Length, prior = new FileInfo(target).Length;
                var destination = new QaNativeReservation
                {
                    Id = targetId,
                    Path = target,
                    Role = role,
                    PriorBytes = prior,
                    MaximumLength = Math.Max(bytes, prior),
                    ExpectedLength = bytes,
                    Source = source
                };
                var retained = backup == null
                    ? null
                    : new QaNativeReservation
                    {
                        Id = backupId,
                        Path = backup,
                        Role = role,
                        PriorBytes = 0,
                        MaximumLength = Math.Max(
                        prior,
                        File.Exists(backup)
                                ? new FileInfo(backup).Length
                                : 0
                    ),
                        ExpectedLength = prior
                    };
                long growth = destination.Growth + (retained?.Growth ?? 0);
                ValidateBudget(null, growth, "runtime");
                FreeCheck(growth);
                State.NativeReservations[targetId] = destination;
                if (retained != null)
                    State.NativeReservations[backupId] = retained;
                Save();
            }
        }
        public void NativeReplaceCommit(string role, string id, string source, string target, string? backup)
        {
            lock (gate)
            {
                source = Resolve(source);
                target = Resolve(target);
                if (File.Exists(source) || !State.NativeReservations.TryGetValue(id + "-target", out var destination)
                || destination.Source != source
                || destination.Path != target
                || destination.Role != role
                || destination.ExpectedLength < 0
                || !File.Exists(target)
                || new FileInfo(target).Length != destination.ExpectedLength)
                    throw new IOException("Native replacement did not complete atomically");
                if (backup != null)
                {
                    backup = Resolve(backup);
                    if (!State.NativeReservations.TryGetValue(
                        id + "-backup",
                        out var retained
                    )
                        || retained.Path != backup
                        || retained.Role != role
                        || retained.ExpectedLength < 0
                        || !File.Exists(backup)
                        || new FileInfo(backup).Length != retained.ExpectedLength)
                        throw new IOException("Native replacement backup differs from reservation");
                    NativeCommit(role, id + "-backup", backup, new FileInfo(backup).Length);
                }
                NativeCommit(role, id + "-target", target, new FileInfo(target).Length);
            }
        }
        public void ReserveFile(string path, long maximum)
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                path = Resolve(path);
                if (State.Phase == "recovery-required")
                    throw new IOException("QA recovery required");
                if (File.Exists(path) && !State.Files.ContainsKey(path))
                    throw new IOException("Unknown existing file cannot be replaced");
                if (State.Reservations.ContainsKey(path))
                    throw new IOException("File already reserved");
                ValidateBudget(path, maximum, Category(path));
                FreeCheck(maximum);
                State.Reservations[path] = new QaReservation
                {
                    Path = path,
                    Maximum = maximum,
                    Category = Category(path)
                };
                Save();
            }
        }
        public void CommitFile(string path, string owner = "controller")
        {
            lock (gate)
            {
                path = Resolve(path);
                var reservation = State.Reservations[path];
                long bytes = new FileInfo(path).Length;
                if (bytes > reservation.Maximum)
                    throw new IOException("Actual file exceeds reserved bytes");
                long prior = State.Files.TryGetValue(path, out var file) ? file.Bytes : 0;
                State.Files[path] = new QaFile
                {
                    Path = path,
                    Bytes = bytes,
                    Category = reservation.Category,
                    Owner = owner
                };
                State.Reservations.Remove(path);
                runWritten += Math.Max(0, bytes - prior);
                Save();
            }
        }
        public void AccountAppend(string path, int bytes, string owner)
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                path = Resolve(path);
                if (State.Phase == "recovery-required")
                    throw new IOException("QA recovery required");
                if (!State.Files.TryGetValue(path, out var file))
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 0)
                        throw new IOException("Unknown append target");
                    file = new QaFile { Path = path, Category = Category(path), Owner = owner };
                    State.Files[path] = file;
                }
                ValidateBudget(null, bytes, file.Category);
                FreeCheck(bytes);
                file.Bytes += bytes;
                runWritten += bytes;
                Save();
            }
        }
        public void NativeWrite(string role, string path, long length)
        {
            lock (gate)
            {
                RequireConfigRollbackOwner();
                path = Resolve(path, false);
                if (length < 0 || State.Phase == "recovery-required")
                    throw new IOException("Invalid native write state");
                if (File.Exists(path) && !State.Files.ContainsKey(path))
                    throw new IOException("Unowned native overwrite");
                AssertNativePath(role, path);
                long prior = State.Files.TryGetValue(path, out var known) ? known.Bytes : 0;
                long growth = Math.Max(0, length - prior);
                ValidateBudget(null, growth, "runtime");
                FreeCheck(growth);
                State.Files[path] = new QaFile { Path = path, Bytes = length, Category = "runtime", Owner = role };
                runWritten += growth;
                Save();
            }
        }
        public void NativeCheck(string role, string path)
        {
            lock (gate)
            {
                path = Resolve(path);
                if (File.Exists(path) && (!State.Files.TryGetValue(path, out var known) || known.Owner != role))
                    throw new IOException("Unowned native mutation");
            }
        }
        public void NativeRemoved(string role, string path)
        {
            lock (gate)
            {
                NativeCheck(role, path);
                if (File.Exists(path))
                    throw new IOException("Native mutation did not remove source");
                State.Files.Remove(Resolve(path));
                Save();
            }
        }
        public void AdoptExisting(string path, string owner)
        {
            lock (gate)
            {
                path = Resolve(path);
                var info = new FileInfo(path);
                if (State.Files.TryGetValue(path, out var existing))
                {
                    if (existing.Owner != owner)
                        throw new IOException("File ownership mismatch");
                    ValidateBudget(null, Math.Max(0, info.Length - existing.Bytes), existing.Category);
                    existing.Bytes = info.Length;
                    Save();
                    return;
                }
                ValidateBudget(null, info.Length, Category(path));
                State.Files[path] = new QaFile
                {
                    Path = path,
                    Bytes = info.Length,
                    Category = Category(path),
                    Owner = owner
                };
                Save();
            }
        }
        public void RemoveOwned(string path)
        {
            lock (gate)
            {
                path = Resolve(path, false);
                if (!State.Files.ContainsKey(path) && !State.Reservations.ContainsKey(path))
                    throw new IOException("Unowned cleanup denied");
                if (File.Exists(path))
                    QaCleanup.Delete(path);
                State.Files.Remove(path);
                State.Reservations.Remove(path);
                Save();
            }
        }

        public void RenameOwned(string source, string target)
        {
            lock (gate)
            {
                source = Resolve(source);
                target = Resolve(target);
                if (Path.GetDirectoryName(source) != Path.GetDirectoryName(target)
                    || !State.Files.TryGetValue(source, out var entry)
                    || State.Reservations.Count != 0
                    || File.Exists(target))
                    throw new IOException("Owned same-directory rename precondition failed");
                string before = QaFiles.Hash(source);
                File.Move(source, target);
                State.Files.Remove(source);
                entry.Path = target;
                State.Files[target] = entry;
                Save();
                if (QaFiles.Hash(target) != before)
                    throw new IOException("Renamed archive content differs");
            }
        }

        public void RenameOwnedTree(string source, string target)
        {
            lock (gate)
            {
                source = Resolve(source);
                target = Resolve(target);
                if (Path.GetDirectoryName(source) != Path.GetDirectoryName(target)
                    || Directory.Exists(target)
                    || File.Exists(target)
                    || State.Reservations.Count != 0)
                    throw new IOException("Owned sibling-folder rename precondition failed");
                var files = QaInventory.Files(source).ToArray();
                if (files.Any(p => !State.Files.ContainsKey(p)))
                    throw new IOException("Unowned file in folder rename");
                var targets = files.ToDictionary(
                    p => p,
                    p => Resolve(Path.Combine(target, Path.GetRelativePath(source, p)))
                );
                Directory.Move(source, target);
                foreach (var pair in targets)
                {
                    var entry = State.Files[pair.Key];
                    State.Files.Remove(pair.Key);
                    entry.Path = pair.Value;
                    State.Files[pair.Value] = entry;
                }
                Save();
                Reconcile(target);
            }
        }
        public void RecoveryRequired()
        {
            lock (gate)
            {
                State.Phase = "recovery-required";
                Save();
            }
        }
        public void BeginReplace(string partial, string target, byte[] bytes)
        {
            lock (gate)
            {
                if (State.Replace != null)
                    throw new IOException("Replacement already active");
                State.Replace = new QaReplace
                {
                    Partial = Resolve(partial),
                    Target = Resolve(target),
                    Bytes = bytes.LongLength,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                };
                Save();
            }
        }
        public void CompleteReplace()
        {
            lock (gate)
            {
                var replace = State.Replace ?? throw new IOException("No replacement transaction");
                if (!File.Exists(replace.Target) || new FileInfo(replace.Target).Length != replace.Bytes
                || QaFiles.Hash(replace.Target) != replace.Sha256)
                    throw new IOException("Replacement hash mismatch");
                State.Files.Remove(replace.Partial);
                State.Reservations.Remove(replace.Partial);
                State.Files[replace.Target] = new QaFile
                {
                    Path = replace.Target,
                    Bytes = replace.Bytes,
                    Category = Category(replace.Target),
                    Owner = "controller"
                };
                State.Replace = null;
                State.Phase = "ready";
                Save();
            }
        }
        public void RecoverReservations()
        {
            lock (gate)
            {
                if (State.Replace != null)
                {
                    var replace = State.Replace;
                    if (!File.Exists(replace.Partial)
                        && File.Exists(replace.Target)
                        && QaFiles.Hash(replace.Target) == replace.Sha256)
                        CompleteReplace();
                    else
                    {
                        if (State.Files.ContainsKey(replace.Partial) || State.Reservations.ContainsKey(replace.Partial))
                            RemoveOwned(replace.Partial);
                        State.Replace = null;
                    }
                }
                foreach (string path in State.Reservations.Keys.ToArray())
                    RemoveOwned(path);
                foreach (var reservation in State.NativeReservations.Values.ToArray())
                {
                    RecoverNativeReservation(reservation);
                }
                foreach (var file in State.Files.Values)
                    if (File.Exists(file.Path))
                        file.Bytes = new FileInfo(file.Path).Length;
                State.Phase = "ready";
                Save();
            }
        }
        private void RecoverNativeReservation(QaNativeReservation reservation)
        {
            if (!State.NativeReservations.ContainsKey(reservation.Id))
                return;
            long Bytes(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
            if (reservation.ExpectedLength >= 0 && reservation.Source.Length > 0)
            {
                QaNativeReservation? backup = null;
                bool replacing = reservation.Id.EndsWith("-target", StringComparison.Ordinal);
                if (replacing)
                    State.NativeReservations.TryGetValue(
                        reservation.Id.Substring(0, reservation.Id.Length - 7) + "-backup",
                        out backup
                    );
                bool before = File.Exists(reservation.Source) && Bytes(reservation.Source) == reservation.ExpectedLength
                && (replacing ? File.Exists(reservation.Path) : !File.Exists(reservation.Path))
                && Bytes(reservation.Path) == reservation.PriorBytes;
                bool after = !File.Exists(reservation.Source) && File.Exists(reservation.Path)
                && Bytes(reservation.Path) == reservation.ExpectedLength;
                if (backup != null)
                {
                    long prior = State.Files.TryGetValue(backup.Path, out var original) ? original.Bytes : 0;
                    before &= Bytes(backup.Path) == prior;
                    after &= File.Exists(backup.Path) && Bytes(backup.Path) == backup.ExpectedLength;
                }
                if (!before && !after)
                {
                    RecoveryRequired();
                    throw new IOException(
                        "Native atomic mutation is neither intact pre-state nor complete post-state: "
                            + reservation.Path
                    );
                }
                if (backup != null)
                    NativeCommit(backup.Role, backup.Id, backup.Path, Bytes(backup.Path));
            }
            else if (reservation.ExpectedLength >= 0)
            {
                if (reservation.Id.EndsWith("-backup", StringComparison.Ordinal)
                    && State.NativeReservations.ContainsKey(reservation.Id.Substring(
                        0,
                        reservation.Id.Length - 7
                    )
                    + "-target"))
                    return;
                if (!File.Exists(reservation.Path) || Bytes(reservation.Path) != reservation.ExpectedLength)
                {
                    RecoveryRequired();
                    throw new IOException("Native replacement backup is incomplete: " + reservation.Path);
                }
            }
            long actual = Bytes(reservation.Path);
            if (actual > reservation.MaximumLength)
            {
                RecoveryRequired();
                throw new IOException("Native file exceeded durable reservation: " + reservation.Path);
            }
            NativeCommit(reservation.Role, reservation.Id, reservation.Path, actual);
        }
        public void Reconcile(string ownedTree)
        {
            lock (gate)
            {
                ownedTree = Resolve(ownedTree);
                foreach (var reservation in State.NativeReservations.Values.Where(p => Inside(p.Path, ownedTree))
                    .ToArray())
                {
                    RecoverNativeReservation(reservation);
                }
                foreach (var file in QaInventory.Files(ownedTree))
                {
                    if (file == StatePath || file == StatePath + ".tmp")
                        continue;
                    if (!State.Files.TryGetValue(file, out var entry))
                        throw new IOException("Unaccounted QA file: " + file);
                    if (new FileInfo(file).Length != entry.Bytes)
                        throw new IOException("Ledger size differs: " + file);
                }
                ValidateBudget(null, 0, "runtime");
                FreeCheck(0);
            }
        }
        private void Save()
        {
            State.Generation++;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(State, Json);
            if (bytes.Length > MiB)
                throw new IOException("QA ledger metadata cap exceeded");
            string temp = StatePath + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            File.Move(temp, StatePath, true);
        }
        public void Dispose() => lease.Dispose();
    }
}
