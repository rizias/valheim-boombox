using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    [HarmonyPatch(typeof(Player), "FindHoverObject")]
    internal static class BoomboxVehicleHover
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[64];
        private static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create(
            (a, b) => a.distance.CompareTo(b.distance)
        );

        private static void Postfix(
            Player __instance,
            ref GameObject hover,
            Transform ___m_eye,
            float ___m_maxInteractDistance,
            int ___m_interactMask
        )
        {
            if (__instance != Player.m_localPlayer || hover == null || GameCamera.instance == null || ___m_eye == null)
                return;
            var container = hover.GetComponentInParent<Container>();
            if (container == null
                || container.GetComponentInParent<Ship>() == null
                && container.GetComponentInParent<Vagon>() == null)
                return;
            var camera = GameCamera.instance.transform;
            int count = Physics.RaycastNonAlloc(camera.position, camera.forward, Hits, 50f, ___m_interactMask);
            if (count == Hits.Length)
                return;
            System.Array.Sort(Hits, 0, count, ByDistance);
            int itemLayer = LayerMask.NameToLayer("item");
            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                var collider = hit.collider;
                if (collider.attachedRigidbody != null
                    && collider.attachedRigidbody.gameObject == __instance.gameObject)
                    continue;
                if (collider.gameObject.layer == itemLayer && collider.GetComponentInParent<Container>() == container)
                    continue;
                var drop = collider.GetComponentInParent<ItemDrop>();
                var hoverable = collider.GetComponentInParent<Hoverable>();
                if (drop != null
                    && BoomboxItemData.IsDevice(drop.m_itemData)
                    && Vector3.Distance(___m_eye.position, hit.point)
                    < ___m_maxInteractDistance + (hoverable?.GetHoverOffset() ?? 0f))
                    hover = drop.gameObject;
                return;
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), "DropItem", new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int) })]
    internal static class BoomboxVehicleDrop
    {
        private static bool Prefix(
            Humanoid __instance,
            Inventory inventory,
            ItemDrop.ItemData item,
            int amount,
            ref bool __result
        )
        {
            var player = Player.m_localPlayer;
            var plugin = BoomboxPlugin.Instance;
            if (__instance != player || player == null || plugin == null || !plugin.AudioWorldActive || player.IsDead()
            || !BoomboxItemData.IsDevice(item) || amount <= 0 || item.m_shared.m_questItem
            || inventory != null && inventory != player.GetInventory() || !player.GetInventory().ContainsItem(item))
                return true;
            var platform = BoomboxPlatformAnchor.Support(player.GetLastGroundCollider());
            if (player.IsAttachedToShip())
                platform = player.GetAttachPoint()?.GetComponentInParent<Ship>()?.GetComponent<ZNetView>();
            if (platform == null || !platform.IsValid())
                return true;
            var deck = plugin.Items.ForItem(item);
            if (!FindPlace(player, deck, platform, out var position, out var rotation, out var support))
            {
                plugin.Notify("text.there_is_no_free_space_for_the_boombox_on_this_vehicle");
                player.Message(MessageHud.MessageType.Center, plugin.Notice);
                __result = false;
                return false;
            }
            player.RemoveEquipAction(item);
            __result = plugin.Items.PutDown(deck, position, rotation, support);
            return false;
        }

        private static bool FindPlace(
            Player player,
            AudioDeck deck,
            ZNetView platform,
            out Vector3 position,
            out Quaternion rotation,
            out Collider support
        )
        {
            position = default;
            rotation = Quaternion.Euler(0, player.transform.eulerAngles.y, 0);
            support = null;
            var body = platform.GetComponent<Rigidbody>();
            if (body == null)
                return false;
            int surfaceMask = LayerMask.GetMask(
                "terrain",
                "piece",
                "Default",
                "static_solid",
                "Default_small",
                "vehicle"
            );
            var surfaces = body.GetComponentsInChildren<Collider>().Where(
                c => c.enabled
                    && !c.isTrigger
                    && c.attachedRigidbody == body
                    && (surfaceMask & (1 << c.gameObject.layer)) != 0
            ).ToArray();
            if (surfaces.Length == 0)
                return false;
            var bounds = surfaces[0].bounds;
            foreach (var collider in surfaces.Skip(1))
                bounds.Encapsulate(collider.bounds);
            var box = deck.Visual.GetComponent<VikingBoomboxVisualState>().BodyBounds;
            var desired = player.transform.position + player.transform.forward;
            var candidates = new List<Vector3> { desired, player.transform.position + player.transform.forward * .5f };
            foreach (float x in new[] { 0f, -.1f, .1f, -.2f, .2f, -.3f, .3f, -.4f, .4f })
                foreach (float z in new[] { 0f, -.1f, .1f, -.2f, .2f, -.3f, .3f, -.4f, .4f })
                    candidates.Add(
                        new Vector3(bounds.center.x + bounds.size.x * x, desired.y, bounds.center.z + bounds.size.z * z)
                    );
            int obstacles = LayerMask.GetMask(
                "terrain",
                "piece",
                "Default",
                "static_solid",
                "Default_small",
                "vehicle",
                "item",
                "character",
                "character_net",
                "character_noenv",
                "hitbox"
            );
            int itemLayer = LayerMask.NameToLayer("item");
            foreach (var candidate in candidates.OrderBy(p => (p - desired).sqrMagnitude))
            {
                var ray = new Ray(new Vector3(candidate.x, bounds.max.y + .5f, candidate.z), Vector3.down);
                if (!Surface(
                    surfaces,
                    ray,
                    bounds.size.y + 1f,
                    out var hit
                )
                    || hit.normal.y < .65f
                    || Mathf.Abs(hit.point.y - player.transform.position.y) > 1.2f
                    || Vector3.Distance(hit.point, player.transform.position) > 3f)
                    continue;
                var root = hit.point - rotation * new Vector3(box.center.x, box.min.y, box.center.z) + Vector3.up * .02f;
                bool supported = true;
                foreach (float x in new[] { -box.extents.x, box.extents.x })
                    foreach (float z in new[] { -box.extents.z, box.extents.z })
                    {
                        var corner = root + rotation * new Vector3(box.center.x + x, box.min.y, box.center.z + z);
                        if (!Surface(surfaces, new Ray(corner + Vector3.up * .15f, Vector3.down), .3f, out var foot)
                        || foot.normal.y < .65f || Mathf.Abs(foot.point.y - hit.point.y) > .05f)
                            supported = false;
                    }
                if (!supported)
                    continue;
                var half = Vector3.Max(box.extents - Vector3.one * .015f, Vector3.one * .01f);
                if (Physics.OverlapBox(
                    root + rotation * box.center,
                    half,
                    rotation,
                    obstacles,
                    QueryTriggerInteraction.Ignore
                )
                .Any(c => !c.transform.IsChildOf(deck.transform)
                    && !(c.gameObject.layer == itemLayer && c.GetComponentInParent<Container>() != null)))
                    continue;
                position = root;
                support = hit.collider;
                return true;
            }
            return false;
        }

        private static bool Surface(Collider[] surfaces, Ray ray, float distance, out RaycastHit hit)
        {
            hit = default;
            bool found = false;
            foreach (var collider in surfaces)
                if (collider.Raycast(ray, out var candidate, distance) && (!found || candidate.distance < hit.distance))
                {
                    hit = candidate;
                    found = true;
                }
            return found;
        }
    }
}
