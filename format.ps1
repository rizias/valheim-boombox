param([switch]$Check)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/DotNet-Toolchain.ps1')
$toolchain = Get-BoomboxToolchain
$sdkRoot = $toolchain.SdkRoot
$formatter = Join-Path $sdkRoot 'DotnetTools/dotnet-format/dotnet-format.dll'
if (-not (Test-Path -LiteralPath $formatter -PathType Leaf)) {
    throw 'The installed .NET SDK does not contain dotnet-format.'
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src'), (Join-Path $PSScriptRoot 'tests'), (Join-Path $PSScriptRoot 'tools/format') -Filter '*.cs' -File -Recurse |
    ForEach-Object { $_.FullName })
$helperSource = Join-Path $PSScriptRoot 'tools/format/FormatLayout.cs'
$helperDirectory = Join-Path $PSScriptRoot 'outputs/format'
$helper = Join-Path $helperDirectory 'FormatLayout.dll'
$sourceList = Join-Path $helperDirectory 'sources.txt'
$roslyn = Join-Path $sdkRoot 'Roslyn/bincore'
New-Item -ItemType Directory -Path $helperDirectory -Force | Out-Null
$references = @(Get-ChildItem -LiteralPath $toolchain.References -Filter '*.dll' |
    ForEach-Object { '/reference:' + $_.FullName })
$compileArguments = @('/nologo', '/target:exe', '/nostdlib+', '/warnaserror+', ('/out:' + $helper)) + $references + @(
    ('/reference:' + (Join-Path $roslyn 'Microsoft.CodeAnalysis.dll')),
    ('/reference:' + (Join-Path $roslyn 'Microsoft.CodeAnalysis.CSharp.dll')),
    $helperSource
)
$response = Join-Path $helperDirectory 'FormatLayout.rsp'
[IO.File]::WriteAllLines($response, @($compileArguments | ForEach-Object { '"' + $_ + '"' }))
& dotnet (Join-Path $roslyn 'csc.dll') ('@' + $response)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
[IO.File]::WriteAllText((Join-Path $helperDirectory 'FormatLayout.runtimeconfig.json'), $toolchain.RuntimeJson)
$mode = if ($Check) { 'check' } else { 'apply' }
[IO.File]::WriteAllLines($sourceList, $sources)
& dotnet $helper $roslyn $mode $sourceList
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$relativeSources = @($sources | ForEach-Object { [IO.Path]::GetRelativePath($PSScriptRoot, $_) })
$arguments = @($formatter, 'whitespace', $PSScriptRoot, '--folder', '--include-generated', '--include') + $relativeSources
if ($Check) { $arguments += '--verify-no-changes' }
Push-Location -LiteralPath $PSScriptRoot
try {
    & dotnet @arguments
    $formatExit = $LASTEXITCODE
    if (-not $Check -and $formatExit -eq 0) {
        # Roslyn can require a second pass to indent nested statements after expanding blocks.
        & dotnet @arguments
        $formatExit = $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
if ($formatExit -ne 0) { exit $formatExit }
