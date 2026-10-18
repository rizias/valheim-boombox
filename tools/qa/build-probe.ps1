$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Qa-Toolchain.ps1')
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bin=Get-QaRuntimePath 'bin'
$integrationSource=Join-Path $project 'tests/integration'
[IO.Directory]::CreateDirectory($bin)|Out-Null
$game=$env:VALHEIM_QA_CLIENT
$core=$env:VALHEIM_BEPINEX_CORE
if(!$game -or !$core){throw 'Set VALHEIM_QA_CLIENT and VALHEIM_BEPINEX_CORE; see QA.md.'}
$out=Join-Path $bin 'QaBootstrap.dll'
$arguments=@('/nologo','/target:library','/nostdlib+','/langversion:latest','/optimize+','/warn:4','/warnaserror+',('/out:'+$out))
foreach($reference in Get-ChildItem -LiteralPath (Join-Path $game 'valheim_Data/Managed') -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
foreach($name in @('BepInEx.dll','0Harmony.dll')){$arguments+='/reference:'+(Join-Path $core $name)}
foreach($sourceFile in Get-ChildItem -LiteralPath (Join-Path $integrationSource 'probe') -Filter '*.cs'){if($sourceFile.Name -ne 'QaPreloader.cs'){$arguments+=$sourceFile.FullName}}
$response=Join-Path $bin 'probe.rsp'
$arguments+=Join-Path $integrationSource 'gameplay/QaGameplayVideoOwnAudioSessionMute.cs'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$patchArgs=@('/nologo','/target:library','/nostdlib+','/langversion:latest',('/out:'+(Join-Path $bin 'QaPreloader.dll')))
foreach($reference in Get-ChildItem -LiteralPath (Join-Path $game 'valheim_Data/Managed') -Filter '*.dll'){$patchArgs+='/reference:'+$reference.FullName}
$patchArgs+='/reference:'+(Join-Path $core 'Mono.Cecil.dll')
$patchArgs+=Join-Path $integrationSource 'probe/QaPreloader.cs'
[IO.File]::WriteAllLines((Join-Path $bin 'patcher.rsp'),@($patchArgs|ForEach-Object{'"'+$_+'"'}))
& dotnet $qaCompiler ('@'+(Join-Path $bin 'patcher.rsp'))
exit $LASTEXITCODE
