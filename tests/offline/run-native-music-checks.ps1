param([string]$AudioTools)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
$checkRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checkBin=Get-QaRuntimePath 'bin/offline/native-bin'
New-Item -ItemType Directory -Path $checkBin -Force | Out-Null
$checkDll=Join-Path $checkBin 'native-music-checks.dll'
$checkArguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest',('/out:'+$checkDll))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$checkArguments+='/reference:'+$reference.FullName}
$checkProduct=Get-QaRuntimePath 'stage/ValheimBoombox.dll'
$checkArguments+='/reference:'+$checkProduct
$checkArguments+=Join-Path $PSScriptRoot 'NativeMusicChecks.cs'
$checkArguments+=Join-Path $PSScriptRoot '../integration/QaEnvironment.cs'
$checkArguments+=Join-Path $PSScriptRoot 'MembraneChecks.cs'
$checkArguments+=Join-Path $PSScriptRoot 'LongTrackChecks.cs'
Copy-Item -LiteralPath $checkProduct -Destination (Join-Path $checkBin 'ValheimBoombox.dll') -Force
[IO.File]::WriteAllLines((Join-Path $checkBin 'compile.rsp'),@($checkArguments|ForEach-Object{'"'+$_+'"'}),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $checkBin 'native-music-checks.runtimeconfig.json'),$qaRuntimeJson,[Text.UTF8Encoding]::new($false))
& dotnet $qaCompiler ('@'+(Join-Path $checkBin 'compile.rsp'))
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
if($AudioTools){& dotnet $checkDll $checkRoot ([IO.Path]::GetFullPath($AudioTools))}else{& dotnet $checkDll $checkRoot}
exit $LASTEXITCODE
