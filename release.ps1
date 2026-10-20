param(
    [ValidateSet('Patch','Minor','Major')][string]$Increment = 'Patch',
    [string]$Version,
    [switch]$Rebuild,
    [switch]$ForPublication,
    [string]$SettingsPath = (Join-Path $PSScriptRoot 'release.local.json')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $SettingsPath -PathType Leaf)) {
    throw 'Create release.local.json using release.settings.example.json. Set GameManaged, BepInExCore, FFmpegRoot and PwshPath to your installed tool directories/executable.'
}
$settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json
if ($PSVersionTable.PSVersion.Major -lt 7) {
    if (-not $settings.PwshPath -or -not (Test-Path -LiteralPath $settings.PwshPath -PathType Leaf)) { throw 'Set PwshPath in release.local.json to the PowerShell 7 executable.' }
    & $settings.PwshPath -NoProfile -NonInteractive -File $PSCommandPath @PSBoundParameters
    exit $LASTEXITCODE
}
Set-StrictMode -Version Latest
if ($ForPublication) {
    if ($env:GITHUB_ACTIONS -ne 'true' -or -not $Version) {
        throw 'Publication version metadata is updated only by the GitHub Release workflow with an explicit version.'
    }
} elseif ($PSBoundParameters.ContainsKey('Version') -or $PSBoundParameters.ContainsKey('Increment')) {
    throw 'Local builds keep the source version. Choose patch, minor or major in the GitHub Release workflow.'
}
$settings = Get-Content -Raw -LiteralPath $SettingsPath | ConvertFrom-Json -AsHashtable
foreach ($key in @('GameManaged','BepInExCore','FFmpegRoot','PwshPath')) {
    if (-not $settings[$key] -or -not [IO.Path]::IsPathFullyQualified($settings[$key]) -or -not (Test-Path -LiteralPath $settings[$key])) { throw ('Set an existing absolute path for ' + $key + ' in release.local.json.') }
    $settings[$key] = (Resolve-Path -LiteralPath $settings[$key]).Path
}
if (-not (Test-Path -LiteralPath $settings.PwshPath -PathType Leaf)) { throw 'PwshPath must point to the PowerShell 7 executable.' }
& $settings.PwshPath -NoProfile -NonInteractive -Command 'if ($PSVersionTable.PSVersion.Major -lt 7) { exit 1 }'
if ($LASTEXITCODE -ne 0) { throw 'PwshPath must run PowerShell 7 or newer.' }
$utf8 = [Text.UTF8Encoding]::new($false)
$outputRoot = Join-Path $PSScriptRoot 'outputs'
$releaseRoot = Join-Path $outputRoot $(if ($ForPublication) { 'releases' } else { 'local' })
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
try { $lock = [IO.File]::Open((Join-Path $outputRoot '.release.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch { throw 'Another release is running. Wait for it to finish and retry.' }

function Invoke-ReleaseScript([string]$Name, [hashtable]$Parameters) {
    $arguments = @('-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot $Name))
    foreach ($key in $Parameters.Keys) {
        $arguments += '-' + $key
        if ($Parameters[$key] -isnot [switch] -and $Parameters[$key] -isnot [bool]) { $arguments += [string]$Parameters[$key] }
    }
    & $settings.PwshPath @arguments
    if ($LASTEXITCODE -ne 0) { throw ($Name + ' failed with exit code ' + $LASTEXITCODE + '. Source version metadata was not advanced.') }
}

function Get-ReleaseFingerprint([hashtable]$Overrides = @{}) {
    $files = [Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File -Force) {
        if ($file.Name -ne 'release.local.json' -and $file.FullName -ne [IO.Path]::GetFullPath($SettingsPath) -and ($file.Extension -in @('.ps1','.cmd','.md','.json','.png') -or $file.Name -in @('LICENSE','.gitignore','.gitattributes'))) {
            $files.Add([pscustomobject]@{ key = $file.Name; path = $file.FullName })
        }
    }
    foreach ($asset in @('valheimboombox_bassfx','valheimboombox_v3_2','valheimboombox_musicfx','contact-shapes.json','pendant-fit.json','pendant-particles.json','ramsund-dragon-head.png','cassette-icon.png','bonus-icons/wave.png','bonus-icons/health.png','bonus-icons/stamina.png','bonus-icons/eitr.png')) {
        $files.Add([pscustomobject]@{key='assets/' + $asset;path=(Join-Path $PSScriptRoot ('assets/' + $asset))})
    }
    foreach ($directory in @('src','Translations','tests','tools','third_party','Screenshots')) {
        if ($directory -eq 'Screenshots' -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $directory))) { continue }
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $directory) -Recurse -File -Force) {
            if ($file.FullName -notmatch '[\\/](bin|obj|node_modules|__pycache__)[\\/]') {
                $files.Add([pscustomobject]@{ key = [IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName).Replace('\','/'); path = $file.FullName })
            }
        }
    }
    foreach ($directory in @('GameManaged','BepInExCore')) {
        foreach ($file in Get-ChildItem -LiteralPath $settings[$directory] -Filter '*.dll' -File) {
            $files.Add([pscustomobject]@{ key = $directory + '/' + $file.Name; path = $file.FullName })
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $settings.FFmpegRoot -Recurse -File -Force) {
        $files.Add([pscustomobject]@{ key = 'FFmpegRoot/' + [IO.Path]::GetRelativePath($settings.FFmpegRoot, $file.FullName).Replace('\','/'); path = $file.FullName })
    }
    $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $files.Add([pscustomobject]@{key='tool/dotnet';path=$dotnet})
    $files.Add([pscustomobject]@{key='tool/pwsh';path=$settings.PwshPath})
    $records = [Collections.Generic.List[string]]::new()
    foreach ($key in @('GameManaged','BepInExCore','FFmpegRoot','PwshPath')) { $records.Add($key + '=' + $settings[$key]) }
    $sdkLines = @(& $dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the .NET toolchain.' }
    $records.Add('dotnetSdks=' + ($sdkLines -join '|'))
    if ($sdkLines.Count -eq 0 -or $sdkLines[-1] -notmatch '^(\S+) \[(.+)\]$') { throw '.NET SDK was not found.' }
    $compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
    $records.Add('compiler=' + $compiler + '=' + (Get-FileHash -LiteralPath $compiler).Hash)
    foreach ($file in $files | Sort-Object key) {
        $path = if ($Overrides.ContainsKey($file.key)) { $Overrides[$file.key] } else { $file.path }
        $records.Add($file.key + '=' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes(($records -join "`n")))).Replace('-','') }
    finally { $sha.Dispose() }
}

try {
    $metadataNames = @('manifest.json','src/BoomboxNetworkProtocol.cs','README.md','README.ru.md','PLAY.md','PLAY.ru.md','CHANGELOG.md','CHANGELOG.ru.md')
    $sourceManifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') | ConvertFrom-Json
    if ($sourceManifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Source manifest version must be major.minor.patch.' }
    if (-not $ForPublication) { $Version = $sourceManifest.version_number }
    foreach ($receiptFile in Get-ChildItem -LiteralPath $releaseRoot -Filter 'release.json' -Recurse -File | Sort-Object LastWriteTimeUtc -Descending) {
        $receipt = Get-Content -Raw -LiteralPath $receiptFile.FullName | ConvertFrom-Json -AsHashtable
        if (-not $receipt.ContainsKey('formatVersion') -or $receipt.formatVersion -ne 2) { continue }
        if ($Version -and $Version -ne $receipt.version) { continue }
        if (@($metadataNames | Where-Object { -not $receipt.metadata.ContainsKey($_) }).Count) { continue }
        $archive = Join-Path $receiptFile.DirectoryName $receipt.archive
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf) -or (Get-FileHash -LiteralPath $archive).Hash -ne $receipt.sha256) { throw ('A recorded release archive is missing or damaged. Preserved receipt: ' + $receiptFile.FullName) }
        if ($receipt.ffmpegSourceArchive -cne ([IO.Path]::GetFileNameWithoutExtension($receipt.archive) + '-FFmpeg-Sources.zip')) { throw 'Invalid recorded FFmpeg source archive identity.' }
        $sourceArchive = Join-Path $receiptFile.DirectoryName $receipt.ffmpegSourceArchive
        if (-not (Test-Path -LiteralPath $sourceArchive -PathType Leaf) -or (Get-FileHash -LiteralPath $sourceArchive).Hash -ne $receipt.ffmpegSourceSha256) { throw ('A recorded FFmpeg source archive is missing or damaged. Preserved receipt: ' + $receiptFile.FullName) }
        $overrides = @{}
        $compatible = $true
        foreach ($name in $metadataNames) {
            $entry = $receipt.metadata[$name]
            $candidate = Join-Path $receiptFile.DirectoryName $entry.candidate
            $currentHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash
            if ($currentHash -notin @($entry.before,$entry.after) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf) -or (Get-FileHash -LiteralPath $candidate).Hash -ne $entry.after) { $compatible = $false; break }
            $overrides[$name] = $candidate
        }
        if (-not $compatible -or (Get-ReleaseFingerprint $overrides) -ne $receipt.fingerprint) { continue }
        if ($ForPublication) {
            foreach ($name in $metadataNames) {
                if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash -ne $receipt.metadata[$name].after) {
                    [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot $name), [IO.File]::ReadAllBytes($overrides[$name]))
                }
            }
        }
        [IO.File]::WriteAllText(($archive + '.sha256'), ($receipt.sha256 + '  ' + [IO.Path]::GetFileName($archive) + "`n"), $utf8)
        [IO.File]::WriteAllText(($sourceArchive + '.sha256'), ($receipt.ffmpegSourceSha256 + '  ' + $receipt.ffmpegSourceArchive + "`n"), $utf8)
        Write-Output ('Reused verified release ' + $receipt.version + ': ' + $archive)
        exit 0
    }

    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be major.minor.patch.' }
    if ($Rebuild -and $Version -ne $sourceManifest.version_number) { throw 'Rebuild requires the current source version.' }
    $initialFingerprint = Get-ReleaseFingerprint
    $versionRoot = if ($ForPublication) {
        Join-Path $releaseRoot $Version
    } else {
        Join-Path (Join-Path $releaseRoot $Version) $initialFingerprint
    }
    if (Test-Path -LiteralPath $versionRoot) { throw ('Release destination already exists. Preserved files: ' + $versionRoot) }
    $workRoot = Join-Path $outputRoot ('release-work/' + $Version + '-' + [guid]::NewGuid().ToString('N'))
    $metadataRoot = Join-Path $workRoot 'metadata'
    New-Item -ItemType Directory -Path (Join-Path $metadataRoot 'src') -Force | Out-Null
    $overrides = @{}
    foreach ($name in $metadataNames) {
        $source = Join-Path $PSScriptRoot $name
        $candidate = Join-Path $metadataRoot $name
        $text = [IO.File]::ReadAllText($source)
        if (-not $ForPublication) {
            [IO.File]::WriteAllText($candidate, $text, $utf8)
            $overrides[$name] = $candidate
            continue
        }
        switch ($name) {
            'manifest.json' { $text = [regex]::Replace($text, '("version_number"\s*:\s*")[^"]+("\s*)', ('${1}' + $Version + '${2}')) }
            'src/BoomboxNetworkProtocol.cs' {
                if ($text -notmatch 'public const int Protocol = 5;') { throw 'Expected protocol 5. Release automation does not change the protocol.' }
                $text = [regex]::Replace($text, '(public const string Version = ")[^"]+(";)', ('${1}' + $Version + '${2}'))
            }
            'CHANGELOG.md' {
                if ($Rebuild) { break }
                if ($text -notmatch '^# Changelog\r?\n') { throw 'Expected # Changelog heading.' }
                $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
                $heading = '## [' + $Version + '] - ' + [DateTime]::UtcNow.ToString('yyyy-MM-dd')
                if ($text -match '(?m)^## \[Unreleased\]\r?$') {
                    $text = [regex]::Replace($text, '(?m)^(## \[Unreleased\])\r?$', ('${1}' + $newline + $newline + $heading))
                } else {
                    $text = [regex]::Replace($text, '^# Changelog\r?\n', ("# Changelog" + $newline + $newline + $heading + $newline))
                }
            }
            'CHANGELOG.ru.md' {
                if ($Rebuild) { break }
                $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
                $heading = '## [' + $Version + '] - ' + [DateTime]::UtcNow.ToString('yyyy-MM-dd')
                $text = [regex]::Replace($text, '(?m)^(## \[Не выпущено\])\r?$', ('${1}' + $newline + $newline + $heading))
            }
            default { $text = $text.Replace($sourceManifest.version_number, $Version) }
        }
        [IO.File]::WriteAllText($candidate, $text, $utf8)
        $overrides[$name] = $candidate
    }
    $candidateManifest = Get-Content -Raw -LiteralPath $overrides['manifest.json'] | ConvertFrom-Json
    if ($candidateManifest.version_number -ne $Version) { throw 'Candidate manifest version update failed.' }
    Write-Output ('Building release ' + $Version + '. No game will be launched.')
    $buildRoot = Join-Path $workRoot 'build'
    Invoke-ReleaseScript 'build.ps1' @{GameManaged=$settings.GameManaged;BepInExCore=$settings.BepInExCore;OutputDirectory=$buildRoot;DllOnly=$true;ProtocolSource=$overrides['src/BoomboxNetworkProtocol.cs']}
    $dll = Join-Path $buildRoot 'ValheimBoombox.dll'
    $testRoot = Join-Path $workRoot 'tests'
    Invoke-ReleaseScript 'test.ps1' @{GameManaged=$settings.GameManaged;BepInExCore=$settings.BepInExCore;ProductDll=$dll;OutputDirectory=$testRoot}
    $testReport = Join-Path $testRoot 'results.json'
    $report = Get-Content -Raw -LiteralPath $testReport | ConvertFrom-Json
    if ($report.passed -ne $true -or $report.productSha256 -ne (Get-FileHash -LiteralPath $dll).Hash) { throw 'Offline checks did not verify the produced DLL.' }
    $stage = Join-Path $workRoot ('Rizias-' + $candidateManifest.name + '-' + $Version)
    Invoke-ReleaseScript 'package.ps1' @{Action='Stage';Destination=$stage;FFmpegRoot=$settings.FFmpegRoot;SourceDll=$dll;IconPath=(Join-Path $PSScriptRoot 'icon.png');MetadataDirectory=$metadataRoot;TestReport=$testReport}
    Invoke-ReleaseScript 'package.ps1' @{Action='Archive';Destination=$stage;MetadataDirectory=$metadataRoot}
    if ((Get-ReleaseFingerprint) -ne $initialFingerprint) { throw 'Release inputs changed during verification. Candidate artifacts were preserved; source version metadata was not advanced.' }
    $finalFingerprint = Get-ReleaseFingerprint $overrides
    New-Item -ItemType Directory -Path (Join-Path $versionRoot 'metadata/src') -Force | Out-Null
    $archiveName = [IO.Path]::GetFileName($stage) + '.zip'
    $archive = Join-Path $versionRoot $archiveName
    [IO.File]::Copy(($stage + '.zip'), $archive, $false)
    $archiveHash = (Get-FileHash -LiteralPath $archive).Hash
    $sourceArchiveName = [IO.Path]::GetFileName($stage) + '-FFmpeg-Sources.zip'
    $sourceArchive = Join-Path $versionRoot $sourceArchiveName
    [IO.File]::Copy(($stage + '-FFmpeg-Sources.zip'), $sourceArchive, $false)
    $sourceArchiveHash = (Get-FileHash -LiteralPath $sourceArchive).Hash
    $metadata = [ordered]@{}
    foreach ($name in $metadataNames) {
        $relative = 'metadata/' + $name
        [IO.File]::Copy($overrides[$name], (Join-Path $versionRoot $relative), $false)
        $metadata[$name] = [ordered]@{before=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $name)).Hash;after=(Get-FileHash -LiteralPath $overrides[$name]).Hash;candidate=$relative}
    }
    $receipt = [ordered]@{formatVersion=2;version=$Version;archive=$archiveName;sha256=$archiveHash;ffmpegSourceArchive=$sourceArchiveName;ffmpegSourceSha256=$sourceArchiveHash;fingerprint=$finalFingerprint;metadata=$metadata}
    [IO.File]::WriteAllText((Join-Path $versionRoot 'release.json'), ($receipt | ConvertTo-Json -Depth 8), $utf8)
    if ((Get-ReleaseFingerprint) -ne $initialFingerprint) { throw 'Release inputs changed before the source metadata update. Verified artifacts were preserved; source metadata was not advanced.' }
    if ($ForPublication) {
        foreach ($name in $metadataNames) { [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot $name), [IO.File]::ReadAllBytes($overrides[$name])) }
    }
    [IO.File]::WriteAllText(($archive + '.sha256'), ($archiveHash + '  ' + $archiveName + "`n"), $utf8)
    [IO.File]::WriteAllText(($sourceArchive + '.sha256'), ($sourceArchiveHash + '  ' + $sourceArchiveName + "`n"), $utf8)
    Write-Output ('Verified release ' + $Version + ': ' + $archive)
    Write-Output 'Offline tests passed. In-game testing was not performed. No installation, Git staging or upload was performed.'
}
finally { $lock.Dispose() }
