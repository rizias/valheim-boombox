# Changelog

[Русский](https://github.com/rizias/valheim-boombox/blob/main/CHANGELOG.ru.md)

## [Unreleased]

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
