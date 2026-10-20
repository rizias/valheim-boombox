# Build, test and publish

[Русский](RELEASE.ru.md)

## The folder to commit

Open **this directory**, containing `Release.cmd`, `manifest.json` and `src`, as the Git repository. Runtime code lives in `src/`. Do not select the parent development archive.

Commit `src/`, `assets/`, `Translations/`, `Screenshots/`, `tests/`, `tools/`, `third_party/`, `.github/`, the root build scripts, configuration examples, documentation and license allowed by `.gitignore`. Local `release.local.json` and generated `outputs/` are ignored. Attach install ZIPs to releases instead of committing them. Push this folder to [rizias/valheim-boombox](https://github.com/rizias/valheim-boombox).

Screenshots and full English and Russian player guides stay in this repository. The install ZIP contains the English README with GitHub links, not copies of the screenshots or full guides. Thunderstore renders the README from the uploaded ZIP. Its image URLs point to GitHub. Push the linked files to `main` and make the repository public before publishing the package; a private repository cannot serve images to other players.

## One-button local build

1. Finish saving source changes and close editors that could save concurrently.
2. Double-click **`Release.cmd`**.
3. Wait for `Verified release` or `Reused verified release`. An error means the candidate is not ready.
4. Take `outputs/local/<version>/<build fingerprint>/Rizias-ValheimBoombox-<version>.zip` from the printed path. Its `.zip.sha256` file sits beside it.

The separate `Rizias-ValheimBoombox-<version>-FFmpeg-Sources.zip` contains corresponding FFmpeg sources and build instructions. It is published beside the installation ZIP with its own checksum. Do not install the source archive or upload it as the mod package to Thunderstore.

FFmpeg/ffprobe are redistributed under LGPL 3.0 or later using upstream's later-version permission. Before uploading the mod to another service, publish both archives on a public GitHub release. Check that the direct source link in the packaged README downloads without signing in. Keep that source download available while distributing the binaries. The selected terms permit a different source server under GPL 3.0 section 6(d), incorporated by LGPL 3.0. Keep the bundled license texts and notice. The mod remains MIT.

Configure `release.local.json` using the next section before the first local build. The button compiles source, runs offline checks against that DLL, packages it and verifies ZIP contents and hashes. It does not launch Valheim, modify Gale or upload anything.

Local builds keep the version already recorded in the source. They do not change the manifest, plugin version, READMEs, player guides, or changelogs. Build as often as needed. Changed inputs produce a separate local build directory; unchanged inputs reuse the verified archive. Earlier outputs are preserved. The build fingerprint identifies the exact inputs, independently of the release version.

Choose `patch`, `minor`, or `major` only when starting **GitHub Actions → Release**. That job calculates the next version from the highest published regular release, ignoring local build numbers, drafts, and prereleases. For example, after published `0.2.7`, `patch` publishes `0.2.8` even if the source still contains a higher number from older local builds. A push alone runs checks and does not publish or increase the version.

## Configure another machine after cloning

Requirements are Windows x64, installed Valheim, compatible BepInExPack Valheim 5.4.2351, PowerShell 7 and .NET SDK 10 with its runtime/reference pack. Game libraries are external inputs, not repository files.

Copy `release.settings.example.json` to **`release.local.json`** beside `Release.cmd`. Replace all four values with existing absolute paths:

| Field | Value |
|---|---|
| `GameManaged` | Installed game's `valheim_Data/Managed`, containing `assembly_valheim.dll`. |
| `BepInExCore` | `BepInEx/core`, containing `BepInEx.dll` and `0Harmony.dll`. |
| `FFmpegRoot` | Distribution containing `bin/ffmpeg.exe`, `bin/ffprobe.exe`, manifest, license and `Sources`. |
| `PwshPath` | Installed PowerShell 7 `pwsh.exe`. |

The example JSON demonstrates path syntax. Escape Windows backslashes by doubling them. Keep actual machine paths in this ignored file. See [FFmpeg rebuild materials](third_party/ffmpeg/README.md). To reuse tools from an install ZIP, put its `Tools/FFmpeg/ffmpeg.exe` and `ffprobe.exe` under distribution `bin/`; retain manifest, license, notices and `Sources/` beside `bin/`. Tool and source hashes must match the distribution manifest.

## Test locally with Gale before publishing

1. Select Valheim in Gale. Use `Profile → Create new profile` for a **new test profile**. Leave your normal profile unchanged.
2. Install the BepInExPack Valheim dependency into this profile.
3. Choose **`Import → …local mod`** and select the complete release ZIP. This imports a mod, not a profile or a standalone DLL.
4. Check that the test profile contains one Valheim Boombox entry with the intended version. Begin without other mods. Extra Slots is optional.
5. Set a separate writable save directory in this test profile's launch arguments using `-savedir "absolute path to a separate directory"`. Example: `-savedir "C:\ValheimBoombox-TestSaves"`. Create a new local character and world for the check. A separate mod profile alone does not isolate game saves.
6. Click **`Launch modded`**. Launching directly from Steam does not test this Gale profile.
7. Run the relevant checks after the world loads. The smoke checks below take about two minutes with items prepared; full progression is a separate session.

| Check | Expected observation |
|---|---|
| Equip, open inventory, and click Boombox | Readable menu, draggable controls, no map overlap. |
| Add a small local music folder, create a playlist, choose Listen to playlist | Track plays; pause/resume and reactive speakers work. |
| Select computer audio, start an external player | Visuals follow the default Windows multimedia output without replaying captured sound. |
| Toggle independent visuals in Effects | Selected visuals disappear independently of gameplay bonuses. |
| Enable personal Music only in Bonuses | Your device stops producing gameplay actions while music continues. |
| Remove/equip shield, walk and run | Inspect device placement and previous jitter symptoms. |
| Exit and reopen the test save | Item/settings survive; no new mod exceptions in the log. |

See [Player guide](PLAY.md) for crafting, the single cassette and all upgrade chains. Automated release checks establish neither native rendering/audio acceptance nor a multiplayer session. `BUILD-INFO.json` deliberately contains `gameTested: false`. A solo smoke check cannot establish real multiplayer acceptance. Server and clients need the same version.

For a defect, record versions, reproduction steps and expected/actual behaviour. Find the log through `File → Open profile folder`, then `BepInEx/LogOutput.log`. Remove personal paths, account IDs and library details before public sharing. Do not present a failing candidate as a tested release.

## Publish the same ZIP

**Thunderstore and Hexium host packages. Gale installs them.** After local testing, run GitHub Release and download its installation ZIP for distribution. Upload that same published ZIP to both package hosts. Local test archives are not published releases. GitHub's generated source ZIP is not an install package.

### Thunderstore

1. Sign in and open [Upload package](https://thunderstore.io/package/create/).
2. Select the ZIP under `Upload file`.
3. Create/select your `Rizias` team under `Team`. Name availability belongs to the platform; the ZIP does not create a team.
4. Select **Valheim** under `Communities`. Select relevant categories, including **AI Generated** for this project.
5. Review version, description and icon, then click **Submit**.
6. After processing, inspect the published README and downloadable version. Save the package link for users.

The builder follows [Thunderstore's package format](https://wiki.thunderstore.io/mods/creating-a-package). Manifest package name stays `ValheimBoombox`; the platform team supplies the author prefix. Keep the same team/package identity for updates and upload a new version.

### Hexium

1. Open the [Valheim submit page](https://valheim.hexium.gg/submit) and sign in.
2. Select upload of an existing ZIP. Use the same archive as Thunderstore.
3. Select your team and installation on **both client and server**. Review metadata before publishing.
4. Inspect the package after processing. Each version can be uploaded once.

Hexium accepts compatible ZIPs up to 512 MB. Retain the bundled license texts and direct source link. Publish the separate FFmpeg source archive on GitHub first, as described above. Follow the [official packaging rules](https://hexium.gg/packaging) for dependency handling.

### GitHub

**Start a release in GitHub through Actions → Release → Run workflow.** Select `main` and choose `patch`, `minor` or `major`. The job builds, tests, updates the version, tags the commit and attaches the package automatically.

Push this source directory, including `.github/workflows/release.yml`, to [rizias/valheim-boombox](https://github.com/rizias/valheim-boombox) before the first run. The workflow becomes available in Actions after that push. No manually uploaded binaries or stored personal token are needed; the job uses its standard `GITHUB_TOKEN` with `contents: write`.

Commit/push source and actual `[Unreleased]` notes in CHANGELOG before release. The job uses the dispatched `main` commit and:

1. Validates the source commit and release notes, then calculates the selected increment from the highest published regular release before downloading dependencies.
2. Obtains dedicated-server Valheim libraries and BepInEx on an ephemeral Windows GitHub runner. FFmpeg is built from hash-verified source; its tool distribution is cached.
3. Checks audio decoding, builds the DLL and runs offline checks through `release.ps1`.
4. Verifies archive/version metadata against the build receipt.
5. Commits version files as `rizias` with the GitHub noreply identity and pushes tag `v<version>`.
6. Creates the GitHub Release using that version's CHANGELOG notes, uploads the installer, FFmpeg source archive and both checksums, validates all four sizes/server SHA256 values and publishes a regular release marked **Latest**.

Heavy work runs on GitHub. It does not use your Gale, running game or saves. A green Release job and published package establish success. An accepted dispatch request alone does not establish a successful release.

To start the same GitHub Actions job from the source folder, double-click **`PublishGitHub.cmd`**. This is a dispatcher, not a local build. It requires GitHub CLI access to the repository. To check connectivity without starting a job:

```powershell
.\PublishGitHub.cmd -CheckOnly
```

Default dispatch chooses `patch`; `PublishGitHub.cmd -Increment Minor` chooses `minor`. `Release.cmd` remains local packaging for Gale testing. Start the real release after local game acceptance. Repository visibility stays unchanged. This workflow does not upload to Thunderstore/Hexium.

`PublishGitHub.cmd` calls `dispatch-release.ps1` to start the workflow. The workflow calls `publish-github.ps1` to verify the build, commit version files, push a tag and upload release assets. These files contain no credentials. Dispatch uses your GitHub CLI login; the hosted job uses its repository-scoped `GITHUB_TOKEN`. [Write access is required to start the workflow](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow). Downloading the source does not grant another user permission to push or publish to this repository.

The hosted job refuses an advanced source branch or an existing target tag without a matching draft. If the calculated next version has an unfinished release draft, Run workflow resumes that version without changing its tag or incrementing the version. Only publication-infrastructure fixes may differ from the tagged source; product changes stop recovery. Existing uploaded assets must match their hashes and are not overwritten. No archive or draft is automatically deleted.

Automation uses [GitHub Actions authentication](https://docs.github.com/en/actions/tutorials/authenticate-with-github_token) and [supported GitHub release commands](https://cli.github.com/manual/gh_release_create). Offline/decoder checks do not establish native game rendering, audio-device or live multiplayer acceptance.

Enable repository Issues for feedback and link it from your mod page. Ask for versions and reproducible steps.
