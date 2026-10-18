$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'tools/Qa-Toolchain.ps1')
$fixture=Get-QaRuntimePath ('bin/offline/restart-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture)|Out-Null
$output=Join-Path $fixture 'restart-check.dll'
$arguments=@('/nologo','/target:exe','/main:RestartCheck','/nostdlib+','/langversion:latest','/nullable:annotations','/warn:4','/warnaserror+',('/out:'+$output))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
foreach($source in 'QaSupervisor.cs','QaJob.cs','QaNetworkSession.cs','QaUiCombo.cs'){$arguments+=Join-Path $project ('tests/integration/'+$source)}
$arguments+=Join-Path $PSScriptRoot 'restart-check.cs'
$response=Join-Path $fixture 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $fixture 'restart-check.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Actual restart source compile failed.'}
$outputLines=@(& dotnet $output 2>&1)
$code=$LASTEXITCODE
[IO.File]::WriteAllLines((Join-Path $fixture 'output.log'),@($outputLines|ForEach-Object{$_.ToString()}))
$outputLines|Write-Output
if($code -ne 0){throw 'Actual restart error-path verification failed.'}
Write-Output ('FIXTURE_RETAINED '+$fixture)
