using System;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaReplication
    {
        private static JsonDocument Read(
            QaLedger ledger,
            string role
        ) => JsonDocument.Parse(
            QaFiles.ReadSharedText(Path.Combine(ledger.Root, "profiles", role, "BepInEx/config/qa-status.json"))
        );
        public static async Task Run(QaLedger ledger)
        {
            await MoveAndObserve(ledger, "client-a", "client-b", 4);
            await MoveAndObserve(ledger, "client-b", "client-a", -4);
        }
        private static async Task MoveAndObserve(QaLedger ledger, string actor, string observer, float offset)
        {
            using var before = Read(ledger, actor);
            long id = before.RootElement.GetProperty("localPlayer").GetInt64();
            JsonElement local = default;
            foreach (var player in before.RootElement.GetProperty("players").EnumerateArray())
                if (player.GetProperty("id").GetInt64() == id)
                    local = player;
            if (local.ValueKind == JsonValueKind.Undefined)
                throw new IOException("Local player is absent from native replication snapshot");
            float x = local.GetProperty("x").GetSingle() + offset, y = local.GetProperty("y").GetSingle(), z = local.GetProperty("z").GetSingle();
            string action = "teleport:" + x.ToString("R", CultureInfo.InvariantCulture) + ":"
                + y.ToString("R", CultureInfo.InvariantCulture)
                + ":"
                + z.ToString("R", CultureInfo.InvariantCulture);
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "profiles", actor, "BepInEx/config/qa-command.txt"),
                Encoding.UTF8.GetBytes(action)
            );
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var after = Read(ledger, observer);
                    using var actorAfter = Read(ledger, actor);
                    JsonElement nativeActor = default;
                    foreach (var native in actorAfter.RootElement.GetProperty("players").EnumerateArray())
                        if (native.GetProperty("id").GetInt64() == id)
                            nativeActor = native;
                    if (nativeActor.ValueKind == JsonValueKind.Undefined
                        || Math.Abs(nativeActor.GetProperty("x").GetSingle() - x) > .3f
                        || Math.Abs(nativeActor.GetProperty("z").GetSingle() - z) > .3f)
                    {
                        await Task.Delay(100);
                        continue;
                    }
                    foreach (var player in after.RootElement.GetProperty("players").EnumerateArray())
                    {
                        if (player.GetProperty("id").GetInt64() != id
                            || Math.Abs(player.GetProperty("x").GetSingle() - nativeActor.GetProperty("x").GetSingle())
                            > .3f
                            || Math.Abs(player.GetProperty("y").GetSingle() - nativeActor.GetProperty("y").GetSingle())
                            > .3f
                            || Math.Abs(player.GetProperty("z").GetSingle() - nativeActor.GetProperty("z").GetSingle())
                            > .3f)
                            continue;
                        QaFiles.Write(
                            ledger,
                            Path.Combine(ledger.Root, "evidence/R09/replication-" + actor + ".json"),
                            JsonSerializer.SerializeToUtf8Bytes(
                                new
                                {
                                    passed = true,
                                    actor,
                                    observer,
                                    id,
                                    nativeAction = "Player.TeleportTo near destination; observer xyz matches the actor after native landing",
                                    expectedX = x,
                                    expectedY = nativeActor.GetProperty("y").GetSingle(),
                                    expectedZ = z,
                                    nativeActor = nativeActor.Clone(),
                                    observed = player.Clone(),
                                    utc = DateTime.UtcNow
                                }
                            )
                        );
                        return;
                    }
                }
                catch (IOException) { }
                catch (JsonException) { }
                await Task.Delay(500);
            }
            throw new TimeoutException("Native player movement was not replicated from " + actor + " to " + observer);
        }
    }
}
