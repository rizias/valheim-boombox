param(
    [switch]$CheckOnly,
    [ValidateSet('Patch','Minor','Major')][string]$Increment = 'Patch',
    [string]$Version,
    [string]$SettingsPath = (Join-Path $PSScriptRoot 'release.local.json')
)
$ErrorActionPreference = 'Stop'
if ($MyInvocation.InvocationName -ne '.' -and $PSVersionTable.PSVersion.Major -lt 7) {
    $settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json
    if (-not $settings.PwshPath -or -not (Test-Path -LiteralPath $settings.PwshPath -PathType Leaf)) { throw 'Set PwshPath in release.local.json to your PowerShell 7 executable.' }
    & $settings.PwshPath -NoProfile -NonInteractive -File $PSCommandPath @PSBoundParameters
    exit $LASTEXITCODE
}

function Invoke-PublicationCommand([string]$Tool, [string[]]$Arguments, [switch]$AllowFailure) {
    $application = @(Get-Command -Name $Tool -CommandType Application -ErrorAction Stop)[0]
    $output = @(& $application.Source @Arguments 2>&1)
    $code = $LASTEXITCODE
    $lines = @($output | ForEach-Object { "$_" })
    if ($code -ne 0 -and -not $AllowFailure) {
        $command = [IO.Path]::GetFileName($Tool) + ' ' + (($Arguments | Select-Object -First 2) -join ' ')
        throw ('Publication command failed (exit ' + $code + '): ' + $command + "`n" + ($lines -join "`n"))
    }
    [pscustomobject]@{ Code=$code; Lines=$lines }
}

