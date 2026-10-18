# Asset credits

[Русский](https://github.com/rizias/valheim-boombox/blob/main/ASSET-CREDITS.ru.md)

The mod embeds the following twelve resources. The project's own work is covered by the accompanying `LICENSE`; external sources are identified below.

| Resource in `assets/` | Origin and distribution basis |
|---|---|
| `valheimboombox_bassfx` | Project-created meshes, particle textures and shaders; project license. |
| `valheimboombox_v3_2` | Project-created boombox model, materials and fixed decorative inscriptions; project license, with the historical illustration and lettering sources described below. |
| `valheimboombox_musicfx` | Seven project-created effect shaders; project license. |
| `contact-shapes.json` | Project-authored geometric calibration data; project license. |
| `pendant-fit.json` | Project-authored attachment calibration; project license. |
| `pendant-particles.json` | Project-authored effect settings; project license. |
| `ramsund-dragon-head.png` | Adaptation of the public-domain Ramsund illustration described below. |
| `bonus-icons/wave.png` | AI-assisted artwork created and approved for this project; project license. |
| `bonus-icons/health.png` | AI-assisted artwork created and approved for this project; project license. |
| `bonus-icons/stamina.png` | AI-assisted artwork created and approved for this project; project license. |
| `bonus-icons/eitr.png` | AI-assisted artwork created and approved for this project; project license. |
| `cassette-icon.png` | AI-assisted artwork created and approved for this project; project license. |

## Historical illustration

The Ramsund/Sigurd ornament derives from the anonymous illustration in *Nordisk familjebok*. Wikimedia Commons credits the PNG conversion to Liquid 2003, vectorisation to Editor at Large, and the current 2023 retrace to 痛. Its file page identifies the illustration as public domain. Cropping, masking, colouring and placement were performed for this project. [Source and file history](https://commons.wikimedia.org/wiki/File:Sigurd.svg).

## Lettering and game resources

Fixed decorative lettering was made using Segoe UI Historic. Only the resulting meshes and textures are distributed, without font files. [Microsoft font FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq).

Valheim's UI fonts, trophies and other native objects are obtained from the installed game at runtime. Valheim and the game artwork shown in screenshots belong to their respective rights holders. This is an unofficial mod.

## Audio tools

FFmpeg and ffprobe 7.1.5 are separate programs under LGPL-2.1-or-later, not the mod's MIT license. `Tools/FFmpeg` contains their notice, license and manifest. `Tools/FFmpeg/Sources` contains the corresponding upstream archive, exact build scripts and the library-response-file patch. The local MSVC distribution also includes the upstream archive signature. The manifest records source/binary SHA256 values and configure flags. GPL/nonfree components are disabled in this build. The tools can be replaced or rebuilt. No personal music is distributed.
