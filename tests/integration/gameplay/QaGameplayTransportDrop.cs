using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using VikingBoombox;

public sealed partial class QaGameplay
{
    private static bool traceVehicleQueries;
    private static readonly JArray vehicleQueries = new JArray();
    private static void VehicleOverlapResult(Vector3 __0, Vector3 __1, Collider[] __result)
    {
        if (traceVehicleQueries)
            vehicleQueries.Add(new JObject
            {
                ["center"] = Point(__0),
                ["half"] = Point(__1),
                ["hits"] = new JArray(__result.Select(c => c.name + "/" + c.transform.root.name))
            });
    }
    private IEnumerator CaseTransportDrop()
    {
        var player = Player.m_localPlayer;
        var plugin = BoomboxPlugin.Instance;
        var original = player.transform.position;
        var rotation = player.transform.rotation;
        var input = InputSystem.settings.backgroundBehavior;
        var distance = AccessTools.Field(typeof(GameCamera), "m_distance");
        float cameraDistance = (float)distance.GetValue(GameCamera.instance);
        var roots = new List<GameObject>();
        var rows = new JArray();
        var proof =
            new JObject
            {
                ["case"] = "K-02",
                ["scope"] =
                    "Actual inventory pick/drop-outside and E pickup on real Cart/Karve. No PutDown call for tested drop. Vehicle motion controlled for support verification.",
                ["rows"] = rows
            };
        lastCapture = proof;
        var observer = new Harmony("local.boombox.qa.vehicle-placement-observation");
        try
        {
            observer.Patch(
                AccessTools.Method(
                    typeof(Physics),
                    "OverlapBox",
                    new[] {
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(Quaternion),
                    typeof(int),
                    typeof(QueryTriggerInteraction)
                    }
                ),
                postfix: new HarmonyMethod(typeof(QaGameplay), nameof(VehicleOverlapResult))
            );
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return CaseReadyInventory();
            Freeze(player, false);
            foreach (string prefab in new[] { "Cart", "Karve" })
            {
                var root = UnityEngine.Object.Instantiate(
                    ZNetScene.instance.GetPrefab(prefab),
                    original + Vector3.up * 50 + Vector3.forward * 6,
                    Quaternion.identity
                );
                roots.Add(root);
                var body = root.GetComponent<Rigidbody>();
                root.GetComponent<ZNetView>().GetZDO().Set("boombox.qa.platform", true);
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
                Create("K02-" + prefab, new[] { 1, 1, 1, 1, 1 }, 0, false, player.transform.position);
                var deck = decks["K02-" + prefab];
                string id = deck.State.id;
                var before = CaseStable(deck.Item);
                Need(
                    CaseClearPlatformPlacement(body, deck, out var place, out var facing, out var support),
                    "K02 free platform area missing"
                );
                var box = deck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
                var surface = place + facing * new Vector3(box.center.x, box.min.y, box.center.z) - Vector3.up * .02f;
                Vector3 standing = surface;
                bool found = false;
                foreach (float offset in new[] { .7f, .5f, .3f, 0f })
                {
                    var point = surface - facing * Vector3.forward * offset;
                    if (Physics.Raycast(
                        point + Vector3.up,
                        Vector3.down,
                        out var foot,
                        2,
                        CasePlatformMask(),
                        QueryTriggerInteraction.Ignore
                    )
                    && foot.collider.attachedRigidbody == body)
                    {
                        standing = foot.point + Vector3.up * .06f;
                        found = true;
                        break;
                    }
                }
                Need(found, "K02 standing position missing");
                CasePlatformPlayer(player, standing, facing);
                yield return new WaitForSecondsRealtime(.6f);
                Need(
                    player.IsOnGround() && player.GetLastGroundCollider()?.attachedRigidbody == body,
                    "K02 native standing contact failed"
                );
                InventoryGui.instance.Show(null, 1);
                yield return new WaitForSecondsRealtime(.4f);
                Need((bool)UiDispatch(UiInventoryItem(deck.Item), false)["hitsTarget"], "K02 item selection missed");
                yield return null;
                proof["placementPrecondition"] = new JObject
                {
                    ["chosenYaw"] = facing.eulerAngles.y,
                    ["playerYaw"] = player.transform.eulerAngles.y,
                    ["player"] = Point(player.transform.position),
                    ["chosenPlace"] = Point(place),
                    ["boxSize"] = Point(box.size),
                    ["surfaces"] = new JArray(
                        body.GetComponentsInChildren<Collider>().Select(
                            c => c.name + ":" + LayerMask.LayerToName(c.gameObject.layer) + ":" + c.enabled + ":"
                                + c.isTrigger
                        )
                    )
                };
                vehicleQueries.Clear();
                traceVehicleQueries = true;
                bool dropHit;
                try
                {
                    dropHit = (bool)UiDispatch(
                        (RectTransform)InventoryGui.instance.m_dropButton.transform,
                        false
                    )["hitsTarget"];
                }
                finally
                {
                    traceVehicleQueries = false;
                }
                Need(dropHit, "K02 outside drop missed");
                yield return new WaitForSecondsRealtime(.3f);
                InventoryGui.instance.Hide();
                var drops = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(
                    d => BoomboxItemData.IsDevice(d.m_itemData) && BoomboxItemData.Read(d.m_itemData).deck.id == id
                ).ToArray();
                proof["dropOutcome"] = new JObject
                {
                    ["prefab"] = prefab,
                    ["notice"] = plugin.Notice,
                    ["worldCopies"] = drops.Length,
                    ["inventoryCopies"] = player.GetInventory().GetAllItems().Count(
                        i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == id
                    ),
                    ["placementQueries"] = vehicleQueries.DeepClone()
                };
                Need(
                    drops.Length == 1,
                    "K02 expected native placement did not create exactly one world item: " + plugin.Notice
                );
                var drop = drops[0];
                Need(JToken.DeepEquals(before, CaseStable(drop.m_itemData)), "K02 native drop changed payload");
                bool anchored = deck.PlatformAnchor.Platform == body.transform;
                var local = body.transform.InverseTransformPoint(deck.transform.position);
                var start = body.position;
                for (int step = 1; step <= 20; step++)
                {
                    body.MovePosition(start + body.transform.forward * (step / 20f));
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForEndOfFrame();
                }
                float drift = Vector3.Distance(local, body.transform.InverseTransformPoint(deck.transform.position));
                var row = new JObject
                {
                    ["prefab"] = prefab,
                    ["anchorMatches"] = anchored,
                    ["platformGuid"] = deck.State.platformGuid,
                    ["drift"] = drift,
                    ["before"] = before,
                    ["afterDrop"] = CaseStable(drop.m_itemData)
                };
                rows.Add(row);
                distance.SetValue(GameCamera.instance, 0f);
                GameObject hover = null;
                var aiming = new JArray();
                row["aiming"] = aiming;
                for (int n = 0; n < 15; n++)
                {
                    var center = drop.GetComponentsInChildren<Collider>().First(c => c.enabled
                        && !c.isTrigger).bounds.center;
                    var aim = center - GameCamera.instance.transform.position;
                    player.transform.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.z));
                    player.SetLookDir(aim, 0);
                    AccessTools.Field(typeof(Player), "m_lookPitch").SetValue(
                        player,
                        Mathf.Atan2(-aim.y, new Vector2(aim.x, aim.z).magnitude) * Mathf.Rad2Deg
                    );
                    yield return new WaitForSecondsRealtime(.1f);
                    hover = player.GetHoverObject();
                    aiming.Add(
                        new JObject
                        {
                            ["center"] = Point(center),
                            ["camera"] = Point(GameCamera.instance.transform.position),
                            ["hover"] = hover?.name
                        }
                    );
                    if (hover != null && hover.GetComponentInParent<ItemDrop>() == drop)
                        break;
                }
                Need(hover != null && hover.GetComponentInParent<ItemDrop>() == drop, "K02 pickup aim missed");
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                yield return UiAwait(
                    () => BoomboxEquipment.Get(player) != null
                        && BoomboxItemData.Read(BoomboxEquipment.Get(player)).deck.id == id,
                    "K02 pickup/equip"
                );
                var copies = player.GetInventory().GetAllItems().Where(
                    i => BoomboxItemData.IsDevice(i) && BoomboxItemData.Read(i).deck.id == id
                ).ToArray();
                Need(
                    copies.Length == 1 && JToken.DeepEquals(before, CaseStable(copies[0])),
                    "K02 pickup changed identity/payload"
                );
                row["afterPickup"] = CaseStable(copies[0]);
                row["inventoryCopies"] = copies.Length;
                Clear();
                ZNetScene.instance.Destroy(root);
                roots.Remove(root);
                CasePlatformPlayer(player, original, rotation);
                yield return null;
            }
            Need(
                rows.All(r => (bool)r["anchorMatches"] && (float)r["drift"] < .03f),
                "K02 native dropped device does not bind/follow vehicle"
            );
            proof["passed"] = true;
        }
        finally
        {
            traceVehicleQueries = false;
            observer.UnpatchSelf();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InventoryGui.instance.Hide();
            Clear();
            foreach (var root in roots)
                if (root != null)
                    ZNetScene.instance.Destroy(root);
            CasePlatformPlayer(player, original, rotation);
            Freeze(player, false);
            distance.SetValue(GameCamera.instance, cameraDistance);
            InputSystem.settings.backgroundBehavior = input;
        }
    }
}