function Invoke-GitHubPublication {
    param([string]$Root = $PSScriptRoot, [string]$SettingsPath = (Join-Path $Root 'release.local.json'), [switch]$CheckOnly, [string]$Increment = 'Patch', [string]$Version)
    Set-StrictMode -Version Latest
    $Root = [IO.Path]::GetFullPath($Root)
    $metadataNames = @('manifest.json','src/BoomboxNetworkProtocol.cs','README.md','README.ru.md','PLAY.md','PLAY.ru.md','CHANGELOG.md','CHANGELOG.ru.md')
    function Git([string[]]$Arguments, [switch]$AllowFailure) { Invoke-PublicationCommand 'git' (@('-C',$Root) + $Arguments) -AllowFailure:$AllowFailure }
    function Gh([string[]]$Arguments) { Invoke-PublicationCommand 'gh' $Arguments }
    function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
    function Changed { @((Git @('-c','core.quotepath=false','diff','--name-only','HEAD','--')).Lines | Where-Object { $_ }) }
    function Read-Receipt([string]$Path) {
        $receipt = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable
        if ($receipt.version -notmatch '^\d+\.\d+\.\d+$' -or $receipt.archive -ne [IO.Path]::GetFileName($receipt.archive) -or -not $receipt.archive.EndsWith('.zip')) { throw 'Invalid local release receipt identity.' }
        if (@($receipt.metadata.Keys).Count -ne $metadataNames.Count) { throw 'Release receipt must contain exactly the expected version metadata files.' }
        foreach ($name in $metadataNames) {
            $entry = $receipt.metadata[$name]
            if (-not $entry -or $entry.candidate -ne ('metadata/' + $name) -or (Hash (Join-Path (Split-Path $Path) $entry.candidate)) -ne $entry.after) { throw ('Invalid release metadata candidate: ' + $name) }
        }
        $receipt
    }
    Git @('--version') | Out-Null
    Gh @('--version') | Out-Null
    if ([IO.Path]::GetFullPath(((Git @('rev-parse','--show-toplevel')).Lines -join '')) -ne $Root) { throw 'Run publication from the standalone mod Git repository.' }
    $head = Git @('rev-parse','--verify','HEAD') -AllowFailure
    if ($head.Code -ne 0) { throw 'One-time setup: commit the public mod source before publishing.' }
    $branch = (Git @('symbolic-ref','--quiet','--short','HEAD') -AllowFailure).Lines -join ''
    if (-not $branch) { throw 'Check out an attached Git branch before publishing.' }
    $origin = (Git @('remote','get-url','origin') -AllowFailure).Lines -join ''
    if ($origin -notmatch '^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$') { throw 'One-time setup: set origin to your GitHub repository, for example git remote add origin https://github.com/OWNER/REPO.git.' }
    $repository = $Matches[1]
    $pushOrigins = @((Git @('remote','get-url','--push','--all','origin')).Lines)
    if ($pushOrigins.Count -ne 1 -or $pushOrigins[0] -notmatch '^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$' -or $Matches[1] -cne $repository) { throw 'Origin must push to the same single GitHub repository that it fetches.' }
    $auth = Invoke-PublicationCommand 'gh' @('auth','status','--hostname','github.com') -AllowFailure
    if ($auth.Code -ne 0) { throw 'GitHub authentication could not be checked. Check network access and gh auth status before changing credentials. No login was started.' }
    Gh @('api',('repos/' + $repository),'--silent') | Out-Null
    if (@((Git @('diff','--cached','--name-only')).Lines | Where-Object { $_ }).Count) { throw 'The Git index must be empty before publication. Commit your source changes first.' }
    if (@((Git @('ls-files','--others','--exclude-standard')).Lines | Where-Object { $_ }).Count) { throw 'Commit the untracked public source files before publication.' }
    $dirty = @(Changed)
    if ($dirty.Count) {
        if (@($dirty | Where-Object { $_ -notin $metadataNames }).Count) { throw 'Commit your public source changes before publication. Build was not started.' }
        $retry = $false
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Root 'outputs/releases') -Filter release.json -Recurse -File -ErrorAction SilentlyContinue) {
            $candidate = Read-Receipt $file.FullName
            $matches = $true
            foreach ($name in $metadataNames) {
                $actual = Hash (Join-Path $Root $name)
                if ($actual -notin @($candidate.metadata[$name].before,$candidate.metadata[$name].after) -or ($name -in $dirty -and $actual -ne $candidate.metadata[$name].after)) { $matches = $false; break }
            }
            if ($matches) { $retry = $true; break }
        }
        if (-not $retry) { throw 'Dirty metadata is not a verified release retry. Commit your source changes first.' }
    }
    $settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json -AsHashtable
    if (-not $settings.PwshPath -or -not (Test-Path -LiteralPath $settings.PwshPath -PathType Leaf)) { throw 'Set PwshPath in release.local.json to your PowerShell 7 executable.' }
    if ($CheckOnly) { return ('Publication preflight passed for ' + $repository + ' on ' + $branch + '. No build or remote write performed.') }
    $outputRoot = Join-Path $Root 'outputs'
    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    try { $lock = [IO.File]::Open((Join-Path $outputRoot '.github-publication.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw 'Another GitHub publication is running. Retry when it finishes.' }
    try {
        $arguments = @('-NoProfile','-NonInteractive','-File',(Join-Path $Root 'release.ps1'),'-SettingsPath',$SettingsPath,'-Increment',$Increment)
        if ($Version) { $arguments += @('-Version',$Version) }
        Invoke-PublicationCommand $settings.PwshPath $arguments | Out-Null
        $versionNow = (Get-Content -Raw -LiteralPath (Join-Path $Root 'manifest.json') | ConvertFrom-Json).version_number
        $releaseDirectory = Join-Path $Root ('outputs/releases/' + $versionNow)
        $receipt = Read-Receipt (Join-Path $releaseDirectory 'release.json')
        if ($receipt.version -ne $versionNow) { throw 'Current source version differs from the release receipt.' }
        foreach ($name in $metadataNames) { if ((Hash (Join-Path $Root $name)) -ne $receipt.metadata[$name].after) { throw ('Release metadata mismatch: ' + $name) } }
        Invoke-PublicationCommand $settings.PwshPath @('-NoProfile','-NonInteractive','-File',(Join-Path $Root 'verify-release.ps1'),'-ReleaseDirectory',$releaseDirectory) | Out-Null
        $changelog = Get-Content -Raw -LiteralPath (Join-Path $Root 'CHANGELOG.md')
        $sections = [regex]::Matches($changelog, ('(?ms)^## \[' + [regex]::Escape($receipt.version) + '\][^\r\n]*\r?\n.*?(?=^## |\z)'))
        $notes = ($sections | ForEach-Object { $_.Value.Trim() }) -join "`n`n"
        if ($notes -notmatch '(?m)^\s*[-*]\s+\S') { throw ('CHANGELOG.md has no release notes for ' + $receipt.version + '. No tag or release was pushed.') }
        $notesPath = Join-Path $releaseDirectory 'release-notes.md'
        [IO.File]::WriteAllText($notesPath, $notes, [Text.UTF8Encoding]::new($false))
        $dirty = @(Changed)
        if (@($dirty | Where-Object { $_ -notin $metadataNames }).Count -or @((Git @('diff','--cached','--name-only')).Lines | Where-Object { $_ }).Count -or @((Git @('ls-files','--others','--exclude-standard')).Lines | Where-Object { $_ }).Count) { throw 'Public source changed during build. Nothing was committed or uploaded.' }
        if (((Git @('rev-parse','HEAD')).Lines -join '') -ne ($head.Lines -join '') -or ((Git @('symbolic-ref','--quiet','--short','HEAD')).Lines -join '') -cne $branch) { throw 'HEAD or branch changed during the build. Nothing was committed or uploaded.' }
        if ($dirty.Count) {
            Git (@('commit','-m',('Release ' + $receipt.version),'--only','--') + $dirty) | Out-Null
        }
        if (@(Changed).Count -or ((Git @('symbolic-ref','--quiet','--short','HEAD')).Lines -join '') -ne $branch) { throw 'Git state changed during publication.' }
        $commit = (Git @('rev-parse','HEAD')).Lines -join ''
        $archive = Join-Path $releaseDirectory $receipt.archive
        $plan = [ordered]@{ repository=$repository; branch=$branch; version=$receipt.version; tag=('v' + $receipt.version); postBuildCommitSha=$commit; archivePath=$archive; archiveSha256=(Hash $archive); checksumPath=($archive + '.sha256'); checksumSha256=(Hash ($archive + '.sha256')); prerelease=$true }
        $publicationPath = Join-Path $releaseDirectory 'github-publication.json'
        if (Test-Path -LiteralPath $publicationPath) {
            $saved = Get-Content -Raw -LiteralPath $publicationPath | ConvertFrom-Json -AsHashtable
            if ($saved.plan.Count -ne $plan.Count -or @($plan.Keys | Where-Object { $saved.plan[$_] -cne $plan[$_] }).Count) { throw 'Publication receipt identity differs. Refusing to change its repository, commit or assets.' }
        } else { [IO.File]::WriteAllText($publicationPath, (@{plan=$plan; url=$null} | ConvertTo-Json -Depth 5)) }
        $remote = (Git @('ls-remote','--tags','origin',('refs/tags/' + $plan.tag),('refs/tags/' + $plan.tag + '^{}'))).Lines
        $remoteCommits = @($remote | Where-Object { $_ } | ForEach-Object { ($_ -split '\s+')[0] })
        if ($remoteCommits.Count -and $remoteCommits[-1] -ne $commit) { throw 'Remote release tag points to another commit. No release was uploaded.' }
        $localTag = Git @('rev-parse','--verify',('refs/tags/' + $plan.tag + '^{commit}')) -AllowFailure
        if ($localTag.Code -eq 0 -and ($localTag.Lines -join '') -ne $commit) { throw 'Local release tag points to another commit.' }
        if (((Git @('remote','get-url','origin')).Lines -join '') -cne $origin -or ((Git @('remote','get-url','--push','--all','origin')).Lines -join '') -cne $pushOrigins[0]) { throw 'Origin changed during publication. No push was performed.' }
        if ($localTag.Code -ne 0) { Git @('tag',$plan.tag,$commit) | Out-Null }
        Git @('push','origin',($commit + ':refs/heads/' + $branch),('refs/tags/' + $plan.tag + ':refs/tags/' + $plan.tag)) | Out-Null
        $pushedTag = @((Git @('ls-remote','--tags','origin',('refs/tags/' + $plan.tag),('refs/tags/' + $plan.tag + '^{}'))).Lines | Where-Object { $_ })
        if (-not $pushedTag.Count -or (($pushedTag[-1] -split '\s+')[0]) -ne $commit) { throw 'Remote tag does not confirm the post-build commit. No release was uploaded.' }
        function Find-Release {
            $pages = ((Gh @('api',('repos/' + $repository + '/releases?per_page=100'),'--paginate','--slurp')).Lines -join "`n") | ConvertFrom-Json
            @($pages | ForEach-Object { $_ } | ForEach-Object { $_ } | Where-Object { $_.tag_name -ceq $plan.tag })
        }
        $releases = @(Find-Release)
        if ($releases.Count -gt 1) { throw 'Multiple GitHub releases use the requested tag.' }
        if (-not $releases.Count) {
            Gh @('release','create',$plan.tag,'--repo',$repository,'--draft','--prerelease','--verify-tag','--target',$plan.postBuildCommitSha,'--latest=false','--title',('Valheim Boombox ' + $plan.version),'--notes-file',$notesPath) | Out-Null
            $releases = @(Find-Release)
        }
        if ($releases.Count -ne 1) { throw 'Created draft release could not be confirmed.' }
        $release = $releases[0]
        if ($release.prerelease -ne $true) { throw 'Existing release is not an alpha prerelease.' }
        $assets = @($plan.archivePath,$plan.checksumPath)
        $names = @($assets | ForEach-Object { [IO.Path]::GetFileName($_) })
        if (@($release.assets | Where-Object { $_.name -cnotin $names }).Count) { throw 'Existing release has unexpected assets.' }
        foreach ($path in $assets) {
            $name = [IO.Path]::GetFileName($path)
            $matching = @($release.assets | Where-Object { $_.name -ceq $name })
            if ($matching.Count -gt 1) { throw ('Duplicate GitHub asset: ' + $name) }
            if (-not $matching.Count) {
                if (-not $release.draft) { throw ('Published release is missing asset: ' + $name) }
                Gh @('release','upload',$plan.tag,$path,'--repo',$repository) | Out-Null
                $release = @(Find-Release)[0]
                $matching = @($release.assets | Where-Object { $_.name -ceq $name })
            }
            if ($matching.Count -ne 1 -or $matching[0].state -ne 'uploaded') { throw ('GitHub asset upload was not confirmed: ' + $name) }
            if ($matching[0].size -ne (Get-Item -LiteralPath $path).Length) { throw ('GitHub asset size mismatch: ' + $name) }
            $expected = 'sha256:' + (Hash $path).ToLowerInvariant()
            $digest = $matching[0].PSObject.Properties['digest']
            if (-not $digest -or $digest.Value -cne $expected) { throw ('GitHub asset SHA256 is missing or mismatched: ' + $name + '. Draft was not published; assets were not overwritten.') }
        }
        if ($release.draft) { Gh @('release','edit',$plan.tag,'--repo',$repository,'--draft=false','--prerelease','--latest=false') | Out-Null }
        $release = @(Find-Release)[0]
        if ($release.draft -or -not $release.prerelease) { throw 'GitHub did not confirm a published prerelease.' }
        [IO.File]::WriteAllText($publicationPath, (@{plan=$plan; url=$release.html_url} | ConvertTo-Json -Depth 5))
        return $release.html_url
    }
    finally { $lock.Dispose() }
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-GitHubPublication -SettingsPath $SettingsPath -CheckOnly:$CheckOnly -Increment $Increment -Version $Version }
