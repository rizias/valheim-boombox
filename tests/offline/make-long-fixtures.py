import os
import hashlib
import json
import subprocess
import sys

import numpy as np

rate = 48000
ffmpeg = sys.argv[1]
target = sys.argv[2]
os.makedirs(target, exist_ok=True)


def marks(seconds):
    found = []
    at = np.float32(0.2)
    gap = 0
    while at < np.float32(seconds - 1):
        found.append(float(at))
        at = np.float32(at + np.float32(np.float32(0.35) + np.float32(0.12) * np.float32(gap % 5)))
        gap += 1
    return found


def pulses(seconds):
    tail = np.zeros(seconds * rate)
    length = int(rate * 0.12)
    i = np.arange(length)
    burst = np.sin(2 * np.pi * 60 * i / rate) * 28000 * np.exp(-i / (rate * 0.04)) / 32768
    for mark in marks(seconds):
        start = int(np.float32(mark) * rate)
        end = min(start + length, len(tail))
        tail[start:end] = burst[: end - start]
    return tail


def raw(signal, name):
    path = os.path.join(target, name)
    np.repeat(np.clip(np.round(signal * 32767), -32768, 32767).astype('<i2'), 2).tofile(path)
    return path


def encode(arguments, out):
    receipt = out + '.encode.json'
    if os.path.exists(out):
        if not os.path.exists(receipt):
            raise ValueError('Existing fixture has no verified encode receipt; preserved unchanged: ' + out)
        with open(receipt, encoding='utf-8') as stream:
            expected = json.load(stream)
        with open(out, 'rb') as stream:
            actual = hashlib.file_digest(stream, 'sha256').hexdigest()
        if expected != {'arguments': arguments, 'sha256': actual}:
            raise ValueError('Existing fixture differs from encode receipt; preserved unchanged: ' + out)
        return
    subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-n'] + arguments + [out], check=True)
    with open(out, 'rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    with open(receipt, 'x', encoding='utf-8') as stream:
        json.dump({'arguments': arguments, 'sha256': digest}, stream)


t = np.arange(14 * rate) / rate
active = raw(np.concatenate([0.5 * np.sin(2 * np.pi * (300 * t + 15 * t * t)), pulses(28)]), 'long-active.raw')
for name, codec in (('long.flac', 'flac'), ('long.wv', 'wavpack')):
    encode(['-f', 'lavfi', '-i', f'anullsrc=r={rate}:cl=stereo:d=11178', '-f', 's16le', '-ar', str(rate), '-ac', '2', '-i', active,
            '-filter_complex', '[0][1]concat=n=2:v=0:a=1,aformat=sample_fmts=s16', '-c:a', codec], os.path.join(target, name))
noise = np.random.default_rng(7).uniform(-0.5, 0.5, 2 * rate)
shorter = raw(np.concatenate([noise, pulses(20)]), 'longer-than-probed.raw')
encode(['-f', 's16le', '-ar', str(rate), '-ac', '2', '-i', shorter, '-c:a', 'libmp3lame', '-q:a', '0', '-write_xing', '0', '-id3v2_version', '0'],
       os.path.join(target, 'longer-than-probed.mp3'))
for path in (active, shorter):
    os.remove(path)
