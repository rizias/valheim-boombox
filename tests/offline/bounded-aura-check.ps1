param([string]$QaDll=(Join-Path $env:VALHEIM_QA_ROOT 'bin/qa.dll'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../tools/Qa-Toolchain.ps1')
Add-Type -Path ([IO.Path]::GetFullPath($QaDll))
$fixture=Get-QaRuntimePath ('bin/offline/boombox-bounded-aura-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture)|Out-Null
$checks=[Collections.Generic.List[string]]::new()
function Write-Proofs {
    foreach($scope in 'cassettes','combined','sources','settings') {
        $groups=if($scope -eq 'cassettes'){@('A-cassette-only','A-decoration-only')}elseif($scope -eq 'combined'){@('A-combined','B-combined')}elseif($scope -eq 'sources'){@('A+B','A+B+C')}else{@()}
        $entries=@()
        if($groups.Count){$entries+=@{id='AURA-movement-control';passed=$true}}
        foreach($group in $groups){foreach($kind in 'native-movement','native-recovery'){$entries+=@{id="$group-$kind";passed=$true}}}
        if($scope -eq 'settings'){$entries+=@{id='CONFIG-HUD-complete';passed=$true}}
        [IO.File]::WriteAllText((Join-Path $fixture "$scope.json"),(@{candidateSha='CURRENT';scope=$scope;completed=$true;cases=$entries}|ConvertTo-Json -Depth 6))
        [IO.File]::WriteAllText((Join-Path $fixture "$scope-cycle.json"),(@{candidateSha='CURRENT';scenario="aura-completion-$scope";passed=$true;seconds=104.1}|ConvertTo-Json))
    }
}
function Reject([string]$name,[scriptblock]$operation) {
    $rejected=$false
    try {& $operation|Out-Null} catch {$rejected=$true}
    if(!$rejected){throw "Incorrectly accepted: $name"}
    $checks.Add($name)
}
Write-Proofs
$accepted=[BoomboxQa.QaAuraCompletion]::RecoveryEvidence($fixture,'CURRENT')
if($accepted.Count -ne 6){throw 'Complete scopes must retain six independent recovery groups'}
$checks.Add('Complete current scopes retain all six recovery groups')
Reject 'Unscoped old action refused before launch' {[BoomboxQa.QaAuraCompletion]::Scenario('network-auras')}
Reject 'Unknown scope refused before launch' {[BoomboxQa.QaAuraCompletion]::Scenario('network-auras-unknown')}
foreach($scope in 'cassettes','combined','sources','settings') {
    if([BoomboxQa.QaAuraCompletion]::Scenario("network-auras-$scope") -ne "aura-completion-$scope"){throw "Invalid scoped route: $scope"}
}
$checks.Add('All four explicit actions route to corresponding scopes')
Reject 'Prior product SHA rejected' {[BoomboxQa.QaAuraCompletion]::RecoveryEvidence($fixture,'ANOTHER')}
[IO.File]::WriteAllText((Join-Path $fixture 'settings.json'),'{}')
Reject 'Partial scope set rejected' {[BoomboxQa.QaAuraCompletion]::RecoveryEvidence($fixture,'CURRENT')}
Write-Proofs
$proof=Get-Content (Join-Path $fixture 'sources.json') -Raw|ConvertFrom-Json
$proof.cases=@($proof.cases|Where-Object id -ne 'A+B-native-recovery')
[IO.File]::WriteAllText((Join-Path $fixture 'sources.json'),($proof|ConvertTo-Json -Depth 6))
Reject 'Missing native recovery case rejected' {[BoomboxQa.QaAuraCompletion]::RecoveryEvidence($fixture,'CURRENT')}
Write-Proofs
$proof=Get-Content (Join-Path $fixture 'cassettes.json') -Raw|ConvertFrom-Json
$proof.cases[1].passed=$false
[IO.File]::WriteAllText((Join-Path $fixture 'cassettes.json'),($proof|ConvertTo-Json -Depth 6))
Reject 'Failed native movement rejected' {[BoomboxQa.QaAuraCompletion]::RecoveryEvidence($fixture,'CURRENT')}
Write-Proofs
$cycle=Get-Content (Join-Path $fixture 'settings-cycle.json') -Raw|ConvertFrom-Json
$cycle.passed=$false
[IO.File]::WriteAllText((Join-Path $fixture 'settings-cycle.json'),($cycle|ConvertTo-Json))
Reject 'Failed cleanup cycle rejected' {[BoomboxQa.QaAuraCompletion]::RecoveryEvidence($fixture,'CURRENT')}
$cycle.passed=$true;$cycle.seconds=120.01
[IO.File]::WriteAllText((Join-Path $fixture 'settings-cycle.json'),($cycle|ConvertTo-Json))
Reject 'Over-budget cycle rejected' {[BoomboxQa.QaAuraCompletion]::RecoveryEvidence($fixture,'CURRENT')}
$result=[ordered]@{passed=$true;qaSha=(Get-FileHash -LiteralPath $QaDll -Algorithm SHA256).Hash;scope='Actual compiled route/evidence consumer; no game run and no native timing claim';checks=$checks;fixture=$fixture}
$json=$result|ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $fixture 'bounded-aura-result.json'),$json)
$json
