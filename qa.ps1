param(
    [string]$Action='environment-check',
    [string]$ClientRoot=$env:VALHEIM_QA_CLIENT,
    [string]$ServerRoot=$env:VALHEIM_QA_SERVER,
    [string]$BepInExCore=$env:VALHEIM_BEPINEX_CORE
)
$ErrorActionPreference='Stop'
if($Action -eq 'cases') {
    Get-Content (Join-Path $PSScriptRoot 'tests/cases.json') -Raw | ConvertFrom-Json | Select-Object id,
    decision,
    currentContext
    return
}
$saved=@{}
foreach(
    $key in @(
        'VALHEIM_QA_ROOT',
        'VALHEIM_QA_CLIENT',
        'VALHEIM_QA_SERVER',
        'VALHEIM_BEPINEX_CORE',
        'VALHEIM_QA_COMPILER',
        'VALHEIM_QA_REFERENCES',
        'VALHEIM_QA_POWERSHELL'
    )
){
    $saved[$key]=[Environment]::GetEnvironmentVariable($key)
}
try {
    $env:VALHEIM_QA_CLIENT=$ClientRoot
    $env:VALHEIM_QA_SERVER=$ServerRoot
    $env:VALHEIM_BEPINEX_CORE=$BepInExCore
    . (Join-Path $PSScriptRoot 'tools/Qa-Toolchain.ps1')
    & $env:VALHEIM_QA_POWERSHELL -NoProfile -File (Join-Path $PSScriptRoot 'tools/qa/run.ps1') -Action $Action
    if($LASTEXITCODE -ne 0){
        exit $LASTEXITCODE
    }
    exit $LASTEXITCODE
} finally {
    foreach($key in $saved.Keys){
        [Environment]::SetEnvironmentVariable($key,$saved[$key])
    }
}
