from contextlib import contextmanager
import os
from pathlib import Path
import time

from ba_common import load_json


@contextmanager
def output_lock(directory):
    path = Path(directory) / ".prepare.lock"
    with path.open("a+b") as stream:
        stream.seek(0, 2)
        if stream.tell() == 0:
            stream.write(b"0")
            stream.flush()
        if os.name == "nt":
            import msvcrt
            while True:
                try:
                    stream.seek(0)
                    msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
                    break
                except OSError:
                    time.sleep(0.1)
            try:
                yield
            finally:
                stream.seek(0)
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX)
            try:
                yield
            finally:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


def signatures(paths):
    result = []
    for path in dict.fromkeys(str(Path(path).resolve()) for path in paths):
        info = Path(path).stat()
        result.append({"path": path, "sizeBytes": info.st_size, "mtimeNs": info.st_mtime_ns})
    return result


def cached_result(directory, track_id):
    try:
        result = load_json(Path(directory) / "result.json")
        if result.get("status") != "ready" or result.get("trackId") != track_id or result.get("cacheVersion") != 1:
            return None
        if Path(result["wavPath"]).resolve().parent != Path(directory).resolve():
            return None
        if Path(result["wavPath"]).stat().st_size != result["wavSizeBytes"]:
            return None
        if signatures(item["path"] for item in result["sourceFiles"]) != result["sourceFiles"]:
            return None
        return result
    except (OSError, ValueError, KeyError, TypeError):
        return None
