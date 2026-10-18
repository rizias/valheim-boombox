# Game checks

[Русский](https://github.com/rizias/valheim-boombox/blob/main/QA.ru.md)

These checks are for contributors who change gameplay, networking, audio or UI. For compilation and offline checks, see [BUILDING.md](BUILDING.md).

## Configure test inputs

Set these variables in your PowerShell session. Keep local values outside committed files.

| Variable | Value |
|---|---|
| `VALHEIM_QA_ROOT` | Existing test-data directory outside this repository. |
| `VALHEIM_QA_CLIENT` | Existing Valheim installation with Doorstop. |
| `VALHEIM_BEPINEX_CORE` | Compatible BepInEx core directory. |
| `VALHEIM_FFMPEG_ROOT` | Audio-tool distribution with `bin/ffmpeg.exe` and `bin/ffprobe.exe`. |
| `VALHEIM_QA_SERVER` | Dedicated-server installation, only for its scenarios. |
| `VALHEIM_QA_REFERENCE_ALBUM` | Optional read-only album for additional library tests. Omission is reported as a skip. |

Use isolated `client-a`, `client-b` and `server` profiles under the test-data directory. Never use normal player saves or a mod manager's working profile. Two local clients share one installed game.

```powershell
./qa.ps1 -Action cases
./qa.ps1 -Action environment-check
./qa.ps1 -Action environment-tests
./qa.ps1 -Action build-candidate
./tools/qa/build-probe.ps1
./qa.ps1 -Action build-gameplay
```

These commands list cases, validate paths or compile code. They do not start Valheim. `tests/offline/verify-final.ps1` runs the full offline checks with `ProductDll`, `GameManaged` and `BepInExCore` arguments.

## Separate source, fixtures and output

`tests/integration` contains the controller and probes. `tests/offline` contains offline checks. `tests/reference` contains reference algorithms. `tests/cases.json` lists scenario IDs and source references. A source reference is not a passed test.

Local fixtures belong under `VALHEIM_QA_ROOT/fixtures`. Audio checks can require `audio/`, `codec-samples/`, `legacy-catalog.json` and `tracks.json`. Optional UI checks require their plugin fixtures. Music, personal catalogues and plugin binaries are excluded from Git. Supply inputs for each corresponding check. A missing required input is not a pass.

Generated data uses `bin`, `stage`, `current`, `evidence` and `history` under the test-data directory. The controller caps tracked data at 6 GiB: 3 GiB runtime, 1.5 GiB evidence and 1.5 GiB staging. New history counts against evidence. A quota refusal does not authorize deleting earlier material.

## Run a game scenario

Choose an action for the changed behaviour. `local-upgrade-check` exercises upgrades. `migration-mp` exercises a local multiplayer session. These actions start Valheim.

Require a hidden test window and mute only its own Windows audio session. Check the quiet-status fields before proceeding. Use the prepared QA character and world. Limit the scenario to 120 seconds after world and character readiness. Measure cold loading separately. Do not take focus from other applications.

Record the DLL hash and observed results. Offline tests do not establish native UI, device-audio or multiplayer acceptance. Preserve earlier recordings and reports needed for comparison.
