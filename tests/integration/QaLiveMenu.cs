using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BoomboxQa
{
    public static class QaLiveMenu
    {
        public static async Task Run(QaLedger ledger)
        {
            string requestFile = Path.Combine(QaEnvironment.QaRoot, "current/menu-live-command.json");
            string last = File.Exists(requestFile) ? QaFiles.ReadSharedText(requestFile) : "";
            await QaAuraCases.PrepareLocal(ledger);
            await QaAuraCases.Command(
                "client-a",
                "audio-setup",
                new
                {
                    fixture = Path.Combine(ledger.Root, "current/r12-video/demo-source.wav")
                }
            );
            await QaAuraCases.Command("client-a", "menu-park");
            Console.WriteLine(
                "QA_MENU_LIVE_READY: persistent client; idle8fps, simulation/audio paused; send measure, inspect, or shutdown through VALHEIM_QA_ROOT/current/menu-live-command.json"
            );
            try
            {
                while (true)
                {
                    await Task.Delay(200);
                    if (!File.Exists(requestFile))
                        continue;
                    string text = QaFiles.ReadSharedText(requestFile);
                    if (text == last)
                        continue;
                    JsonNode request;
                    try
                    {
                        request = JsonNode.Parse(text)!;
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                    last = text;
                    string action = request["action"]?.GetValue<string>() ?? "";
                    if (action == "shutdown")
                        break;
                    if (action != "measure" && action != "inspect" && action != "hud-fixes" && action != "icon-preview"
                    && action != "gameplay-settings"
                    && action != "crack-origin"
                    && action != "effect-preview"
                    && action != "visibility-buffs")
                    {
                        Console.WriteLine("QA_MENU_LIVE_UNKNOWN_ACTION");
                        continue;
                    }
                    try
                    {
                        await QaAuraCases.Command("client-a", "menu-resume");
                        var result = await QaAuraCases.Command(
                            "client-a",
                            action == "measure"
                                ? "menu-latency"
                                : action == "inspect"
                                    ? "menu-inspect"
                                    : action,
                            new
                            {
                                recreate = request["recreate"]?.GetValue<bool>() ?? false
                            },
                            seconds: 30
                        );
                        string output = Path.Combine(ledger.Root, "evidence/R10/menu-live-" + action + ".json");
                        QaFiles.Write(ledger, output, JsonSerializer.SerializeToUtf8Bytes(result));
                        if (action != "inspect" && action != "effect-preview" && action != "visibility-buffs")
                            foreach (var frame in result["capture"]!["frames"]!.AsArray())
                            {
                                string path = frame!["file"]!.GetValue<string>();
                                QaFiles.Copy(
                                    ledger,
                                    path,
                                    Path.Combine(ledger.Root, "evidence/R10/menu-live-" + Path.GetFileName(path))
                                );
                            }
                        Console.WriteLine("QA_MENU_LIVE_RESULT " + output);
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine("QA_MENU_LIVE_CASE_FAILED " + error.Message);
                    }
                    finally
                    {
                        await QaAuraCases.Command("client-a", "menu-park");
                    }
                }
            }
            finally
            {
                await QaAuraCases.Command("client-a", "menu-resume");
                await QaAuraCases.Command("client-a", "audio-clear");
            }
        }
    }
}
