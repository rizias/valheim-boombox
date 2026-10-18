using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace BoomboxQa
{
    public sealed class QaConfigSnapshot
    {
        public string Role = "", Sha256 = "", Owner = "";
        public long Bytes;
    }

    public sealed partial class QaLedger
    {
        private bool configRollbackOwned;
        private static readonly string[] ConfigRoles = { "server", "client-a", "client-b" };
        private void RequireConfigRollbackOwner()
        {
            if (State.PendingConfigs.Count != 0 && !configRollbackOwned)
                throw new IOException("QA config rollback pending; recover before new work");
        }
        private static void RequireNoConfigWriters()
        {
            foreach (string name in new[] { "valheim", "valheim_server" })
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length != 0)
                        throw new IOException("Valheim writer active; config rollback postponed");
                }
                finally
                {
                    foreach (var process in processes)
                        process.Dispose();
                }
            }
        }
        private string ConfigTarget(QaConfigSnapshot snapshot)
        {
            if (!ConfigRoles.Contains(snapshot.Role, StringComparer.Ordinal)
                || snapshot.Bytes < 0
                || snapshot.Bytes > RestoreMaximum
                || snapshot.Sha256.Length != 64
                || snapshot.Sha256.Any(c => !char.IsAsciiHexDigit(c))
                || (snapshot.Owner != snapshot.Role && snapshot.Owner != "controller"))
                throw new IOException("Invalid QA config rollback metadata");
            string target = Resolve(
                Path.Combine(Root, "profiles", snapshot.Role, "BepInEx", "config", "local.valheim.boombox.cfg")
            );
            if (State.Files.TryGetValue(target, out var known)
                ? known.Owner != snapshot.Role && known.Owner != "controller"
                : File.Exists(target))
                throw new IOException("QA config target is not role-owned");
            return target;
        }
        private string ConfigSnapshotPath(
            QaConfigSnapshot snapshot
        ) => Resolve(Path.Combine(Root, "baseline", "config-rollback", snapshot.Sha256 + ".cfg"));

        public void BeginConfigRollback()
        {
            lock (gate)
            {
                if (State.PendingConfigs.Count != 0)
                    throw new IOException("Unresolved QA config rollback; recover first");
                RequireNoConfigWriters();
                Preflight(0);
                var captured = new List<(QaConfigSnapshot Snapshot, byte[] Bytes)>();
                foreach (string role in ConfigRoles)
                {
                    string path = Resolve(
                        Path.Combine(Root, "profiles", role, "BepInEx", "config", "local.valheim.boombox.cfg")
                    );
                    if (!State.Files.TryGetValue(path, out var known)
                        || (known.Owner != role && known.Owner != "controller"))
                        throw new IOException("QA config target is not role-owned");
                    long length = new FileInfo(path).Length;
                    if (length > RestoreMaximum || captured.Sum(p => p.Snapshot.Bytes) + length > RestoreMaximum)
                        throw new IOException("QA config restore cap exceeded");
                    if (known.Bytes != length || known.Category != "runtime")
                        throw new IOException("QA config accounting differs from owned baseline");
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes.LongLength != length)
                        throw new IOException("QA config changed during snapshot");
                    captured.Add(
                        (new QaConfigSnapshot
                        {
                            Role = role,
                            Bytes = length,
                            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                            Owner = known.Owner
                        }, bytes)
                    );
                }
                long restore = captured.Sum(p => p.Snapshot.Bytes);
                long snapshotGrowth = captured.GroupBy(p => p.Snapshot.Sha256).Where(
                    g => !File.Exists(ConfigSnapshotPath(g.First().Snapshot))
                ).Sum(
                    g => g.First().Snapshot.Bytes
                );
                Preflight(snapshotGrowth + restore);
                foreach (var item in captured)
                {
                    string path = ConfigSnapshotPath(item.Snapshot);
                    if (!File.Exists(path))
                        QaFiles.Write(this, path, item.Bytes);
                    if (!State.Files.TryGetValue(path, out var known)
                        || known.Owner != "controller"
                        || known.Bytes != item.Snapshot.Bytes
                        || known.Category != "runtime"
                        || new FileInfo(path).Length != item.Snapshot.Bytes
                        || QaFiles.Hash(path) != item.Snapshot.Sha256)
                        throw new IOException("QA config snapshot hash or ownership mismatch");
                }
                State.PendingConfigs = captured.Select(p => p.Snapshot).ToList();
                configRollbackOwned = true;
                Save();
            }
        }

        public void RestoreConfigRollback()
        {
            lock (gate)
            {
                if (State.PendingConfigs.Count == 0)
                    return;
                try
                {
                    RequireNoConfigWriters();
                    if (State.PendingConfigs.Count != 3
                    || State.PendingConfigs.Select(p => p.Role).Distinct(StringComparer.Ordinal).Count() != 3
                    || State.PendingConfigs.Sum(p => p.Bytes) > RestoreMaximum)
                        throw new IOException("Invalid QA config rollback set");
                    var originals = new List<(QaConfigSnapshot Snapshot, string Target, byte[] Bytes)>();
                    foreach (var snapshot in State.PendingConfigs)
                    {
                        string target = ConfigTarget(snapshot), source = ConfigSnapshotPath(snapshot);
                        if (!State.Files.TryGetValue(source, out var known)
                            || known.Owner != "controller"
                            || known.Bytes != snapshot.Bytes
                            || known.Category != "runtime"
                            || new FileInfo(source).Length != snapshot.Bytes
                            || QaFiles.Hash(source) != snapshot.Sha256)
                            throw new IOException(
                                "QA config snapshot hash or ownership mismatch; retained for recovery"
                            );
                        byte[] bytes = File.ReadAllBytes(source);
                        if (bytes.LongLength != snapshot.Bytes
                            || Convert.ToHexString(SHA256.HashData(bytes)) != snapshot.Sha256)
                            throw new IOException("QA config snapshot changed while reading");
                        originals.Add((snapshot, target, bytes));
                    }
                    long needed = originals.Sum(p => p.Snapshot.Bytes);
                    if (new DriveInfo(Path.GetPathRoot(Project)!).AvailableFreeSpace < Reserve + needed)
                        throw new IOException("Insufficient recovery headroom; baseline retained");
                    foreach (var item in originals)
                    {
                        RequireNoConfigWriters();
                        string partial = Resolve(item.Target + ".config-rollback");
                        if (File.Exists(partial)
                            && (!State.Reservations.TryGetValue(partial, out var prior) || prior.Maximum
                                != item.Snapshot.Bytes))
                            throw new IOException("Unowned config recovery partial");
                        State.Reservations[partial] = new QaReservation
                        {
                            Path = partial,
                            Maximum = item.Snapshot.Bytes,
                            Category = "runtime"
                        };
                        Save();
                        using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            output.Write(item.Bytes);
                            output.Flush(true);
                        }
                        if (QaFiles.Hash(partial) != item.Snapshot.Sha256)
                            throw new IOException("QA config recovery partial hash mismatch");
                        RequireNoConfigWriters();
                        ConfigTarget(item.Snapshot);
                        File.Move(partial, item.Target, true);
                        if (QaFiles.Hash(item.Target) != item.Snapshot.Sha256)
                            throw new IOException("QA config restore hash mismatch");
                        State.Reservations.Remove(partial);
                        long priorBytes = State.Files.TryGetValue(item.Target, out var priorTarget)
                            ? priorTarget.Bytes
                            : 0;
                        State.Files[item.Target] = new QaFile
                        {
                            Path = item.Target,
                            Bytes = item.Snapshot.Bytes,
                            Category = "runtime",
                            Owner = item.Snapshot.Owner
                        };
                        runWritten += Math.Max(0, item.Snapshot.Bytes - priorBytes);
                        Save();
                    }
                    foreach (var item in originals)
                        if (QaFiles.Hash(item.Target) != item.Snapshot.Sha256)
                            throw new IOException("QA config restore verification failed");
                    State.PendingConfigs.Clear();
                    configRollbackOwned = false;
                    Save();
                }
                catch
                {
                    RecoveryRequired();
                    throw;
                }
            }
        }
    }
}
