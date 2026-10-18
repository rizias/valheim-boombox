param([Parameter(Mandatory=$true)][string]$Music, [Parameter(Mandatory=$true)][string]$FFmpeg, [string]$Work, [string]$ProductDll)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
if(!$Work){$Work=Get-QaRuntimePath 'current/offline/strike-checks'}
$checkRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checkBin=Get-QaRuntimePath 'bin/offline/strike-bin'
New-Item -ItemType Directory -Path $checkBin -Force | Out-Null
$checkDll=Join-Path $checkBin 'strike-checks.dll'
$checkArguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest',('/out:'+$checkDll))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$checkArguments+='/reference:'+$reference.FullName}
$checkProduct=if($ProductDll){[IO.Path]::GetFullPath($ProductDll)}else{Get-QaRuntimePath 'stage/ValheimBoombox.dll'}
$checkArguments+='/reference:'+$checkProduct
$checkArguments+=Join-Path $PSScriptRoot 'StrikeChecks.cs'
Copy-Item -LiteralPath $checkProduct -Destination (Join-Path $checkBin 'ValheimBoombox.dll') -Force
[IO.File]::WriteAllLines((Join-Path $checkBin 'compile.rsp'),@($checkArguments|ForEach-Object{'"'+$_+'"'}),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $checkBin 'strike-checks.runtimeconfig.json'),$qaRuntimeJson,[Text.UTF8Encoding]::new($false))
& dotnet $qaCompiler ('@'+(Join-Path $checkBin 'compile.rsp'))
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet $checkDll $checkRoot $Music $FFmpeg $Work
exit $LASTEXITCODE
