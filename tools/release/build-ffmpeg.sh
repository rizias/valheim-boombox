#!/usr/bin/env bash
set -euo pipefail
source_url="${1:?source URL required}"
version="${2:?version required}"
source_sha="${3:?source SHA256 required}"
work="$(cygpath -u "${4:?work directory required}")"
distribution="$(cygpath -u "${5:?distribution directory required}")"
script_root="$(cd -- "$(dirname -- "$0")" && pwd)"
source_patch="$(cygpath -u "${6:-$script_root/library-response-file.patch}")"
source_patch="$(cd -- "$(dirname -- "$source_patch")" && pwd)/$(basename -- "$source_patch")"
[[ -f "$source_patch" ]]
[[ "$source_url" == "https://ffmpeg.org/releases/ffmpeg-$version.tar.xz" ]]
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$source_sha" =~ ^[a-fA-F0-9]{64}$ ]]
[[ "${MSYSTEM:-}" == UCRT64 ]]
[[ ! -e "$distribution" && ! -e "$work" ]]
mkdir -p "$work" "$distribution/bin" "$distribution/Sources"
archive="$work/ffmpeg-$version.tar.xz"
curl --fail --location --retry 3 --proto '=https' --tlsv1.2 "$source_url" --output "$archive"
printf '%s  %s\n' "$source_sha" "$archive" | sha256sum --check --status
tar -xJf "$archive" -C "$work"
cd "$work/ffmpeg-$version"
patch --batch --forward -p1 < "$source_patch"
./configure --arch=x86_64 --target-os=mingw32 --cc=gcc --cxx=g++ --ar=ar --ranlib=ranlib --prefix=/opt/valheim-audio --pkg-config=false --disable-shared --enable-static --extra-ldflags=-static --disable-autodetect --disable-gpl --disable-nonfree --disable-version3 --disable-debug --disable-doc --disable-stripping --disable-network --disable-avdevice --disable-postproc --disable-swscale --disable-ffplay --disable-everything --enable-ffmpeg --enable-ffprobe --enable-demuxers --enable-parsers --enable-decoders --enable-encoder=pcm_s16le,pcm_f32le --enable-muxer=wav,pcm_f32le --enable-protocol=file,pipe --enable-filter=aresample,aformat,anull,atrim,asetpts,abuffer,abuffersink,highpass,lowpass
grep -q '^CONFIG_PCM_F32LE_MUXER=yes$' ffbuild/config.mak
make -j"${NUMBER_OF_PROCESSORS:-2}" ffmpeg.exe ffprobe.exe
cp ffmpeg.exe ffprobe.exe "$distribution/bin/"
cp COPYING.LGPLv2.1 "$distribution/LICENSE"
cp "$archive" "$distribution/Sources/"
cp "$script_root/build-ffmpeg.sh" "$script_root/complete-ffmpeg.ps1" "$distribution/Sources/"
cp "$source_patch" "$distribution/Sources/library-response-file.patch"
gcc --version > "$distribution/Sources/compiler-version.txt"
pacman -Q > "$distribution/Sources/build-packages.txt"
cp ffbuild/config.mak "$distribution/Sources/config.mak"
printf '%s\n' 'FFmpeg compilation completed. Run complete-ffmpeg.ps1 to record hashes and verify the distribution.'
