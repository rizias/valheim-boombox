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
    private static bool capeDrive;
    private static Vector3 capeDirection;
    private static bool CapeController()
    {
        if (!capeDrive || Player.m_localPlayer == null)
            return true;
        Player.m_localPlayer.SetControls(
            capeDirection,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            capeDirection.sqrMagnitude > 0,
            false
        );
        return false;
    }
    private IEnumerator CapeAndGlow(JObject result)
    {
        var player = Player.m_localPlayer;
        var origin = player.transform.position;
        var rotation = player.transform.rotation;
        var oldCape = (ItemDrop.ItemData)AccessTools.Field(typeof(Humanoid), "m_shoulderItem").GetValue(player);
        var rows = new JArray();
        var frames = new JArray();
        result["capeCases"] = rows;
        result["capeFrames"] = frames;
        var patch = new Harmony("local.boombox.qa.cape-current");
        patch.Patch(
            AccessTools.Method(typeof(PlayerController), "FixedUpdate"),
            prefix: new HarmonyMethod(typeof(QaGameplay), nameof(CapeController))
        );
        capeDrive = true;
        capeDirection = Vector3.zero;
        Floor(origin);
        Freeze(player, false);
        var cameraRoot = new GameObject("QA cape camera");
        var camera = cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.fieldOfView = 45;
        camera.aspect = 1280f / 960;
        var target = new RenderTexture(1280, 960, 24);
        target.Create();
        ItemDrop.ItemData cape = null;
        try
        {
            foreach (string name in new[] { "CapeDeerHide", "CapeTrollHide" })
            {
                var prefab = ObjectDB.instance.GetItemPrefab(name);
                cape = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                cape.m_dropPrefab = prefab;
                Need(
                    player.GetInventory().AddItem(cape) && player.EquipItem(cape, false),
                    "Native cape equip refused: " + name
                );
                yield return new WaitForSecondsRealtime(2);
                foreach (string pose in new[] { "standing", "running", "crouching" })
                {
                    player.transform.SetPositionAndRotation(origin, rotation);
                    var body = player.GetComponent<Rigidbody>();
                    body.position = origin;
                    body.linearVelocity = Vector3.zero;
                    AccessTools.Method(typeof(Player), "SetCrouch").Invoke(
                        player,
                        new object[] { pose == "crouching" }
                    );
                    capeDirection = pose == "running" ? player.transform.right : Vector3.zero;
                    yield return new WaitForSecondsRealtime(1.5f);
                    var visual = audioDeck.Visual;
                    var bounds = visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
                    var equipment = (List<GameObject>)AccessTools.Field(typeof(VisEquipment), "m_shoulderItemInstances")
                        .GetValue(
                        player.GetVisEquipment()
                    );
                    var skins = equipment.Where(o => o != null).SelectMany(
                        o => o.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    ).Where(
                        r => r.enabled && r.gameObject.activeInHierarchy
                    ).ToArray();
                    int maximum = 0;
                    float minimumOffset = float.PositiveInfinity, maximumOffset = float.NegativeInfinity;
                    var baked = new Mesh();
                    try
                    {
                        for (int n = 0; n < 15; n++)
                        {
                            yield return new WaitForEndOfFrame();
                            int inside = 0;
                            foreach (var skin in skins)
                            {
                                skin.BakeMesh(baked, false);
                                foreach (var vertex in baked.vertices)
                                {
                                    var p = visual.transform.InverseTransformPoint(
                                        skin.transform.TransformPoint(vertex)
                                    );
                                    if (p.x > bounds.min.x + .002f
                                        && p.x < bounds.max.x - .002f
                                        && p.y > bounds.min.y + .002f
                                        && p.y < bounds.max.y - .002f
                                        && p.z > bounds.min.z + .002f
                                        && p.z < bounds.max.z - .002f)
                                        inside++;
                                }
                            }
                            maximum = Mathf.Max(maximum, inside);
                            minimumOffset = Mathf.Min(minimumOffset, audioDeck.Mount.OutwardOffset);
                            maximumOffset = Mathf.Max(maximumOffset, audioDeck.Mount.OutwardOffset);
                        }
                    }
                    finally
                    {
                        Destroy(baked);
                    }
                    rows.Add(new JObject
                    {
                        ["cape"] = name,
                        ["pose"] = pose,
                        ["samples"] = 15,
                        ["skins"] = skins.Length,
                        ["insideVertices"] = maximum,
                        ["offsetVariation"] = maximumOffset - minimumOffset,
                        ["passed"] = skins.Length > 0 && maximum == 0 && maximumOffset - minimumOffset < .0001f
                    });
                    var center = visual.transform.TransformPoint(bounds.center);
                    camera.transform.position = center + visual.transform.right * 1.5f + visual.transform.forward * .3f
                        + Vector3.up * .15f;
                    camera.transform.LookAt(center);
                    frames.Add(ContextPng(camera, target, "current-" + name + "-" + pose + ".png"));
                    capeDirection = Vector3.zero;
                }
                player.UnequipItem(cape, false);
                player.GetInventory().RemoveItem(cape);
                cape = null;
            }
            var settings = BoomboxPlugin.Instance.Settings;
            float originalSetting = settings.YellowMushroomGlow.Value;
            var mushroom = audioDeck.Visual.GetComponentsInChildren<Renderer>(false).First(
                r => r.sharedMaterial != null && r.sharedMaterial.name == "Boletus_Yellow"
            );
            var original = mushroom.sharedMaterial.GetColor("_EmissionColor");
            var glow = new JArray();
            result["mushroomGlow"] = glow;
            try
            {
                foreach (float value in new[] { 0f, .25f, 1f, 2f })
                {
                    settings.YellowMushroomGlow.Value = value;
                    yield return new WaitForSecondsRealtime(.2f);
                    var block = new MaterialPropertyBlock();
                    mushroom.GetPropertyBlock(block, 0);
                    var actual = block.GetColor("_EmissionColor");
                    var expected = original * value;
                    bool match = Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g)
                        + Mathf.Abs(actual.b - expected.b)
                        < .0001f;
                    glow.Add(new JObject { ["value"] = value, ["actual"] = actual.ToString("F5"), ["passed"] = match });
                }
            }
            finally
            {
                settings.YellowMushroomGlow.Value = originalSetting;
            }
        }
        finally
        {
            capeDrive = false;
            capeDirection = Vector3.zero;
            patch.UnpatchSelf();
            AccessTools.Method(typeof(Player), "SetCrouch").Invoke(player, new object[] { false });
            if (cape != null)
            {
                player.UnequipItem(cape, false);
                player.GetInventory().RemoveItem(cape);
            }
            if (oldCape != null)
                player.EquipItem(oldCape, false);
            player.transform.SetPositionAndRotation(origin, rotation);
            target.Release();
            Destroy(target);
            Destroy(cameraRoot);
        }
    }
}
