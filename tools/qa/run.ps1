param([string]$Action='status')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../Qa-Toolchain.ps1')
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bin=Get-QaRuntimePath 'bin'
$source=Join-Path $project 'tests/integration'
New-Item -ItemType Directory -Path $bin -Force|Out-Null
$arguments=@('/nologo','/target:exe','/nostdlib+','/deterministic+','/langversion:latest','/nullable:annotations','/warn:4','/warnaserror+',('/out:'+(Join-Path $bin 'qa.dll')))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
foreach($source in Get-ChildItem -LiteralPath $source -Filter '*.cs'){$arguments+=$source.FullName}
$response=Join-Path $bin 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $bin 'qa.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet (Join-Path $bin 'qa.dll') $project $Action
exit $LASTEXITCODE
