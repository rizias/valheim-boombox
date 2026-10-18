param(
    [Parameter(Mandatory)][string]$Distribution,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ('../../outputs/audio-tool-check/' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$inputPath = Join-Path $OutputDirectory 'tone.wav'
$outputPath = Join-Path $OutputDirectory 'decoded.f32'
$writer = [IO.BinaryWriter]::new([IO.File]::Open($inputPath, 'CreateNew', 'Write', 'None'))
try {
    $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([uint32]16036)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([uint32]16)
    $writer.Write([uint16]1); $writer.Write([uint16]1); $writer.Write([uint32]8000)
    $writer.Write([uint32]16000); $writer.Write([uint16]2); $writer.Write([uint16]16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([uint32]16000)
    for ($sample = 0; $sample -lt 8000; $sample++) { $writer.Write([int16](8192 * [Math]::Sin(2 * [Math]::PI * 440 * $sample / 8000))) }
} finally { $writer.Dispose() }
$probe = @(& (Join-Path $Distribution 'bin/ffprobe.exe') -v error -show_entries stream=codec_name,sample_rate,channels -of json $inputPath)
if ($LASTEXITCODE -ne 0) { throw 'Packaged ffprobe could not read the audio fixture.' }
$streams = @(($probe -join "`n" | ConvertFrom-Json).streams)
if ($streams.Count -ne 1 -or $streams[0].codec_name -ne 'pcm_s16le' -or $streams[0].sample_rate -ne '8000' -or $streams[0].channels -ne 1) { throw 'ffprobe returned incorrect audio stream metadata.' }
& (Join-Path $Distribution 'bin/ffmpeg.exe') -v error -n -i $inputPath -map 0:a:0 -f f32le -acodec pcm_f32le -ac 1 -ar 8000 $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Packaged ffmpeg could not decode audio to float PCM.' }
$pcm = [IO.File]::ReadAllBytes($outputPath)
if ($pcm.Length -ne 32000) { throw 'Decoded audio duration, channel count or sample format differs.' }
$peak = 0.0
for ($offset = 0; $offset -lt $pcm.Length; $offset += 4) {
    $value = [BitConverter]::ToSingle($pcm, $offset)
    if ([single]::IsNaN($value) -or [single]::IsInfinity($value)) { throw 'Decoded audio contains invalid samples.' }
    $peak = [Math]::Max($peak, [Math]::Abs($value))
}
if ($peak -lt 0.24 -or $peak -gt 0.26) { throw 'Decoded audio amplitude differs from the known source.' }
Write-Output 'FFprobe metadata and FFmpeg float PCM decoding passed against a generated one-second tone. No audio device or game was opened.'
