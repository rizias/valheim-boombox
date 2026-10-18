using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaMigrationMp
    {
        public static async Task Run(QaLedger ledger)
        {
            QaAuraCases.UseLedger(ledger);
            await QaAuraCases.Command("client-a", "host", seconds: 5);
            await QaAuraCases.Command("client-a", "prepare", seconds: 8);
            await QaAuraCases.Command("client-b", "prepare", seconds: 8);
            var a = await QaAuraCases.Command("client-a", "snapshot", seconds: 5);
            var p = a["position"]!;
            var near = new
            {
                x = p["x"]!.GetValue<float>() + 3,
                y = p["y"]!.GetValue<float>(),
                z = p["z"]!.GetValue<float>()
            };
            await QaAuraCases.Command(
                "client-b",
                "aura-position",
                new
                {
                    position = near
                },
                seconds: 5
            );
            await QaAuraCases.Command(
                "client-a",
                "create",
                new
                {
                    key = "MIGRATION",
                    ranks = new[] { 1, 1, 1, 1, 1 },
                    decorations = 0,
                    placed = true,
                    position = new
                    {
                        x = near.x - 3,
                        y = near.y,
                        z = near.z
                    }
                },
                seconds: 5
            );
            await Task.Delay(1500);
            var nearSample = await QaAuraCases.Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 20
                },
                seconds: 9
            );
            long owner = a["playerId"]!.GetValue<long>();
            bool observed = nearSample["sources"]!.AsArray().Any(
                s => s!["owner"]!.GetValue<long>() == owner && s["active"]!.GetValue<bool>()
            );
            bool recovered = nearSample["sample"]!["ticks"]!.AsArray().All(t => t!.GetValue<double>() > 0);
            bool noRemoteAudio = !nearSample["audioDecks"]!.AsArray().Any(d => d!["owner"]!.GetValue<long>() == owner);
            if (!observed || !recovered || !noRemoteAudio)
                throw new IOException("Migrated MP near-source/recovery/local-audio criterion failed");
            await QaAuraCases.Command(
                "client-a",
                "toggle",
                new
                {
                    key = "MIGRATION"
                },
                seconds: 5
            );
            await Task.Delay(1500);
            var paused = await QaAuraCases.Command(
                "client-b",
                "sample",
                new
                {
                    duration = 5.4,
                    missing = 20
                },
                seconds: 9
            );
            if (paused["sample"]!["ticks"]!.AsArray().Any(t => Math.Abs(t!.GetValue<double>()) > .001))
                throw new IOException("Paused remote source still restored resources");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R09/migration-mp-gameplay.json"),
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        passed = true,
                        nearSample,
                        paused,
                        scope = "Actual two clients: remote source metadata, three native recovery counters, pause cessation, no foreign AudioDeck. Fixture items and synthetic local rhythm; not a full multiplayer regression."
                    }
                )
            );
            await QaAuraCases.Command("client-a", "clear", seconds: 5);
            await QaAuraCases.Command("client-b", "clear", seconds: 5);
            Console.WriteLine("QA_MIGRATED_MP_GAMEPLAY_PASS");
        }
    }
}
