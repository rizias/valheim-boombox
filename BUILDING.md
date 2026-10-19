# Build and package

[Русский](BUILDING.ru.md)

This guide is for developers who compile the mod or prepare an install ZIP. Players can use the [player guide](PLAY.md).

For the normal workflow, double-click **`Release.cmd`**. It builds, tests, updates the version and creates the install ZIP. Read [Build, test and publish](RELEASE.md) for setup, local Gale testing and upload instructions. The commands below are separate developer operations.

Use **GitHub Actions → Release** with patch/minor/major to publish automatically. **`PublishGitHub.cmd`** dispatches the same hosted job. The release guide documents dependency sources, prerequisites and limits.

## Compile the DLL

1. Install PowerShell 7 and .NET SDK 10.
2. Install Valheim and BepInExPack Valheim 5.4.2351.
3. Open PowerShell 7 in the repository directory.
4. Enter the dependency directories when prompted. `GameManaged` is the game's `valheim_Data/Managed` directory. `BepInExCore` is the `BepInEx/core` directory of a compatible installation.

```powershell
$gameManaged = Read-Host 'Valheim Managed directory'
$bepInExCore = Read-Host 'BepInEx core directory'
./build.ps1 -GameManaged $gameManaged -BepInExCore $bepInExCore -OutputDirectory './outputs/local' -DllOnly
```

The result is `outputs/local/ValheimBoombox.dll`. The compiler reads dependency libraries and embeds resources from `assets/`. It does not start the game or install the DLL. Keep dependency paths in your local shell, outside committed files.

`-Compiler` selects a Roslyn `csc.dll`. Without it, the script selects an installed SDK. Without `-DllOnly`, README and translations are copied beside the DLL.

## C# formatting

Run `./format.ps1` to format C# sources and tests. Run `./format.ps1 -Check` to check without editing them. Both commands use Roslyn from the installed .NET SDK 10 and expand compressed blocks, separate statements and wrap long argument lists and expressions.

Run these commands locally as needed. They do not start Valheim or access a game profile.

## Run offline tests

Use the same shell variables and the DLL you just built.

```powershell
./test.ps1 -GameManaged $gameManaged -BepInExCore $bepInExCore -ProductDll './outputs/local/ValheimBoombox.dll' -OutputDirectory './outputs/tests'
```

The command writes fixtures and `results.json` under `outputs/tests` and returns a nonzero exit code on failure. It does not start Valheim. See [Contributing](CONTRIBUTING.md) for coverage and limits.

## Prepare an install ZIP

Local-file playback needs separate FFmpeg and ffprobe programs. Supply a complete distribution containing `bin/ffmpeg.exe`, `bin/ffprobe.exe`, `manifest.json`, `LICENSE`, `README.txt` and `Sources/`. The source directory must include the corresponding source archive and rebuild materials. Use the [FFmpeg rebuild guide](third_party/ffmpeg/README.md).

Use `Release.cmd` for distribution. It passes the exact DLL and matching offline test results to `package.ps1` and creates `BUILD-INFO.json`. The install ZIP contains the plugin, translations, audio tools, licenses, manifest, icon, changelog and English `README.md`. The README links to the Russian description and both player guides on GitHub.

Keep screenshots and full player guides in the source repository. They are not copied into the install ZIP. The source README uses relative image paths, which GitHub resolves for readers with repository access. Packaging changes only those image paths to `raw.githubusercontent.com` URLs for Thunderstore. The repository must be public and the referenced files pushed to `main` before publishing a package with those images.

`package.ps1` is the lower-level helper. `Stage` requires explicit `SourceDll`, `IconPath`, `FFmpegRoot` and `TestReport` from `test.ps1`; `MetadataDirectory` can select candidate release metadata. `Archive` takes the prepared `Destination`. Both write only inside `outputs/`. Existing destinations are preserved, not overwritten. The normal release command selects unique working destinations and maintains the version.

Archive validates the manifest, 256×256 icon, file hashes and size budget. It does not upload anything or establish native game acceptance. See [the complete release procedure](RELEASE.md).

## Update translations or resources

Edit `Translations/*.json` directly. `Translations/catalog.json` selects the authoritative fallback dictionary. After adding a key there, run `./check-translations.ps1 -Sync` to add missing keys to the other languages as `null` and validate all dictionaries. Existing translations are preserved. Fill every `null` with a translation and rerun the same command until it succeeds. Missing or pending keys make the command fail with file and key details. Without `-Sync`, the command only checks; the build uses this read-only mode and stops on translation errors. See [Add a translation](CONTRIBUTING.md#add-a-translation) for additional languages, local overrides, and culture settings. The build embeds all translation JSON files, so the DLL also has a fallback when external dictionaries are missing.

The compiled Unity bundles in `assets/` are build inputs. Editable Blender and Unity authoring projects are not included. Keep the [asset credits](ASSET-CREDITS.md) and separate third-party licenses with redistributed resources.
