using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static int CasePlatformMask() => LayerMask.GetMask("terrain", "piece", "Default", "static_solid", "Default_small", "vehicle");
    private static bool CasePlatformSpot(Rigidbody body, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        var surfaces = body.GetComponentsInChildren<Collider>(true).Where(
            c => c.enabled && !c.isTrigger && c.attachedRigidbody == body
        ).ToArray();
        if (surfaces.Length == 0)
            return false;
        var bounds = surfaces[0].bounds;
        foreach (var c in surfaces.Skip(1))
            bounds.Encapsulate(c.bounds);
        foreach (float x in new[] { 0f, -.15f, .15f, -.3f, .3f })
            foreach (float z in new[] { 0f, -.15f, .15f, -.3f, .3f })
            {
                var origin = new Vector3(
                    bounds.center.x + bounds.size.x * x,
                    bounds.max.y + .5f,
                    bounds.center.z + bounds.size.z * z
                );
                if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var ground,
                    bounds.size.y + 1,
                    CasePlatformMask(),
                    QueryTriggerInteraction.Ignore
                )
                || ground.collider.attachedRigidbody != body
                || ground.normal.y < .65f)
                    continue;
                for (int direction = 0; direction < 8; direction++)
                {
                    var facing = Quaternion.Euler(0, direction * 45, 0);
                    var standing = ground.point + Vector3.up * .06f;
                    if (!Physics.Raycast(
                        standing + facing * Vector3.forward * 1.3f + Vector3.up * 2,
                        Vector3.down,
                        out var hit,
                        6,
                        CasePlatformMask(),
                        QueryTriggerInteraction.Ignore
                    )
                    || hit.collider.attachedRigidbody != body
                    || hit.normal.y < .65f
                    || Mathf.Abs(hit.point.y - ground.point.y) > .3f)
                        continue;
                    position = standing;
                    rotation = facing;
                    return true;
                }
            }
        return false;
    }
    private static void CasePlatformPlayer(Player player, Vector3 position, Quaternion rotation)
    {
        player.transform.SetPositionAndRotation(position, rotation);
        player.SetLookDir(rotation * Vector3.forward, 0);
        player.SetMouseLookForward(false);
        var body = player.GetComponent<Rigidbody>();
        body.position = position;
        body.rotation = rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    private static bool CaseClearPlatformPlacement(
        Rigidbody body,
        AudioDeck deck,
        out Vector3 position,
        out Quaternion rotation,
        out Collider support
    )
    {
        position = default;
        rotation = Quaternion.identity;
        support = null;
        var box = deck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
        var surfaces = body.GetComponentsInChildren<Collider>().Where(c => c.enabled
            && !c.isTrigger
            && c.attachedRigidbody == body).ToArray();
        var bounds = surfaces[0].bounds;
        foreach (var c in surfaces.Skip(1))
            bounds.Encapsulate(c.bounds);
        foreach (float x in new[] { 0f, -.1f, .1f, -.2f, .2f, -.3f, .3f, -.4f, .4f })
            foreach (float z in new[] { 0f, -.1f, .1f, -.2f, .2f, -.3f, .3f, -.4f, .4f })
            {
                var origin = new Vector3(
                    bounds.center.x + bounds.size.x * x,
                    bounds.max.y + .5f,
                    bounds.center.z + bounds.size.z * z
                );
                if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var hit,
                    bounds.size.y + 1,
                    CasePlatformMask(),
                    QueryTriggerInteraction.Ignore
                )
                || hit.collider.attachedRigidbody != body
                || hit.normal.y < .65f)
                    continue;
                foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                {
                    var turn = body.rotation * Quaternion.Euler(0, yaw, 0);
                    var root = hit.point - turn * new Vector3(box.center.x, box.min.y, box.center.z) + Vector3.up * .02f;
                    bool supported = true;
                    foreach (float dx in new[] { -box.extents.x, box.extents.x })
                        foreach (float dz in new[] { -box.extents.z, box.extents.z })
                        {
                            var corner = root + turn * new Vector3(box.center.x + dx, box.min.y, box.center.z + dz);
                            if (!Physics.Raycast(
                                corner + Vector3.up * .15f,
                                Vector3.down,
                                out var foot,
                                .3f,
                                CasePlatformMask(),
                                QueryTriggerInteraction.Ignore
                            )
                            || foot.collider.attachedRigidbody != body
                            || Mathf.Abs(foot.point.y - hit.point.y) > .05f)
                                supported = false;
                        }
                    if (!supported)
                        continue;
                    var half = Vector3.Max(box.extents - Vector3.one * .015f, Vector3.one * .01f);
                    if (Physics.OverlapBox(
                        root + turn * box.center,
                        half,
                        turn,
                        CasePlatformMask(),
                        QueryTriggerInteraction.Ignore
                    ).Any(
                        c => c.attachedRigidbody == body
                    ))
                        continue;
                    position = root;
                    rotation = turn;
                    support = hit.collider;
                    return true;
                }
            }
        return false;
    }
    private IEnumerator CasePlatformMove(
        AudioDeck deck,
        Rigidbody body,
        Transform reference,
        bool carried,
        JArray rows,
        string prefab
    )
    {
        var start = body.position;
        var rotation = body.rotation;
        var target = start + rotation * Vector3.forward * 5;
        var turn = Quaternion.AngleAxis(30, Vector3.up) * rotation;
        var local = reference.InverseTransformPoint(deck.transform.position);
        var localRotation = Quaternion.Inverse(reference.rotation) * deck.transform.rotation;
        var before = CaseStable(deck.Item);
        float error = 0, angle = 0;
        for (int step = 1; step <= 60; step++)
        {
            body.Move(Vector3.Lerp(start, target, step / 60f), Quaternion.Slerp(rotation, turn, step / 60f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();
            error = Mathf.Max(error, Vector3.Distance(local, reference.InverseTransformPoint(deck.transform.position)));
            angle = Mathf.Max(
                angle,
                Quaternion.Angle(localRotation, Quaternion.Inverse(reference.rotation) * deck.transform.rotation)
            );
        }
        body.GetComponent<ZSyncTransform>().SyncNow();
        Need(
            Vector3.Distance(body.position, target) < .05f && Quaternion.Angle(body.rotation, turn) < 1,
            "K01 native platform stimulus incomplete"
        );
        Need(
            error < (carried ? .08f : .03f) && angle < (carried ? 8f : 2f),
            "K01 local pose drift: " + prefab + " carried=" + carried + " pos=" + error + " angle=" + angle
        );
        Need(
            JToken.DeepEquals(before, CaseStable(deck.Item)) && deck.IsSyntheticActive && deck.State.onBack == carried,
            "K01 identity/music state changed"
        );
        rows.Add(new JObject
        {
            ["prefab"] = prefab,
            ["carried"] = carried,
            ["travel"] = Vector3.Distance(start, body.position),
            ["turn"] = Quaternion.Angle(rotation, body.rotation),
            ["positionError"] = error,
            ["rotationError"] = angle,
            ["before"] = before,
            ["after"] = CaseStable(deck.Item)
        });
    }
    private JObject CasePlatformFrame(AudioDeck deck, Camera camera, RenderTexture target, string name)
    {
        var visual = deck.Visual.GetComponent<VikingBoomboxVisualState>();
        var center = deck.Visual.transform.TransformPoint(visual.BodyBounds.center);
        var outward = center - visual.BackMountAnchor.position;
        outward.y = 0;
        if (outward.sqrMagnitude < .0001f)
            outward = deck.transform.forward;
        outward.Normalize();
        var side = Vector3.Cross(Vector3.up, outward);
        camera.transform.position = center + outward * 2.2f + side * 1.1f
            + Vector3.up * (name.Contains("placed") || name.Contains("Karve") ? 1.8f : .8f);
        camera.transform.LookAt(center);
        var result = ContextPng(camera, target, name);
        result["view"] = "Front quarter of boombox, outward from its back mounting anchor";
        return result;
    }
    private IEnumerator CaseTransportMotion()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var original = player.transform.position;
        var rotation = player.transform.rotation;
        bool day = EnvMan.instance.m_debugTimeOfDay;
        float time = EnvMan.instance.m_debugTime;
        var roots = new List<GameObject>();
        var rows = new JArray();
        var frames = new JArray();
        var proof =
            new JObject
            {
                ["case"] = "K-01",
                ["scope"] =
                    "Real Cart/Karve, native helm/ground contact and ZSync. Controlled kinematic travel 5m/30deg; not sailing/towing acceptance. Placement is fixture setup through retained PutDown; native drop is separate K-02.",
                ["rows"] = rows,
                ["frames"] = frames
            };
        lastCapture = proof;
        var cameraRoot = new GameObject("QA platform observation camera");
        var camera = cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main);
        camera.enabled = false;
        camera.fieldOfView = 45;
        camera.aspect = 1280f / 960;
        var texture = new RenderTexture(1280, 960, 24);
        texture.Create();
        try
        {
            yield return CaseReadyInventory();
            Freeze(player, false);
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = .5f;
            Need(Mathf.Abs(plugin.Settings.BackScale.Value - .8f) < .0001f, "Approved carried scale changed");
            foreach (string prefab in new[] { "Cart", "Karve" })
            {
                var root = UnityEngine.Object.Instantiate(
                    ZNetScene.instance.GetPrefab(prefab),
                    original + Vector3.up * 10 + Vector3.forward * 6,
                    Quaternion.identity
                );
                roots.Add(root);
                var body = root.GetComponent<Rigidbody>();
                var view = root.GetComponent<ZNetView>();
                Need(body != null && view != null && view.IsValid(), "K01 real platform components missing");
                view.GetZDO().Set("boombox.qa.platform", true);
                if (root.GetComponent<Ship>() is Ship ship)
                    ship.enabled = false;
                if (root.GetComponent<Vagon>() is Vagon cart)
                    cart.enabled = false;
                foreach (var part in root.GetComponentsInChildren<Rigidbody>())
                {
                    part.linearVelocity = Vector3.zero;
                    part.angularVelocity = Vector3.zero;
                    part.isKinematic = true;
                }
                Physics.SyncTransforms();
                Create("K01-" + prefab, new[] { 1, 1, 1, 1, 1 }, 0, false, player.transform.position);
                var deck = decks["K01-" + prefab];
                if (prefab == "Karve")
                {
                    var helm = root.GetComponentInChildren<ShipControlls>(true);
                    Need(helm != null && helm.m_attachPoint != null, "K01 native helm absent");
                    player.AttachStart(
                        helm.m_attachPoint,
                        null,
                        false,
                        false,
                        true,
                        helm.m_attachAnimation,
                        helm.m_detachOffset,
                        null
                    );
                    yield return new WaitForSecondsRealtime(.5f);
                    Need(player.IsAttachedToShip(), "K01 native helm attachment failed");
                }
                else
                {
                    Need(
                        CasePlatformSpot(body, out var standing, out var facing),
                        "K01 Cart standing/placement pair missing"
                    );
                    CasePlatformPlayer(player, standing, facing);
                    yield return new WaitForSecondsRealtime(.6f);
                    Need(
                        player.IsOnGround() && player.GetLastGroundCollider()?.attachedRigidbody == body,
                        "K01 Cart ground contact absent"
                    );
                }
                yield return new WaitForSecondsRealtime(.3f);
                Need(deck.Mount.BackAnchor != null, "K01 back reference missing");
                yield return CasePlatformMove(deck, body, deck.Mount.BackAnchor, true, rows, prefab);
                frames.Add(CasePlatformFrame(deck, camera, texture, "K01-" + prefab + "-carried.png"));
                player.StopDoodadControl();
                player.AttachStop();
                CasePlatformPlayer(player, original, rotation);
                Need(
                    CaseClearPlatformPlacement(body, deck, out var place, out var look, out var support),
                    "K01 clear supported fixture area missing"
                );
                Need(plugin.Items.PutDown(deck, place, look, support), "K01 fixture placement refused");
                yield return new WaitForSecondsRealtime(.2f);
                Need(deck.PlatformAnchor.Platform == body.transform, "K01 placed support mismatch");
                yield return CasePlatformMove(deck, body, body.transform, false, rows, prefab);
                frames.Add(CasePlatformFrame(deck, camera, texture, "K01-" + prefab + "-placed.png"));
                Clear();
                ZNetScene.instance.Destroy(root);
                roots.Remove(root);
                CasePlatformPlayer(player, original, rotation);
                yield return null;
            }
            proof["actionsPassed"] = true;
            proof["visualReviewRequired"] = true;
            proof["passed"] = false;
        }
        finally
        {
            player.StopDoodadControl();
            player.AttachStop();
            Clear();
            foreach (var root in roots)
                if (root != null)
                    ZNetScene.instance.Destroy(root);
            CasePlatformPlayer(player, original, rotation);
            Freeze(player, false);
            EnvMan.instance.m_debugTimeOfDay = day;
            EnvMan.instance.m_debugTime = time;
            texture.Release();
            Destroy(texture);
            Destroy(cameraRoot);
        }
    }
}
