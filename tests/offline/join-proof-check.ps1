$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'tools/Qa-Toolchain.ps1')
$fixture=Get-QaRuntimePath ('bin/offline/boombox-join-proof-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture)|Out-Null
$output=Join-Path $fixture 'join-proof-check.dll'
$arguments=@('/nologo','/target:exe','/main:JoinProofCheck','/nostdlib+','/langversion:latest','/nullable:annotations','/warn:4','/warnaserror+',('/out:'+$output))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
foreach($source in Get-ChildItem -LiteralPath (Join-Path $project 'tests/integration') -Filter '*.cs'){$arguments+=$source.FullName}
$arguments+=Join-Path $PSScriptRoot 'join-proof-check.cs'
$response=Join-Path $fixture 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $fixture 'join-proof-check.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Join proof actual-controller compile failed.'}
& dotnet $output (Join-Path $fixture 'fixtures')
if($LASTEXITCODE -ne 0){throw 'Join proof behavior check failed.'}
Write-Output ('FIXTURES_RETAINED '+$fixture)
