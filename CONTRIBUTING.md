# Working on the code

[Русский](https://github.com/rizias/valheim-boombox/blob/main/CONTRIBUTING.ru.md)

For game scenarios and external fixtures, see [Game checks](QA.md).

Start with [BUILDING.md](BUILDING.md). Game and BepInEx assemblies are external read-only build inputs; never commit them. Keep local outputs under the ignored `outputs/` directory. The source tree contains runtime C#, embedded resources, and JSON translation dictionaries. It does not contain personal profiles, game copies, saves or an editable model project.

| Area | Starting points in src/ |
|---|---|
| Startup and settings | BoomboxPlugin, BoomboxConfiguration, BoomboxSettings |
| Audio and library | AudioDeck, MusicLiveStream, MusicPreparation, UserMusicLibrary, BoomboxDesktopCapture |
| Progression | BoomboxProgression, BoomboxCassetteProgression, BoomboxCassetteWorkshop |
| Networking and bonuses | BoomboxNetworkProtocol, BoomboxNetworkHandshake, BoomboxAuraNetwork, BoomboxAuraSources |
| UI | BoomboxMenu, BoomboxBonusPanel, BoomboxEffectsPanel, BoomboxEffectPreview |
| Appearance | VikingBoomboxVisual, BoomboxMount, BoomboxVisualVisibility, MusicEffectView |

Keep patches focused on observable behaviour. Explain the problem, change and verification; add a small regression check for a reproduced bug when it can run meaningfully without the game. Offline tests load the real compiled DLL: progression, config preservation, library recovery, cancellation, bonus formatting, capture shutdown, optional Extra Slots API fallback and membrane silence. Test doubles are confined to unavailable external APIs; they do not demonstrate native gameplay.

Offline tests do not check native UI rendering, audio-device capture or a real multiplayer session. For changes to those areas, describe the corresponding in-game check. Use a separate test profile and disposable saves.

Before committing, inspect the staged diff and filenames. Exclude logs, machine paths, credentials, music, binaries and personal config. Every repository language dictionary must contain all keys and completed translations. Keep attribution and license notices. Use repository issues for bug reports and pull requests for patches.

## Add a translation

1. Create `Translations/German.json`, for example, containing `{"meta.culture":"de-DE"}`. Use Valheim's internal language name as the filename. The loader reads the name from `Localization.GetSelectedLanguage()` and appends `.json`; it does not map language codes such as `de`.
2. Run `./check-translations.ps1 -Sync` from the repository directory in PowerShell 7. It adds missing keys to every language as JSON `null`, preserves existing translations, and then validates all dictionaries. A nonzero result listing the file and pending keys is expected until every `null` is replaced with a translation. Translate values on the right; keep keys on the left unchanged, including `text.*`, `boombox_*`, and `config.*`. Preserve leading and trailing spaces, placeholders such as `{0}`, `\n` escapes, and formatting tags. Save valid JSON in UTF-8 and rerun the same command until it succeeds.
3. To try the translation without rebuilding the DLL, create `BepInEx/config/ValheimBoombox/Translations` in a separate test profile. Put `German.json` there. This path is relative to the profile, not the source repository.
4. Select the matching language in Valheim. After editing the file, switch to another language and back, or restart the game. The loader does not watch files for changes.
5. In the game, check item names and descriptions, menus, bonus values, tooltips, and config descriptions. Check long labels for clipping. An older or partial installed override can still use the fallback dictionary named in `Translations/catalog.json`; this runtime recovery does not permit incomplete repository dictionaries. An invalid external JSON file produces a file path and error in the BepInEx log while valid fallback strings remain available.
6. Submit `Translations/German.json` in a pull request. `package.ps1` includes the entire `Translations` directory in the release archive. The installed dictionary goes in `BepInEx/plugins/Rizias-ValheimBoombox/Translations`. Local dictionaries in `BepInEx/config/ValheimBoombox/Translations` override matching keys from the installed dictionary; remove or update a local override when testing a packaged translation.

Adding a JSON file does not add a language to Valheim's language selector. Use a language that the game can select.

Edit translations directly in `Translations/*.json`. They are source files, not generated output. `Translations/catalog.json` names the authoritative fallback language, currently English. Each language file supplies `meta.culture` for number formatting. To add a message, add its key and text to the fallback dictionary, run `./check-translations.ps1 -Sync`, fill the new `null` entries in the other dictionaries, and rerun the same command. Use `BoomboxText.Get("message.key")` or `BoomboxText.Format("message.key", value)` in C#. Translate a complete message with placeholders when values appear inside a sentence, so translators can reorder them. Keep stored notices as a `BoomboxMessage` containing a key and arguments, so they follow language changes. The build embeds the JSON files and the package includes external copies. Additional languages need no C# branch or source exporter.

Synchronization never copies English text into another language or removes obsolete keys. An intentional translation identical to English is valid; `null` explicitly means untranslated. Unknown keys are preserved and reported for manual review. Missing or pending keys, duplicate keys, invalid JSON, incorrect value types and mismatched placeholders fail validation. Without `-Sync`, `./check-translations.ps1` only checks and never writes. `build.ps1` uses this read-only mode and stops on translation errors. To run isolated command checks without the game, use `./tests/offline/TranslationSyncChecks.ps1`; results go under ignored `outputs/translation-sync-checks`.

Config section and setting names in C# are saved configuration identifiers. Keep those identifiers stable; their displayed names come from `config.*` entries. User-provided music titles, file paths, and external program errors remain data. Do not add translated prose to C#.
