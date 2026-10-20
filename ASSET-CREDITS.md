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

FFmpeg and ffprobe 7.1.5 are separate programs, not covered by the mod's MIT license. Upstream permits LGPL 2.1 or any later version. Max Smirnov (Rizias) elects LGPL 3.0 or later for this redistribution, as permitted by LGPL 2.1 section 13. `Tools/FFmpeg` retains the original LGPL 2.1 text and includes full LGPL 3.0 and GPL 3.0 texts, a notice and the original build manifest.

Each GitHub release provides a separate `FFmpeg-Sources.zip` with the complete upstream source, exact build scripts and the response-file patch to `ffbuild/library.mak`, dated 2026-10-20. No codec changes were made. The installation README and tool notice link directly to that version's source archive. Source access must remain available when distributing the binaries, including through another hosting service, under GPL 3.0 section 6(d) as incorporated by LGPL 3.0. The tools may be rebuilt or replaced. No personal music is distributed.

The bundled tool notices also retain FFmpeg's third-party copyright notices, MinGW-w64 runtime notices, winpthreads' MIT/BSD notices, and GCC Runtime Library Exception 3.1 with GPL 3.0. The FFmpeg tools are based in part on the work of the Independent JPEG Group. Project build scripts and the response-file patch are MIT-licensed; their license is included in the source archive.
