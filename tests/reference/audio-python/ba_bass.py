from array import array
from collections import deque
import math
from pathlib import Path
import sys

from ba_decode import run_ffmpeg


def energy_envelope(path):
    energies = array("d")
    history = deque()
    total = 0.0
    with Path(path).open("rb") as stream:
        while True:
            block = stream.read(80)
            if not block:
                break
            samples = array("f")
            samples.frombytes(block)
            if sys.byteorder != "little":
                samples.byteswap()
            energy = sum(value * value for value in samples) / len(samples)
            history.append(energy)
            total += energy
            if len(history) > 4:
                total -= history.popleft()
            energies.append(max(0.0, total / len(history)))
    return energies


def prefix_sum(values):
    result = array("d", [0.0])
    total = 0.0
    for value in values:
        total += value
        result.append(total)
    return result


def detect_impulses(energies, duration):
    if not energies:
        return [], []
    integrated = prefix_sum(energies)
    flux = array("d")
    for index, energy in enumerate(energies):
        left = max(0, index - 10)
        previous = (integrated[index] - integrated[left]) / max(1, index - left)
        flux.append(max(0.0, energy - previous))
    flux_sum = prefix_sum(flux)
    flux_squares = prefix_sum(value * value for value in flux)
    candidates = []
    for index, energy in enumerate(energies):
        left, right = max(0, index - 50), min(len(energies), index + 51)
        count = right - left
        background = (integrated[right] - integrated[left]) / count
        mean_flux = (flux_sum[right] - flux_sum[left]) / count
        variance = max(0.0, (flux_squares[right] - flux_squares[left]) / count - mean_flux * mean_flux)
        novelty = max(flux[max(0, index - 5):index + 1], default=0)
        if energy < 1e-9 or energy < background * 1.35:
            continue
        if novelty < mean_flux + math.sqrt(variance) * 0.8:
            continue
        if energy < max(energies[max(0, index - 5):min(len(energies), index + 6)]):
            continue
        timestamp = max(0.0, (index + 1) * 0.01 - 0.025)
        if timestamp >= duration:
            continue
        strength = math.sqrt(energy) * novelty / max(background, 1e-12)
        if candidates and timestamp - candidates[-1][0] < 0.18:
            if strength > candidates[-1][1]:
                candidates[-1] = (timestamp, strength)
        else:
            candidates.append((timestamp, strength))
    peak = max((strength for _, strength in candidates), default=1.0)
    return [round(timestamp, 6) for timestamp, _ in candidates], [round(strength / peak, 6) for _, strength in candidates]


def analyze(wav, directory, duration, executable, timeout):
    filtered = Path(directory) / "bass.f32"
    arguments = ["-y", "-i", str(wav), "-map", "0:a:0", "-vn", "-af", "highpass=f=35,lowpass=f=180", "-ac", "1", "-ar", "2000", "-c:a", "pcm_f32le", "-f", "f32le", str(filtered)]
    run_ffmpeg(arguments, executable, timeout, directory)
    return detect_impulses(energy_envelope(filtered), duration)
