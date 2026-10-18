param(
    [Parameter(Mandatory)][string]$Distribution,
    [string]$SourceManifest = (Join-Path $PSScriptRoot 'source.json'),
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Distribution = [IO.Path]::GetFullPath($Distribution)
$manifestPath = Join-Path $Distribution 'manifest.json'
function Hash([string]$Relative) { (Get-FileHash -LiteralPath (Join-Path $Distribution $Relative) -Algorithm SHA256).Hash }
function Inspect-Tool([string]$Name) {
    $lines = @(& (Join-Path $Distribution ('bin/' + $Name + '.exe')) -version)
    if ($LASTEXITCODE -ne 0) { throw ($Name + ' failed to run on Windows x64.') }
    $lines
}
$ffmpeg = @(Inspect-Tool 'ffmpeg')
$ffprobe = @(Inspect-Tool 'ffprobe')
if (-not $VerifyOnly) {
    $source = Get-Content -Raw -LiteralPath $SourceManifest | ConvertFrom-Json
    if ($source.version -notmatch '^\d+\.\d+\.\d+$' -or $source.source -cne ('https://ffmpeg.org/releases/ffmpeg-' + $source.version + '.tar.xz') -or $source.sourceSha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid FFmpeg source identity.' }
    $configuration = @($ffmpeg | Where-Object { $_ -match '^configuration: ' })
    if ($configuration.Count -ne 1) { throw 'Cannot read the actual FFmpeg configure flags.' }
    $manifest = [ordered]@{
        version=$source.version; source=$source.source; sourceSha256=$source.sourceSha256
        license='LGPL-2.1-or-later'; platform='Windows x64'; configuration=$configuration[0]
        ffmpegSha256=(Hash 'bin/ffmpeg.exe'); ffprobeSha256=(Hash 'bin/ffprobe.exe')
        patchSha256=(Hash 'Sources/library-response-file.patch')
    }
    $utf8 = [Text.UTF8Encoding]::new($false)
    $sourceIdentity = [ordered]@{version=$source.version;source=$source.source;sourceSha256=$source.sourceSha256}
    [IO.File]::WriteAllText((Join-Path $Distribution 'Sources/source.json'), ($sourceIdentity | ConvertTo-Json), $utf8)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), $utf8)
    $notice = @"
FFmpeg and ffprobe $($source.version) for Valheim Boombox, Windows x64
Copyright (c) 2000-2026 the FFmpeg developers.
These separate executables are licensed under LGPL 2.1 or later.
See LICENSE and licensing files in the complete original source archive.
The mod's MIT license does not cover these tools. No music is included.

Source: $($source.source)
Original source and SHA256: Sources/ffmpeg-$($source.version).tar.xz and source.json.
Executable hashes and actual configure flags: manifest.json and TOOLS.json.
Built with MSYS2 UCRT64 GCC for native Windows x64, statically linked compiler
runtime. Compiler version and installed build package versions are recorded
in Sources/compiler-version.txt and Sources/build-packages.txt.
GPL, nonfree, version3, external autodetection and networking are disabled.
The executables decode local audio to PCM. They are not video encoders.

Rebuild on Windows x64:
1. Install MSYS2, PowerShell 7, and these MSYS2 packages in the UCRT64 shell:
   pacman -S mingw-w64-ucrt-x86_64-gcc make nasm diffutils tar xz curl patch
2. In the UCRT64 shell, cd to Sources and run the exact included recipe:
   bash build-ffmpeg.sh '$($source.source)' '$($source.version)' '$($source.sourceSha256)' 'C:/ffmpeg-work' 'C:/ffmpeg-rebuilt'
   Both output directories must be new. The source download is verified
   against the pinned SHA256 before extraction. The included response-file
   patch passes object paths to the archiver through a file to keep Windows
   command lengths within platform limits. Codec source is unchanged.
3. In PowerShell 7 from Sources, record new hashes and verify:
   ./complete-ffmpeg.ps1 -Distribution C:/ffmpeg-rebuilt -SourceManifest ./source.json
The supplied source archive is the complete corresponding source. All build
recipes are included. Exact package versions describe this build; updated
compilers may produce different executable hashes. Build tools are separate.

You may rebuild, modify and replace these executables. Close the game and
replace both files in Tools/FFmpeg, or set FFmpegPath and FFprobePath in the
shared Mods settings menu. The mod invokes them as separate processes.
Keep this notice, LICENSE and corresponding source with redistributed copies.
See the license for its full terms.
"@
    [IO.File]::WriteAllText((Join-Path $Distribution 'README.txt'), $notice, $utf8)
}
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$source = Get-Content -Raw -LiteralPath (Join-Path $Distribution 'Sources/source.json') | ConvertFrom-Json
if ($manifest.version -ne $source.version -or $manifest.source -ne $source.source -or $manifest.sourceSha256 -ne $source.sourceSha256 -or $manifest.license -ne 'LGPL-2.1-or-later') { throw 'FFmpeg provenance differs from its corresponding-source identity.' }
foreach ($entry in @(@('bin/ffmpeg.exe',$manifest.ffmpegSha256),@('bin/ffprobe.exe',$manifest.ffprobeSha256),@(('Sources/ffmpeg-' + $manifest.version + '.tar.xz'),$manifest.sourceSha256))) {
    if (-not $entry[1] -or (Hash $entry[0]) -ne $entry[1]) { throw ('FFmpeg distribution hash mismatch: ' + $entry[0]) }
}
if ((Hash 'Sources/library-response-file.patch') -ne $manifest.patchSha256) { throw 'FFmpeg response-file patch hash mismatch.' }
foreach ($required in @('LICENSE','README.txt','Sources/build-ffmpeg.sh','Sources/complete-ffmpeg.ps1','Sources/compiler-version.txt','Sources/build-packages.txt','Sources/config.mak')) {
    if (-not (Test-Path -LiteralPath (Join-Path $Distribution $required) -PathType Leaf)) { throw ('Missing FFmpeg rebuild material: ' + $required) }
}
if ($ffmpeg[0] -notlike ('ffmpeg version ' + $manifest.version + '*') -or $ffprobe[0] -notlike ('ffprobe version ' + $manifest.version + '*')) { throw 'FFmpeg version differs from the pinned source.' }
foreach ($flag in @('--disable-gpl','--disable-nonfree','--disable-version3','--disable-autodetect','--disable-network','--disable-shared','--enable-static','--extra-ldflags=-static','--target-os=mingw32')) {
    if ($manifest.configuration -notmatch ('(?:^|\s)' + [regex]::Escape($flag) + '(?:\s|$)')) { throw ('Missing required FFmpeg build flag: ' + $flag) }
}
if (@($ffmpeg | Where-Object { $_ -eq $manifest.configuration }).Count -ne 1 -or @($ffprobe | Where-Object { $_ -eq $manifest.configuration }).Count -ne 1) { throw 'Executable configure flags differ from the distribution manifest.' }
Write-Output 'FFmpeg binaries, pinned source archive, license and exact rebuild materials verified.'
