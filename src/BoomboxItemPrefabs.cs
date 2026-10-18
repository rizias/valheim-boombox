using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    public static class BoomboxItemPrefabs
    {
        public const int EquipmentType = 8371;
        private static GameObject container;
        private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        private static Sprite cassetteIcon;
        public static bool Ready => prefabs.Count == BoomboxItemData.Properties.Length + 3;

        public static void Register(ObjectDB database, ZNetScene scene, bool updateSceneIndex = true)
        {
            if (database == null || database.m_items == null)
                return;
            var template = database.m_items.FirstOrDefault(p => p != null && p.name == "Wood");
            if (template == null)
                return;
            if (container == null)
            {
                container = new GameObject("Boombox item prefabs");
                container.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(container);
            }
            if (!Ready)
            {
                Create(template, BoomboxItemData.DevicePrefab, VikingBoomboxVisual.ModelPrefabKey, true);
                foreach (string property in BoomboxItemData.Properties)
                    Create(template, BoomboxItemData.CassettePrefix + property, "Cassette" + property, false);
                Create(template, BoomboxItemData.ArchivePrefab, "CassetteRed", false);
                Create(template, BoomboxItemData.UpgradedPrefab, "CassetteRed", false);
            }
            foreach (var prefab in prefabs.Values)
            {
                if (!database.m_items.Contains(prefab))
                    database.m_items.Add(prefab);
                if (scene != null)
                {
                    if (!scene.m_prefabs.Contains(prefab))
                        scene.m_prefabs.Add(prefab);
                    var named = (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs")
                        .GetValue(scene);
                    if (updateSceneIndex)
                        named[prefab.name.GetStableHashCode()] = prefab;
                }
            }
            AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(database, null);
            if (scene != null)
                RegisterRecipes(database, scene);
        }

        private static void Create(GameObject template, string name, string modelKey, bool device)
        {
            if (prefabs.ContainsKey(name))
                return;
            var root = UnityEngine.Object.Instantiate(template, container.transform, false);
            root.name = name;
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (var collider in root.GetComponents<Collider>())
                UnityEngine.Object.DestroyImmediate(collider);
            var visual = VikingBoomboxVisual.CreateAsset(modelKey, root.transform);
            visual.name = "attach";
            if (VikingBoomboxVisual.CanRender && name == BoomboxItemData.ArchivePrefab)
            {
                BoomboxCassetteAppearance.Apply(visual, new string[0]);
                foreach (var node in visual.GetComponentsInChildren<Transform>(true))
                    if (node.name.StartsWith("Property", StringComparison.Ordinal))
                        node.gameObject.SetActive(false);
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(material =>
                    {
                        if (!material.name.StartsWith("CassetteLabel", StringComparison.Ordinal))
                            return material;
                        var neutral = new Material(material);
                        VikingBoomboxVisualState.Color(neutral, new Color(.45f, .45f, .42f));
                        return neutral;
                    }).ToArray();
            }
            var bounds = BoundsOf(visual);
            var box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            var body = root.GetComponent<Rigidbody>();
            body.mass = device ? 8f : 0.2f;
            var drop = root.GetComponent<ItemDrop>();
            drop.m_autoPickup = false;
            drop.m_itemData = new ItemDrop.ItemData
            {
                m_dropPrefab = root,
                m_shared = new ItemDrop.ItemData.SharedData
                {
                    m_name = device
                        ? "$boombox_name"
                        : name == BoomboxItemData.ArchivePrefab
                            ? "$boombox_cassette_archive"
                            : "$boombox_cassette_upgraded",
                    m_description = device
                        ? "$boombox_description"
                        : name == BoomboxItemData.ArchivePrefab
                            ? "$boombox_cassette_description"
                            : "$boombox_cassette_upgraded_description",
                    m_itemType = device
                        ? (ItemDrop.ItemData.ItemType)EquipmentType
                        : ItemDrop.ItemData.ItemType.Material,
                    m_maxStackSize = 1,
                    m_maxQuality = device ? BoomboxProgression.MaxTier : 1,
                    m_weight = device ? 8f : 0.2f,
                    m_teleportable = true,
                    m_useDurability = false,
                    m_icons = new[] { VikingBoomboxVisual.CanRender
                        ? (device
                            ? MakeIcon(visual)
                            : CassetteIcon())
                        : template.GetComponent<ItemDrop>().m_itemData.GetIcon() }
                }
            };
            prefabs.Add(name, root);
        }

        public static ItemDrop.ItemData NewItem(string name)
        {
            if (!prefabs.TryGetValue(name, out var prefab))
                throw new InvalidOperationException(BoomboxText.Get("text.item_is_not_registered_yet") + name);
            var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_customData = new Dictionary<string, string>();
            BoomboxItemData.Read(item);
            return item;
        }

        public static Sprite CassetteIcon()
        {
            if (!VikingBoomboxVisual.CanRender)
                return null;
            if (cassetteIcon != null)
                return cassetteIcon;
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                false
            )
            {
                name = "Boombox approved cassette",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear
            };
            try
            {
                using (var input = typeof(BoomboxItemPrefabs).Assembly.GetManifestResourceStream(
                    "ValheimBoombox.CassetteIcon.png"
                ))
                using (var bytes = new MemoryStream())
                {
                    if (input == null)
                        throw new IOException("Missing approved cassette icon.");
                    input.CopyTo(bytes);
                    if (!texture.LoadImage(bytes.ToArray(), true))
                        throw new IOException("Invalid approved cassette icon.");
                }
                var icon = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(.5f, .5f),
                    100
                );
                icon.name = texture.name;
                cassetteIcon = icon;
                return icon;
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
        }

        internal static void ReleaseIcons()
        {
            if (cassetteIcon != null)
            {
                UnityEngine.Object.Destroy(cassetteIcon.texture);
                UnityEngine.Object.Destroy(cassetteIcon);
            }
            cassetteIcon = null;
        }

        public static GameObject CreatePropertySymbol(string property, Transform parent)
        {
            if (!prefabs.TryGetValue(BoomboxItemData.CassettePrefix + property, out var prefab))
                return null;
            var source = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name
                == "Property" + property);
            if (source == null)
                return null;
            var symbol = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            symbol.SetActive(true);
            var bounds = BoundsOf(symbol);
            symbol.transform.localPosition -= bounds.center;
            return symbol;
        }

        private static void RegisterRecipes(ObjectDB database, ZNetScene scene)
        {
            var workbench = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            BoomboxNativeUpgrade.Tiers.Clear();
            BoomboxNativeUpgrade.Amounts.Clear();
            if (workbench == null)
                return;
            foreach (var prefab in prefabs.Values)
            {
                string name = "Recipe_" + prefab.name;
                bool device = prefab.name == BoomboxItemData.DevicePrefab;
                if (!device && prefab.name != BoomboxItemData.ArchivePrefab)
                {
                    var obsolete = database.m_recipes.FirstOrDefault(r => r != null && r.name == name);
                    if (obsolete != null)
                        obsolete.m_enabled = false;
                    continue;
                }
                var recipe = database.m_recipes.FirstOrDefault(r => r != null
                    && r.name == name)
                    ?? ScriptableObject.CreateInstance<Recipe>();
                recipe.name = name;
                recipe.m_item = prefab.GetComponent<ItemDrop>();
                recipe.m_amount = 1;
                recipe.m_enabled = true;
                recipe.m_craftingStation = workbench;
                recipe.m_minStationLevel = 1;
                var cost = device ? BoomboxProgression.GetTier(1).Recipe : BoomboxCassetteWorkshop.BaseRecipe;
                recipe.m_craftingStation = scene.GetPrefab(cost.StationPrefab)?.GetComponent<CraftingStation>();
                recipe.m_minStationLevel = cost.StationLevel;
                if (!ApplyRequirements(database, recipe, cost))
                    continue;
                if (!database.m_recipes.Contains(recipe))
                    database.m_recipes.Add(recipe);
            }
            for (int tier = 1; tier <= BoomboxProgression.MaxTier; tier++)
            {
                string name = "Recipe_BoomboxUpgrade_" + tier;
                var cost = BoomboxProgression.GetTier(tier).Recipe;
                var station = scene.GetPrefab(cost.StationPrefab)?.GetComponent<CraftingStation>();
                if (station == null)
                    continue;
                var recipe = database.m_recipes.FirstOrDefault(r => r != null
                    && r.name == name)
                    ?? ScriptableObject.CreateInstance<Recipe>();
                recipe.name = name;
                recipe.m_item = prefabs[BoomboxItemData.DevicePrefab].GetComponent<ItemDrop>();
                recipe.m_amount = 1;
                recipe.m_enabled = tier <= BoomboxProgression.EnabledMaxTier;
                recipe.m_noCraftOnlyUpgrade = true;
                recipe.m_craftingStation = station;
                recipe.m_minStationLevel = cost.StationLevel;
                if (!ApplyRequirements(database, recipe, cost))
                    continue;
                BoomboxNativeUpgrade.Tiers[recipe] = tier;
                foreach (var requirement in recipe.m_resources)
                    BoomboxNativeUpgrade.Amounts[requirement] = requirement.m_amount;
                if (!database.m_recipes.Contains(recipe))
                    database.m_recipes.Add(recipe);
            }
        }

        private static bool ApplyRequirements(ObjectDB database, Recipe recipe, UpgradeRecipe cost)
        {
            if (recipe.m_craftingStation == null
            || cost.Ingredients.Any(i => database.GetItemPrefab(i.Prefab)?.GetComponent<ItemDrop>() == null))
            {
                recipe.m_enabled = false;
                BoomboxPlugin.Instance.LogError(
                    recipe.name + ": configured station or material prefab was not found; recipe disabled"
                );
                return false;
            }
            recipe.m_resources = cost.Ingredients.Select(i => Requirement(database, i.Prefab, i.Amount)).ToArray();
            return true;
        }

        private static Piece.Requirement Requirement(
            ObjectDB database,
            string material,
            int amount
        ) => new Piece.Requirement
        {
            m_resItem = database.GetItemPrefab(material).GetComponent<ItemDrop>(),
            m_amount = amount,
            m_amountPerLevel = 0,
            m_recover = true
        };

        private static Bounds BoundsOf(GameObject visual)
        {
            var result = new Bounds();
            bool found = false;
            foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!VisibleUnder(filter.transform, visual.transform))
                    continue;
                var bounds = filter.sharedMesh.bounds;
                var matrix = visual.transform.parent.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(
                        bounds.center + Vector3.Scale(
                            bounds.extents,
                            new Vector3(
                                (i & 1) == 0
                                    ? -1
                                    : 1,
                                (i & 2) == 0
                                    ? -1
                                    : 1,
                                (i & 4) == 0
                                    ? -1
                                    : 1
                            )
                        )
                    );
                    if (!found)
                    {
                        result = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                        result.Encapsulate(point);
                }
            }
            if (!found)
                throw new InvalidOperationException(BoomboxText.Get("text.the_item_contains_no_geometry"));
            return result;
        }

        internal static bool VisibleUnder(Transform node, Transform root)
        {
            for (var current = node; current != null && current != root; current = current.parent)
                if (!current.gameObject.activeSelf)
                    return false;
            return true;
        }

        private static Sprite MakeIcon(GameObject source)
        {
            var stage = new GameObject("Boombox item icon");
            stage.transform.position = Vector3.zero;
            var texture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            var iconMaterials = new List<Material>();
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).ToDictionary(
                l => l,
                l => l.cullingMask
            );
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            var reflection = RenderSettings.reflectionIntensity;
            bool fog = RenderSettings.fog;
            try
            {
                foreach (var worldLight in lights.Keys)
                    worldLight.cullingMask &= ~(1 << 31);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
                RenderSettings.reflectionIntensity = 0;
                RenderSettings.fog = false;
                var model = UnityEngine.Object.Instantiate(source, stage.transform, false);
                model.SetActive(true);
                foreach (var node in model.GetComponentsInChildren<Transform>(true))
                    node.gameObject.layer = 31;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(original =>
                    {
                        var material = new Material(original);
                        iconMaterials.Add(material);
                        material.DisableKeyword("_METALLICGLOSSMAP");
                        if (material.HasProperty("_MetallicGlossMap"))
                            material.SetTexture("_MetallicGlossMap", null);
                        if (material.HasProperty("_Metallic"))
                            material.SetFloat("_Metallic", 0f);
                        if (material.HasProperty("_Glossiness"))
                            material.SetFloat("_Glossiness", .15f);
                        if (material.HasProperty("_EmissionColor"))
                            material.SetColor("_EmissionColor", material.GetColor("_EmissionColor") * .06f);
                        return material;
                    }).ToArray();
                }
                var bounds = BoundsOf(model);
                var cameraRoot = new GameObject("camera");
                cameraRoot.transform.SetParent(stage.transform, false);
                var camera = cameraRoot.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.size.x, bounds.size.y) * 0.59f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << 31;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 4f;
                camera.transform.localPosition = bounds.center + Vector3.forward * 2f;
                camera.transform.LookAt(stage.transform.TransformPoint(bounds.center));
                camera.targetTexture = texture;
                var lightRoot = new GameObject("Icon key light");
                lightRoot.transform.SetParent(stage.transform, false);
                lightRoot.transform.rotation = Quaternion.Euler(35f, 145f, 0f);
                var light = lightRoot.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 0.75f;
                light.cullingMask = 1 << 31;
                camera.Render();
                RenderTexture.active = texture;
                var pixels = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                pixels.Apply();
                return Sprite.Create(pixels, new Rect(0, 0, 256, 256), new Vector2(0.5f, 0.5f));
            }
            finally
            {
                foreach (var light in lights)
                    if (light.Key != null)
                        light.Key.cullingMask = light.Value;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.reflectionIntensity = reflection;
                RenderSettings.fog = fog;
                RenderTexture.active = previous;
                stage.SetActive(false);
                UnityEngine.Object.Destroy(stage);
                foreach (var material in iconMaterials)
                    UnityEngine.Object.Destroy(material);
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }
        }
    }

    internal static class BoomboxNativeUpgrade
    {
        internal static readonly Dictionary<Recipe, int> Tiers = new Dictionary<Recipe, int>();
        internal static readonly Dictionary<Piece.Requirement, int> Amounts = new Dictionary<Piece.Requirement, int>();
        [ThreadStatic] internal static UpgradeContext Current;
        internal sealed class UpgradeContext
        {
            internal ItemDrop.ItemData Original;
            internal Dictionary<string, string> Data;
            internal BoomboxItemPayload Payload;
            internal AudioDeck Deck;
            internal int Tier;
            internal bool Equipped;
        }
    }

    [HarmonyPatch(typeof(Recipe), "GetRequiredStationLevel")]
    internal static class BoomboxUpgradeStation
    {
        private static bool Prefix(Recipe __instance, ref int __result)
        {
            if (!BoomboxNativeUpgrade.Tiers.TryGetValue(__instance, out int tier))
                return true;
            __result = BoomboxProgression.GetTier(tier).Recipe.StationLevel;
            return false;
        }
    }

    [HarmonyPatch(typeof(Piece.Requirement), "GetAmount")]
    internal static class BoomboxUpgradeAmount
    {
        private static bool Prefix(Piece.Requirement __instance, ref int __result)
        {
            if (!BoomboxNativeUpgrade.Amounts.TryGetValue(__instance, out int amount))
                return true;
            __result = amount;
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "AddRecipeToList")]
    internal static class BoomboxUpgradeRows
    {
        private static bool Prefix(Recipe recipe, ItemDrop.ItemData item)
        {
            if (BoomboxNativeUpgrade.Tiers.TryGetValue(recipe, out int tier))
                return tier <= BoomboxProgression.EnabledMaxTier && BoomboxItemData.IsDevice(item)
                && BoomboxItemData.Read(item).deck.upgradeTier + 1 == tier;
            return item == null || recipe.m_item == null || !BoomboxItemData.IsDevice(recipe.m_item.m_itemData);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class BoomboxUpgradeCraft
    {
        private static bool Prefix(InventoryGui __instance, Player player)
        {
            var recipe = (Recipe)AccessTools.Field(typeof(InventoryGui), "m_craftRecipe").GetValue(__instance);
            if (recipe == null || !BoomboxNativeUpgrade.Tiers.TryGetValue(recipe, out int tier))
                return true;
            var item = (ItemDrop.ItemData)AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem")
                .GetValue(__instance);
            if (!BoomboxItemData.IsDevice(item) || !player.GetInventory().ContainsItem(item))
                return false;
            BoomboxPlugin.Instance.Items.Flush();
            var payload = BoomboxItemData.Read(item);
            if (tier > BoomboxProgression.EnabledMaxTier || payload.deck.upgradeTier + 1 != tier)
                return false;
            BoomboxNativeUpgrade.Current = new BoomboxNativeUpgrade.UpgradeContext
            {
                Original = item,
                Payload = payload,
                Data = new Dictionary<string, string>(item.m_customData),
                Tier = tier,
                Deck = BoomboxPlugin.Instance.Decks.Values.FirstOrDefault(d => d != null && d.Item == item),
                Equipped = BoomboxEquipment.Get(player) == item
            };
            return true;
        }
        private static void Finalizer()
        {
            BoomboxNativeUpgrade.Current = null;
        }
    }

    [HarmonyPatch(typeof(Inventory), "AddItem", new Type[] {
    typeof(string),
    typeof(int),
    typeof(int),
    typeof(int),
    typeof(long),
    typeof(string),
    typeof(Vector2i),
    typeof(bool),
    typeof(bool),
    typeof(bool)
    })]
    internal static class BoomboxUpgradeTransfer
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Inventory __instance, int quality, ItemDrop.ItemData __result)
        {
            var context = BoomboxNativeUpgrade.Current;
            var player = Player.m_localPlayer;
            if (context == null
                || player == null
                || __instance != player.GetInventory()
                || !BoomboxItemData.IsDevice(__result)
                || quality != context.Tier)
                return;
            __result.m_customData = context.Data;
            context.Payload.deck.upgradeTier = context.Tier;
            BoomboxItemData.Write(__result, context.Payload);
            if (context.Deck != null)
            {
                context.Deck.State.upgradeTier = context.Tier;
                context.Deck.Item = __result;
            }
            if (context.Equipped)
            {
                player.UnequipItem(context.Original, false);
                BoomboxEquipment.Queue(player, __result);
            }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class BoomboxRegisterDatabase
    {
        private static void Postfix(ObjectDB __instance) => BoomboxItemPrefabs.Register(__instance, ZNetScene.instance);
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class BoomboxCopyDatabase
    {
        private static void Postfix(ObjectDB __instance) => BoomboxItemPrefabs.Register(__instance, ZNetScene.instance);
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class BoomboxRegisterScene
    {
        private static void Prefix(
            ZNetScene __instance
        ) => BoomboxItemPrefabs.Register(ObjectDB.instance, __instance, false);
        private static void Postfix(ZNetScene __instance) => BoomboxItemPrefabs.Register(ObjectDB.instance, __instance);
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetIcon")]
    internal static class BoomboxCassetteIcon
    {
        private static void Postfix(ItemDrop.ItemData __instance, ref Sprite __result)
        {
            if (!BoomboxItemData.IsCassette(__instance))
                return;
            var icon = BoomboxItemPrefabs.CassetteIcon();
            if (icon != null)
                __result = icon;
        }
    }
}
