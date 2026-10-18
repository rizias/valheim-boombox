param([string]$ProductDll)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bin=Get-QaRuntimePath 'bin/offline/bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$dll=Join-Path $bin 'data-checks.dll'
$arguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest',('/out:'+$dll))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
$product=if($ProductDll){[IO.Path]::GetFullPath($ProductDll)}else{Get-QaRuntimePath 'stage/ValheimBoombox.dll'}
$arguments+='/reference:'+$product
$arguments+=Join-Path $PSScriptRoot 'DataChecks.cs'
$arguments+=Join-Path $PSScriptRoot '../integration/QaEnvironment.cs'
Copy-Item -LiteralPath $product -Destination (Join-Path $bin 'ValheimBoombox.dll') -Force
[IO.File]::WriteAllLines((Join-Path $bin 'compile.rsp'),@($arguments|ForEach-Object{'"'+$_+'"'}),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $bin 'data-checks.runtimeconfig.json'),$qaRuntimeJson,[Text.UTF8Encoding]::new($false))
& dotnet $qaCompiler ('@'+(Join-Path $bin 'compile.rsp'))
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet $dll $root
exit $LASTEXITCODE
