$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Reference downloads are restricted to the ephemeral GitHub runner.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$toolRoot = Join-Path $env:RUNNER_TEMP 'boombox-references'
$steamRoot = Join-Path $toolRoot 'steamcmd'
$serverRoot = Join-Path $toolRoot 'server'
$bepRoot = Join-Path $toolRoot 'bepinex'
[IO.Directory]::CreateDirectory($steamRoot) | Out-Null
$steamZip = Join-Path $toolRoot 'steamcmd.zip'
Invoke-WebRequest -Uri 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $steamZip
Expand-Archive -LiteralPath $steamZip -DestinationPath $steamRoot
$steamCmd = Join-Path $steamRoot 'steamcmd.exe'
Push-Location -LiteralPath $steamRoot
try {
    & $steamCmd +login anonymous +app_info_update 1 +app_info_print 896660 +quit
    if ($LASTEXITCODE -ne 0) { throw 'SteamCMD could not initialize the Valheim application metadata.' }
    & $steamCmd +force_install_dir $serverRoot +login anonymous +app_update 896660 validate +quit
    if ($LASTEXITCODE -ne 0) { throw 'SteamCMD could not install Valheim dedicated-server build references.' }
} finally { Pop-Location }
$managed = Join-Path $serverRoot 'valheim_server_Data/Managed'
if (-not (Test-Path -LiteralPath (Join-Path $managed 'assembly_valheim.dll') -PathType Leaf)) { throw 'Dedicated-server Managed references are missing.' }
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'manifest.json') | ConvertFrom-Json
$dependency = @($manifest.dependencies | Where-Object { $_ -match '^denikson-BepInExPack_Valheim-(\d+\.\d+\.\d+)$' })
if ($dependency.Count -ne 1) { throw 'Manifest must pin one denikson BepInExPack_Valheim version.' }
$bepVersion = $dependency[0].Substring('denikson-BepInExPack_Valheim-'.Length)
$bepZip = Join-Path $toolRoot 'bepinex.zip'
Invoke-WebRequest -Uri ('https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/' + $bepVersion + '/') -OutFile $bepZip
Expand-Archive -LiteralPath $bepZip -DestinationPath $bepRoot
$cores = @(Get-ChildItem -LiteralPath $bepRoot -Recurse -File -Filter BepInEx.dll | Where-Object { $_.Directory.Name -eq 'core' -and $_.Directory.Parent.Name -eq 'BepInEx' })
if ($cores.Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $cores[0].DirectoryName '0Harmony.dll'))) { throw 'Downloaded pack does not contain one complete BepInEx/core directory.' }
$settings = [ordered]@{ GameManaged=$managed; BepInExCore=$cores[0].DirectoryName; FFmpegRoot=(Join-Path $env:RUNNER_TEMP 'boombox-ffmpeg'); PwshPath=(Get-Process -Id $PID).Path }
[IO.File]::WriteAllText((Join-Path $root 'release.local.json'), ($settings | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output 'Ephemeral build references prepared. No game or dedicated server was launched.'
