param(
    [ValidateSet('Stage','Archive')][string]$Action='Stage',
    [string]$Destination,
    [string]$FFmpegRoot,
    [string]$SourceDll,
    [string]$IconPath,
    [string]$MetadataDirectory = $PSScriptRoot,
    [string]$TestReport,
    [long]$MaximumBytes=536870912
)
$ErrorActionPreference='Stop'
if($MaximumBytes -le 0 -or $MaximumBytes -gt 536870912){throw 'Package byte budget must be between 1 and 512 MiB'}
if(-not ('BoomboxPackageQuotaStream' -as [type])){
Add-Type -TypeDefinition @'
using System;
using System.IO;
public sealed class BoomboxPackageQuotaStream : Stream {
    private readonly FileStream file;
    private readonly long maximum;
    public BoomboxPackageQuotaStream(string path, long cap) { maximum=cap; file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read); }
    public override bool CanRead => false;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => file.Length;
    public override long Position { get => file.Position; set { if(value<0 || value>maximum)throw new IOException("Package seek byte quota"); file.Position=value; } }
    public override void Flush() => file.Flush(true);
    public override int Read(byte[] buffer,int offset,int count) => throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin) { long p=origin==SeekOrigin.Begin?offset:origin==SeekOrigin.Current?Position+offset:Length+offset; Position=p; return p; }
    public override void SetLength(long value) { if(value<0 || value>maximum)throw new IOException("Package length byte quota"); file.SetLength(value); }
    public override void Write(byte[] buffer,int offset,int count) { if(count<0 || Position>maximum-count)throw new IOException("Package streaming byte quota"); file.Write(buffer,offset,count); }
    protected override void Dispose(bool disposing) { if(disposing)file.Dispose(); base.Dispose(disposing); }
}
'@
}
$script:packageBytesWritten=0L
$packageManifest=Get-Content -Raw -LiteralPath (Join-Path $MetadataDirectory 'manifest.json') | ConvertFrom-Json
if(-not $Destination){$Destination=Join-Path $PSScriptRoot ('outputs/Rizias-'+$packageManifest.name+'-'+$packageManifest.version_number)}
$packageRoot=[IO.Path]::GetFullPath($Destination)
$packageOutputs=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'outputs'))+[IO.Path]::DirectorySeparatorChar
if(-not $packageRoot.StartsWith($packageOutputs,[StringComparison]::OrdinalIgnoreCase)){throw 'Package must be created inside mod/outputs'}
function Copy-PackageFile([string]$Source,[string]$Target){
    $sourceInfo=Get-Item -LiteralPath $Source
    if(($sourceInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Package source reparse points are not allowed'}
    if($sourceInfo.Length -gt $MaximumBytes-$script:packageBytesWritten){throw 'Package aggregate byte budget'}
    New-Item -ItemType Directory -Path (Split-Path -Parent $Target) -Force | Out-Null
    $inputFile=[IO.File]::Open($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $outputFile=[BoomboxPackageQuotaStream]::new($Target,$MaximumBytes-$script:packageBytesWritten)
    try{$inputFile.CopyTo($outputFile,65536);$outputFile.Flush();$script:packageBytesWritten+=$outputFile.Length}
    finally{$outputFile.Dispose();$inputFile.Dispose()}
}
function Write-PackageText([string]$Target,[string]$Text){
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($Text)
    if($bytes.LongLength -gt $MaximumBytes-$script:packageBytesWritten){throw 'Package text byte budget'}
    $outputFile=[BoomboxPackageQuotaStream]::new($Target,$MaximumBytes-$script:packageBytesWritten)
    try{$outputFile.Write($bytes,0,$bytes.Length);$outputFile.Flush();$script:packageBytesWritten+=$outputFile.Length}finally{$outputFile.Dispose()}
}
function Copy-PackageTree([string]$Source,[string]$Target){
    $sourceRoot=[IO.Path]::GetFullPath($Source).TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach($item in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Force){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Package tree reparse points are not allowed'}
        if(-not $item.PSIsContainer){Copy-PackageFile $item.FullName (Join-Path $Target $item.FullName.Substring($sourceRoot.Length+1))}
    }
}
if($Action -eq 'Stage'){
    if(-not $FFmpegRoot){throw 'Supply FFmpegRoot containing bin, LICENSE, README.txt and manifest.json'}
    if(-not $SourceDll -or -not $IconPath){throw 'Supply explicit SourceDll and user-approved IconPath; output/icon must not be selected implicitly'}
    $SourceDll=[IO.Path]::GetFullPath($SourceDll);$IconPath=[IO.Path]::GetFullPath($IconPath)
    if(-not (Test-Path -LiteralPath $SourceDll -PathType Leaf) -or [IO.Path]::GetExtension($SourceDll) -ne '.dll'){throw 'Explicit candidate DLL does not exist'}
    $packageDllHash=(Get-FileHash -LiteralPath $SourceDll -Algorithm SHA256).Hash
    if($TestReport){
        $packageTest=Get-Content -Raw -LiteralPath $TestReport | ConvertFrom-Json
        if($packageTest.passed -ne $true -or $packageTest.productSha256 -ne $packageDllHash){throw 'Offline test report does not match the candidate DLL'}
    }
    if(-not (Test-Path -LiteralPath (Join-Path $FFmpegRoot 'Sources') -PathType Container)){throw 'Corresponding FFmpeg source and build materials are required'}
    $toolManifest=Get-Content -LiteralPath (Join-Path $FFmpegRoot 'manifest.json') -Raw|ConvertFrom-Json
    foreach($toolHash in @(@('bin/ffmpeg.exe',$toolManifest.ffmpegSha256),@('bin/ffprobe.exe',$toolManifest.ffprobeSha256),@(('Sources/ffmpeg-'+$toolManifest.version+'.tar.xz'),$toolManifest.sourceSha256))){
        if(!$toolHash[1] -or (Get-FileHash -LiteralPath (Join-Path $FFmpegRoot $toolHash[0])).Hash -ne $toolHash[1]){throw ('FFmpeg distribution hash mismatch: '+$toolHash[0])}
    }
    if(Test-Path -LiteralPath $packageRoot){throw 'Package directory already exists; preserve previous candidates'}
    $packageExpected=(Get-Item -LiteralPath $SourceDll).Length+(Get-Item -LiteralPath $IconPath).Length+1048576
    foreach($tree in @($FFmpegRoot,(Join-Path $PSScriptRoot 'Translations'))){$packageExpected+=((Get-ChildItem -LiteralPath $tree -Recurse -File|Measure-Object Length -Sum).Sum)}
    if($packageExpected -gt $MaximumBytes -or ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($packageRoot))).AvailableFreeSpace -lt 4294967296+$packageExpected){throw 'Package preflight budget/free-space refusal'}
    $packagePlugin=Join-Path $packageRoot 'BepInEx/plugins/Rizias-ValheimBoombox'
    $packageToolRoot=if(Test-Path -LiteralPath (Join-Path $FFmpegRoot 'bin') -PathType Container){Join-Path $FFmpegRoot 'bin'}else{$FFmpegRoot}
    Copy-PackageFile $SourceDll (Join-Path $packagePlugin 'ValheimBoombox.dll')
    $packageReadme = [IO.File]::ReadAllText((Join-Path $MetadataDirectory 'README.md'))
    $packageReadme = $packageReadme.Replace('](Screenshots/', '](https://raw.githubusercontent.com/rizias/valheim-boombox/main/Screenshots/')
    Write-PackageText (Join-Path $packageRoot 'README.md') $packageReadme
    Copy-PackageFile (Join-Path $MetadataDirectory 'manifest.json') (Join-Path $packageRoot 'manifest.json')
    Copy-PackageFile $IconPath (Join-Path $packageRoot 'icon.png')
    Copy-PackageFile $IconPath (Join-Path $packagePlugin 'icon.png')
    Copy-PackageFile (Join-Path $PSScriptRoot 'LICENSE') (Join-Path $packageRoot 'LICENSE')
    Copy-PackageFile (Join-Path $PSScriptRoot 'ASSET-CREDITS.md') (Join-Path $packageRoot 'ASSET-CREDITS.md')
    Copy-PackageFile (Join-Path $MetadataDirectory 'CHANGELOG.md') (Join-Path $packageRoot 'CHANGELOG.md')
    Copy-PackageTree (Join-Path $PSScriptRoot 'Translations') (Join-Path $packagePlugin 'Translations')
    foreach($packageTool in @('ffmpeg.exe','ffprobe.exe')){Copy-PackageFile (Join-Path $packageToolRoot $packageTool) (Join-Path $packagePlugin ('Tools/FFmpeg/'+$packageTool))}
    Copy-PackageFile (Join-Path $FFmpegRoot 'manifest.json') (Join-Path $packagePlugin 'Tools/FFmpeg/manifest.json')
    foreach($packageLicense in @('LICENSE','README.txt')){Copy-PackageFile (Join-Path $FFmpegRoot $packageLicense) (Join-Path $packagePlugin ('Tools/FFmpeg/'+$packageLicense))}
    Copy-PackageTree (Join-Path $FFmpegRoot 'Sources') (Join-Path $packagePlugin 'Tools/FFmpeg/Sources')
    $packageProvenance=[ordered]@{musicProcessing='C# inside ValheimBoombox.dll';ffmpeg=(Get-Content -LiteralPath (Join-Path $FFmpegRoot 'manifest.json') -Raw|ConvertFrom-Json)}
    Write-PackageText (Join-Path $packageRoot 'TOOLS.json') ($packageProvenance|ConvertTo-Json -Depth 10)
    if($TestReport){
        $packageBuildInfo=[ordered]@{version=$packageManifest.version_number;dllSha256=$packageDllHash;offlineTests='passed';gameTested=$false}
        Write-PackageText (Join-Path $packageRoot 'BUILD-INFO.json') ($packageBuildInfo|ConvertTo-Json)
    }
    [pscustomobject]@{stage=$packageRoot;plugin=$packagePlugin}|ConvertTo-Json
    exit 0
}
$packageActualManifest=Get-Content -Raw -LiteralPath (Join-Path $packageRoot 'manifest.json') | ConvertFrom-Json
if(Test-Path -LiteralPath (Join-Path $packageRoot 'BUILD-INFO.json')){
    $packageBuildInfo=Get-Content -Raw -LiteralPath (Join-Path $packageRoot 'BUILD-INFO.json') | ConvertFrom-Json
    if($packageBuildInfo.version -ne $packageActualManifest.version_number -or $packageBuildInfo.offlineTests -ne 'passed' -or $packageBuildInfo.gameTested -ne $false -or $packageBuildInfo.dllSha256 -ne (Get-FileHash -LiteralPath (Join-Path $packageRoot 'BepInEx/plugins/Rizias-ValheimBoombox/ValheimBoombox.dll')).Hash){throw 'Build information does not match the staged DLL and manifest'}
}elseif(-not (Test-Path -LiteralPath (Join-Path $packageRoot 'VERIFICATION.md'))){throw 'Supply automatic BUILD-INFO.json or the legacy verification report'}
if($packageActualManifest.name -notmatch '^[A-Za-z0-9_]{1,128}$' -or $packageActualManifest.version_number -notmatch '^\d+\.\d+\.\d+$' -or $packageActualManifest.description.Length -gt 250 -or @($packageActualManifest.dependencies).Count -eq 0){throw 'Invalid Thunderstore manifest'}
$packageIcon=[IO.File]::ReadAllBytes((Join-Path $packageRoot 'icon.png'))
if($packageIcon.Length -lt 24 -or [BitConverter]::ToString($packageIcon[0..7]) -ne '89-50-4E-47-0D-0A-1A-0A' -or [BitConverter]::ToString($packageIcon[16..23]) -ne '00-00-01-00-00-00-01-00'){throw 'Thunderstore icon must be a 256x256 PNG'}
$packageFiles=@(Get-ChildItem -LiteralPath $packageRoot -Recurse -File|Where-Object Name -ne 'SHA256.json'|Sort-Object FullName)
$script:packageBytesWritten=[long](($packageFiles|Measure-Object Length -Sum).Sum)
if($script:packageBytesWritten -gt $MaximumBytes){throw 'Existing stage exceeds package budget'}
$packageForbidden=@($packageFiles|Where-Object {$_.Name -match '^(python|RuntimeProbe|QaBootstrap|QaGameplay|QaPreloader|QaTransport)' -or $_.Name -eq 'assembly_valheim.dll' -or $_.Name -match '^UnityEngine\.' -or $_.Extension -in @('.py','.pyc','.pyd','.wav','.flac','.mp3','.ape','.wv','.cue','.cfg','.fch','.fwl','.db','.pdb') -or $_.FullName -match '[\\/](test-saves|test-game|MusicCache|QaBootstrap|QaGameplay|config)[\\/]'})
if($packageForbidden){throw ('Forbidden runtime/test/music files: '+($packageForbidden.Name -join ', '))}
$packageHashes=[ordered]@{}
foreach($packageFile in $packageFiles){$packageHashes[$packageFile.FullName.Substring($packageRoot.Length+1).Replace('\','/')]=(Get-FileHash -LiteralPath $packageFile.FullName -Algorithm SHA256).Hash}
Write-PackageText (Join-Path $packageRoot 'SHA256.json') ($packageHashes|ConvertTo-Json)
$packageArchive=$packageRoot+'.zip'
if(Test-Path -LiteralPath $packageArchive){throw 'Archive already exists'}
if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($packageRoot))).AvailableFreeSpace -lt 4294967296+$script:packageBytesWritten+1048576){throw 'Archive free-space refusal'}
$archiveOutput=[BoomboxPackageQuotaStream]::new($packageArchive,$MaximumBytes)
$archiveWriter=[IO.Compression.ZipArchive]::new($archiveOutput,[IO.Compression.ZipArchiveMode]::Create,$true)
try{
    foreach($packageFile in Get-ChildItem -LiteralPath $packageRoot -Recurse -File|Sort-Object FullName){
        $archiveEntry=$archiveWriter.CreateEntry($packageFile.FullName.Substring($packageRoot.Length+1).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
        $archiveInput=[IO.File]::OpenRead($packageFile.FullName);$archiveEntryOutput=$archiveEntry.Open()
        try{$archiveInput.CopyTo($archiveEntryOutput,65536)}finally{$archiveEntryOutput.Dispose();$archiveInput.Dispose()}
    }
}finally{try{$archiveWriter.Dispose()}finally{$archiveOutput.Dispose()}}
$packageZip=[IO.Compression.ZipFile]::OpenRead($packageArchive)
try{
    foreach($packageName in $packageHashes.Keys){
        $packageEntry=$packageZip.GetEntry($packageName)
        if(-not $packageEntry){throw ('Archive entry missing: '+$packageName)}
        $packageStream=$packageEntry.Open()
        $packageSha=[Security.Cryptography.SHA256]::Create()
        try{$packageActual=[BitConverter]::ToString($packageSha.ComputeHash($packageStream)).Replace('-','')}
        finally{$packageSha.Dispose();$packageStream.Dispose()}
        if($packageActual -ne $packageHashes[$packageName]){throw ('Archive hash mismatch: '+$packageName)}
    }
}finally{$packageZip.Dispose()}
[pscustomobject]@{archive=$packageArchive;files=$packageHashes.Count+1;bytes=(Get-Item -LiteralPath $packageArchive).Length;sha256=(Get-FileHash -LiteralPath $packageArchive).Hash}|ConvertTo-Json
exit 0
