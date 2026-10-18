param([switch]$KeepFixtures)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'tools/Qa-Toolchain.ps1')
$fixtureParent=[IO.Path]::GetFullPath((Get-QaRuntimePath 'bin/offline'))
$fixtureName='boombox-config-rollback-'+[guid]::NewGuid().ToString('N')
$fixture=Join-Path $fixtureParent $fixtureName
if(Test-Path -LiteralPath $fixture){throw 'New rollback fixture directory already exists.'}
[IO.Directory]::CreateDirectory($fixture)|Out-Null
$output=Join-Path $fixture 'config-rollback-check.dll'
$arguments=@('/nologo','/target:exe','/nostdlib+','/langversion:latest','/nullable:annotations','/warn:4','/warnaserror+',('/out:'+$output))
foreach($reference in Get-ChildItem -LiteralPath $qaReferences -Filter '*.dll'){$arguments+='/reference:'+$reference.FullName}
foreach($source in @('QaLedger.cs','QaFiles.cs','QaConfigRollback.cs')){$arguments+=Join-Path $project ('tests/integration/'+$source)}
$arguments+=Join-Path $PSScriptRoot 'config-rollback-check.cs'
$response=Join-Path $fixture 'compile.rsp'
[IO.File]::WriteAllLines($response,@($arguments|ForEach-Object{'"'+$_+'"'}))
[IO.File]::WriteAllText((Join-Path $fixture 'config-rollback-check.runtimeconfig.json'),$qaRuntimeJson)
& dotnet $qaCompiler ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Rollback fixture compile failed.'}
$primary=Join-Path $fixture 'primary'
& dotnet $output $primary prepare
if($LASTEXITCODE -ne 0){throw 'Rollback prepare fixture failed.'}
& dotnet $output $primary recover
if($LASTEXITCODE -ne 0){throw 'Rollback new-process recovery fixture failed.'}
& dotnet $output (Join-Path $fixture 'safety') safety
if($LASTEXITCODE -ne 0){throw 'Rollback safety fixture failed.'}
if($KeepFixtures){
    Write-Output ('FIXTURES_RETAINED '+$fixture)
}else{
    $resolved=[IO.Path]::GetFullPath($fixture)
    $directory=Get-Item -LiteralPath $resolved
    if($directory.Parent.FullName -ne $fixtureParent -or $directory.Name -cne $fixtureName -or
       ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)){
        throw 'Rollback fixture cleanup target does not match this run.'
    }
    if(@(Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object {
        $_.Attributes -band [IO.FileAttributes]::ReparsePoint
    }).Count){throw 'Rollback fixture contains a reparse point; preserving it.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
    Write-Output 'Rollback check passed; successful temporary fixture removed.'
}
