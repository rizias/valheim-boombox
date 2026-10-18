using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private IEnumerator ShieldCatalog(Player player, ItemDrop.ItemData cape, JObject result)
    {
        var allShields = ObjectDB.instance.m_items.Where(
            p => p != null && p.GetComponent<ItemDrop>() != null
                && p.GetComponent<ItemDrop>().m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield
        ).GroupBy(
            p => p.name
        ).Select(
            g => g.First()
        ).OrderBy(
            p => p.name
        ).ToArray();
        var shields = allShields.Where(p => p.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons.Length > 0).ToArray();
        result["nonInventoryShieldPrefabs"] = new JArray(allShields.Except(shields).Select(p => p.name));
        Need(shields.Length > 0 && shields.Length <= 48, "Unexpected native shield catalogue size");
        var rows = new JArray();
        result["shieldCases"] = rows;
        result["shieldPrefabs"] = new JArray(shields.Select(p => p.name));
        var root = new GameObject("QA shield geometry camera");
        var camera = root.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = .85f;
        camera.aspect = 1;
        var target = new RenderTexture(512, 512, 24);
        target.Create();
        var pixels = new Texture2D(512, 512, TextureFormat.RGB24, false);
        var atlas = new Texture2D(1024, 2048, TextureFormat.RGB24, false);
        var backField = AccessTools.Field(typeof(VisEquipment), "m_leftBackItemInstance");
        var readMesh = typeof(AudioDeck).Assembly.GetType("VikingBoombox.BoomboxHeadPlacement").GetMethod(
            "CollisionMesh",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        ItemDrop.ItemData shield = null;
        try
        {
            for (int index = 0; index < shields.Length; index++)
            {
                var prefab = shields[index];
                shield = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                shield.m_dropPrefab = prefab;
                shield.m_variant = 0;
                Need(player.GetInventory().AddItem(shield), "Shield inventory fixture failed");
                var previous = backField.GetValue(player.GetVisEquipment()) as GameObject;
                Need(
                    player.EquipItem(shield, false) && player.HideHandItems(false, false),
                    "Native shield equip failed: " + prefab.name
                );
                float deadline = Time.realtimeSinceStartup + 2;
                while (backField.GetValue(player.GetVisEquipment()) as GameObject == null
                || backField.GetValue(player.GetVisEquipment()) as GameObject == previous)
                {
                    Need(Time.realtimeSinceStartup < deadline, "Shield visual did not update: " + prefab.name);
                    yield return null;
                }
                for (int withCape = 0; withCape < 2; withCape++)
                {
                    if (withCape == 0)
                        player.UnequipItem(cape, false);
                    else
                        Need(player.EquipItem(cape, false), "Shield/cape equip failed");
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var back = (GameObject)backField.GetValue(player.GetVisEquipment());
                    var visual = audioDeck.Visual;
                    var bounds = visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
                    var center = player.transform.position + Vector3.up * 1.3f;
                    camera.transform.position = center + player.transform.right * 2f - player.transform.forward * .45f;
                    camera.transform.LookAt(center, Vector3.up);
                    camera.targetTexture = target;
                    camera.Render();
                    camera.targetTexture = null;
                    var prior = RenderTexture.active;
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                    pixels.Apply();
                    RenderTexture.active = prior;
                    atlas.SetPixels(withCape * 512, (3 - index % 4) * 512, 512, 512, pixels.GetPixels());
                    var colliders = new List<MeshCollider>();
                    var temporaryMeshes = new List<Mesh>();
                    int hits = 0;
                    float minimum = float.PositiveInfinity;
                    bool projectionOverlap = false;
                    try
                    {
                        foreach (var filter in back.GetComponentsInChildren<MeshFilter>(false))
                        {
                            if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null
                            || !filter.GetComponent<MeshRenderer>().enabled)
                                continue;
                            var holder = new GameObject("QA shield surface");
                            holder.layer = 31;
                            holder.transform.SetPositionAndRotation(
                                filter.transform.position,
                                filter.transform.rotation
                            );
                            holder.transform.localScale = filter.transform.lossyScale;
                            var collider = holder.AddComponent<MeshCollider>();
                            collider.sharedMesh = (Mesh)readMesh.Invoke(null, new object[] { filter });
                            colliders.Add(collider);
                        }
                        foreach (var skin in back.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(
                            s => s.enabled && s.sharedMesh != null
                        ))
                        {
                            var mesh = new Mesh();
                            skin.BakeMesh(mesh, true);
                            temporaryMeshes.Add(mesh);
                            var holder = new GameObject("QA skinned shield surface");
                            holder.layer = 31;
                            holder.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                            holder.transform.localScale = skin.transform.lossyScale;
                            var collider = holder.AddComponent<MeshCollider>();
                            collider.sharedMesh = mesh;
                            colliders.Add(collider);
                        }
                        Physics.SyncTransforms();
                        foreach (var collider in colliders)
                        {
                            var projected = new Bounds();
                            bool first = true;
                            var worldBounds = collider.bounds;
                            for (int corner = 0; corner < 8; corner++)
                            {
                                var point = visual.transform.InverseTransformPoint(
                                    worldBounds.center + Vector3.Scale(
                                        worldBounds.extents,
                                        new Vector3(
                                            (corner & 1) == 0
                                                ? -1
                                                : 1,
                                            (corner & 2) == 0
                                                ? -1
                                                : 1,
                                            (corner & 4) == 0
                                                ? -1
                                                : 1
                                        )
                                    )
                                );
                                if (first)
                                {
                                    projected = new Bounds(point, Vector3.zero);
                                    first = false;
                                }
                                else
                                    projected.Encapsulate(point);
                            }
                            projectionOverlap |= projected.max.x >= bounds.min.x
                                && projected.min.x <= bounds.max.x
                                && projected.max.y >= bounds.min.y
                                && projected.min.y <= bounds.max.y;
                        }
                        for (int y = 0; y < 5; y++)
                            for (int x = 0; x < 5; x++)
                            {
                                var point = new Vector3(
                                    Mathf.Lerp(bounds.min.x, bounds.max.x, (x + .5f) / 5),
                                    Mathf.Lerp(bounds.min.y, bounds.max.y, (y + .5f) / 5),
                                    bounds.max.z + .01f
                                );
                                var ray = new Ray(visual.transform.TransformPoint(point), -visual.transform.forward);
                                foreach (var collider in colliders)
                                    if (collider.Raycast(ray, out var hit, 1.5f))
                                    {
                                        hits++;
                                        minimum = Mathf.Min(minimum, hit.distance);
                                    }
                            }
                    }
                    finally
                    {
                        foreach (var collider in colliders)
                        {
                            collider.gameObject.SetActive(false);
                            Destroy(collider.gameObject);
                        }
                        foreach (var mesh in temporaryMeshes)
                            Destroy(mesh);
                    }
                    float rearDistance = (bounds.max.z + .01f - bounds.min.z) * visual.transform.lossyScale.z;
                    bool passed = hits > 0
                        ? minimum >= rearDistance - .002f
                        : colliders.Count > 0 && !projectionOverlap;
                    rows.Add(new JObject
                    {
                        ["shield"] = prefab.name,
                        ["cape"] = withCape != 0,
                        ["surfaceHits"] = hits,
                        ["minimumRearGap"] = hits == 0 ? -1 : minimum - rearDistance,
                        ["outwardOffset"] = audioDeck.Mount.OutwardOffset,
                        ["atlas"] = index / 4,
                        ["row"] = index % 4,
                        ["column"] = withCape,
                        ["passed"] = passed
                    });
                    ((JObject)rows.Last)["projectionOverlap"] = projectionOverlap;
                }
                player.UnequipItem(shield, false);
                player.GetInventory().RemoveItem(shield);
                shield = null;
                if (index % 4 == 3 || index == shields.Length - 1)
                {
                    atlas.Apply();
                    string name = "shield-catalog-" + (index / 4).ToString("D2") + ".png";
                    var bytes = atlas.EncodeToPNG();
                    Need(bytes.Length < 4 * 1024 * 1024, "Shield atlas byte cap");
                    File.WriteAllBytes(Path.Combine(BepInEx.Paths.ConfigPath, "capture/support", name), bytes);
                    ((JArray)result["frames"]).Add(
                        new JObject { ["file"] = name, ["bytes"] = bytes.Length, ["actualScene"] = true }
                    );
                }
            }
            result["shieldCatalogPassed"] = rows.All(r => (bool)r["passed"]);
        }
        finally
        {
            if (shield != null)
            {
                player.UnequipItem(shield, false);
                player.GetInventory().RemoveItem(shield);
            }
            player.UnequipItem(cape, false);
            target.Release();
            Destroy(target);
            Destroy(pixels);
            Destroy(atlas);
            Destroy(root);
        }
    }
}
