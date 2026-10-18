using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static JObject mountCameraTrace;
    private static readonly Dictionary<int, int> mountFits = new Dictionary<int, int>();
    private static void MountFitted(BoomboxMount __instance)
    {
        int id = __instance.GetInstanceID();
        mountFits[id] = mountFits.TryGetValue(id, out int count) ? count + 1 : 1;
    }
    private static float MountAngle(BoomboxMount mount) => Quaternion.Angle(
        mount.BackAnchor.rotation * Quaternion.Euler(0, 180, 0)
            * Quaternion.Euler(BoomboxPlugin.Instance.Settings.BackRotation.Value),
        mount.transform.rotation
    );
    private static void MountCameraBefore(BoomboxMount __instance, Camera camera)
    {
        if (camera.name != "QA four equipment states" || __instance.BackAnchor == null)
            return;
        mountCameraTrace = new JObject
        {
            ["before"] = MountAngle(__instance),
            ["frame"] = Time.frameCount,
            ["instance"] = __instance.GetInstanceID(),
            ["rotationSetting"] = Point(BoomboxPlugin.Instance.Settings.BackRotation.Value)
        };
    }
    private static void MountCameraAfter(BoomboxMount __instance, Camera camera)
    {
        if (camera.name != "QA four equipment states" || __instance.BackAnchor == null)
            return;
        mountCameraTrace["after"] = MountAngle(__instance);
    }
    private IEnumerator MountCapture()
    {
        yield return AwaitAudio();
        float started = Time.realtimeSinceStartup;
        var player = Player.m_localPlayer;
        var origin = player.transform.position;
        var rotation = player.transform.rotation;
        var settings = BoomboxPlugin.Instance.Settings;
        bool adaptive = settings.AdaptiveBackClearance.Value;
        var env = EnvMan.instance;
        bool oldDebugWind = env.m_debugWind;
        float oldWindAngle = env.m_debugWindAngle, oldWindIntensity = env.m_debugWindIntensity;
        mountFits.Clear();
        var windRows = new JArray();
        Need(Mathf.Abs(settings.BackScale.Value - .8f) < .0001f, "Approved carried scale must remain 0.8");
        var oldItems = new[] { "m_shoulderItem", "m_leftItem", "m_rightItem", "m_hiddenLeftItem", "m_hiddenRightItem" }
        .Select(
            n => (ItemDrop.ItemData)AccessTools.Field(typeof(Humanoid), n).GetValue(player)
        ).Where(
            i => i != null
        ).Distinct().ToArray();
        foreach (var item in oldItems)
            player.UnequipItem(item, false);
        ItemDrop.ItemData Make(string name)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(name);
            var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab;
            Need(player.GetInventory().AddItem(item), "Mount fixture inventory full");
            return item;
        }
        var shield = Make("ShieldBanded");
        var cape = Make("CapeDeerHide");
        var root = new GameObject("QA four equipment states");
        var camera = root.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.fieldOfView = 45;
        camera.aspect = 1280f / 960;
        var target = new RenderTexture(1280, 960, 24);
        target.Create();
        var rows = new JArray();
        var frames = new JArray();
        var result =
            new JObject
            {
                ["cases"] = rows,
                ["frames"] = frames,
                ["method"] =
                    "Native equipment/HideHandItems and scripted Player.SetControls; side views plus current mount-space surfaces; not hardware input"
            };
        lastCapture = result;
        result["windCases"] = windRows;
        var patch = new Harmony("local.boombox.qa.mount-four");
        patch.Patch(
            AccessTools.Method(typeof(PlayerController), "FixedUpdate"),
            prefix: new HarmonyMethod(typeof(QaGameplay), nameof(CapeController))
        );
        patch.Patch(
            AccessTools.Method(typeof(BoomboxMount), "BeforeCamera"),
            prefix: new HarmonyMethod(typeof(QaGameplay), nameof(MountCameraBefore)),
            postfix: new HarmonyMethod(typeof(QaGameplay), nameof(MountCameraAfter))
        );
        patch.Patch(
            AccessTools.Method(typeof(BoomboxMount), "FitRenderedVolume"),
            postfix: new HarmonyMethod(typeof(QaGameplay), nameof(MountFitted))
        );
        capeDrive = true;
        capeDirection = Vector3.zero;
        settings.AdaptiveBackClearance.Value = false;
        Floor(origin);
        Freeze(player, false);
        var bodySkin = player.GetVisEquipment().m_bodyModel;
        var bakedBody = new Mesh();
        try
        {
            foreach (string state in new[] { "empty", "shield", "cape", "shield-cape", "empty-return" })
            {
                player.UnequipItem(shield, false);
                player.UnequipItem(cape, false);
                bool hasShield = state.Contains("shield"), hasCape = state.Contains("cape");
                if (hasCape)
                    Need(player.EquipItem(cape, false), "Cape equip failed");
                if (hasShield)
                {
                    Need(player.EquipItem(shield, false), "Shield equip failed");
                    Need(player.HideHandItems(false, false), "Shield stow failed");
                }
                yield return new WaitForSecondsRealtime(.5f);
                float stateOffset = float.NaN;
                int stateFits = -1;
                Vector3 stateLocalPosition = Vector3.zero;
                foreach (string pose in state == "empty-return"
                    ? new[] { "standing" }
                    : new[] { "standing", "running", "crouching" })
                {
                    player.transform.SetPositionAndRotation(origin, rotation);
                    player.SetLookDir(rotation * Vector3.forward);
                    var body = player.GetComponent<Rigidbody>();
                    body.position = origin;
                    body.linearVelocity = Vector3.zero;
                    AccessTools.Method(typeof(Player), "SetCrouch").Invoke(
                        player,
                        new object[] { pose == "crouching" }
                    );
                    capeDirection = pose == "running" ? rotation * Vector3.forward : Vector3.zero;
                    yield return new WaitForSecondsRealtime(.5f);
                    yield return new WaitForEndOfFrame();
                    var back = (GameObject)AccessTools.Field(typeof(VisEquipment), "m_leftBackItemInstance")
                        .GetValue(player.GetVisEquipment());
                    var mount = audioDeck.Mount;
                    var visual = audioDeck.Visual;
                    var bounds = visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
                    Need((back != null) == hasShield, "Actual shield state differs: " + state);
                    int inside = 0, torsoInside = 0;
                    float closestBodyZ = float.NegativeInfinity;
                    Vector3 supportPoint = Vector3.zero;
                    var neck = bodySkin.bones.First(b => b.name == "Neck");
                    var spine = bodySkin.bones.First(b => b.name == "Spine");
                    var center = visual.transform.TransformPoint(bounds.center);
                    var torsoUp = (neck.position - spine.position).normalized;
                    float alongTorso = Vector3.Dot(
                        center - spine.position,
                        torsoUp
                    ), torsoLength = Vector3.Distance(neck.position, spine.position);
                    var focus = player.transform.position + Vector3.up;
                    camera.transform.position = focus + player.transform.right * 2.65f - player.transform.forward * .15f;
                    camera.transform.LookAt(focus, Vector3.up);
                    frames.Add(ContextPng(camera, target, "full-mount-" + state + "-" + pose + "-side.png"));
                    if (pose == "standing")
                    {
                        camera.transform.position = focus - player.transform.forward * 2.65f
                            + player.transform.right * .2f;
                        camera.transform.LookAt(focus, Vector3.up);
                        frames.Add(ContextPng(camera, target, "full-mount-" + state + "-" + pose + "-back.png"));
                    }
                    float afterRenderAngle = MountAngle(mount);
                    if (hasShield)
                        Need(mount.EquipmentSurfaceDepth > 0, "Shield surface was ignored after render");
                    if (!hasShield && !hasCape)
                        Need(mount.EquipmentSurfaceDepth == 0f, "Equipment offset retained after removal");
                    var bones = bodySkin.bones;
                    var positions = bones.Select(b => b.position).ToArray();
                    var rotations = bones.Select(b => b.rotation).ToArray();
                    bodySkin.BakeMesh(bakedBody, true);
                    inside = 0;
                    closestBodyZ = float.NegativeInfinity;
                    float boneDrift = 0f, boneAngle = 0f;
                    for (int b = 0; b < bones.Length; b++)
                    {
                        boneDrift = Mathf.Max(boneDrift, Vector3.Distance(positions[b], bones[b].position));
                        boneAngle = Mathf.Max(boneAngle, Quaternion.Angle(rotations[b], bones[b].rotation));
                    }
                    foreach (var vertex in bakedBody.vertices)
                    {
                        var p = visual.transform.InverseTransformPoint(bodySkin.transform.TransformPoint(vertex));
                        if (p.x > bounds.min.x
                            && p.x < bounds.max.x
                            && p.y > bounds.min.y
                            && p.y < bounds.max.y
                            && p.z > closestBodyZ)
                        {
                            closestBodyZ = p.z;
                            supportPoint = bodySkin.transform.TransformPoint(vertex);
                        }
                        if (p.x > bounds.min.x + .002f
                            && p.x < bounds.max.x - .002f
                            && p.y > bounds.min.y + .002f
                            && p.y < bounds.max.y - .002f
                            && p.z > bounds.min.z + .002f
                            && p.z < bounds.max.z - .002f)
                        {
                            inside++;
                            if (Mathf.Abs(p.x * visual.transform.lossyScale.x) < .06f)
                                torsoInside++;
                        }
                    }
                    center = visual.transform.TransformPoint(bounds.center);
                    torsoUp = (neck.position - spine.position).normalized;
                    alongTorso = Vector3.Dot(center - spine.position, torsoUp);
                    torsoLength = Vector3.Distance(neck.position, spine.position);
                    float bodyGap = (bounds.min.z - closestBodyZ) * visual.transform.lossyScale.z;
                    float rotationError = Quaternion.Angle(
                        mount.BackAnchor.rotation * Quaternion.Euler(0f, 180f, 0f)
                            * Quaternion.Euler(settings.BackRotation.Value),
                        visual.transform.rotation
                    );
                    var localPosition = Quaternion.Inverse(mount.BackAnchor.rotation)
                        * (visual.transform.position - mount.BackAnchor.position);
                    int fits = mountFits.TryGetValue(mount.GetInstanceID(), out int fitCount) ? fitCount : 0;
                    if (float.IsNaN(stateOffset))
                    {
                        stateOffset = mount.OutwardOffset;
                        stateFits = fits;
                        stateLocalPosition = localPosition;
                    }
                    float offsetChange = Mathf.Abs(stateOffset - mount.OutwardOffset), localChange = Vector3.Distance(stateLocalPosition, localPosition);
                    bool passed = torsoInside == 0
                        && mount.BackAnchor == player.GetVisEquipment().m_backShield.parent
                        && alongTorso > .30f
                        && alongTorso < torsoLength
                        && rotationError < .1f
                        && boneDrift < .0001f
                        && boneAngle < .1f
                        && offsetChange < .0001f
                        && localChange < .001f
                        && fits == stateFits;
                    rows.Add(new JObject
                    {
                        ["state"] = state,
                        ["pose"] = pose,
                        ["shieldVisible"] = back != null,
                        ["anchor"] = mount.BackAnchor.name,
                        ["bodyDepth"] = mount.BodySurfaceDepth,
                        ["equipmentDepth"] = mount.EquipmentSurfaceDepth,
                        ["outwardOffset"] = mount.OutwardOffset,
                        ["bodyVerticesInsideCase"] = inside,
                        ["bodyRearGap"] = bodyGap,
                        ["bodySupportPoint"] = Point(supportPoint),
                        ["boneUp"] = Point(mount.BackAnchor.up),
                        ["torsoUp"] = Point(torsoUp),
                        ["visualUp"] = Point(visual.transform.up),
                        ["rendererScale"] = Point(bodySkin.transform.lossyScale),
                        ["centerAlongTorso"] = alongTorso,
                        ["torsoLength"] = torsoLength,
                        ["passed"] = passed
                    });
                    ((JObject)rows.Last)["renderRotationErrorDegrees"] = rotationError;
                    ((JObject)rows.Last)["boneDriftDuringMeasurement"] = boneDrift;
                    ((JObject)rows.Last)["boneAngleDuringMeasurement"] = boneAngle;
                    ((JObject)rows.Last)["cameraTrace"] = mountCameraTrace?.DeepClone();
                    ((JObject)rows.Last)["angleAfterRenderBeforeBake"] = afterRenderAngle;
                    ((JObject)rows.Last)["frame"] = Time.frameCount;
                    ((JObject)rows.Last)["mountInstance"] = mount.GetInstanceID();
                    ((JObject)rows.Last)["torsoVerticesInsideCase"] = torsoInside;
                    ((JObject)rows.Last)["armOverlapAllowedByOwner"] = true;
                    ((JObject)rows.Last)["offsetChange"] = offsetChange;
                    ((JObject)rows.Last)["boneLocalPositionChange"] = localChange;
                    ((JObject)rows.Last)["fitsSinceEquip"] = fits - stateFits;
                    capeDirection = Vector3.zero;
                    if (state == "cape" && pose == "standing")
                    {
                        foreach (float angle in new[] { 0f, 180f, 90f })
                        {
                            env.SetDebugWind(angle, 1f);
                            yield return new WaitForSecondsRealtime(.25f);
                            yield return new WaitForEndOfFrame();
                            camera.targetTexture = target;
                            camera.Render();
                            camera.targetTexture = null;
                            float change = Mathf.Abs(stateOffset - mount.OutwardOffset);
                            int currentFits = mountFits[mount.GetInstanceID()];
                            windRows.Add(new JObject
                            {
                                ["angle"] = angle,
                                ["debugIntensity"] = 1,
                                ["actualWind"] = Point(env.GetWindForce()),
                                ["offsetChange"] = change,
                                ["fitCalls"] = currentFits - stateFits,
                                ["passed"] = change < .0001f && currentFits == stateFits
                            });
                        }
                    }
                }
            }
            yield return ShieldCatalog(player, cape, result);
            result["coreSeconds"] = Time.realtimeSinceStartup - started;
            result["passed"] = rows.All(r => (bool)r["passed"])
                && windRows.All(r => (bool)r["passed"])
                && (bool)result["shieldCatalogPassed"];
            result["measurement"] = "fixed-shoulder-equipment-offset";
            result["carriedScale"] = settings.BackScale.Value;
        }
        finally
        {
            capeDrive = false;
            capeDirection = Vector3.zero;
            patch.UnpatchSelf();
            AccessTools.Method(typeof(Player), "SetCrouch").Invoke(player, new object[] { false });
            player.UnequipItem(shield, false);
            player.UnequipItem(cape, false);
            player.GetInventory().RemoveItem(shield);
            player.GetInventory().RemoveItem(cape);
            foreach (var item in oldItems)
                player.EquipItem(item, false);
            env.m_debugWind = oldDebugWind;
            env.m_debugWindAngle = oldWindAngle;
            env.m_debugWindIntensity = oldWindIntensity;
            settings.AdaptiveBackClearance.Value = adaptive;
            player.transform.SetPositionAndRotation(origin, rotation);
            target.Release();
            Destroy(target);
            Destroy(root);
            Destroy(bakedBody);
            lastCapture = result;
        }
    }
}
