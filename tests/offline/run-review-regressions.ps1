param(
    [Parameter(Mandatory=$true)][string]$ProductDll,
    [Parameter(Mandatory=$true)][string]$GameManaged,
    [Parameter(Mandatory=$true)][string]$BepInExCore,
    [string]$ReportPath
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
$bin=Get-QaRuntimePath 'bin/offline/review-bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$arguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest',('/out:'+(Join-Path $bin 'review-checks.dll')))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
$arguments+=Join-Path $PSScriptRoot 'ReviewRegressionChecks.cs'
$response=Join-Path $bin 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $bin 'review-checks.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
$result=& dotnet (Join-Path $bin 'review-checks.dll') $ProductDll $GameManaged $BepInExCore (Join-Path $bin 'fixtures')
$resultCode=$LASTEXITCODE
$result | Write-Output
$report=if($ReportPath){[IO.Path]::GetFullPath($ReportPath)}else{Join-Path $bin ('result-'+(Get-FileHash -LiteralPath $ProductDll).Hash.Substring(0,8)+'.json')}
[IO.File]::WriteAllText($report,($result -join [Environment]::NewLine))
exit $resultCode
