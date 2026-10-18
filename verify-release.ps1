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
$archive = Join-Path $ReleaseDirectory $receipt.archive
if ((Get-FileHash -LiteralPath $archive).Hash -ne $receipt.sha256) { throw 'Release archive SHA256 mismatch.' }
$checksum = Get-Content -Raw -LiteralPath ($archive + '.sha256')
if ($checksum.Trim() -ne ($receipt.sha256 + '  ' + $receipt.archive)) { throw 'Release checksum file mismatch.' }
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
try {
    $hashes = Read-Entry 'SHA256.json' | ConvertFrom-Json
    if ($zip.Entries.Count -ne @($hashes.PSObject.Properties).Count + 1) { throw 'Archive entry count differs from its hash manifest.' }
    foreach ($property in $hashes.PSObject.Properties) {
        $entry = $zip.GetEntry($property.Name)
        if (-not $entry) { throw ('Missing hashed archive entry: ' + $property.Name) }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ne $property.Value) { throw ('Archive entry hash mismatch: ' + $property.Name) }
    }
    $manifest = Read-Entry 'manifest.json' | ConvertFrom-Json
    $info = Read-Entry 'BUILD-INFO.json' | ConvertFrom-Json
    if ($manifest.version_number -ne $receipt.version -or $info.version -ne $receipt.version -or $info.offlineTests -ne 'passed' -or $info.gameTested -ne $false) { throw 'Packaged build information/version mismatch.' }
    if ($info.dllSha256 -ne $hashes.'BepInEx/plugins/Rizias-ValheimBoombox/ValheimBoombox.dll') { throw 'Build information DLL hash mismatch.' }
    foreach ($document in @('README.md')) {
        $text = Read-Entry $document
        $expected = Get-Content -Raw -LiteralPath (Join-Path $ReleaseDirectory $receipt.metadata.$document.candidate)
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
Write-Output ('Release verified: ' + $receipt.version + '. Archive hashes, metadata, player documentation and offline-test provenance agree. gameTested=false.')
