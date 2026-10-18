from datetime import datetime, timezone
import os
from pathlib import Path

from ba_catalog import catalog_track
from ba_sources import parse_cue, parse_source
from ba_folder_sources import AUDIO_EXTENSIONS, file_tracks, archive_tracks


def build_folder_catalog(folders, ffprobe="ffprobe", timeout=60.0):
    if isinstance(folders, dict):
        folders = folders.get("folders", [])
    if not isinstance(folders, list):
        raise ValueError("Список музыкальных папок должен быть массивом")
    roots = []
    files = {}
    errors = []
    for value in folders:
        if not isinstance(value, str) or not value.strip():
            errors.append({"path": str(value), "error": "Пустой или некорректный путь папки"})
            continue
        root = Path(os.path.expandvars(str(value))).expanduser().resolve()
        if str(root).casefold() in {str(existing).casefold() for existing in roots}:
            continue
        roots.append(root)
        if not root.is_dir():
            errors.append({"path": str(root), "error": "Папка отсутствует или недоступна"})
            continue
        def on_error(error):
            errors.append({"path": error.filename, "error": str(error)})
        for directory, directories, names in os.walk(root, onerror=on_error, followlinks=False):
            directories[:] = sorted(name for name in directories if not (Path(directory) / name).is_symlink() and not getattr(os.path, "isjunction", lambda _: False)(Path(directory) / name))
            for name in sorted(names):
                path = Path(directory) / name
                if path.suffix.lower() in AUDIO_EXTENSIONS | {".cue", ".zip"}:
                    files.setdefault(str(path.resolve()).casefold(), path.resolve())
    tracks = []
    covered = set()
    cue_cache = {}
    for path in files.values():
        if path.suffix.lower() != ".cue":
            continue
        raw = "file://" + str(path)
        try:
            entries = parse_cue(path.read_bytes())
            for entry in entries:
                tracks.append(catalog_track(raw + "\t" + str(entry.number), cue_cache, ffprobe, timeout))
                covered.add(str((path.parent / entry.file).resolve()).casefold())
        except Exception as error:
            errors.append({"path": str(path), "error": str(error)})
    for key, path in files.items():
        if key in covered or path.suffix.lower() == ".cue":
            continue
        try:
            if path.suffix.lower() == ".zip":
                archive, archive_errors = archive_tracks(path, cue_cache, ffprobe, timeout)
                tracks.extend(archive)
                errors.extend(archive_errors)
            else:
                tracks.extend(file_tracks(path, cue_cache, ffprobe, timeout))
        except Exception as error:
            item = catalog_track("file://" + str(path), cue_cache, ffprobe, timeout)
            item.update(available=False, error=str(error))
            tracks.append(item)
    unique = {track["id"]: track for track in tracks}
    ordered = sorted(unique.values(), key=lambda track: (track.get("artist", "").casefold(), track["title"].casefold(), track["id"]))
    return {"version": 2, "generatedAt": datetime.now(timezone.utc).isoformat(), "folders": [str(root) for root in roots],
            "errors": errors, "playlists": [{"id": "local-library", "name": "Музыкальная библиотека", "tracks": ordered}]}


def preserve_track_ids(catalog, previous):
    def key(track):
        source = parse_source(track["source"])
        return (str(source.path.resolve()).casefold(), source.member or "", track.get("subsong"))
    originals = {}
    aliases = {}
    for playlist in previous.get("playlists", []):
        for track in playlist.get("tracks", []):
            try:
                originals.setdefault(key(track), []).append(track)
            except (KeyError, ValueError, OSError):
                continue
    for track in catalog["playlists"][0]["tracks"]:
        old = originals.get(key(track), [])
        if old:
            generated = track["id"]
            track["id"] = old[0]["id"]
            if generated != track["id"]:
                aliases[generated] = track["id"]
            for entry in old[1:]:
                if entry["id"] != track["id"]:
                    aliases[entry["id"]] = track["id"]
    ids = {track["id"] for track in catalog["playlists"][0]["tracks"]}
    for alias, target in previous.get("aliases", {}).items():
        if target in ids:
            aliases[alias] = target
    catalog["aliases"] = aliases
    return catalog
