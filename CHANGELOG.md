# Changelog

[Русский](https://github.com/rizias/valheim-boombox/blob/main/CHANGELOG.ru.md)

## [Unreleased]

- Correct cassette textures without changing its geometry, attachment, or embedded reel animation.
- Remove obsolete cassette property markers and emitted symbols.
- Fix player visibility over the main menu, mute and repeat icons, and inventory label sizing.
- Number decoration positions in physical order while preserving saved placements.
- Keep local builds on the source version without changing release metadata.
- Calculate GitHub release versions from published regular releases instead of local build numbers.
- Verify corresponding FFmpeg sources for both local MSVC and hosted GCC builds.

## [0.2.7] - 2026-10-20

- Move FFmpeg source code and build scripts out of the installation package into a separate source archive.
- Retain complete license notices and distribute the audio tools under LGPL 3.0 or later, with a direct corresponding-source link.

## [0.2.6] - 2026-10-20

- Remove outdated private-repository instructions from the installation README.
- Correct music-button instructions and clarify that recipe overrides and decoration slot prices are local settings.

## [0.2.5] - 2026-10-20

- Open the boombox and cassette decoration designs by default without consuming either item, including existing devices.
- Restore the floating player toggle and reset, and retain the inventory music button while the player is hidden.
- Keep existing decoration geometry during device upgrades and avoid a full character/map save on each cassette upgrade.
- Publish regular GitHub releases with the Latest label instead of alpha prereleases.

## [0.2.4] - 2026-10-20

- Keep screenshots and full player guides on GitHub; use GitHub links in the install package README.
- Separate English and Russian documentation and distinguish local crafting settings from shared host settings.
- Allow release dispatch for public and private repositories without changing GitHub authentication.
- Hide boat sail effects with the world-visuals switch while retaining movement assistance.

## [0.2.3] - 2026-10-19

### Added

- GitHub Actions prerelease publishing with a patch/minor/major button, hosted build, automatic version/tag, ZIP/checksum upload and remote asset hash verification.

### Fixed

- Use the approved cassette artwork in inventory, crafting and cassette menus instead of low-contrast model thumbnails. One icon represents the single cassette type at every rank.

## [0.2.2] - 2026-10-18

## [0.2.1] - 2026-10-18

### Added

- Local music folders, playlists, CUE support and streamed playback.
- Optional visual reaction to computer audio.
- Eight device levels and one cassette with five independent boss-gated property upgrades.
- Twenty-four decoration positions, reactive speakers and configurable effect previews.
- Nearby multiplayer bonuses with host-controlled balance.
- Russian and English interface translations.

### Fixed

- Recover the music library from a valid backup when the primary file is damaged.
- Keep a cancelled catalogue scan from overlapping with a replacement scan.
- Preserve custom cassette recipes when loading configuration.
- Display cassette bonuses using the host's balance settings.
- Release the desktop capture worker and its stop handle on shutdown.
- Use the normal inventory when the optional Extra Slots API is unavailable or incompatible.
