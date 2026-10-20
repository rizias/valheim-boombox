$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $project 'publish-github.ps1')
$fixtures = Join-Path $project ('outputs/github-publication-check/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixtures) | Out-Null
$metadata = @('manifest.json','src/BoomboxNetworkProtocol.cs','README.md','README.ru.md','PLAY.md','PLAY.ru.md','CHANGELOG.md','CHANGELOG.ru.md')
function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Git { throw 'A PowerShell function shadowed git.exe' }
function Gh { throw 'A PowerShell function shadowed gh.exe' }
$nativeGit = Invoke-PublicationCommand 'git' @('--version')
Assert ($nativeGit.Code -eq 0 -and ($nativeGit.Lines -join ' ') -match 'git version') 'Native Git executable was not resolved through a shadowing function'
$nativeGh = Invoke-PublicationCommand 'gh' @('--version')
Assert ($nativeGh.Code -eq 0 -and ($nativeGh.Lines -join ' ') -match 'gh version') 'Native GitHub CLI executable was not resolved through a shadowing function'
function New-Fixture([string]$Name) {
    $script:root = Join-Path $fixtures $Name
    [IO.Directory]::CreateDirectory((Join-Path $root 'src')) | Out-Null
    foreach ($name in $metadata) { [IO.File]::WriteAllText((Join-Path $root $name), 'source 1.0.0') }
    [IO.File]::WriteAllText((Join-Path $root 'manifest.json'), '{"version_number":"1.0.0"}')
    $script:settingsFile = Join-Path $root 'release.local.json'
    [IO.File]::WriteAllText($settingsFile, (@{PwshPath=(Get-Process -Id $PID).Path} | ConvertTo-Json))
    $script:state = @{head=('a' * 40); branch='main'; origin='https://github.com/example/boombox.git'; pushOrigin=''; auth=$true; dirty=@(); staged=@(); untracked=@(); build=0; verify=0; pushes=0; creates=0; uploads=0; publishes=0; commits=0; remoteTag=''; localTag=''; release=$null; failVerify=$false; failUpload=$false; failCommit=$false;hideNewRelease=$false;infrastructureDiff=@()}
}
function Fake-Build {
    $state.build++
    $directory = Join-Path $root 'outputs/releases/1.0.1'
    if (Test-Path -LiteralPath $directory) { return }
    [IO.Directory]::CreateDirectory((Join-Path $directory 'metadata/src')) | Out-Null
    $entries = [ordered]@{}
    foreach ($name in $metadata) {
        $source = Join-Path $root $name
        $before = (Get-FileHash -LiteralPath $source).Hash
        $text = if ($name -eq 'manifest.json') { '{"version_number":"1.0.1"}' } elseif ($name -eq 'CHANGELOG.md') { "# Changelog`n`n## [1.0.1]`n`n- Publish the selected version automatically.`n" } else { 'released 1.0.1 ' + $name }
        [IO.File]::WriteAllText($source, $text)
        [IO.File]::WriteAllText((Join-Path $directory ('metadata/' + $name)), $text)
        $entries[$name] = @{before=$before; after=(Get-FileHash -LiteralPath $source).Hash; candidate=('metadata/' + $name)}
    }
    $archive = Join-Path $directory 'Boombox-1.0.1.zip'
    [IO.File]::WriteAllBytes($archive, [byte[]]@(80,75,3,4,42))
    $hash = (Get-FileHash -LiteralPath $archive).Hash
    [IO.File]::WriteAllText(($archive + '.sha256'), ($hash + '  Boombox-1.0.1.zip' + "`n"))
    $sourceArchive = Join-Path $directory 'Boombox-1.0.1-FFmpeg-Sources.zip'
    [IO.File]::WriteAllBytes($sourceArchive, [byte[]]@(80,75,3,4,43))
    $sourceHash = (Get-FileHash -LiteralPath $sourceArchive).Hash
    [IO.File]::WriteAllText(($sourceArchive + '.sha256'), ($sourceHash + '  Boombox-1.0.1-FFmpeg-Sources.zip' + "`n"))
    [IO.File]::WriteAllText((Join-Path $directory 'release.json'), (@{formatVersion=2;version='1.0.1'; archive='Boombox-1.0.1.zip'; sha256=$hash;ffmpegSourceArchive='Boombox-1.0.1-FFmpeg-Sources.zip';ffmpegSourceSha256=$sourceHash; metadata=$entries} | ConvertTo-Json -Depth 5))
    $state.dirty = $metadata
}
function Asset([string]$Path) {
    [pscustomobject]@{id=($state.uploads + 1); name=[IO.Path]::GetFileName($Path); state='uploaded'; size=(Get-Item -LiteralPath $Path).Length; digest=('sha256:' + (Get-FileHash -LiteralPath $Path).Hash.ToLowerInvariant())}
}
function Invoke-PublicationCommand([string]$Tool, [string[]]$Arguments, [switch]$AllowFailure) {
    $code = 0
    $lines = @()
    if ($Tool -eq 'git') {
        $a = @($Arguments | Select-Object -Skip 2)
        $key = $a -join ' '
        switch -Wildcard ($key) {
            '--version' { $lines = @('git fixture') }
            'rev-parse --show-toplevel' { $lines = @($root) }
            'rev-parse --verify HEAD' { if ($state.head) { $lines = @($state.head) } else { $code = 1 } }
            'rev-parse HEAD' { $lines = @($state.head) }
            'symbolic-ref*' { $lines = @($state.branch) }
            'remote get-url*' { $lines = @(if ($a -contains '--push' -and $state.pushOrigin) { $state.pushOrigin } else { $state.origin }) }
            'diff --cached --name-only' { $lines = $state.staged }
            'diff --name-only *' { $lines = $state.infrastructureDiff }
            'ls-files*' { $lines = $state.untracked }
            '-c core.quotepath=false diff*' { $lines = $state.dirty }
            'commit *' { Assert ($a[3] -eq '--only' -and $a[4] -eq '--') 'Metadata commit must not stage source files'; Assert (@($a | Select-Object -Skip 5 | Where-Object { $_ -notin $metadata }).Count -eq 0) 'Committed a file outside release metadata'; if ($state.failCommit) { throw 'Fixture metadata commit failed' }; $state.commits++; $state.head = 'b' * 40; $state.dirty = @(); $state.staged = @() }
            'ls-remote*' { if ($state.remoteTag) { $lines = @($state.remoteTag + "`trefs/tags/v1.0.1") } }
            'rev-parse --verify refs/tags/*' { if ($state.localTag) { $lines = @($state.localTag) } else { $code = 1 } }
            'tag *' { Assert ($a[2] -eq $state.head) 'Tag does not target post-build HEAD'; $state.localTag = $a[2] }
            'push *' { Assert ($a[2] -eq ($state.head + ':refs/heads/main')) 'Push does not target post-build HEAD'; $state.pushes++; $state.remoteTag = $state.localTag }
            default { throw ('Unexpected fixture git call: ' + $key) }
        }
    } elseif ($Tool -eq 'gh') {
        $a = $Arguments
        $key = $a -join ' '
        switch -Wildcard ($key) {
            '--version' { $lines = @('gh fixture') }
            'auth status*' { if (-not $state.auth) { $code = 1 } }
            'api repos/example/boombox --silent' { }
            'api repos/example/boombox/releases?per_page=100*' { Assert ($a -contains '--paginate' -and $a -contains '--slurp') 'Release listing must include all pages'; $items = @($state.release | Where-Object { $null -ne $_ -and -not ($state.hideNewRelease -and $state.creates) }); $pages = [Collections.Generic.List[object]]::new(); $pages.Add(@()); $pages.Add($items); $lines = @((ConvertTo-Json -InputObject $pages -Depth 8 -Compress)) }
            'api --method POST repos/example/boombox/releases --input *' {
                $request = Get-Content -Raw -LiteralPath $a[-1] | ConvertFrom-Json
                Assert ($request.draft -and -not $request.prerelease) 'Release was not created as a regular draft'
                Assert ($state.remoteTag -eq $request.target_commitish) 'Release does not target the verified remote tag'
                Assert ($request.body -match 'Publish the selected version automatically') 'Release notes do not come from CHANGELOG'
                $state.creates++
                $state.release = [pscustomobject]@{
                    id = 1
                    tag_name = 'v1.0.1'
                    draft = $true
                    prerelease = $false
                    assets = @()
                    html_url = 'https://github.com/example/boombox/releases/tag/v1.0.1'
                }
                $lines = @($state.release | ConvertTo-Json -Depth 8 -Compress)
            }
            'api repos/example/boombox/releases/1' { $lines=@($state.release|ConvertTo-Json -Depth 8 -Compress) }
            'release upload *' { if ($state.failUpload) { throw 'Fixture upload failed' }; $state.uploads++; $state.release.assets += Asset $a[3] }
            'release edit *' {
                Assert ($state.release.assets.Count -eq 4) 'Published before all four assets existed'
                Assert ($a -contains '--draft=false' -and $a -contains '--prerelease=false' -and $a -contains '--latest=true') 'Release must be regular and Latest'
                $state.publishes++
                $state.release.draft = $false
            }
            default { throw ('Unexpected fixture gh call: ' + $key) }
        }
    } elseif ($Arguments -contains (Join-Path $root 'release.ps1')) { Fake-Build }
    elseif ($Arguments -contains (Join-Path $root 'verify-release.ps1')) { $state.verify++; if ($state.failVerify) { throw 'Fixture verifier failed' } }
    else { throw ('Unexpected fixture tool: ' + $Tool) }
    if ($code -ne 0 -and -not $AllowFailure) { throw 'Fixture command failed' }
    [pscustomobject]@{Code=$code; Lines=@($lines)}
}
function Run { Invoke-GitHubPublication -Root $root -SettingsPath $settingsFile }
function Reject([string]$Contains) {
    $errorText = ''
    try { Run | Out-Null } catch { $errorText = $_.Exception.Message }
    Assert ($errorText.Contains($Contains)) ('Expected refusal containing ' + $Contains + ', got ' + $errorText)
    Assert ($state.publishes -eq 0) 'Published after a failure'
}

New-Fixture 'create-repeat'
$url = Run
Assert ($url -eq 'https://github.com/example/boombox/releases/tag/v1.0.1') 'Published URL missing'
Assert ($state.build -eq 1 -and $state.verify -eq 1 -and $state.commits -eq 1 -and $state.creates -eq 1 -and $state.uploads -eq 4 -and $state.publishes -eq 1) 'Create flow incomplete'
$again = Run
Assert ($again -eq $url -and $state.build -eq 2 -and $state.commits -eq 1 -and $state.uploads -eq 4 -and $state.publishes -eq 1) 'Repeat did not reconcile published release'
$saved = Get-Content -Raw -LiteralPath (Join-Path $root 'outputs/releases/1.0.1/github-publication.json') | ConvertFrom-Json
Assert ($saved.plan.postBuildCommitSha -eq $state.head -and $saved.url -eq $url) 'Publication receipt is not bound to actual HEAD and URL'
Assert ($saved.plan.ffmpegSourceArchiveSha256 -eq (Get-FileHash -LiteralPath $saved.plan.ffmpegSourceArchivePath).Hash -and $saved.plan.ffmpegSourceChecksumSha256 -eq (Get-FileHash -LiteralPath $saved.plan.ffmpegSourceChecksumPath).Hash) 'Publication receipt is not bound to both source assets'

New-Fixture 'partial-draft'
$state.failUpload = $true
Reject 'upload failed'
Assert ($state.release.draft -and $state.uploads -eq 0) 'Failed upload changed draft state'
$state.failUpload = $false
$state.uploads = 1
$state.release.assets = @(Asset (Join-Path $root 'outputs/releases/1.0.1/Boombox-1.0.1.zip'))
Run | Out-Null
Assert ($state.creates -eq 1 -and $state.uploads -eq 4 -and $state.publishes -eq 1) 'Partial draft was not resumed with only missing assets'

New-Fixture 'source-checksum-partial-draft'
$state.failUpload = $true
Reject 'upload failed'
$state.failUpload = $false
$directory = Join-Path $root 'outputs/releases/1.0.1'
$state.release.assets = @('Boombox-1.0.1.zip','Boombox-1.0.1.zip.sha256','Boombox-1.0.1-FFmpeg-Sources.zip' | ForEach-Object { Asset (Join-Path $directory $_) })
$state.uploads = 3
Run | Out-Null
Assert ($state.creates -eq 1 -and $state.uploads -eq 4 -and $state.publishes -eq 1) 'Draft did not resume only its missing source checksum'

New-Fixture 'source-checksum-mismatch'
$state.failUpload = $true
Reject 'upload failed'
$state.failUpload = $false
$directory = Join-Path $root 'outputs/releases/1.0.1'
$state.release.assets = @('Boombox-1.0.1.zip','Boombox-1.0.1.zip.sha256','Boombox-1.0.1-FFmpeg-Sources.zip','Boombox-1.0.1-FFmpeg-Sources.zip.sha256' | ForEach-Object { Asset (Join-Path $directory $_) })
$state.release.assets[3].digest = 'sha256:wrong'
Reject 'SHA256'
Assert ($state.uploads -eq 0) 'Mismatched fourth asset was overwritten'

New-Fixture 'asset-mismatch'
$state.failUpload = $true
Reject 'upload failed'
$state.failUpload = $false
$bad = Asset (Join-Path $root 'outputs/releases/1.0.1/Boombox-1.0.1.zip')
$bad.digest = 'sha256:wrong'
$state.release.assets = @($bad)
Reject 'SHA256'
Assert ($state.uploads -eq 0) 'Mismatched existing asset was overwritten'

New-Fixture 'tag-mismatch'
$state.remoteTag = 'c' * 40
Reject 'Remote release tag'
Assert ($state.pushes -eq 0 -and $state.creates -eq 0) 'Conflicting tag caused a remote write'

New-Fixture 'verify-failure'
$state.failVerify = $true
Reject 'verifier failed'
Assert ($state.commits -eq 0 -and $state.pushes -eq 0 -and $state.creates -eq 0) 'Verifier failure did not stop publication'
$state.failVerify = $false
Run | Out-Null
Assert ($state.build -eq 2 -and $state.publishes -eq 1) 'Receipt-verified dirty metadata retry failed'

New-Fixture 'commit-failure'
$state.failCommit = $true
Reject 'metadata commit failed'
Assert ($state.staged.Count -eq 0 -and $state.commits -eq 0 -and $state.pushes -eq 0) 'Failed metadata commit changed the index or pushed'
$state.failCommit = $false
Run | Out-Null
Assert ($state.commits -eq 1 -and $state.publishes -eq 1) 'Metadata commit failure could not resume the verified release'

foreach ($case in @('dirty-source','dirty-metadata','staged','untracked','auth','unborn','detached','origin','push-origin')) {
    New-Fixture $case
    switch ($case) {
        dirty-source { $state.dirty = @('src/Feature.cs') }
        dirty-metadata { $state.dirty = @('README.md') }
        staged { $state.staged = @('README.md') }
        untracked { $state.untracked = @('new-source.cs') }
        auth { $state.auth = $false }
        unborn { $state.head = '' }
        detached { $state.branch = '' }
        origin { $state.origin = 'https://other.example/repo.git' }
        push-origin { $state.pushOrigin = 'https://github.com/example/other-repo.git' }
    }
    $refused = $false
    try { Run | Out-Null } catch { $refused = $true }
    Assert ($refused -and $state.build -eq 0 -and $state.commits -eq 0 -and $state.pushes -eq 0) ('Preflight did not refuse before build: ' + $case)
}
New-Fixture 'draft-list-not-updated'
$state.hideNewRelease=$true
$url=Run
Assert ($state.publishes -eq 1 -and $state.creates -eq 1 -and $state.uploads -eq 4) 'Draft creation depends on a refreshed release listing'

New-Fixture 'legacy-receipt-retry'
$state.dirty = @('README.md')
$legacyDirectory = Join-Path $root 'outputs/releases/0.9.0'
[IO.Directory]::CreateDirectory($legacyDirectory) | Out-Null
[IO.File]::WriteAllText((Join-Path $legacyDirectory 'release.json'), '{"version":"0.9.0","archive":"old.zip","metadata":{}}')
Reject 'Dirty metadata is not a verified release retry'
Assert ($state.build -eq 0 -and $state.pushes -eq 0) 'Legacy receipt caused a build or push'

New-Fixture 'resume-existing-draft'
$state.failUpload=$true
Reject 'Fixture upload failed'
$state.failUpload=$false
$state.head='c'*40
$state.infrastructureDiff=@('publish-github.ps1')
$oldPushes=$state.pushes
$oldCommits=$state.commits
Invoke-GitHubPublication -Root $root -SettingsPath $settingsFile -Version '1.0.1' -Resume | Out-Null
Assert ($state.publishes -eq 1 -and $state.creates -eq 1 -and $state.pushes -eq $oldPushes -and $state.commits -eq $oldCommits -and $state.remoteTag -eq ('b'*40)) 'Resuming changed the version/tag or created another release'

New-Fixture 'resume-refuses-product-changes'
$state.failUpload=$true
Reject 'Fixture upload failed'
$state.failUpload=$false
$state.head='c'*40
$state.infrastructureDiff=@('src/AudioDeck.cs')
$refused=$false
try { Invoke-GitHubPublication -Root $root -SettingsPath $settingsFile -Version '1.0.1' -Resume | Out-Null } catch { $refused=$_.Exception.Message -like '*product files differ*' }
Assert ($refused -and $state.publishes -eq 0 -and $state.remoteTag -eq ('b'*40)) 'Product changes were allowed under the old tag'

New-Fixture 'check-only'
$before = @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object { $_.FullName + '=' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n"
Invoke-GitHubPublication -Root $root -SettingsPath $settingsFile -CheckOnly | Out-Null
$after = @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object { $_.FullName + '=' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n"
Assert ($before -ceq $after -and $state.build -eq 0 -and $state.commits -eq 0 -and $state.pushes -eq 0) 'CheckOnly modified files or built/pushed'
Write-Output 'GitHub publication behavior checks passed. Create, repeat, draft recovery, conflicts, failure gates and preflight were exercised through the real publisher.'
Write-Output ('FIXTURES_RETAINED ' + $fixtures)
