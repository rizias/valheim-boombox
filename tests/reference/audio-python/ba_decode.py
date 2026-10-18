import json
from pathlib import Path
import queue
import re
import shutil
import subprocess
import tempfile
import threading
import time


def process_options():
    if hasattr(subprocess, "CREATE_NO_WINDOW"):
        return {"creationflags": subprocess.CREATE_NO_WINDOW}
    return {}


def executable_path(value):
    resolved = shutil.which(value)
    if not resolved:
        raise RuntimeError(f"Исполняемый файл не найден: {value}")
    shim = Path(resolved).with_suffix(".shim")
    if shim.is_file():
        match = re.search(r'^path\s*=\s*"([^"]+)"\s*$', shim.read_text(encoding="utf-8-sig"), re.MULTILINE)
        if match and Path(match.group(1)).is_file():
            return match.group(1)
    return resolved


def probe(path, executable, timeout):
    command = [executable_path(executable), "-v", "error", "-show_format", "-show_streams", "-of", "json", str(path)]
    try:
        result = subprocess.run(command, capture_output=True, timeout=timeout, **process_options())
    except subprocess.TimeoutExpired:
        raise RuntimeError(f"ffprobe не завершил чтение заголовка за {timeout:g} секунд: {path}") from None
    if result.returncode:
        raise RuntimeError("ffprobe: " + result.stderr.decode("utf-8", errors="replace").strip())
    data = json.loads(result.stdout.decode("utf-8"))
    if not any(stream.get("codec_type") == "audio" for stream in data.get("streams", [])):
        raise ValueError(f"В файле нет аудиопотока: {path}")
    return data


def run_ffmpeg(arguments, executable, timeout, directory):
    command = [executable_path(executable), "-hide_banner", "-nostdin", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-stats_period", "1", *arguments]
    updates = queue.Queue()
    with tempfile.TemporaryFile(dir=directory) as errors:
        process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=errors, **process_options())

        def read_progress():
            for line in process.stdout:
                updates.put(line)

        reader = threading.Thread(target=read_progress, daemon=True)
        reader.start()
        advanced_at = time.monotonic()
        largest_position = -1
        try:
            while True:
                try:
                    line = updates.get(timeout=0.2).decode("ascii", errors="replace").strip()
                    key, _, value = line.partition("=")
                    if key in ("out_time_us", "out_time_ms") and value.lstrip("-").isdigit():
                        position = int(value)
                        if position > largest_position:
                            largest_position = position
                            advanced_at = time.monotonic()
                except queue.Empty:
                    pass
                if process.poll() is not None:
                    break
                if time.monotonic() - advanced_at > timeout:
                    raise RuntimeError(f"ffmpeg не продвигается {timeout:g} секунд; обработка остановлена")
            reader.join(timeout=2)
            if process.returncode:
                errors.seek(0, 2)
                size = errors.tell()
                errors.seek(max(0, size - 16384))
                message = errors.read().decode("utf-8", errors="replace").strip()
                raise RuntimeError(f"ffmpeg завершился с кодом {process.returncode}: {message}")
        finally:
            if process.poll() is None:
                process.kill()
            process.wait()
            reader.join(timeout=2)
            process.stdout.close()


def decode(source, cue, destination, executable, timeout):
    arguments = ["-y"]
    if cue:
        arguments.extend(["-ss", f"{cue.start:.9f}"])
    arguments.extend(["-i", str(source)])
    if cue and cue.end is not None:
        arguments.extend(["-t", f"{cue.end - cue.start:.9f}"])
    arguments.extend(["-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "-1", "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-rf64", "auto", "-f", "wav", str(destination)])
    run_ffmpeg(arguments, executable, timeout, Path(destination).parent)


def wav_metadata(data):
    stream = next(item for item in data["streams"] if item.get("codec_type") == "audio")
    duration = float(stream.get("duration") or data["format"]["duration"])
    if stream.get("codec_name") != "pcm_s16le" or int(stream["channels"]) != 2 or int(stream["sample_rate"]) != 48000 or duration <= 0:
        raise ValueError("Подготовленный WAV не соответствует PCM16 stereo 48000 Hz с ненулевой длительностью")
    return {"durationSeconds": duration, "sampleRate": 48000, "channels": 2, "bitsPerSample": 16}


def source_tags(data):
    tags = {}
    for stream in data.get("streams", []):
        if stream.get("codec_type") == "audio":
            tags.update({key.lower(): str(value) for key, value in stream.get("tags", {}).items()})
            break
    tags.update({key.lower(): str(value) for key, value in data.get("format", {}).get("tags", {}).items()})
    return {key: tags[key] for key in ("title", "artist") if tags.get(key)}
