#!/usr/bin/env bash
set -euo pipefail
export PATH="/usr/bin:/bin:$PATH"
root="$(cd -- "$(dirname -- "$(cygpath -u "$0")")" && pwd)"
vc="$(cygpath -u "$VCToolsInstallDir")/bin/Hostx64/x64"
export PATH="$vc:$root/nasm/nasm-2.16.03:$root/make-4.4.1/WinRel:$PATH"
export MSYS2_ARG_CONV_EXCL='*'
cd "$root/ffmpeg-7.1.5"
if [[ ! -f ffbuild/config.mak || "${FFMPEG_RECONFIGURE:-0}" == 1 ]]; then
    ./configure --toolchain=msvc --arch=x86_64 --prefix=/opt/valheim-audio --disable-autodetect --disable-gpl --disable-nonfree --disable-version3 --disable-debug --disable-doc --disable-stripping --disable-network --disable-avdevice --disable-postproc --disable-swscale --disable-ffplay --disable-everything --enable-ffmpeg --enable-ffprobe --enable-demuxers --enable-parsers --enable-decoders --enable-encoder=pcm_s16le,pcm_f32le --enable-muxer=wav,f32le --enable-protocol=file,pipe --enable-filter=aresample,aformat,anull,atrim,asetpts,abuffer,abuffersink,highpass,lowpass
fi
"$root/make-4.4.1/WinRel/gnumake.exe" -r -j8 "SHELL=C:/Program Files/Git/usr/bin/sh.exe" 'CCDEP=:' 'ASDEP=:' 'HOSTCCDEP=:' ffmpeg.exe ffprobe.exe
