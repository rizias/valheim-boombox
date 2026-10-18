using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private const string TransportTag = "boombox.qa.K03";
    private static string TransportCheckpoint => Path.Combine(BepInEx.Paths.ConfigPath, "qa-K03-checkpoint.json");
    private void InstallTransportPersistence()
    {
        if (!(Environment.GetEnvironmentVariable("BOOMBOX_QA_CASE") ?? "").Contains("K03"))
            return;
        safetyHarmony.Patch(
            AccessTools.Method(typeof(ZNetView), "Awake"),
            postfix: new HarmonyMethod(typeof(QaGameplay), nameof(FreezeSavedTransport))
        );
    }
    private static void FreezeSavedTransport(ZNetView __instance)
    {
        if (__instance == null || !__instance.IsValid() || !__instance.GetZDO().GetBool(TransportTag, false))
            return;
        var root = __instance.gameObject;
        if (root.GetComponent<Ship>() is Ship ship)
            ship.enabled = false;
        if (root.GetComponent<Vagon>() is Vagon cart)
            cart.enabled = false;
        foreach (var body in root.GetComponentsInChildren<Rigidbody>())
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
    }
    private static ZNetView[] TransportFixtures() => UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(
        v => v.IsValid() && v.GetZDO().GetBool(TransportTag, false)
    ).ToArray();
    private static AudioDeck TransportDeck(
        string id
    ) => UnityEngine.Object.FindObjectsByType<AudioDeck>(FindObjectsSortMode.None).FirstOrDefault(d => d.State.id == id);
    private IEnumerator TransportWait(Func<bool> condition, string message, float seconds = 15)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until)
            yield return new WaitForSecondsRealtime(.2f);
        Need(condition(), message);
    }
    private static JObject TransportVerify(JObject expected)
    {
        string id = (string)expected["id"];
        var deck = TransportDeck(id);
        Need(deck != null, "K03 missing saved device");
        var platform = deck.PlatformAnchor.Platform;
        Need(platform != null, "K03 missing platform binding");
        var local = expected["local"].ToObject<float[]>();
        var q = expected["rotation"].ToObject<float[]>();
        float positionError = Vector3.Distance(
            new Vector3(local[0], local[1], local[2]),
            platform.InverseTransformPoint(deck.transform.position)
        );
        float rotationError = Quaternion.Angle(
            new Quaternion(q[0], q[1], q[2], q[3]),
            Quaternion.Inverse(platform.rotation) * deck.transform.rotation
        );
        var actual = JToken.Parse(CaseStable(deck.Item).ToString());
        Need(JToken.DeepEquals(expected["item"], actual), "K03 changed item/cassette payload");
        Need(
            deck.State.platformGuid == (string)expected["guid"]
                && platform.GetComponent<ZNetView>().GetZDO().GetString(BoomboxPlatformAnchor.PlatformTokenKey, "")
                == (string)expected["guid"],
            "K03 wrong transport identity"
        );
        Need(positionError < .03f && rotationError < 2, "K03 changed relative pose");
        int copies = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Count(
            d => BoomboxItemData.IsDevice(d.m_itemData) && BoomboxItemData.Read(d.m_itemData).deck.id == id
        );
        Need(copies == 1, "K03 duplicate/missing world item");
        return new JObject
        {
            ["id"] = id,
            ["prefab"] = expected["prefab"],
            ["positionError"] = positionError,
            ["rotationError"] = rotationError,
            ["copies"] = copies,
            ["item"] = actual
        };
    }
    private IEnumerator CaseTransportPersistence(bool save)
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        int fps = Application.targetFrameRate;
        Application.targetFrameRate = 30;
        var start = player.transform.position;
        var turn = player.transform.rotation;
        var body = player.GetComponent<Rigidbody>();
        bool kinematic = body.isKinematic;
        var proof =
            new JObject
            {
                ["case"] = "K-03",
                ["phase"] = save ? "save" : "reload",
                ["scope"] =
                    "Native world save/new process and actual area unload/recreation. QA-marked transport physics held still; production anchor logic unchanged."
            };
        lastCapture = proof;
        try
        {
            if (save)
            {
                yield return CaseReadyInventory();
                var records = new JArray();
                foreach (string prefab in new[] { "Cart", "Karve" })
                {
                    var root = UnityEngine.Object.Instantiate(
                        ZNetScene.instance.GetPrefab(prefab),
                        start + Vector3.up * 40 + Vector3.right * (prefab == "Cart" ? 8 : 18),
                        Quaternion.identity
                    );
                    var view = root.GetComponent<ZNetView>();
                    view.GetZDO().Set(TransportTag, true);
                    FreezeSavedTransport(view);
                    Physics.SyncTransforms();
                    var item = CaseDevice("K03-" + prefab, "Green", 2);
                    var deck = TransportDeck(BoomboxItemData.Read(item).deck.id);
                    Need(
                        CaseClearPlatformPlacement(
                            root.GetComponent<Rigidbody>(),
                            deck,
                            out var position,
                            out var rotation,
                            out var support
                        ),
                        "K03 fixture surface missing"
                    );
                    Need(plugin.Items.PutDown(deck, position, rotation, support), "K03 fixture placement failed");
                    yield return null;
                    var local = root.transform.InverseTransformPoint(deck.transform.position);
                    var q = Quaternion.Inverse(root.transform.rotation) * deck.transform.rotation;
                    records.Add(new JObject
                    {
                        ["prefab"] = prefab,
                        ["id"] = deck.State.id,
                        ["guid"] = deck.State.platformGuid,
                        ["item"] = CaseStable(deck.Item),
                        ["local"] = new JArray(local.x, local.y, local.z),
                        ["rotation"] = new JArray(q.x, q.y, q.z, q.w)
                    });
                    root.GetComponent<ZSyncTransform>().SyncNow();
                }
                plugin.Items.Flush();
                Game.instance.SavePlayerProfile(true);
                ZNet.instance.Save(true, false, false);
                var checkpoint = new JObject { ["sha"] = productSha, ["records"] = records };
                File.WriteAllText(TransportCheckpoint, checkpoint.ToString());
                proof["checkpoint"] = checkpoint;
                proof["passed"] = true;
                yield break;
            }
            UiParkWindow();
            Freeze(player, true);
            body.isKinematic = true;
            var expected = JObject.Parse(File.ReadAllText(TransportCheckpoint));
            Need((string)expected["sha"] == productSha, "K03 candidate changed between phases");
            var entries = expected["records"].Cast<JObject>().ToArray();
            yield return TransportWait(
                () => entries.All(e => TransportDeck((string)e["id"])?.PlatformAnchor.Platform != null),
                "K03 fresh process did not restore both bindings"
            );
            yield return new WaitForEndOfFrame();
            proof["afterProcessReload"] = new JArray(entries.Select(TransportVerify));
            var originals = TransportFixtures();
            Need(originals.Length == 2, "K03 expected two QA platforms");
            var instanceIds = originals.Select(v => v.GetInstanceID()).ToArray();
            var far = start + new Vector3(4096, 100, 4096);
            CasePlatformPlayer(player, far, turn);
            player.GetComponent<ZSyncTransform>().SyncNow();
            ZNet.instance.SetReferencePosition(far);
            yield return TransportWait(
                () => TransportFixtures().Length == 0 && originals.All(v => v == null),
                "K03 actual area unload failed",
                20
            );
            proof["unloaded"] = true;
            CasePlatformPlayer(player, start, turn);
            player.GetComponent<ZSyncTransform>().SyncNow();
            ZNet.instance.SetReferencePosition(start);
            yield return TransportWait(
                () => TransportFixtures().Length == 2
                    && entries.All(e => TransportDeck((string)e["id"])?.PlatformAnchor.Platform != null),
                "K03 actual area reload failed",
                20
            );
            yield return new WaitForEndOfFrame();
            Need(
                TransportFixtures().All(v => !instanceIds.Contains(v.GetInstanceID())),
                "K03 old instances were not replaced"
            );
            proof["afterAreaReload"] = new JArray(entries.Select(TransportVerify));
            proof["passed"] = true;
        }
        finally
        {
            Application.targetFrameRate = fps;
            if (player != null)
            {
                CasePlatformPlayer(player, start, turn);
                player.GetComponent<ZSyncTransform>().SyncNow();
                ZNet.instance.SetReferencePosition(start);
                body.isKinematic = kinematic;
                Freeze(player, false);
            }
            if (!save && player != null)
            {
                Clear();
                foreach (var view in TransportFixtures())
                    ZNetScene.instance.Destroy(view.gameObject);
                Game.instance.SavePlayerProfile(true);
                ZNet.instance.Save(true, false, false);
            }
        }
    }
}
