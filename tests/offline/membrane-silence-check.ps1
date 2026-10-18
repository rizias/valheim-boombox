param(
    [Parameter(Mandatory=$true)][string]$ProductDll,
    [Parameter(Mandatory=$true)][string]$GameManaged,
    [Parameter(Mandatory=$true)][string]$BepInExCore
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
$bin=Get-QaRuntimePath 'bin/offline/membrane-silence'
[IO.Directory]::CreateDirectory($bin)|Out-Null
$output=Join-Path $bin 'membrane-silence.dll'
$arguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest',('/out:'+$output))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
$arguments+=Join-Path $PSScriptRoot 'membrane-silence-check.cs'
$response=Join-Path $bin 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $bin 'membrane-silence.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet $output $ProductDll $GameManaged $BepInExCore
exit $LASTEXITCODE
