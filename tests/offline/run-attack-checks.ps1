$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
$attackRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$attackBin=Get-QaRuntimePath 'bin/offline/attack-bin'
New-Item -ItemType Directory -Path $attackBin -Force|Out-Null
$attackArgs=@('/nologo','/target:exe','/nostdlib+','/langversion:latest',('/out:'+(Join-Path $attackBin 'checks.dll')))
foreach($attackRef in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$attackArgs+='/reference:'+$attackRef.FullName}
$attackProduct=Get-QaRuntimePath 'stage/ValheimBoombox.dll'
$attackArgs+='/reference:'+$attackProduct
$attackArgs+=Join-Path $PSScriptRoot 'AttackChecks.cs'
$attackArgs+=Join-Path $PSScriptRoot '../integration/QaEnvironment.cs'
Copy-Item -LiteralPath $attackProduct -Destination $attackBin -Force
[IO.File]::WriteAllLines((Join-Path $attackBin 'compile.rsp'),@($attackArgs|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $attackBin 'checks.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+(Join-Path $attackBin 'compile.rsp'))
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet (Join-Path $attackBin 'checks.dll') $attackRoot
exit $LASTEXITCODE
