using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VikingBoombox
{
    [BepInPlugin(BoomboxNetworkProtocol.Guid, "Valheim Boombox", BoomboxNetworkProtocol.Version)]
    [BepInDependency("shudnal.ExtraSlots", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("shudnal.HipLantern", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class BoomboxPlugin : BaseUnityPlugin
    {
        public static BoomboxPlugin Instance;
        public ConfigEntry<string> CatalogPath;
        public ConfigEntry<string> CachePath;
        public ConfigEntry<string> FFmpegPath;
        public ConfigEntry<string> FFprobePath;
        public bool CatalogRefreshing;
        public bool ShuttingDown;
        public BoomboxSettings Settings;
        public ConfigEntry<float> SoundDistance;
        public ConfigEntry<KeyboardShortcut> MenuShortcut;
        public ConfigEntry<KeyboardShortcut> PlayPauseShortcut;
        internal StateStore Store;
        public MusicCatalog Catalog = new MusicCatalog();
        public int CatalogRevision
        {
            get;
            private set;
        }
        public UserMusicLibrary MusicLibrary;
        public AudioDeck Current;
        public bool MenuOpen;
        public bool NativeInventoryVisible;
        private BoomboxMessage notice = BoomboxMessage.Raw("");
        public string Notice
        {
            get => notice.ToString();
            set => notice = BoomboxMessage.Raw(value);
        }
        public void Notify(string key, params object[] arguments) => notice = new BoomboxMessage(key, arguments);
        public BoomboxInventorySlot InventorySlot;
        private readonly BoomboxCompactBuffs compactBuffs = new BoomboxCompactBuffs();
        public BoomboxItems Items;
        internal readonly Dictionary<string, AudioDeck> Decks = new Dictionary<string, AudioDeck>();
        private Dictionary<string, MusicTrack> trackLookup = new Dictionary<string, MusicTrack>();
        private Harmony harmony;
        private BoomboxMenu menu;
        private long world;
        private long playerId;
        private Player attachedPlayer;
        private float nextSave;
        private float nextLoad;
        private bool initialized;
        private bool localizationReady;
        private CancellationTokenSource catalogPreparation;
        public bool AudioWorldActive => initialized
            && !ShuttingDown
            && Player.m_localPlayer != null
            && ZNet.instance != null
            && ZNet.instance.GetWorld() != null
            && ZNet.instance.GetWorldUID() == world
            && Player.m_localPlayer.GetPlayerID() == playerId;

        private void Awake()
        {
            Instance = this;
            BoomboxStaple.AllowLoading();
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string data = Path.Combine(Paths.ConfigPath, "ValheimBoombox");
            CatalogPath = Config.Bind(
                "Music",
                "CatalogPath",
                Path.Combine(data, "catalog.json"),
                BoomboxText.Description("text.scanned_music_catalogue_folders_are_selected_in")
            );
            string bundledFFmpeg = Path.Combine(directory, "Tools", "FFmpeg", "ffmpeg.exe");
            string bundledFFprobe = Path.Combine(directory, "Tools", "FFmpeg", "ffprobe.exe");
            Settings = BoomboxConfiguration.Load(Config);
            CachePath =
                Config.Bind(
                "Music",
                "CachePath",
                Path.Combine(data, "MusicCache"),
                BoomboxText.Description("text.legacy_path_retained_for_configuration")
            );
            FFmpegPath = Config.Bind(
                "Music",
                "FFmpegPath",
                File.Exists(bundledFFmpeg) ? bundledFFmpeg : "ffmpeg",
                BoomboxText.Description("text.ffmpeg_executable_path_or_command_name")
            );
            FFprobePath = Config.Bind(
                "Music",
                "FFprobePath",
                File.Exists(bundledFFprobe) ? bundledFFprobe : "ffprobe",
                BoomboxText.Description("text.ffprobe_executable_path_or_command_name")
            );
            MenuShortcut = Config.Bind(
                "Controls",
                "MenuShortcut",
                new KeyboardShortcut(KeyCode.F7),
                BoomboxText.Description("text.open_the_dedicated_boombox_slot")
            );
            PlayPauseShortcut = Config.Bind(
                "Controls",
                "PlayPauseShortcut",
                new KeyboardShortcut(KeyCode.F4),
                BoomboxText.Description("text.play_or_pause_your_boombox_with_one")
            );
            SoundDistance = Config.Bind(
                "Audio",
                "Distance",
                35f,
                BoomboxText.Description("text.distance_at_which_local_music_fades_to_silence", new AcceptableValueRange<float>(2f, 200f))
            );
            Store = new StateStore(data);
            MusicLibrary = UserMusicLibrary.Load(data);
            string ownedCatalog = Path.Combine(data, "catalog.json");
            if (!string.Equals(
                Path.GetFullPath(CatalogPath.Value),
                Path.GetFullPath(ownedCatalog),
                StringComparison.OrdinalIgnoreCase
            ))
            {
                if (!File.Exists(ownedCatalog) && File.Exists(CatalogPath.Value))
                    File.Copy(CatalogPath.Value, ownedCatalog);
                CatalogPath.Value = ownedCatalog;
            }
            menu = new BoomboxMenu(this);
            InventorySlot = new BoomboxInventorySlot(this);
            Items = new BoomboxItems(this);
            harmony = new Harmony("local.valheim.boombox");
            harmony.PatchAll(typeof(BoomboxPlugin).Assembly);
            BoomboxEquipment.Register();
            BoomboxText.RefreshConfig();
            new Terminal.ConsoleCommand("boombox", BoomboxText.Get("command.open_menu"), args => OpenOwn());
            ReloadCatalog();
            Logger.LogInfo(
                "Boombox loaded; native item data, Extra Slots slot " + (BoomboxEquipment.SlotPosition.HasValue
                    ? "registered."
                    : "absent, vanilla equip.")
            );
        }

        public void ReloadCatalog()
        {
            try
            {
                var next = StateStore.Read<MusicCatalog>(CatalogPath.Value) ?? new MusicCatalog();
                if (next.playlists == null || next.playlists.Any(p => p.tracks == null))
                    throw new InvalidDataException(BoomboxText.Get("text.the_catalog_track_list_is_damaged"));
                var lookup = next.playlists.SelectMany(p => p.tracks)
                    .Where(t => !string.IsNullOrEmpty(t.id))
                    .GroupBy(t => t.id)
                    .ToDictionary(
                    g => g.Key,
                    g => g.First()
                );
                foreach (var alias in next.aliases ?? new Dictionary<string, string>())
                    if (lookup.TryGetValue(alias.Value, out var target))
                        lookup[alias.Key] = target;
                foreach (var track in Catalog.playlists.SelectMany(p => p.tracks))
                    MusicLibrary?.Remember(track);
                Catalog = next;
                trackLookup = lookup;
                CatalogRevision++;
                foreach (var track in Catalog.playlists.SelectMany(p => p.tracks))
                    MusicLibrary?.Remember(track);
                MusicLibrary?.ImportCatalog(Catalog);
                MusicLibrary?.Save();
                Notify("notice.library_summary", Catalog.playlists.SelectMany(p => p.tracks).Count(), (Catalog.errors?.Count ?? 0));
            }
            catch (Exception e)
            {
                Notify("notice.catalog_error", e.Message);
                Logger.LogError(e);
            }
        }

        public MusicTrack FindTrack(string id)
        {
            if (id == null)
                return null;
            if (BoomboxSyntheticTrackId.IsSynthetic(id))
                return BoomboxSyntheticTrackId.For(id);
            if (trackLookup.TryGetValue(id, out var track))
                return track;
            return MusicLibrary != null && MusicLibrary.rememberedTracks.TryGetValue(id, out track) ? track : null;
        }
        public void UpdateTrackMetadata(string id, string title, string artist)
        {
            var track = FindTrack(id);
            if (track == null)
                return;
            bool changed = (!string.IsNullOrEmpty(title) && track.title != title)
                || (!string.IsNullOrEmpty(artist) && track.artist != artist);
            if (!string.IsNullOrEmpty(title))
                track.title = title;
            if (!string.IsNullOrEmpty(artist))
                track.artist = artist;
            MusicLibrary?.Remember(track);
            if (changed)
                CatalogRevision++;
        }
        public void RefreshCatalog()
        {
            if (CatalogRefreshing)
                return;
            catalogPreparation?.Dispose();
            catalogPreparation = new CancellationTokenSource();
            StartCoroutine(MusicPreparation.Refresh(this, catalogPreparation.Token));
        }

        public void CancelCatalogRefresh()
        {
            catalogPreparation?.Cancel();
        }

        internal void CompleteCatalogRefresh()
        {
            catalogPreparation?.Dispose();
            catalogPreparation = null;
            CatalogRefreshing = false;
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            long nextWorld = 0;
            try
            {
                if (ZNet.instance != null && ZNet.instance.GetWorld() != null)
                    nextWorld = ZNet.instance.GetWorldUID();
            }
            catch (NullReferenceException) { }
            if (initialized
                && (nextWorld != world || (player != null && player.GetPlayerID() != 0 && player.GetPlayerID() != playerId)))
                Unload();
            if (player == null || (MenuOpen && player.IsDead()))
            {
                CloseMenu();
                if (player == null)
                    return;
            }
            if (!localizationReady)
            {
                BoomboxLocalization.Register();
                localizationReady = true;
            }
            if (!initialized && nextWorld != 0 && player.GetPlayerID() != 0 && Time.unscaledTime >= nextLoad)
            {
                nextLoad = Time.unscaledTime + 3f;
                try
                {
                    LoadWorld(nextWorld, player);
                }
                catch (Exception e)
                {
                    ClearDecks();
                    Notify("notice.save_error", e.Message);
                    Logger.LogError(e);
                    return;
                }
            }
            if (!initialized)
                return;
            Items.Update();
            if (attachedPlayer != player)
            {
                attachedPlayer = player;
                foreach (var deck in Decks.Values)
                    PositionDeck(deck);
            }
            bool shortcutsAllowed = Application.isFocused && !Console.IsVisible() && !TextInput.IsVisible()
            && !(Chat.instance != null && Chat.instance.HasFocus())
            && !BoomboxTrophyCatalog.SearchFocused;
            if (ShortcutDown(MenuShortcut.Value) && shortcutsAllowed)
            {
                if (MenuOpen)
                    CloseMenu();
                else
                    OpenOwn();
            }
            if (ShortcutDown(PlayPauseShortcut.Value) && shortcutsAllowed)
                ToggleOwnPlayback();
            if (MenuOpen
                && Application.isFocused
                && Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame)
                CloseMenu();
            if (Time.unscaledTime >= nextSave)
            {
                nextSave = Time.unscaledTime + 5f;
                Save();
            }
        }

        private void LoadWorld(long id, Player player)
        {
            Store.Load(id, player.GetPlayerID());
            BoomboxLocalization.RestoreReadTutorials(player);
            foreach (var playlist in Store.Library.cassettes)
                MusicLibrary.Import(
                    "record:" + player.GetPlayerID() + ":" + playlist.id,
                    playlist.name,
                    playlist.tracks
                );
            foreach (var track in Catalog.playlists.SelectMany(p => p.tracks))
                MusicLibrary.Remember(track);
            MusicLibrary.Save();
            world = id;
            playerId = player.GetPlayerID();
            attachedPlayer = player;
            initialized = true;
            Items.Migrate();
            Items.Update();
            Current = Items.Own;
            Logger.LogInfo(
                "Boombox world loaded: " + world + ", player " + playerId + ", instances " + Store.World.decks.Count
            );
        }

        internal AudioDeck SpawnDeck(DeckState state)
        {
            var root = new GameObject("ValheimBoombox_" + state.id);
            var deck = root.AddComponent<AudioDeck>();
            Decks.Add(state.id, deck);
            deck.Initialize(BoomboxDecorationCatalog.WithoutUnlisted(state));
            var collider = root.AddComponent<BoxCollider>();
            var modelBounds = deck.Visual.GetComponent<VikingBoomboxVisualState>().ModelBounds;
            collider.center = modelBounds.center;
            collider.size = modelBounds.size;
            root.AddComponent<BoomboxInteraction>().Deck = deck;
            int layer = LayerMask.NameToLayer("piece");
            if (layer >= 0)
                root.layer = layer;
            PositionDeck(deck);
            return deck;
        }

        private void PositionDeck(AudioDeck deck)
        {
            if (deck.State.onBack)
            {
                var player = Player.m_localPlayer;
                if (player == null)
                    return;
                deck.Mount.Follow(player);
            }
            else
            {
                deck.transform.SetParent(null, true);
                deck.Mount.StandOnGround();
                deck.transform.position = deck.State.Position;
                deck.transform.rotation = Quaternion.Euler(0, deck.State.yaw, 0);
                deck.PlatformAnchor.Follow();
            }
            var collider = deck.GetComponent<BoxCollider>();
            if (collider != null)
                collider.enabled = !deck.State.onBack;
        }

        public bool PickUp(AudioDeck deck)
        {
            var player = Player.m_localPlayer;
            if (player == null || deck == null || deck.State.onBack || deck.State.owner != playerId
            || Vector3.Distance(player.transform.position, deck.transform.position) > 4f)
                return false;
            return Items.PickUp(deck);
        }

        public bool CanControl(AudioDeck deck)
        {
            return deck != null && Player.m_localPlayer != null && deck.State.owner == playerId
            && (deck.State.onBack || deck.Item != null && Player.m_localPlayer.GetInventory().ContainsItem(deck.Item)
            || Vector3.Distance(Player.m_localPlayer.transform.position, deck.transform.position) <= 4f);
        }

        public void OpenOwn()
        {
            if (!initialized || !BoomboxEquipment.Known)
                return;
            Open(Items.Own);
        }
        internal void OpenLibrary()
        {
            OpenOwn();
            if (MenuOpen)
                menu.SelectNativeTab(0);
        }
        internal void OpenBonuses()
        {
            OpenOwn();
            if (MenuOpen)
                menu.SelectNativeTab(7);
        }
        public void ListenPlaylist(AudioDeck deck, IEnumerable<string> tracks, string playlistId)
        {
            var queue = tracks.Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (CanControl(deck)
                && Sounding(deck)
                && deck.State.playlistId == playlistId
                && deck.State.tracks.SequenceEqual(queue))
                return;
            PlayQueue(deck, queue, playlistId, 0);
        }
        private static bool Sounding(AudioDeck deck) => deck.State.playing && deck.State.insertedCassette != null
        && (deck.Loading || deck.Source != null && deck.Source.clip != null
        || BoomboxSyntheticTrackId.IsSynthetic(deck.State.TrackId));
        public void PlayQueue(AudioDeck deck, IEnumerable<string> tracks, string playlistId, int start)
        {
            if (!CanControl(deck))
                return;
            var queue = tracks.Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (queue.Count == 0)
            {
                Notify("text.the_playlist_is_empty");
                return;
            }
            int index = Mathf.Clamp(start, 0, queue.Count - 1);
            bool sameTrack = Sounding(deck) && deck.State.TrackId == queue[index];
            deck.Capture();
            deck.State.tracks = queue;
            deck.State.playlistId = playlistId;
            deck.State.trackIndex = index;
            if (sameTrack)
            {
                Save();
                return;
            }
            deck.State.seconds = 0;
            deck.State.playing = deck.State.insertedCassette != null;
            deck.LoadTrack();
            Save();
            if (deck.State.insertedCassette == null)
                Notify("text.music_selected_insert_a_cassette_and_press_play");
        }
        public void Open(AudioDeck deck)
        {
            if (InventoryGui.instance != null && !NativeInventoryVisible)
                InventoryGui.instance.Show(null, 1);
            Current = deck;
            MenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        public void CloseMenu()
        {
            MenuOpen = false;
            if (!ShuttingDown)
                menu?.HideNative();
        }
        private void LateUpdate()
        {
            if (Settings != null)
                compactBuffs.Update(this);
            if (MenuOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (initialized)
                menu.Update();
        }

        private static bool ShortcutDown(KeyboardShortcut shortcut)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return false;
            var key = MapKey(shortcut.MainKey);
            if ((keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed)
            && !shortcut.Modifiers.Any(m => m == KeyCode.LeftAlt || m == KeyCode.RightAlt))
                return false;
            return key != Key.None && keyboard[key].wasPressedThisFrame
            && shortcut.Modifiers.All(m => MapKey(m) != Key.None && keyboard[MapKey(m)].isPressed);
        }

        public void ToggleOwnPlayback()
        {
            if (!initialized)
                return;
            var deck = Items.Own;
            if (deck == null)
            {
                OpenOwn();
                return;
            }
            if (deck.State.TrackId == null)
            {
                Open(deck);
                Notice = Localization.instance.Localize("$boombox_cassette_description");
            }
            else
            {
                deck.TogglePlayback();
                Player.m_localPlayer.Message(
                    MessageHud.MessageType.Center,
                    (deck.State.playing
                        ? BoomboxText.Get("text.boombox_playing")
                        : BoomboxText.Get("text.boombox_paused"))
                );
            }
        }

        private static Key MapKey(KeyCode code)
        {
            if (code == KeyCode.LeftControl)
                return Key.LeftCtrl;
            if (code == KeyCode.RightControl)
                return Key.RightCtrl;
            if (code == KeyCode.Return)
                return Key.Enter;
            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9)
                return Enum.TryParse<Key>("Digit" + (code - KeyCode.Alpha0), out var digit) ? digit : Key.None;
            return Enum.TryParse<Key>(code.ToString(), true, out var key) ? key : Key.None;
        }

        public void LogError(string message)
        {
            Logger.LogError(message);
        }
        public void LogInfo(string message)
        {
            Logger.LogInfo(message);
        }

        public void Save()
        {
            try
            {
                Items?.Flush();
                foreach (var deck in Decks.Values)
                    if (deck != null)
                    {
                        deck.Capture();
                        if (deck.WorldDrop != null)
                            AccessTools.Method(typeof(ItemDrop), "Save").Invoke(deck.WorldDrop, null);
                    }
                Store.Save();
                MusicLibrary?.Save();
            }
            catch (Exception e)
            {
                Notify("notice.music_error", e.Message);
                Logger.LogError(e);
            }
        }

        private void Unload()
        {
            CancelCatalogRefresh();
            Save();
            ClearDecks();
        }

        private void ClearDecks()
        {
            BoomboxAuraRecipient.Clear();
            BoomboxGameMusic.Release();
            foreach (var deck in Decks.Values)
                if (deck != null)
                    Destroy(deck.gameObject);
            Decks.Clear();
            BoomboxEquipment.Clear();
            Current = null;
            initialized = false;
            CloseMenu();
        }

        private void OnApplicationQuit()
        {
            ShuttingDown = true;
            CancelCatalogRefresh();
            Save();
        }
        private void OnDisable()
        {
            BoomboxGameMusic.Release();
        }
        private void OnDestroy()
        {
            compactBuffs.Dispose();
            if (ShuttingDown)
            {
                ClearDecks();
            }
            else
            {
                Unload();
            }

            BoomboxGameMusic.Release();
            BoomboxMovement.Clear();
            menu?.Dispose();
            InventorySlot?.Dispose();
            BoomboxHeadPlacement.Clear();
            BoomboxChain.Clear();
            BoomboxStaple.Clear();
            BoomboxParticleAssets.Clear();
            BoomboxBonusArt.Release();
            BoomboxItemPrefabs.ReleaseIcons();
            VikingBoomboxVisual.ReleaseAssets();
            BoomboxWaveMaterials.Clear();
            ValheimBoombox.Visuals.BassShockwaveVisual.ReleaseAssets();
            MusicEffectAssets.Release();
            harmony?.UnpatchSelf();
            Instance = null;
        }
    }

    public sealed class BoomboxInteraction : MonoBehaviour, Hoverable, Interactable
    {
        public AudioDeck Deck;
        public string GetHoverName()
        {
            return BoomboxText.Get("text.boombox");
        }
        public float GetHoverOffset()
        {
            return 0.4f;
        }
        public string GetHoverText()
        {
            string action = Player.m_localPlayer != null && Deck.State.owner == Player.m_localPlayer.GetPlayerID()
                ? BoomboxText.Get("text.pick_up_and_equip")
                : BoomboxText.Get("text.inspect");
            return Localization.instance.Localize(
                BoomboxText.Get("text.boombox") + "\n[<color=yellow><b>$KEY_Use</b></color>] " + (action) + "\n"
                    + BoomboxPlugin.Instance.PlayPauseShortcut.Value
                    + BoomboxText.Get("text.play_pause_2")
                    + BoomboxPlugin.Instance.MenuShortcut.Value
                    + BoomboxText.Get("text.cassettes")
            );
        }
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || user != Player.m_localPlayer)
                return false;
            if (BoomboxPlugin.Instance.PickUp(Deck))
            {
                Player.m_localPlayer.Message(
                    MessageHud.MessageType.Center,
                    BoomboxText.Get("text.boombox_equipped") + BoomboxPlugin.Instance.PlayPauseShortcut.Value
                        + BoomboxText.Get("text.play_pause")
                );
                return true;
            }
            BoomboxPlugin.Instance.Open(Deck);
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class BoomboxInput
    {
        private static void Postfix(ref bool __result)
        {
            if (BoomboxPlugin.Instance != null && BoomboxPlugin.Instance.MenuOpen)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class BoomboxControllerInput
    {
        private static void Postfix(ref bool __result)
        {
            if (BoomboxPlugin.Instance != null && BoomboxPlugin.Instance.MenuOpen)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    internal static class BoomboxCamera
    {
        private static bool Prefix()
        {
            return BoomboxPlugin.Instance == null || !BoomboxPlugin.Instance.MenuOpen;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "IsVisible")]
    internal static class BoomboxInventoryContext
    {
        private static void Postfix(ref bool __result)
        {
            var plugin = BoomboxPlugin.Instance;
            if (plugin == null)
                return;
            plugin.NativeInventoryVisible = __result;
            if (plugin.MenuOpen)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Hide")]
    internal static class BoomboxInventoryClose
    {
        private static void Postfix()
        {
            var plugin = BoomboxPlugin.Instance;
            if (plugin != null && plugin.MenuOpen)
                plugin.CloseMenu();
        }
    }

}
