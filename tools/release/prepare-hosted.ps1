$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This entry point requires a GitHub-hosted release job.' }
if ($env:RELEASE_BRANCH -ne 'main' -or $env:RELEASE_REF -cne ('refs/heads/' + $env:RELEASE_BRANCH)) { throw 'Dispatch Release on the default main branch.' }
if ($env:RELEASE_SOURCE_SHA -notmatch '^[0-9a-f]{40}$') { throw 'Missing immutable workflow source SHA.' }
function Invoke-HostedGit([string[]]$Arguments) {
    $lines = @(& git -C $root @Arguments)
    if ($LASTEXITCODE -ne 0) { throw ('Git release preflight failed: ' + ($Arguments -join ' ')) }
    $lines
}
if ((Invoke-HostedGit @('rev-parse','HEAD')) -cne $env:RELEASE_SOURCE_SHA) { throw 'Checkout differs from the dispatched source SHA.' }
$remoteHead = @(Invoke-HostedGit @('ls-remote','origin',('refs/heads/' + $env:RELEASE_BRANCH)))
if ($remoteHead.Count -ne 1 -or ($remoteHead[0] -split '\s+')[0] -cne $env:RELEASE_SOURCE_SHA) { throw 'Default branch advanced since dispatch. Start a new release run from its current commit.' }
$manifest = Get-Content -Raw -LiteralPath (Join-Path $root 'manifest.json') | ConvertFrom-Json
if ($manifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Manifest version must be major.minor.patch.' }
$current = [version]$manifest.version_number
$releaseJson = @(& gh api ('repos/' + $env:GITHUB_REPOSITORY + '/releases?per_page=100') --paginate --slurp)
if ($LASTEXITCODE -ne 0) { throw 'Cannot read existing releases before selecting a version.' }
$pages = ($releaseJson -join "`n") | ConvertFrom-Json
$existing = @($pages | ForEach-Object { $_ } | ForEach-Object { $_ } | Where-Object { $_.tag_name -ceq ('v' + $manifest.version_number) })
if ($existing.Count -gt 1) { throw 'Multiple releases use the current version tag.' }
$resume = $existing.Count -eq 1 -and $existing[0].draft -and $existing[0].prerelease
$next = if ($resume) { $manifest.version_number } else { switch ($env:RELEASE_BUMP) {
    'patch' { '{0}.{1}.{2}' -f $current.Major,$current.Minor,($current.Build + 1) }
    'minor' { '{0}.{1}.0' -f $current.Major,($current.Minor + 1) }
    'major' { '{0}.0.0' -f ($current.Major + 1) }
    default { throw 'Release bump must be patch, minor or major.' }
} }
$changelog = Get-Content -Raw -LiteralPath (Join-Path $root 'CHANGELOG.md')
$unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\]\r?\n(?<notes>.*?)(?=^## |\z)')
if (-not $resume -and (-not $unreleased.Success -or $unreleased.Groups['notes'].Value -notmatch '(?m)^-\s+\S')) { throw 'CHANGELOG.md must contain actual bullet notes under [Unreleased] before release.' }
$tags = @(Invoke-HostedGit @('ls-remote','--tags','origin',('refs/tags/v' + $next),('refs/tags/v' + $next + '^{}')))
if ($resume -and -not $tags.Count) { throw 'Cannot resume the draft because its remote tag is missing.' }
if (-not $resume -and $tags.Count) { throw ('Tag v' + $next + ' already exists. This run will not overwrite it or choose another version.') }
Invoke-HostedGit @('switch','-C',$env:RELEASE_BRANCH,$env:RELEASE_SOURCE_SHA) | Out-Null
Invoke-HostedGit @('config','user.name','rizias') | Out-Null
Invoke-HostedGit @('config','user.email','42223893+rizias@users.noreply.github.com') | Out-Null
[IO.Directory]::CreateDirectory($env:VALHEIM_QA_ROOT) | Out-Null
$ffmpeg = Get-Content -Raw -LiteralPath (Join-Path $root 'third_party/ffmpeg/manifest.json') | ConvertFrom-Json
if ($ffmpeg.source -notmatch '^https://ffmpeg\.org/releases/ffmpeg-\d+\.\d+\.\d+\.tar\.xz$' -or $ffmpeg.sourceSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $ffmpeg.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid pinned FFmpeg source identity.' }
foreach ($entry in @(('version=' + $next),('resume=' + $resume.ToString().ToLowerInvariant()),('ffmpeg-version=' + $ffmpeg.version),('ffmpeg-source=' + $ffmpeg.source),('ffmpeg-sha256=' + $ffmpeg.sourceSha256))) {
    [IO.File]::AppendAllText($env:GITHUB_OUTPUT, ($entry + "`n"), [Text.UTF8Encoding]::new($false))
}
Write-Output ('Release v' + $next + ' reserved from ' + $env:RELEASE_SOURCE_SHA + '. Existing tags will not be overwritten.')
