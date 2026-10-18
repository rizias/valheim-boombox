FFmpeg and ffprobe 7.1.5 for Valheim Boombox, Windows x64
Copyright (c) 2000-2026 the FFmpeg developers.
These separate executable tools are licensed under LGPL 2.1 or later;
see LICENSE and the licensing files inside the original source archive.
They are not covered by the mod's MIT license. No music is included.

Source: https://ffmpeg.org/releases/ffmpeg-7.1.5.tar.xz
The original complete source archive and its upstream signature are in Sources.
Source and executable SHA256 values and the exact configure flags are in
the package's TOOLS.json. GPL, nonfree, version3, external autodetection and
network support were disabled. The executables decode local audio to PCM;
they are not general-purpose video encoders.

Rebuild:
1. Use Windows x64, Visual Studio 2019 Build Tools (MSVC 19.29.30159),
   Windows SDK, Git for Windows Bash, GNU make 4.4.1 and NASM 2.16.03.
   Compiler tools are not included. Their upstream sources are:
   https://ftp.gnu.org/gnu/make/make-4.4.1.tar.gz
   https://www.nasm.us/pub/nasm/releasebuilds/2.16.03/
2. Extract ffmpeg-7.1.5.tar.xz beside build-local.ps1 and build-local.sh.
   Place NASM under nasm/nasm-2.16.03 and GNU make's Windows build under
   make-4.4.1/WinRel/gnumake.exe, or adapt the PATH lines in build-local.sh.
3. In Bash, cd ffmpeg-7.1.5 and run:
   patch -p1 < ../library-response-file.patch
   This sole source patch passes library object files via a response file
   to avoid the Windows command-length limit. Codec source is unchanged.
4. Run build-local.ps1 in PowerShell. Adjust the compiler and Git paths if
   installed elsewhere. The script sets up MSVC, runs configure and make,
   and writes ffmpeg-build.log. Results are ffmpeg-7.1.5/ffmpeg.exe and
   ffmpeg-7.1.5/ffprobe.exe. The distribution build used these commands.

You may rebuild, modify and replace these executables. Close the game and
replace the two files in Tools/FFmpeg, or select your own FFmpegPath and
FFprobePath through the shared Mods settings menu. The mod invokes them
as separate processes. Keep this notice, LICENSE and corresponding source
materials with redistributed copies. See the license for its full terms.
