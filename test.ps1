param(
    [Parameter(Mandatory)][string]$GameManaged,
    [Parameter(Mandatory)][string]$BepInExCore,
    [Parameter(Mandatory)][string]$ProductDll,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'outputs/tests'),
    [ValidateSet(
        'UpgradeChecks',
        'ReviewRegressionChecks',
        'SlotsApiChecks',
        'membrane-silence-check',
        'LocalizationChecks',
        'BoatVisibilityChecks'
    )]
    [string[]]$Suites = @(
        'UpgradeChecks',
        'ReviewRegressionChecks',
        'SlotsApiChecks',
        'membrane-silence-check',
        'LocalizationChecks',
        'BoatVisibilityChecks'
    )
)
$ErrorActionPreference = 'Stop'
$GameManaged = (Resolve-Path -LiteralPath $GameManaged).Path
$BepInExCore = (Resolve-Path -LiteralPath $BepInExCore).Path
$ProductDll = (Resolve-Path -LiteralPath $ProductDll).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
. (Join-Path $PSScriptRoot 'tools/DotNet-Toolchain.ps1')
$toolchain = Get-BoomboxToolchain
$compiler = $toolchain.Compiler
$references = @(Get-ChildItem -LiteralPath $toolchain.References -Filter '*.dll' |
    ForEach-Object { '/reference:' + $_.FullName })
$runtimeConfig = $toolchain.RuntimeJson
$results = [Collections.Generic.List[object]]::new()
foreach ($suite in $Suites) {
    $output = Join-Path $OutputDirectory ($suite + '.dll')
    $arguments = @('/nologo','/target:exe','/nostdlib+','/langversion:latest','/nullable:annotations',('/out:' + $output)) + $references
    if ($suite -eq 'UpgradeChecks') {
        $arguments += '/reference:' + $ProductDll
        $arguments += '/reference:' + (Join-Path $BepInExCore 'BepInEx.dll')
    }
    if ($suite -eq 'BoatVisibilityChecks') {
        $arguments += Join-Path $PSScriptRoot 'src/BoomboxBoatHelp.cs'
    }
    $arguments += Join-Path $PSScriptRoot ('tests/offline/' + $suite + '.cs')
    $response = Join-Path $OutputDirectory ($suite + '.rsp')
    [IO.File]::WriteAllLines($response, @($arguments | ForEach-Object { '"' + $_ + '"' }))
    & dotnet $compiler ('@' + $response)
    if ($LASTEXITCODE -ne 0) { throw ('Compilation failed: ' + $suite) }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory ($suite + '.runtimeconfig.json')), $runtimeConfig)
    $variants = if ($suite -eq 'SlotsApiChecks') { @('absent','missing-add','missing-find','missing-grid','wrong-return','throw-add','throw-find','throw-grid','working') } else { @('default') }
    foreach ($variant in $variants) {
        $fixture = Join-Path $OutputDirectory ($suite + '-' + $variant)
        New-Item -ItemType Directory -Path $fixture -Force | Out-Null
        $lastArgument = if ($suite -eq 'SlotsApiChecks') { $variant } else { $fixture }
        $lines = @(& dotnet $output $ProductDll $GameManaged $BepInExCore $lastArgument)
        if ($LASTEXITCODE -ne 0) { throw ('Check failed: ' + $suite + ' / ' + $variant + ': ' + ($lines -join "`n")) }
        $result = ($lines | Select-Object -Last 1) | ConvertFrom-Json
        if (-not $result.passed) { throw ('Failed result: ' + $suite) }
        $results.Add([ordered]@{ suite = $suite; variant = $variant; result = $result })
        Write-Output ($suite + ' / ' + $variant + ': passed')
    }
}
$report = [ordered]@{ passed = $true; productSha256 = (Get-FileHash -LiteralPath $ProductDll).Hash; results = $results }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'results.json'), ($report | ConvertTo-Json -Depth 12))
Write-Output 'Offline checks passed; no game, installation or profile changes.'
