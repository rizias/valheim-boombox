using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Splatform;
using UnityEngine;

public static class QaPreferences
{
    private static readonly object Gate = new object();
    private static JObject values = new JObject();
    private static string path;
    public static void Install(Harmony harmony)
    {
        path = Path.Combine(BepInEx.Paths.ConfigPath, "qa-preferences.json");
        if (File.Exists(path))
            values = JObject.Parse(File.ReadAllText(path));
        foreach (var pair in new[] {
            new[] { "GetInt", nameof(GetInt) },
            new[] { "GetFloat", nameof(GetFloat) },
            new[] { "GetString", nameof(GetString) },
            new[] { "GetBool", nameof(GetBool) },
            new[] { "HasKey", nameof(HasKey) },
            new[] { "SetInt", nameof(Set) },
            new[] { "SetFloat", nameof(Set) },
            new[] { "SetString", nameof(Set) },
            new[] { "SetBool", nameof(Set) },
            new[] { "DeleteKey", nameof(Delete) },
            new[] { "DeleteAll", nameof(Clear) },
            new[] { "Save", nameof(Save) }
        })
            harmony.Patch(
                AccessTools.Method(typeof(PlatformPrefs), pair[0]),
                prefix: new HarmonyMethod(typeof(QaPreferences), pair[1])
            );
        foreach (string name in new[] { "SetInt", "SetFloat", "SetString", "DeleteKey", "DeleteAll", "Save" })
            harmony.Patch(
                AccessTools.Method(typeof(PlayerPrefs), name),
                prefix: new HarmonyMethod(
                    typeof(QaPreferences),
                    name.StartsWith("Set", StringComparison.Ordinal)
                        ? nameof(Set)
                        : name == "DeleteKey"
                            ? nameof(Delete)
                            : name == "DeleteAll"
                                ? nameof(Clear)
                                : nameof(Save)
                )
            );
        PlatformPrefs.SetString("boombox.qa.role", Environment.GetEnvironmentVariable("BOOMBOX_QA_ROLE"));
    }
    private static bool GetInt(string __0, int __1, ref int __result)
    {
        lock (Gate)
            __result = values[__0] == null ? __1 : values[__0].Value<int>();
        return false;
    }
    private static bool GetFloat(string __0, float __1, ref float __result)
    {
        lock (Gate)
            __result = values[__0] == null ? __1 : values[__0].Value<float>();
        return false;
    }
    private static bool GetString(string __0, string __1, ref string __result)
    {
        lock (Gate)
            __result = values[__0] == null ? __1 : values[__0].Value<string>();
        return false;
    }
    private static bool GetBool(string __0, bool __1, ref bool __result)
    {
        lock (Gate)
            __result = values[__0] == null ? __1 : values[__0].Value<bool>();
        return false;
    }
    private static bool HasKey(string __0, ref bool __result)
    {
        lock (Gate)
            __result = values[__0] != null;
        return false;
    }
    private static bool Set(MethodBase __originalMethod, object[] __args)
    {
        lock (Gate)
        {
            values[(string)__args[0]] = JToken.FromObject(__args[1]);
            Persist();
        }
        return false;
    }
    private static bool Delete(string __0)
    {
        lock (Gate)
        {
            values.Remove(__0);
            Persist();
        }
        return false;
    }
    private static bool Clear()
    {
        lock (Gate)
        {
            values.RemoveAll();
            Persist();
        }
        return false;
    }
    private static bool Save()
    {
        lock (Gate)
            Persist();
        return false;
    }
    private static void Persist()
    {
        string temporary = path + ".pending";
        File.WriteAllText(temporary, values.ToString());
        if (File.Exists(path))
            File.Replace(temporary, path, null);
        else
            File.Move(temporary, path);
    }
    public static JObject Evidence() => new JObject
    {
        ["file"] = path,
        ["role"] = (string)values["boombox.qa.role"],
        ["sharedUnitySentinel"] = PlayerPrefs.HasKey("boombox.qa.role")
    };
}
