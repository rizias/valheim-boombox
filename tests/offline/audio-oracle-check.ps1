param([Parameter(Mandatory=$true)][string]$ProductDll)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'tools/Qa-Toolchain.ps1')
$fixture=Get-QaRuntimePath ('bin/offline/audio-oracle-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture)|Out-Null
$output=Join-Path $fixture 'audio-oracle-check.dll'
$arguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest','/main:AudioOracleChecks',('/out:'+$output))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
$arguments+='/reference:'+([IO.Path]::GetFullPath($ProductDll))
foreach($source in @('StrikeChecks.cs','NativeMusicChecks.cs','MembraneChecks.cs','LongTrackChecks.cs')){$arguments+=Join-Path $project ('tests/offline/'+$source)}
$arguments+=Join-Path $project 'tests/integration/QaEnvironment.cs'
$arguments+=Join-Path $PSScriptRoot 'audio-oracle-check.cs'
$response=Join-Path $fixture 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $fixture 'audio-oracle-check.runtimeconfig.json'),$qaRuntimeJson)
Copy-Item -LiteralPath $ProductDll -Destination (Join-Path $fixture 'ValheimBoombox.dll')
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Audio oracle compile failed.'}
& dotnet $output $fixture
if($LASTEXITCODE -ne 0){throw 'Audio oracle behavior verification failed.'}
Write-Output ('FIXTURE_RETAINED '+$fixture)
