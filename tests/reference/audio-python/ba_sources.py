from dataclasses import dataclass
from pathlib import Path, PurePosixPath, PureWindowsPath
import posixpath
import re
import shutil
import stat
from urllib.parse import unquote
import zipfile

from ba_common import UnavailableError, require_file


@dataclass
class Source:
    path: Path
    member: str | None = None


@dataclass
class CueTrack:
    number: int
    file: str
    title: str = ""
    artist: str = ""
    start: float | None = None
    end: float | None = None


def local_path(uri):
    if not uri.startswith("file://"):
        raise ValueError(f"Не поддерживается источник: {uri}")
    value = uri[7:]
    if value.startswith("/") and re.match(r"/[A-Za-z]:", value):
        value = value[1:]
    raw = Path(value)
    if raw.exists():
        return raw
    return Path(unquote(value))


def parse_source(value):
    if value.startswith("unpack://zip|"):
        parts = value.split("|", 3)
        if len(parts) != 4:
            raise ValueError("Некорректный источник ZIP")
        return Source(local_path(parts[2]), safe_member(parts[3]))
    return Source(local_path(value))


def safe_member(value):
    value = value.replace("\\", "/")
    path = PurePosixPath(value)
    if not value or path.is_absolute() or PureWindowsPath(value).drive:
        raise ValueError(f"Недопустимый путь внутри ZIP: {value}")
    if ".." in path.parts or any(":" in part for part in path.parts) or "\0" in value:
        raise ValueError(f"Недопустимый путь внутри ZIP: {value}")
    return str(path)


def decode_cue(data):
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16")
    try:
        return data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return data.decode("cp1251")


def unquote_cue(value):
    value = value.strip()
    if value.startswith('"') and value.endswith('"'):
        return value[1:-1]
    return value


def cue_seconds(value):
    match = re.fullmatch(r"(\d+):(\d{2}):(\d{2})", value)
    if not match:
        raise ValueError(f"Некорректное время CUE: {value}")
    minutes, seconds, frames = map(int, match.groups())
    if seconds >= 60 or frames >= 75:
        raise ValueError(f"Некорректное время CUE: {value}")
    return minutes * 60 + seconds + frames / 75


def parse_cue(data):
    tracks = []
    filename = ""
    album = ""
    artist = ""
    current = None
    for line in decode_cue(data).splitlines():
        parts = line.strip().split(None, 1)
        if len(parts) != 2:
            continue
        command, value = parts[0].upper(), parts[1]
        if command == "FILE":
            match = re.fullmatch(r'(?:"([^"]+)"|(.+?))\s+\S+', value)
            if not match:
                raise ValueError(f"Некорректная строка FILE в CUE: {value}")
            filename = match.group(1) or match.group(2)
        elif command == "TRACK":
            number, kind = value.split()
            current = CueTrack(int(number), filename, artist=artist)
            if kind.upper() != "AUDIO":
                raise ValueError("CUE содержит неаудиотрек")
            tracks.append(current)
        elif command in ("TITLE", "PERFORMER"):
            text = unquote_cue(value)
            if current is None:
                if command == "TITLE":
                    album = text
                else:
                    artist = text
            elif command == "TITLE":
                current.title = text
            else:
                current.artist = text
        elif command == "INDEX" and current is not None:
            index, position = value.split()
            if index == "01":
                current.start = cue_seconds(position)
    if not tracks:
        raise ValueError("В CUE нет аудиотреков")
    for index, track in enumerate(tracks):
        if not track.file or track.start is None:
            raise ValueError(f"У CUE TRACK {track.number} нет FILE или INDEX 01")
        if not track.title:
            track.title = f"{album or Path(track.file).stem} — Track {track.number:02d}"
        if index + 1 < len(tracks) and tracks[index + 1].file == track.file:
            track.end = tracks[index + 1].start
            if track.end is None or track.end <= track.start:
                raise ValueError(f"Некорректная граница CUE TRACK {track.number}")
    return tracks


def select_cue(tracks, subsong):
    if subsong is None:
        raise ValueError("Для CUE необходим номер подтрека subsong")
    for track in tracks:
        if track.number == subsong:
            return track
    raise UnavailableError(f"В CUE нет TRACK {subsong}")


def read_cue(path, subsong):
    return select_cue(parse_cue(require_file(path).read_bytes()), subsong)


def embedded_cue(metadata, subsong):
    text = None
    for section in [metadata.get("format", {}), *metadata.get("streams", [])]:
        for key, value in section.get("tags", {}).items():
            if key.lower() == "cuesheet":
                text = value
    if not text:
        raise UnavailableError(f"В аудиофайле нет текстового CUESHEET для подтрека {subsong}")
    tracks = parse_cue(text.encode("utf-8"))
    if len({track.file.casefold() for track in tracks}) != 1:
        raise ValueError("Встроенный CUESHEET с несколькими FILE неоднозначен")
    return select_cue(tracks, subsong)


def zip_info(archive, member):
    try:
        info = archive.getinfo(member)
    except KeyError:
        matches = [entry for entry in archive.infolist() if entry.filename.replace("\\", "/") == member]
        if len(matches) != 1:
            raise UnavailableError(f"В ZIP отсутствует файл: {member}") from None
        info = matches[0]
    safe_member(info.filename)
    if info.is_dir() or stat.S_ISLNK(info.external_attr >> 16):
        raise ValueError(f"ZIP-участник не является обычным файлом: {member}")
    return info


def extract_member(archive, member, directory, label):
    info = zip_info(archive, safe_member(member))
    destination = Path(directory) / (label + PurePosixPath(member).suffix)
    with archive.open(info) as source, destination.open("wb") as target:
        shutil.copyfileobj(source, target, length=1024 * 1024)
    return destination


def materialize(source, subsong, directory):
    require_file(source.path)
    if source.member is None:
        if source.path.suffix.lower() != ".cue":
            return source.path, None
        cue = read_cue(source.path, subsong)
        return require_file(source.path.parent / cue.file), cue
    with zipfile.ZipFile(source.path) as archive:
        if PurePosixPath(source.member).suffix.lower() != ".cue":
            return extract_member(archive, source.member, directory, "source"), None
        cue = select_cue(parse_cue(archive.read(zip_info(archive, source.member))), subsong)
        safe_member(cue.file)
        member = safe_member(posixpath.join(posixpath.dirname(source.member), cue.file.replace("\\", "/")))
        return extract_member(archive, member, directory, "source"), cue
