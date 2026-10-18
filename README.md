# Valheim Boombox

[Русский](https://github.com/rizias/valheim-boombox/blob/main/README.ru.md) · Windows x64 · 0.2.3 alpha · By **Rizias**

Carry your own music through Valheim. Play local tracks and playlists, react to other Windows audio, and turn music events into enemy waves, equipment charge, and nearby party bonuses.

![A playing boombox with trophy heads, pendants, glowing mushrooms, and runes](https://raw.githubusercontent.com/rizias/valheim-boombox/main/Screenshots/boombox-showcase.png)

*A decorated boombox during playback.*

[Read the complete player guide](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md) for setup, every menu, full default recipes, and troubleshooting.

## What you can do

- Build and upgrade the boombox through eight device levels. Improve five independent cassette properties through rank VII.
- Play MP3, FLAC, WAV, OGG, APE, WV, M4A, AAC, Opus, and WMA. Scan folders, ZIP archives, and CUE albums. Search, select albums, and build ordered playlists with repeat.
- React to the combined default Windows audio output, Valheim's music, or fixed rhythms. Computer-audio mode does not duplicate, record, or transmit sound.
- Wear the device with your hands free, place it in the world, or carry it on a boat or cart.
- Fit 24 decoration positions with eligible trophy heads, mushrooms, items, and miniature objects. Choose each decoration's bonus and particles independently of its appearance.
- Charge a bow, shield, melee weapon, staff, or crossbow. Fully charged resource tools and thrown spears can repeat their hit with a ghost strike. Musical pulses can assist a boat at the helm.
- Share wave-damage, health, stamina, eitr, and movement-speed bonuses nearby. Inspect source contributions, choose personal Music only, and let the host set world rules.
- Preview five music effects, pick colours, control 14 groups of world visuals, and move the player and bonus HUD.

Music stays local to each player. Multiplayer shares gameplay and appearance data. English and Russian menus follow Valheim's language. A supported Extra Slots version provides a dedicated equipment slot.

## Install and start

1. Install BepInExPack Valheim 5.4.2351 in a Valheim mod-manager profile.
2. Import the complete release ZIP through local mod import in Gale, r2modman, or Thunderstore Mod Manager. Launch modded.
3. Craft the first device and a basic cassette at Workbench level 1. The [guide lists the materials](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#install-and-start-your-first-track).
4. In your inventory, right-click the cassette to insert it, then equip the boombox.
5. Click **Choose music**, open **Folders**, and add your music folder. After the scan, play a track from **Library**.

For manual installation, copy the release's `BepInEx` tree into an existing BepInEx installation and retain its subfolders. Keep one plugin DLL. The source-code ZIP is not the release package. The complete release includes FFmpeg tools and their source and licence materials. No music is included.

![Library with a local album, computer audio, game music, and rhythm sources](https://raw.githubusercontent.com/rizias/valheim-boombox/main/Screenshots/manual-en-library.png)

*Choose a source, play an album, or select tracks for a playlist from the same library.*

## Find the controls and recipes

| Goal | Guide |
|---|---|
| Import music, browse albums, manage playlists, or choose computer audio | [Music library](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#build-a-music-library) and [audio sources](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#choose-an-audio-source). |
| Look up every device or cassette recipe | [Device levels](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#upgrade-the-device) and [cassette properties](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#improve-a-cassette). |
| Unlock decoration positions, reuse designs, and choose particles | [Decorations](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#decorate-the-boombox). |
| Use combat waves, weapon charge, ghost strikes, or boat assistance | [Gameplay](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#use-waves-and-charged-equipment). |
| Inspect shared bonuses, move the HUD, or change world balance | [Bonuses and world rules](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#share-bonuses-and-set-world-rules). |
| Preview effects, pick colours, and hide selected visuals | [Effects and visibility](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#set-effects-and-visibility). |
| Back up a library, fix missing music, or report a problem | [Paths and troubleshooting](https://github.com/rizias/valheim-boombox/blob/main/PLAY.md#keep-your-library-and-solve-problems). |

![Carved side panel, chains, trophy heads, and a miniature boombox](https://raw.githubusercontent.com/rizias/valheim-boombox/main/Screenshots/runes-detail.png)

*Decorations have reusable designs, selectable bonuses, and cosmetic particles.*

## Multiplayer and compatibility

![Active bonus amounts, source contributions, and HUD placement controls](https://raw.githubusercontent.com/rizias/valheim-boombox/main/Screenshots/manual-en-bonuses.png)

*The Bonuses page explains what each effect gives and where its on-screen indicators appear.*

Use the same version on the server and every client. A client without the required mod receives Valheim's connection rejection. The server log records the required version. The host controls shared bonus strength and range. Recipes and upgrade caps use each installation's local configuration and are not synchronized by the server.

This is an unofficial Windows x64 alpha. Extra Slots is optional. Compatibility with every other inventory or equipment mod is not guaranteed. The mod does not grant rights to music used in streams or videos.

![Speaker membrane, cassette, and illuminated body runes during playback](https://raw.githubusercontent.com/rizias/valheim-boombox/main/Screenshots/speaker-reactive.png)

*Speakers, cassette reels, and body lighting react to playback. Visual switches leave gameplay controls separate.*
