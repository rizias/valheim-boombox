using System;
using System.IO;
using Newtonsoft.Json.Linq;

public sealed partial class QaGameplay
{
    private static string UiIdentity(string kind)
    {
        string path = Path.Combine(BepInEx.Paths.ConfigPath, "qa-ui-identity.json");
        string id = (string)JObject.Parse(File.ReadAllText(path))[kind];
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Craft the QA " + kind + " through the UI before continuing its route");
        return id;
    }

    private static void RememberUiIdentity(string kind, string id)
    {
        string path = Path.Combine(BepInEx.Paths.ConfigPath, "qa-ui-identity.json");
        var identity = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        identity[kind] = id;
        File.WriteAllText(path, identity.ToString());
    }
}
