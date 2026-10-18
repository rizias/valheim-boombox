param(
    [Parameter(Mandatory=$true)][string]$ProductDll,
    [Parameter(Mandatory=$true)][string]$GameManaged,
    [Parameter(Mandatory=$true)][string]$BepInExCore
)
$ErrorActionPreference='Stop'
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
 . (Join-Path $project 'tools/Qa-Toolchain.ps1')
$shell=(Get-Process -Id $PID).Path
$sha=(Get-FileHash -LiteralPath $ProductDll).Hash
$evidence=Get-QaRuntimePath ('evidence/offline/'+$sha.Substring(0,8)+'-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
[IO.Directory]::CreateDirectory($evidence)|Out-Null
$env:VALHEIM_QA_CLIENT=Split-Path (Split-Path $GameManaged -Parent) -Parent
$env:VALHEIM_BEPINEX_CORE=$BepInExCore
$commands=@(
    @{name='regressions';file='tests/offline/run-review-regressions.ps1';args=@('-ProductDll',$ProductDll,'-GameManaged',$GameManaged,'-BepInExCore',$BepInExCore)},
    @{name='data';file='tests/offline/run-data-checks.ps1';args=@('-ProductDll',$ProductDll)},
    @{name='upgrades';file='tests/offline/run-upgrade-checks.ps1';args=@('-ProductDll',$ProductDll,'-ReportPath',(Join-Path $evidence 'upgrade-result.json'))},
    @{name='membrane';file='tests/offline/membrane-silence-check.ps1';args=@('-ProductDll',$ProductDll,'-GameManaged',$GameManaged,'-BepInExCore',$BepInExCore)},
    @{name='config-rollback';file='tests/offline/config-rollback-check.ps1';args=@()},
    @{name='release-receipt';file='tests/offline/release-receipt-check.ps1';args=@()},
    @{name='join-proof';file='tests/offline/join-proof-check.ps1';args=@()},
    @{name='bounded-aura';file='tests/offline/bounded-aura-check.ps1';args=@()},
    @{name='audio-oracle';file='tests/offline/audio-oracle-check.ps1';args=@('-ProductDll',$ProductDll)}
)
$results=[Collections.Generic.List[object]]::new()
foreach($command in $commands){
    $file=Join-Path $project $command.file
    $arguments=$command.args
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $output=@(& $shell -NoProfile -File $file @arguments 2>&1)
    $code=$LASTEXITCODE
    $log=Join-Path $evidence ($command.name+'.log')
    [IO.File]::WriteAllLines($log,@($output|ForEach-Object{$_.ToString()}),[Text.UTF8Encoding]::new($false))
    $results.Add([ordered]@{name=$command.name;script=$command.file;scriptSha=(Get-FileHash -LiteralPath $file).Hash;exitCode=$code;seconds=$clock.Elapsed.TotalSeconds;log=$log})
    $result=[ordered]@{passed=(@($results|Where-Object exitCode -ne 0).Count -eq 0);productSha=$sha;qaSha=(Get-FileHash -LiteralPath (Get-QaRuntimePath 'bin/qa.dll')).Hash;scope='Offline corrected product and actual QA predicates; no game, install or native-render acceptance';commands=$results}
    $result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
    if($code -ne 0){throw ('Failed '+$command.name+'; exact output retained in '+$log)}
    Write-Output ('PASS '+$command.name+' '+$clock.Elapsed.TotalSeconds.ToString('0.000')+'s')
}
Write-Output ('EVIDENCE '+$evidence)
