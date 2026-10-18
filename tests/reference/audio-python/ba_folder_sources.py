from pathlib import PurePosixPath
import posixpath
import struct
import zipfile

from ba_catalog import catalog_track, track_id
from ba_decode import probe, source_tags
from ba_sources import parse_cue, safe_member, zip_info


AUDIO_EXTENSIONS = {".flac", ".mp3", ".wav", ".ogg", ".ape", ".wv", ".m4a", ".aac", ".opus", ".wma"}


def flac_tags(stream):
    if stream.read(4) != b"fLaC":
        return {}
    tags = {}
    while True:
        header = stream.read(4)
        if len(header) != 4:
            raise ValueError("Оборван заголовок FLAC")
        length = int.from_bytes(header[1:], "big")
        if header[0] & 127 == 4:
            data = stream.read(length)
            offset = 4 + struct.unpack_from("<I", data)[0]
            count = struct.unpack_from("<I", data, offset)[0]
            offset += 4
            for _ in range(count):
                size = struct.unpack_from("<I", data, offset)[0]
                offset += 4
                value = data[offset:offset + size].decode("utf-8", errors="replace")
                offset += size
                key, separator, text = value.partition("=")
                if separator:
                    tags[key.casefold()] = text
        else:
            stream.seek(length, 1)
        if header[0] & 128:
            return tags


def tagged_tracks(raw, tags, cache, ffprobe, timeout):
    base = catalog_track(raw, cache, ffprobe, timeout)
    for name in ("title", "artist"):
        if tags.get(name):
            base[name] = tags[name]
    if not tags.get("cuesheet"):
        return [base]
    entries = parse_cue(tags["cuesheet"].encode("utf-8"))
    if len({entry.file.casefold() for entry in entries}) != 1:
        raise ValueError("Неоднозначный встроенный CUE с несколькими FILE")
    result = []
    for entry in entries:
        item = dict(base, id=track_id(raw, entry.number), subsong=entry.number, title=entry.title, artist=entry.artist or base["artist"])
        if entry.end is not None:
            item["durationSeconds"] = entry.end - entry.start
        result.append(item)
    return result


def file_tracks(path, cache, ffprobe, timeout):
    raw = "file://" + str(path)
    tags = {}
    if path.suffix.lower() == ".flac":
        with path.open("rb") as stream:
            tags = flac_tags(stream)
    elif path.suffix.lower() in {".ape", ".wv"}:
        metadata = probe(path, ffprobe, timeout)
        tags = source_tags(metadata)
        for section in [metadata.get("format", {}), *metadata.get("streams", [])]:
            tags.update({key.casefold(): value for key, value in section.get("tags", {}).items() if key.casefold() == "cuesheet"})
    return tagged_tracks(raw, tags, cache, ffprobe, timeout)


def archive_tracks(path, cache, ffprobe, timeout):
    result, errors, covered = [], [], set()
    with zipfile.ZipFile(path) as archive:
        names = []
        for info in archive.infolist():
            if info.is_dir():
                continue
            try:
                names.append(safe_member(zip_info(archive, info.filename).filename))
            except ValueError as error:
                errors.append({"path": str(path), "error": str(error)})
        def raw(member):
            return "unpack://zip|0|file://" + str(path) + "|" + member
        for name in names:
            if PurePosixPath(name).suffix.lower() != ".cue":
                continue
            try:
                for entry in parse_cue(archive.read(zip_info(archive, name))):
                    member = safe_member(posixpath.join(posixpath.dirname(name), entry.file.replace("\\", "/")))
                    item = catalog_track(raw(name) + "\t" + str(entry.number), cache, ffprobe, timeout)
                    item.update(title=entry.title, artist=entry.artist)
                    try:
                        zip_info(archive, member)
                        covered.add(member.casefold())
                    except Exception as error:
                        item.update(available=False, error=str(error))
                    if entry.end is not None:
                        item["durationSeconds"] = entry.end - entry.start
                    result.append(item)
            except Exception as error:
                errors.append({"path": str(path) + "|" + name, "error": str(error)})
        for name in names:
            suffix = PurePosixPath(name).suffix.lower()
            if suffix not in AUDIO_EXTENSIONS or name.casefold() in covered:
                continue
            try:
                tags = {}
                if suffix == ".flac":
                    with archive.open(zip_info(archive, name)) as stream:
                        tags = flac_tags(stream)
                result.extend(tagged_tracks(raw(name), tags, cache, ffprobe, timeout))
            except Exception as error:
                errors.append({"path": str(path) + "|" + name, "error": str(error)})
    return result, errors
