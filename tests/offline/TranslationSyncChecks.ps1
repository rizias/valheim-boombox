param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../outputs/translation-sync-checks'))

$ErrorActionPreference = 'Stop'
$command = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../check-translations.ps1'))
$pwsh = (Get-Process -Id $PID).Path
$root = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root -Force
$checks = [Collections.Generic.List[string]]::new()

function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    $checks.Add($Name)
}

function Write-Map([string]$Path, $Values) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Values -Depth 5), [Text.UTF8Encoding]::new($false))
}

function Read-Map([string]$Fixture, [string]$Language) {
    return Get-Content -Raw -LiteralPath (Join-Path $Fixture ('Translations/' + $Language + '.json')) | ConvertFrom-Json -AsHashtable
}

function Fixture([string]$Name) {
    $path = Join-Path $root $Name
    $null = New-Item -ItemType Directory -Path (Join-Path $path 'Translations'), (Join-Path $path 'src') -Force
    Write-Map (Join-Path $path 'Translations/catalog.json') @{ fallback = 'English' }
    $values = [ordered]@{
        'meta.culture' = 'en-US'
        'ui.keep' = 'Keep {0}'
        'ui.same' = 'OK'
        'ui.literal' = " leading «quoted» \ path`ntrailing "
        'ui.blank' = ''
    }
    Write-Map (Join-Path $path 'Translations/English.json') $values
    foreach ($language in @('Russian', 'German')) {
        $translated = [ordered]@{}
        foreach ($key in $values.Keys) { $translated[$key] = $values[$key] }
        $translated['meta.culture'] = if ($language -eq 'Russian') { 'ru-RU' } else { 'de-DE' }
        $translated['ui.keep'] = if ($language -eq 'Russian') { 'Сохранить {0}' } else { 'Behalten {0}' }
        Write-Map (Join-Path $path ('Translations/' + $language + '.json')) $translated
    }
    [IO.File]::WriteAllText((Join-Path $path 'src/Caption.cs'), 'class Caption { string Value() => BoomboxText.Get("ui.keep"); }')
    return $path
}

function Hashes([string]$Fixture) {
    $values = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Fixture 'Translations') -Filter '*.json' -File) {
        $values[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash
    }
    return ConvertTo-Json -InputObject $values -Compress
}

function Run([string]$Fixture, [switch]$Sync) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $pwsh
    $start.WorkingDirectory = $Fixture
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $command, '-TranslationDirectory', 'Translations', '-SourceDirectory', 'src')) {
        $start.ArgumentList.Add($argument)
    }
    if ($Sync) { $start.ArgumentList.Add('-Sync') }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) {
            $process.Kill($true)
            throw 'Translation command exceeded the fixture timeout.'
        }
        return [pscustomobject]@{ Code = $process.ExitCode; Output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult() }
    }
    finally { $process.Dispose() }
}

$fixture = Fixture 'valid catalog with spaces'
$before = Hashes $fixture
Check ((Run $fixture).Code -eq 0) 'A complete catalog passes read-only validation'
Check ((Run $fixture -Sync).Code -eq 0 -and (Hashes $fixture) -eq $before) 'Synchronizing a complete catalog makes no file changes'
$english = Read-Map $fixture 'English'
$english['ui.new'] = 'New {0}'
Write-Map (Join-Path $fixture 'Translations/English.json') $english
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $fixture 'Translations/English.json')).Hash
$original = @{}
foreach ($language in @('Russian', 'German')) { $original[$language] = Read-Map $fixture $language }
$readOnlyBefore = Hashes $fixture
$result = Run $fixture
Check ($result.Code -ne 0 -and (Hashes $fixture) -eq $readOnlyBefore) 'Read-only validation fails on omissions without writing'
$result = Run $fixture -Sync
Check ($result.Code -ne 0) 'Synchronization adds pending keys and still fails until translated'
foreach ($language in @('Russian', 'German')) {
    $map = Read-Map $fixture $language
    Check ($map.Contains('ui.new') -and $null -eq $map['ui.new']) ($language + ' receives an explicit null pending key')
    Check ($result.Output.Contains($language + '.json') -and $result.Output.Contains('ui.new')) ($language + ' and its missing key are named in the diagnostic')
    foreach ($key in $original[$language].Keys) {
        Check ($map[$key] -ceq $original[$language][$key]) ($language + ' preserves existing value ' + $key)
    }
}
Check ([IO.File]::ReadAllText((Join-Path $fixture 'Translations/Russian.json')).Contains('Сохранить')) 'Synchronized translations keep readable Unicode text'
Check ((Get-FileHash -LiteralPath (Join-Path $fixture 'Translations/English.json')).Hash -eq $sourceHash) 'Synchronization never rewrites the authoritative dictionary'
$pendingBefore = Hashes $fixture
Check ((Run $fixture -Sync).Code -ne 0 -and (Hashes $fixture) -eq $pendingBefore) 'A second synchronization is byte-idempotent while pending entries remain'
foreach ($language in @('Russian', 'German')) {
    $map = Read-Map $fixture $language
    $map['ui.new'] = $language + ' {0}'
    Write-Map (Join-Path $fixture ('Translations/' + $language + '.json')) $map
}
Check ((Run $fixture -Sync).Code -eq 0) 'Filling pending values makes the same synchronization command pass'

