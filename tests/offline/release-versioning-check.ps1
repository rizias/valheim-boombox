$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$fixtures = Join-Path $project ('outputs/release-versioning-check/' + [guid]::NewGuid().ToString('N'))
$checks = [Collections.Generic.List[string]]::new()
function Assert-Versioning([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $checks.Add($Message)
}
function Write-Fixture([string]$Root, [string]$Name, [string]$Text) {
    $path = Join-Path $Root $Name
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $Text)
}
$local = Join-Path $fixtures 'local'
foreach ($directory in @('src','Translations','tests','tools','third_party','managed','core','ffmpeg')) {
    [IO.Directory]::CreateDirectory((Join-Path $local $directory)) | Out-Null
}
Copy-Item -LiteralPath (Join-Path $project 'release.ps1') -Destination (Join-Path $local 'release.ps1')
Write-Fixture $local 'manifest.json' '{"name":"ValheimBoombox","version_number":"0.2.10"}'
Write-Fixture $local 'src/BoomboxNetworkProtocol.cs' 'public const string Version = "0.2.10"; public const int Protocol = 5;'
foreach ($name in @('README.md','README.ru.md','PLAY.md','PLAY.ru.md')) { Write-Fixture $local $name 'Version 0.2.10' }
Write-Fixture $local 'CHANGELOG.md' "# Changelog`n`n## [Unreleased]`n`n- Keep local builds independent of published versions.`n"
Write-Fixture $local 'CHANGELOG.ru.md' "# Список изменений`n`n## [Не выпущено]`n`n- Локальные сборки не повышают версию выпуска.`n"
foreach ($asset in @(
    'valheimboombox_bassfx','valheimboombox_v3_2','valheimboombox_musicfx','contact-shapes.json',
    'pendant-fit.json','pendant-particles.json','ramsund-dragon-head.png','cassette-icon.png',
    'bonus-icons/wave.png','bonus-icons/health.png','bonus-icons/stamina.png','bonus-icons/eitr.png'
)) { Write-Fixture $local ('assets/' + $asset) 'fixture' }
$pwsh = (Get-Process -Id $PID).Path
Write-Fixture $local 'release.local.json' (@{
    GameManaged=(Join-Path $local 'managed'); BepInExCore=(Join-Path $local 'core')
    FFmpegRoot=(Join-Path $local 'ffmpeg'); PwshPath=$pwsh
} | ConvertTo-Json)
Write-Fixture $local 'build.ps1' @'
param($GameManaged,$BepInExCore,$OutputDirectory,[switch]$DllOnly,$ProtocolSource)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
[IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'ValheimBoombox.dll'), [byte[]]@(1,2,3))
'@
Write-Fixture $local 'test.ps1' @'
param($GameManaged,$BepInExCore,$ProductDll,$OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'results.json'),
    (@{passed=$true;productSha256=(Get-FileHash -LiteralPath $ProductDll).Hash} | ConvertTo-Json))
'@
Write-Fixture $local 'package.ps1' @'
param($Action,$Destination,$FFmpegRoot,$SourceDll,$IconPath,$MetadataDirectory,$TestReport)
if ($Action -eq 'Archive') {
    [IO.File]::WriteAllText(($Destination + '.zip'), 'installer fixture')
    [IO.File]::WriteAllText(($Destination + '-FFmpeg-Sources.zip'), 'source fixture')
}
'@
$metadata = @('manifest.json','src/BoomboxNetworkProtocol.cs','README.md','README.ru.md','PLAY.md','PLAY.ru.md','CHANGELOG.md','CHANGELOG.ru.md')
function Metadata-Hashes {
    @($metadata | ForEach-Object { $_ + '=' + (Get-FileHash -LiteralPath (Join-Path $local $_)).Hash }) -join "`n"
}
function Local-Build([string[]]$Extra = @()) {
    $output = @(& $pwsh -NoProfile -NonInteractive -File (Join-Path $local 'release.ps1') @Extra 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ($output -join "`n") }
}
$before = Metadata-Hashes
Local-Build
$receipts = @(Get-ChildItem -LiteralPath (Join-Path $local 'outputs/local') -Recurse -Filter release.json)
Assert-Versioning ($receipts.Count -eq 1 -and (Metadata-Hashes) -ceq $before) 'Local build preserves all eight source metadata files'
Assert-Versioning ((Get-Content -Raw -LiteralPath $receipts[0].FullName | ConvertFrom-Json).version -eq '0.2.10') 'Local package keeps the source version'
$archiveHash = (Get-Content -Raw -LiteralPath $receipts[0].FullName | ConvertFrom-Json).sha256
Local-Build
Assert-Versioning (@(Get-ChildItem -LiteralPath (Join-Path $local 'outputs/local') -Recurse -Filter release.json).Count -eq 1) 'Unchanged local build reuses its existing archive'
Write-Fixture $local 'src/Feature.cs' 'changed source fixture'
Local-Build
$afterReceipts = @(Get-ChildItem -LiteralPath (Join-Path $local 'outputs/local') -Recurse -Filter release.json)
Assert-Versioning ($afterReceipts.Count -eq 2 -and (Metadata-Hashes) -ceq $before) 'Changed local build produces another artifact without a version increment'
Assert-Versioning ((Get-Content -Raw -LiteralPath $receipts[0].FullName | ConvertFrom-Json).sha256 -eq $archiveHash) 'Earlier local build is retained'
Assert-Versioning (-not (Test-Path -LiteralPath (Join-Path $local 'outputs/releases'))) 'Local builds do not create publication artifacts'

