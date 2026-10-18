import argparse
import json
import math
import os
from pathlib import Path
import sys
import tempfile

from ba_bass import analyze
from ba_cache import cached_result, output_lock, signatures
from ba_catalog import build_catalog, find_track
from ba_folders import build_folder_catalog, preserve_track_ids
from ba_common import UnavailableError, atomic_json, load_json
from ba_decode import decode, probe, source_tags, wav_metadata
from ba_sources import embedded_cue, materialize, parse_source


def positive_number(value):
    number = float(value)
    if not math.isfinite(number) or number <= 0:
        raise argparse.ArgumentTypeError("Число должно быть конечным и больше нуля")
    return number


def parser():
    result = argparse.ArgumentParser(description="Музыкальная библиотека папок и подготовка трека для Valheim Boombox")
    commands = result.add_subparsers(dest="command", required=True)
    catalog = commands.add_parser("catalog")
    catalog.add_argument("--foobar-dir", required=True)
    catalog.add_argument("--output", required=True)
    catalog.add_argument("--ffprobe", default="ffprobe")
    catalog.add_argument("--stall-timeout-seconds", type=positive_number, default=60.0)
    scan = commands.add_parser("scan")
    scan.add_argument("--folders-file", required=True)
    scan.add_argument("--output", required=True)
    scan.add_argument("--ffprobe", default="ffprobe")
    scan.add_argument("--stall-timeout-seconds", type=positive_number, default=60.0)
    prepare = commands.add_parser("prepare")
    prepare.add_argument("--catalog", required=True)
    prepare.add_argument("--track", required=True)
    prepare.add_argument("--output-dir", required=True)
    prepare.add_argument("--ffmpeg", default="ffmpeg")
    prepare.add_argument("--ffprobe", default="ffprobe")
    prepare.add_argument("--stall-timeout-seconds", type=positive_number, default=60.0)
    return result


def prepare_track(arguments, output):
    track = find_track(load_json(arguments.catalog), arguments.track)
    cached = cached_result(output, track["id"])
    if cached:
        return cached
    source = parse_source(track["source"])
    with tempfile.TemporaryDirectory(prefix=".prepare-", dir=output) as temporary:
        path, cue = materialize(source, track.get("subsong"), temporary)
        source_files = signatures([source.path] if source.member else [source.path, path])
        original = probe(path, arguments.ffprobe, arguments.stall_timeout_seconds)
        if cue is None and track.get("subsong") not in (None, 0):
            cue = embedded_cue(original, track["subsong"])
        partial = Path(temporary) / "audio.wav"
        decode(path, cue, partial, arguments.ffmpeg, arguments.stall_timeout_seconds)
        metadata = wav_metadata(probe(partial, arguments.ffprobe, arguments.stall_timeout_seconds))
        beats, strengths = analyze(partial, temporary, metadata["durationSeconds"], arguments.ffmpeg, arguments.stall_timeout_seconds)
        if any(not math.isfinite(beat) or beat < 0 or beat >= metadata["durationSeconds"] for beat in beats) or any(a >= b for a, b in zip(beats, beats[1:])):
            raise ValueError("Басовые отметки не соответствуют времени WAV")
        details = {"title": track["title"], "artist": track.get("artist", "")}
        details.update(source_tags(original))
        if cue:
            details.update(title=cue.title, artist=cue.artist)
        destination = output / "audio.wav"
        if source_files != signatures(item["path"] for item in source_files):
            raise RuntimeError("Исходный файл изменился во время подготовки; повторите запрос")
        result = {"version": 1, "status": "ready", "trackId": track["id"], **details, "wavPath": str(destination), **metadata, "bassBeatsSeconds": beats, "bassBeatStrengths": strengths, "cacheVersion": 1, "sourceFiles": source_files, "wavSizeBytes": partial.stat().st_size}
        os.replace(partial, destination)
        atomic_json(output / "result.json", result)
    return result


def run(arguments):
    if arguments.command == "prepare":
        output = Path(arguments.output_dir).resolve()
        output.mkdir(parents=True, exist_ok=True)
        with output_lock(output):
            try:
                return prepare_track(arguments, output)
            except Exception as error:
                unavailable = isinstance(error, (UnavailableError, FileNotFoundError))
                failure = {"version": 1, "status": "unavailable" if unavailable else "error", "trackId": arguments.track, "error": str(error)}
                atomic_json(output / "result.json", failure)
                raise
    catalog = build_folder_catalog(load_json(arguments.folders_file), arguments.ffprobe, arguments.stall_timeout_seconds) if arguments.command == "scan" else build_catalog(arguments.foobar_dir, arguments.ffprobe, arguments.stall_timeout_seconds)
    if arguments.command == "scan" and Path(arguments.output).is_file():
        preserve_track_ids(catalog, load_json(arguments.output))
    atomic_json(arguments.output, catalog)
    return {"version": 1, "status": "ready", "catalogPath": str(Path(arguments.output).resolve()), "playlistCount": len(catalog["playlists"]), "trackCount": sum(len(item["tracks"]) for item in catalog["playlists"]), "unavailableTrackCount": sum(not track["available"] for item in catalog["playlists"] for track in item["tracks"])}


def main():
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    arguments = parser().parse_args()
    try:
        result = run(arguments)
        code = 0
    except Exception as error:
        unavailable = isinstance(error, (UnavailableError, FileNotFoundError))
        code = 2 if unavailable else 1
        result = {"version": 1, "status": "unavailable" if unavailable else "error", "error": str(error)}
        if arguments.command == "prepare":
            result["trackId"] = arguments.track
        print(result["error"], file=sys.stderr)
    print(json.dumps(result, ensure_ascii=False, allow_nan=False, separators=(",", ":")))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
