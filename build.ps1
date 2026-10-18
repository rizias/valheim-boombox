param(
    [Parameter(Mandatory = $true)][string]$GameManaged,
    [Parameter(Mandatory = $true)][string]$BepInExCore,
    [string]$Compiler,
    [string]$OutputDirectory,
    [switch]$DllOnly,
    [string]$ProtocolSource
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'check-translations.ps1')
$outputFolder = if ($OutputDirectory) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    Join-Path $PSScriptRoot 'outputs\ValheimBoombox'
}
New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null
if (-not $Compiler) {
    $buildSdks = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if ($buildSdks.Count -eq 0 -or $buildSdks[-1] -notmatch '^(\S+) \[(.+)\]$') {
        throw '.NET SDK was not found'
    }
    $Compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
}
if (-not (Test-Path -LiteralPath (Join-Path $GameManaged 'assembly_valheim.dll')) -or
    -not (Test-Path -LiteralPath (Join-Path $BepInExCore 'BepInEx.dll'))) {
    throw 'Supply the installed Valheim Managed and BepInEx core directories'
}
$arguments = @(
    $compiler,
    '/nologo',
    '/target:library',
    '/nostdlib+',
    '/optimize+',
    '/deterministic+',
    '/langversion:latest',
    '/warn:4',
    '/warnaserror+',
    ('/out:' + (Join-Path $outputFolder 'ValheimBoombox.dll'))
)
foreach (
    $reference in Get-ChildItem -LiteralPath $GameManaged -Filter '*.dll'
) {
    $arguments += '/reference:' + $reference.FullName
}
$arguments += '/reference:' + (Join-Path $BepInExCore 'BepInEx.dll')
$arguments += '/reference:' + (Join-Path $BepInExCore '0Harmony.dll')
foreach ($translation in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Translations') -Filter '*.json' | Sort-Object Name) {
    $arguments += '/resource:' + $translation.FullName + ',ValheimBoombox.Translations.' + $translation.Name
}
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\valheimboombox_bassfx') + ',ValheimBoombox.BassEffects.bundle'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\valheimboombox_v3_2') + ',ValheimBoombox.Model.bundle'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\valheimboombox_musicfx') + ',ValheimBoombox.MusicEffects.bundle'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\contact-shapes.json') + ',ValheimBoombox.ContactShapes.json'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\pendant-fit.json') + ',ValheimBoombox.PendantFit.json'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\pendant-particles.json') + ',ValheimBoombox.PendantParticles.json'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\ramsund-dragon-head.png') + ',ValheimBoombox.RamsundDragonHead.png'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\cassette-icon.png') + ',ValheimBoombox.CassetteIcon.png'
foreach (
    $icon in @(
        'wave',
        'health',
        'stamina',
        'eitr'
    )
) {
    $arguments += '/resource:' + (Join-Path $PSScriptRoot ('assets/bonus-icons/' + $icon + '.png')) + ',ValheimBoombox.Bonus.' + $icon + '.png'
}
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs') {
    $arguments += if (
        $ProtocolSource -and $source.Name -eq 'BoomboxNetworkProtocol.cs'
    ) {
        [IO.Path]::GetFullPath($ProtocolSource)
    } else {
        $source.FullName
    }
}
$responseFile = Join-Path $outputFolder 'build.rsp'
[IO.File]::WriteAllLines(
    $responseFile,
    @(
        $arguments | Select-Object -Skip 1 | ForEach-Object {
            '"' + $_ + '"'
        }
    ),
    [Text.UTF8Encoding]::new($false)
)
& dotnet $Compiler ('@' + $responseFile)
$buildExit = $LASTEXITCODE
if ($buildExit -ne 0) {
    exit $buildExit
}
if (-not $DllOnly) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $outputFolder 'README.md') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Translations') -Destination $outputFolder -Recurse -Force
}
Write-Output 'boombox_build_ok'
