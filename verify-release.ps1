param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ReleaseDirectory = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
foreach ($name in @('release.ps1','package.ps1','verify-release.ps1')) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw ('PowerShell parse error in ' + $name + ': ' + ($errors -join '; ')) }
}
$receipt = Get-Content -Raw -LiteralPath (Join-Path $ReleaseDirectory 'release.json') | ConvertFrom-Json
if ($receipt.formatVersion -ne 2 -or $receipt.archive -cne [IO.Path]::GetFileName($receipt.archive) -or $receipt.ffmpegSourceArchive -cne ([IO.Path]::GetFileNameWithoutExtension($receipt.archive) + '-FFmpeg-Sources.zip')) { throw 'Invalid release receipt format or archive identity.' }
$archive = Join-Path $ReleaseDirectory $receipt.archive
if ((Get-FileHash -LiteralPath $archive).Hash -ne $receipt.sha256) { throw 'Release archive SHA256 mismatch.' }
$checksum = Get-Content -Raw -LiteralPath ($archive + '.sha256')
if ($checksum.Trim() -ne ($receipt.sha256 + '  ' + $receipt.archive)) { throw 'Release checksum file mismatch.' }
$sourceArchive = Join-Path $ReleaseDirectory $receipt.ffmpegSourceArchive
if ((Get-FileHash -LiteralPath $sourceArchive).Hash -ne $receipt.ffmpegSourceSha256) { throw 'FFmpeg source archive SHA256 mismatch.' }
if ((Get-Content -Raw -LiteralPath ($sourceArchive + '.sha256')).Trim() -cne ($receipt.ffmpegSourceSha256 + '  ' + $receipt.ffmpegSourceArchive)) { throw 'FFmpeg source checksum file mismatch.' }
foreach ($property in $receipt.metadata.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $property.Name)).Hash -ne $property.Value.after) { throw ('Source metadata differs from the release: ' + $property.Name) }
    if ((Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory $property.Value.candidate)).Hash -ne $property.Value.after) { throw ('Retained candidate metadata hash mismatch: ' + $property.Name) }
}
$protocol = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'src/BoomboxNetworkProtocol.cs')
if ($protocol -notmatch ('public const string Version = "' + [regex]::Escape($receipt.version) + '";') -or $protocol -notmatch 'public const int Protocol = 5;') { throw 'Source release version/protocol mismatch.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
function Read-Entry([string]$Name) {
    $entry = $zip.GetEntry($Name)
    if (-not $entry) { throw ('Missing archive entry: ' + $Name) }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Verify-Inventory([IO.Compression.ZipArchive]$Archive) {
    $inventory = $Archive.GetEntry('SHA256.json')
    if (-not $inventory) { throw 'Missing archive hash inventory.' }
    $reader = [IO.StreamReader]::new($inventory.Open())
    try { $hashes = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    if ($Archive.Entries.Count -ne @($hashes.PSObject.Properties).Count + 1 -or @($Archive.Entries.FullName | Select-Object -Unique).Count -ne $Archive.Entries.Count) { throw 'Archive entry count differs from its hash manifest.' }
    foreach ($property in $hashes.PSObject.Properties) {
        $entry = $Archive.GetEntry($property.Name)
        if (-not $entry) { throw ('Missing hashed archive entry: ' + $property.Name) }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $property.Value) { throw ('Archive entry hash mismatch: ' + $property.Name) }
    }
    return $hashes
}
try {
    $hashes = Verify-Inventory $zip
    if (@($zip.Entries | Where-Object { $_.FullName -match '(^|/)Sources(/|$)|\.(ps1|sh|patch)$' }).Count) { throw 'Installer contains FFmpeg source or build scripts.' }
    $ffmpegPrefix = 'BepInEx/plugins/Rizias-ValheimBoombox/Tools/FFmpeg/'
    $toolManifest = Read-Entry ($ffmpegPrefix + 'manifest.json') | ConvertFrom-Json
    if ($toolManifest.license -ne 'LGPL-2.1-or-later') { throw 'FFmpeg build provenance does not permit the selected later license.' }
    if ($toolManifest.ffmpegSha256 -ne $hashes.($ffmpegPrefix + 'ffmpeg.exe') -or $toolManifest.ffprobeSha256 -ne $hashes.($ffmpegPrefix + 'ffprobe.exe')) { throw 'Installed FFmpeg executable identity mismatch.' }
    $sourceUrl = 'https://github.com/rizias/valheim-boombox/releases/download/v' + $receipt.version + '/' + $receipt.ffmpegSourceArchive
    if (-not (Read-Entry ($ffmpegPrefix + 'README.txt')).Contains($sourceUrl)) { throw 'Installed FFmpeg notice lacks its versioned corresponding-source URL.' }
    $licenseHashes = @{
        'COPYING.LGPLv3' = 'DA7EABB7BAFDF7D3AE5E9F223AA5BDC1EECE45AC569DC21B3B037520B4464768'
        'COPYING.GPLv3' = '8CEB4B9EE5ADEDDE47B31E975C1D90C73AD27B6B165A1DCD80C7C545EB65B903'
        'COPYING.GCC-RUNTIME.txt' = '9D6B43CE4D8DE0C878BF16B54D8E7A10D9BD42B75178153E3AF6A815BDC90F74'
        'COPYING.MinGW-w64-runtime.txt' = '1DB8DA07B436C68833C0673FFEE3D9FCB2526047F3820B81661865DFEDC79A1F'
        'COPYING.winpthreads.txt' = '63263614CDD29F2F93CBA85E992F041B31F9FC7B4033692F31269489A8A1B177'
        'COPYING.FFmpeg-third-party.txt' = 'F53E3845DDAD7383FC61D1448E5433C71BC4EF6DEC31DB0F8E889BADFFAA1E56'
    }
    foreach ($name in $licenseHashes.Keys) {
        if ($hashes.($ffmpegPrefix + $name) -ne $licenseHashes[$name]) { throw ('Missing or altered FFmpeg license text: ' + $name) }
    }
    $runtimeNotice = Read-Entry ($ffmpegPrefix + 'README.txt')
    if ($hashes.($ffmpegPrefix + 'LICENSE') -ne '246041B6ECF9BC32D718A62C57877C78B5EB397B6467E74ED7AE2626AB189C30') { throw 'Original LGPL 2.1 license text is missing or altered.' }
    if (-not $runtimeNotice.Contains('elects LGPL 3.0 or later') -or -not $runtimeNotice.Contains('Modification dated 2026-10-20')) { throw 'FFmpeg notice lacks the selected license or modification notice.' }
    if (-not (Read-Entry 'README.md').Contains($sourceUrl)) { throw 'Package description lacks the direct corresponding-source download.' }
    $manifest = Read-Entry 'manifest.json' | ConvertFrom-Json
    $info = Read-Entry 'BUILD-INFO.json' | ConvertFrom-Json
    if ($manifest.version_number -ne $receipt.version -or $info.version -ne $receipt.version -or $info.offlineTests -ne 'passed' -or $info.gameTested -ne $false) { throw 'Packaged build information/version mismatch.' }
    if ($info.dllSha256 -ne $hashes.'BepInEx/plugins/Rizias-ValheimBoombox/ValheimBoombox.dll') { throw 'Build information DLL hash mismatch.' }
    foreach ($document in @('README.md')) {
        $text = Read-Entry $document
        $expected = Get-Content -Raw -LiteralPath (Join-Path $ReleaseDirectory $receipt.metadata.$document.candidate)
        $expected = $expected.Replace('](Screenshots/', '](https://raw.githubusercontent.com/rizias/valheim-boombox/main/Screenshots/')
        if ($text -cne $expected) { throw ('Installed player document differs from its canonical source: ' + $document) }
        foreach ($link in [regex]::Matches($text, '\[[^\]]*\]\(([^\s)]+)\)')) {
            $target = $link.Groups[1].Value
            if ($target.StartsWith('#') -or $target -match '^https?://') { continue }
            $path = [Uri]::UnescapeDataString(($target -split '#',2)[0])
            if (-not $zip.GetEntry($path)) { throw ('Missing player documentation link in ' + $document + ': ' + $target) }
        }
    }
    if ((Read-Entry 'BUILD-INFO.json') -match '(?i)[a-z]:[\\/]|\\\\') { throw 'Build information contains a private absolute path.' }
}
finally { $zip.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($sourceArchive)
try {
    $sourceHashes = Verify-Inventory $zip
    if ($sourceHashes.'manifest.json' -ne $hashes.($ffmpegPrefix + 'manifest.json')) { throw 'FFmpeg source manifest differs from installed manifest.' }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -notin @('LICENSE','README.txt','NOTICE.txt','BUILD-SCRIPTS-LICENSE.txt','COPYING.FFmpeg-third-party.txt','manifest.json','SHA256.json') -and -not $licenseHashes.ContainsKey($entry.FullName) -and -not $entry.FullName.StartsWith('Sources/')) { throw ('Unexpected FFmpeg source archive entry: ' + $entry.FullName) }
    }
    foreach ($name in $licenseHashes.Keys) {
        if ($sourceHashes.$name -ne $licenseHashes[$name]) { throw ('Missing or altered source-archive license text: ' + $name) }
    }
    if ((Read-Entry 'NOTICE.txt') -cne $runtimeNotice) { throw 'Source and binary redistribution notices differ.' }
    if ($sourceHashes.LICENSE -ne $hashes.($ffmpegPrefix + 'LICENSE')) { throw 'Source and binary original LGPL notices differ.' }
    if ($sourceHashes.'BUILD-SCRIPTS-LICENSE.txt' -ne $hashes.LICENSE) { throw 'Source archive lacks the project build-script license.' }
    foreach ($name in @('LICENSE','README.txt','Sources/build-ffmpeg.sh','Sources/complete-ffmpeg.ps1','Sources/compiler-version.txt','Sources/build-packages.txt','Sources/config.mak','Sources/source.json','Sources/library-response-file.patch',('Sources/ffmpeg-' + $toolManifest.version + '.tar.xz'))) { if (-not $zip.GetEntry($name)) { throw ('Missing FFmpeg source material: ' + $name) } }
    $sourceIdentity = Read-Entry 'Sources/source.json' | ConvertFrom-Json
    if ($sourceIdentity.version -ne $toolManifest.version -or $sourceIdentity.source -ne $toolManifest.source -or $sourceIdentity.sourceSha256 -ne $toolManifest.sourceSha256) { throw 'FFmpeg corresponding source identity mismatch.' }
    $patchProperty = $toolManifest.PSObject.Properties['patchSha256']
    $patchHash = if ($patchProperty) { $patchProperty.Value } else { (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'third_party/ffmpeg/library-response-file.patch')).Hash }
    if ($sourceHashes.('Sources/ffmpeg-' + $toolManifest.version + '.tar.xz') -ne $toolManifest.sourceSha256 -or $sourceHashes.'Sources/library-response-file.patch' -ne $patchHash) { throw 'FFmpeg corresponding source or patch identity mismatch.' }
}
finally { $zip.Dispose() }
Write-Output ('Release verified: ' + $receipt.version + '. Installer and FFmpeg source hashes, metadata, player documentation and offline-test provenance agree. gameTested=false.')
