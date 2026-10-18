from datetime import datetime, timezone
import hashlib
from pathlib import Path, PurePosixPath

from ba_common import UnavailableError, require_file
from ba_decode import probe
from ba_sources import embedded_cue, parse_source, parse_cue, select_cue

def split_entry(line):
    source, separator, subsong = line.rpartition("\t")
    if separator:
        return source, int(subsong)
    return line, None


def track_id(source, subsong):
    value = source + "\t" + (str(subsong) if subsong is not None else "")
    return hashlib.sha256(value.encode("utf-8")).hexdigest()[:32]


def catalog_track(line, cue_cache, ffprobe, timeout):
    try:
        raw, subsong = split_entry(line)
    except ValueError:
        raw, subsong = line, None
    item = {"id": track_id(raw, subsong), "title": raw, "artist": "", "source": raw, "subsong": subsong}
    try:
        source = parse_source(raw)
        item["title"] = PurePosixPath(source.member).stem if source.member else source.path.stem
        if source.member:
            item["archiveMember"] = source.member
        require_file(source.path)
        if not source.member and source.path.suffix.lower() == ".cue":
            key = str(source.path)
            if key not in cue_cache:
                try:
                    cue_cache[key] = parse_cue(source.path.read_bytes())
                except (OSError, UnicodeError, ValueError) as error:
                    cue_cache[key] = error
            tracks = cue_cache[key]
            if isinstance(tracks, Exception):
                raise tracks
            cue = select_cue(tracks, subsong)
            item.update(title=cue.title, artist=cue.artist)
            require_file(source.path.parent / cue.file)
            if cue.end is not None:
                item["durationSeconds"] = cue.end - cue.start
        elif not source.member and subsong not in (None, 0):
            key = str(source.path)
            if key not in cue_cache:
                try:
                    cue_cache[key] = probe(source.path, ffprobe, timeout)
                except Exception as error:
                    cue_cache[key] = error
            data = cue_cache[key]
            if isinstance(data, Exception):
                raise data
            cue = embedded_cue(data, subsong)
            item.update(title=cue.title, artist=cue.artist)
            if cue.end is not None:
                item["durationSeconds"] = cue.end - cue.start
        item["available"] = True
    except Exception as error:
        item.update(available=False, error=str(error))
    return item


def build_catalog(foobar_dir, ffprobe="ffprobe", timeout=60.0):
    root = Path(foobar_dir)
    folder = root if root.name == "playlists-v2.0" else root / "playlists-v2.0"
    index = require_file(folder / "index.txt").read_text(encoding="utf-8-sig")
    playlists = []
    cue_cache = {}
    for line in index.splitlines():
        if not line.strip():
            continue
        identifier, separator, name = line.partition(":")
        if not separator:
            raise ValueError(f"Некорректная строка индекса foobar: {line}")
        if Path(identifier).name != identifier or any(char in identifier for char in "\\/:"):
            raise ValueError("Некорректный идентификатор плейлиста")
        playlist = {"id": identifier, "name": name, "tracks": []}
        try:
            text = require_file(folder / f"playlist-{identifier}.fplite").read_text(encoding="utf-8-sig")
            playlist["tracks"] = [catalog_track(entry, cue_cache, ffprobe, timeout) for entry in text.splitlines() if entry]
        except Exception as error:
            playlist["error"] = str(error)
        playlists.append(playlist)
    return {"version": 1, "generatedAt": datetime.now(timezone.utc).isoformat(), "foobarDirectory": str(root.resolve()), "playlists": playlists}


def find_track(catalog, identifier):
    identifier = catalog.get("aliases", {}).get(identifier, identifier)
    for playlist in catalog["playlists"]:
        for track in playlist["tracks"]:
            if track["id"] == identifier:
                return track
    raise UnavailableError(f"Трек отсутствует в каталоге: {identifier}")