$environmentNames = @('GITHUB_ACTIONS','RELEASE_BRANCH','RELEASE_REF','RELEASE_SOURCE_SHA','RELEASE_BUMP','GITHUB_REPOSITORY','GITHUB_OUTPUT','VALHEIM_QA_ROOT')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$previousNativeExitCode = $global:LASTEXITCODE
try {
    $env:GITHUB_ACTIONS = 'true'
    Local-Build @('-ForPublication','-Version','0.2.8')
    Assert-Versioning ((Get-Content -Raw -LiteralPath (Join-Path $local 'manifest.json') | ConvertFrom-Json).version_number -eq '0.2.8') 'Hosted build accepts a published-release version below an old local number'
    Assert-Versioning (Test-Path -LiteralPath (Join-Path $local 'outputs/releases/0.2.8/release.json')) 'Only publication mode updates source version metadata'

    $hostRoot = Join-Path $fixtures 'hosted'
    [IO.Directory]::CreateDirectory((Join-Path $hostRoot 'tools/release')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $project 'tools/release/prepare-hosted.ps1') -Destination (Join-Path $hostRoot 'tools/release/prepare-hosted.ps1')
    Write-Fixture $hostRoot 'third_party/ffmpeg/manifest.json' '{}'
    Copy-Item -LiteralPath (Join-Path $project 'third_party/ffmpeg/manifest.json') -Destination (Join-Path $hostRoot 'third_party/ffmpeg/manifest.json')
    Write-Fixture $hostRoot 'manifest.json' '{"version_number":"9.9.99"}'
    Write-Fixture $hostRoot 'CHANGELOG.md' "# Changelog`n`n## [Unreleased]`n`n- Actual release note.`n"
    $env:RELEASE_BRANCH = 'main'
    $env:RELEASE_REF = 'refs/heads/main'
    $env:RELEASE_SOURCE_SHA = 'a' * 40
    $env:GITHUB_REPOSITORY = 'fixture/boombox'
    $env:VALHEIM_QA_ROOT = Join-Path $hostRoot 'qa'
    function git {
        $global:LASTEXITCODE = 0
        $arguments = @($args | Select-Object -Skip 2)
        switch ($arguments[0]) {
            'rev-parse' { $env:RELEASE_SOURCE_SHA }
            'ls-remote' {
                if ($arguments -contains '--tags') {
                    if ($hostState.tag) { $env:RELEASE_SOURCE_SHA + "`t" + $arguments[-1] }
                } else { $env:RELEASE_SOURCE_SHA + "`trefs/heads/main" }
            }
            'switch' { }
            'config' { }
            default { throw ('Unexpected git operation: ' + ($arguments -join ' ')) }
        }
    }
    function gh {
        if (($args -join ' ') -ne 'api repos/fixture/boombox/releases?per_page=100 --paginate --slurp') {
            throw 'Network mutation or unexpected GitHub call in version planning'
        }
        $global:LASTEXITCODE = if ($hostState.failApi) { 1 } else { 0 }
        if (-not $hostState.failApi) {
            $pages = [Collections.Generic.List[object]]::new()
            $pages.Add(@($hostState.releases))
            ConvertTo-Json -InputObject $pages -Depth 5 -Compress
        }
    }
    function Release([string]$Tag, [bool]$Draft = $false, [bool]$Prerelease = $false) {
        [pscustomobject]@{tag_name=$Tag;draft=$Draft;prerelease=$Prerelease}
    }
    function Plan([string]$Name, [string]$Bump, [object[]]$Releases, [bool]$Tag = $false, [bool]$FailApi = $false) {
        $script:hostState = @{releases=$Releases;tag=$Tag;failApi=$FailApi}
        $env:RELEASE_BUMP = $Bump
        $env:GITHUB_OUTPUT = Join-Path $hostRoot ($Name + '.out')
        & (Join-Path $hostRoot 'tools/release/prepare-hosted.ps1') | Out-Null
        Get-Content -LiteralPath $env:GITHUB_OUTPUT
    }
    $published = @(Release 'v0.2.6'; Release 'v0.2.7'; Release 'v8.0.0' $true; Release 'v7.0.0' $false $true)
    Assert-Versioning (@(Plan 'patch' 'patch' $published) -contains 'version=0.2.8') 'Patch follows published 0.2.7 despite source 9.9.99 and newer drafts or prereleases'
    Assert-Versioning (@(Plan 'minor' 'minor' $published) -contains 'version=0.3.0') 'Minor follows the published release'
    Assert-Versioning (@(Plan 'major' 'major' $published) -contains 'version=1.0.0') 'Major follows the published release'
    Assert-Versioning (@(Plan 'first' 'patch' @()) -contains 'version=0.0.1') 'First patch starts at 0.0.1 without published releases'
    $draft = @(Plan 'resume' 'patch' (@(Release 'v0.2.7'; Release 'v0.2.8' $true)) $true)
    Assert-Versioning ($draft -contains 'version=0.2.8' -and $draft -contains 'resume=true') 'Retry resumes the pending next version without another increment'
    $refused = $false
    try { Plan 'tag-conflict' 'patch' $published $true | Out-Null } catch { $refused = $_.Exception.Message -like '*already exists*' }
    Assert-Versioning $refused 'An existing tag without a draft is never overwritten or skipped'
    $refused = $false
    try { Plan 'offline' 'patch' $published $false $true | Out-Null } catch { $refused = $_.Exception.Message -like '*Cannot read existing releases*' }
    Assert-Versioning $refused 'GitHub API failure cannot fall back to a local version'
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    $global:LASTEXITCODE = $previousNativeExitCode
}
Write-Output ('Release versioning checks passed: ' + $checks.Count + '. No game, push, tag or publication was performed.')
Write-Output ('FIXTURES_RETAINED ' + $fixtures)