$fixture = Fixture 'obsolete key'
$map = Read-Map $fixture 'Russian'
$map['ui.obsolete'] = 'Keep this old translation'
Write-Map (Join-Path $fixture 'Translations/Russian.json') $map
$before = Hashes $fixture
$result = Run $fixture -Sync
Check ($result.Code -ne 0 -and $result.Output.Contains('ui.obsolete') -and (Hashes $fixture) -eq $before) 'Obsolete keys are preserved and rejected explicitly'
$english = Read-Map $fixture 'English'
$english['ui.new'] = 'New value'
Write-Map (Join-Path $fixture 'Translations/English.json') $english
$result = Run $fixture -Sync
$map = Read-Map $fixture 'Russian'
Check (
    $result.Code -ne 0 -and
    $result.Output.Contains('ui.obsolete') -and
    $map['ui.obsolete'] -ceq 'Keep this old translation' -and
    $map.Contains('ui.new') -and
    $null -eq $map['ui.new']
) 'A real synchronization rewrite also preserves obsolete translations'

$fixture = Fixture 'invalid culture before synchronization'
$english = Read-Map $fixture 'English'
$english['ui.new'] = 'New value'
Write-Map (Join-Path $fixture 'Translations/English.json') $english
$map = Read-Map $fixture 'Russian'
$map['meta.culture'] = 'invalid_culture!'
Write-Map (Join-Path $fixture 'Translations/Russian.json') $map
$before = Hashes $fixture
$result = Run $fixture -Sync
Check (
    $result.Code -ne 0 -and
    $result.Output.Contains('Russian.json') -and
    $result.Output.Contains('meta.culture') -and
    (Hashes $fixture) -eq $before
) 'Invalid language culture fails before any synchronization writes'

$fixture = Fixture 'missing fallback selection'
Write-Map (Join-Path $fixture 'Translations/catalog.json') @{}
$before = Hashes $fixture
$result = Run $fixture -Sync
Check (
    $result.Code -ne 0 -and
    $result.Output.Contains('catalog.json / fallback') -and
    (Hashes $fixture) -eq $before
) 'A missing fallback selection is named and rejected without writes'

foreach ($invalid in @('malformed', 'duplicate', 'wrong-type', 'null-base')) {
    $fixture = Fixture $invalid
    $english = Read-Map $fixture 'English'
    $english['ui.new'] = 'New value'
    Write-Map (Join-Path $fixture 'Translations/English.json') $english
    switch ($invalid) {
        'malformed' { [IO.File]::WriteAllText((Join-Path $fixture 'Translations/German.json'), '{') }
        'duplicate' { [IO.File]::WriteAllText((Join-Path $fixture 'Translations/German.json'), '{"meta.culture":"de-DE","ui.keep":"a","ui.keep":"b"}') }
        'wrong-type' { Write-Map (Join-Path $fixture 'Translations/German.json') @{ 'meta.culture' = 'de-DE'; 'ui.keep' = 42 } }
        'null-base' { $english['ui.new'] = $null; Write-Map (Join-Path $fixture 'Translations/English.json') $english }
    }
    $before = Hashes $fixture
    $result = Run $fixture -Sync
    Check ($result.Code -ne 0 -and (Hashes $fixture) -eq $before) ($invalid + ' fails preflight before any language writes')
}

$fixture = Fixture 'placeholder mismatch'
$map = Read-Map $fixture 'Russian'
$map['ui.keep'] = 'Неверно {1}'
Write-Map (Join-Path $fixture 'Translations/Russian.json') $map
Check ((Run $fixture).Code -ne 0) 'An existing translation with mismatched placeholders fails'

$fixture = Fixture 'missing source key'
[IO.File]::WriteAllText((Join-Path $fixture 'src/Caption.cs'), 'class Caption { string Value() => BoomboxText.Get("ui.not_in_catalog"); }')
$result = Run $fixture -Sync
Check ($result.Code -ne 0 -and $result.Output.Contains('ui.not_in_catalog')) 'A source reference absent from the authoritative dictionary fails'

$report = [ordered]@{ passed = $true; checks = $checks; fixtureDirectory = $root }
$json = ConvertTo-Json -InputObject $report -Depth 5
[IO.File]::WriteAllText((Join-Path $root 'results.json'), $json)
Write-Output $json
