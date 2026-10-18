param([string]$ProductDll,[string]$ReportPath)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bin=Get-QaRuntimePath 'bin/offline/upgrade-bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$game=$env:VALHEIM_QA_CLIENT
$core=$env:VALHEIM_BEPINEX_CORE
if(!$game -or !$core){throw 'Set VALHEIM_QA_CLIENT and VALHEIM_BEPINEX_CORE; see QA.md.'}
$product=if($ProductDll){[IO.Path]::GetFullPath($ProductDll)}else{Get-QaRuntimePath 'stage/ValheimBoombox.dll'}
$arguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest',('/out:'+(Join-Path $bin 'upgrade-checks.dll')))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
$arguments+='/reference:'+$product
$arguments+='/reference:'+(Join-Path $core 'BepInEx.dll')
$arguments+=Join-Path $PSScriptRoot 'UpgradeChecks.cs'
$arguments+=Join-Path $PSScriptRoot '../integration/QaEnvironment.cs'
$response=Join-Path $bin 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $bin 'upgrade-checks.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
if(!$ReportPath){$ReportPath=Get-QaRuntimePath 'evidence/offline/upgrade-result.json'}
& dotnet (Join-Path $bin 'upgrade-checks.dll') $product (Join-Path $game 'valheim_Data/Managed') $core $bin
$upgradeExit=$LASTEXITCODE
if($ReportPath -and (Test-Path -LiteralPath (Join-Path $bin 'upgrade-result.json'))){[IO.Directory]::CreateDirectory((Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))))|Out-Null;Copy-Item -LiteralPath (Join-Path $bin 'upgrade-result.json') -Destination $ReportPath -Force}
exit $upgradeExit
