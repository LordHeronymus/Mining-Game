"""Offline, reversible loudness matching. Source audio and Unity GUIDs stay paired.

Run with a Python environment containing numpy, scipy, soundfile, pyloudnorm,
and imageio-ffmpeg. Analysis and candidate rendering never modify Assets.
"""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys
import re
from concurrent.futures import ThreadPoolExecutor

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Temp/AudioMasteringPython'))
import numpy as np
import soundfile as sf
from scipy import signal
import pyloudnorm as pyln

WORK = ROOT / 'AudioMastering'
CEILING = -1.0  # dBTP, including reconstruction headroom for Unity's decoder.

def digest(path):
    h = hashlib.sha256()
    with open(path, 'rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()

def read_audio(path):
    return sf.read(str(path), dtype='float64', always_2d=True)

def db(value):
    return 20 * math.log10(max(float(value), 1e-12))

def true_peak(audio, factor=4):
    peak = float(np.max(np.abs(audio)))
    # Overlapping chunks retain interpolation context without allocating entire
    # five-minute clips at four times the source sample rate.
    for start in range(0, len(audio), 65536):
        lo, hi = max(0, start - 64), min(len(audio), start + 65536 + 64)
        expanded = signal.resample_poly(audio[lo:hi], factor, 1, axis=0)
        first = (start - lo) * factor
        last = min(len(audio) - start, 65536) * factor + first
        peak = max(peak, float(np.max(np.abs(expanded[first:last]))))
    return db(peak)

def group(entry):
    p = entry['path'].lower()
    name = Path(p).stem
    if '/archive/' in p:
        return 'archive'
    if name in ('homescreenambience', 'gameovermusic', 'cavetribalsong'):
        return 'music'
    if name in ('meadowambience', 'undergroundclay', 'caveambience',
                'windambience', 'ultroniumaltarambience', 'loadingyogaambience'):
        return 'ambience'
    return 'effects'

def measure(path, family):
    audio, rate = read_audio(path)
    if not len(audio) or not np.all(np.isfinite(audio)):
        raise ValueError(f'Invalid audio: {path}')
    # 100 ms gating for short effects avoids diluting impacts with long silence.
    # Music and beds use the standard 400 ms BS.1770 loudness gate.
    block = .4 if family in ('music', 'ambience') else .1
    meter = pyln.Meter(rate, block_size=block)
    padded = audio
    minimum = math.ceil(block * rate)
    if len(audio) < minimum:
        padded = np.pad(audio, ((0, minimum - len(audio)), (0, 0)))
    loudness = float(meter.integrated_loudness(padded))
    peak = true_peak(audio)
    return dict(rate=rate, channels=audio.shape[1], frames=len(audio),
                duration=len(audio) / rate, loudness=loudness if math.isfinite(loudness) else None,
                true_peak=peak, sample_peak=db(np.max(np.abs(audio))),
                clipped_samples=int(np.count_nonzero(np.abs(audio) >= 1)),
                block_seconds=block, subtype=sf.info(str(path)).subtype)

def analyze():
    catalog = json.loads((WORK / 'catalog-before.json').read_text(encoding='utf-8-sig'))
    rows = []
    for i, entry in enumerate(catalog):
        path = ROOT / entry['path']
        row = dict(entry, group=group(entry), sha256=digest(path),
                   meta_sha256=digest(str(path) + '.meta'))
        row['before'] = measure(path, row['group'])
        if row['before']['loudness'] is not None:
            row['maximum_linear_loudness'] = row['before']['loudness'] + CEILING - row['before']['true_peak']
        rows.append(row)
        print(f'{i+1}/{len(catalog)} {path.name}: {row["before"]["loudness"]} LU, {row["before"]["true_peak"]:.2f} dBTP', flush=True)
        (WORK / 'analysis.json').write_text(json.dumps(rows, indent=2, allow_nan=False), encoding='utf-8')

def render():
    rows = json.loads((WORK / 'analysis.json').read_text(encoding='utf-8'))
    backups = WORK / 'Originals'
    for relative in ('Assets/Resources/Gameplay/GpsProfile.asset',
                     'Assets/Resources/Audio/AudioClipTuningSettings.asset'):
        src, dst = ROOT / relative, backups / relative
        if not dst.exists():
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
    for i, row in enumerate(rows):
        source = ROOT / row['path']
        if digest(source) != row['sha256']:
            raise RuntimeError(f'Source changed since analysis: {source}')
        for src in (source, Path(str(source) + '.meta')):
            dst = backups / src.relative_to(ROOT)
            dst.parent.mkdir(parents=True, exist_ok=True)
            if dst.exists() and digest(dst) != digest(src):
                raise RuntimeError(f'Backup mismatch: {dst}')
            if not dst.exists():
                shutil.copy2(src, dst)
        audio, rate = read_audio(source)
        # Independent resamplers disagree slightly on very sharp transients.
        # 0.21 dB additional margin keeps both meters under the -1 dBTP ceiling.
        gain_db = CEILING - .21 - row['before']['true_peak']
        audio *= 10 ** (gain_db / 20)
        relative = Path(row['path']).with_suffix('.wav')
        if relative != Path(row['path']) and (ROOT / relative).exists():
            raise RuntimeError(f'WAV destination already exists: {relative}')
        candidate = WORK / 'Candidates' / relative
        candidate.parent.mkdir(parents=True, exist_ok=True)
        sf.write(str(candidate), audio, rate, subtype='PCM_24')
        after = measure(candidate, row['group'])
        if after['true_peak'] > CEILING or after['clipped_samples']:
            raise RuntimeError(f'Candidate exceeds peak ceiling: {candidate}')
        if any(after[field] != row['before'][field] for field in ('rate', 'channels', 'frames')):
            raise RuntimeError(f'Candidate timing changed: {candidate}')
        row.update(destination=relative.as_posix(), candidate=candidate.relative_to(ROOT).as_posix(),
                   after=after, gain_db=gain_db, output_sha256=digest(candidate))
        print(f'{i+1}/{len(rows)} {source.name}: {gain_db:+.2f} dB -> {after["true_peak"]:.3f} dBTP', flush=True)
        (WORK / 'rendered.json').write_text(json.dumps(rows, indent=2, allow_nan=False), encoding='utf-8')
    # Highest common *nominal* loudness reachable without clipping or changing
    # waveform dynamics. Reserve the full positive volume-spread excursion.
    targets = {}
    for family in {row['group'] for row in rows}:
        targets[family] = min(row['after']['loudness'] - db(1 + row['volumeSpread'])
                              for row in rows if row['group'] == family and row['after']['loudness'] is not None)
    for row in rows:
        target = targets[row['group']]
        nominal = row['after']['loudness']
        volume = min(1 / (1 + row['volumeSpread']), 10 ** ((target - nominal) / 20)) if nominal is not None else 0
        # A deliberately muted clip stays muted. Nonzero clip faders are replaced
        # by the new measured balance; category/mixer levels are not altered.
        row['new_volume'] = volume if row['volume'] > 0 else 0
        row['target_loudness'] = target
        row['balanced_loudness'] = nominal + db(volume) if volume else None
    manifest = dict(ceiling_dbtp=CEILING, targets=targets, clips=rows)
    (WORK / 'manifest.json').write_text(json.dumps(manifest, indent=2, allow_nan=False), encoding='utf-8')
    with open(WORK / 'report.csv', 'w', newline='', encoding='utf-8-sig') as stream:
        writer = csv.writer(stream)
        writer.writerow(['Clip', 'Group', 'Source', 'Output', 'Gain dB', 'Before loudness',
                         'Before dBTP', 'After loudness', 'After dBTP', 'GPS volume %',
                         'Balanced loudness', 'Original full-scale samples'])
        for row in rows:
            writer.writerow([row['name'], row['group'], row['path'], row['destination'],
                             row['gain_db'], row['before']['loudness'], row['before']['true_peak'],
                             row['after']['loudness'], row['after']['true_peak'], row['new_volume'] * 100,
                             row['balanced_loudness'], row['before']['clipped_samples']])
    print(json.dumps({'targets': targets, 'clips': len(rows)}, indent=2), flush=True)

def verify():
    import imageio_ffmpeg
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    manifest = json.loads((WORK / 'manifest.json').read_text(encoding='utf-8'))
    def check(row):
        path = ROOT / row['candidate']
        result = subprocess.run([ffmpeg, '-hide_banner', '-nostdin', '-i', str(path),
                                 '-af', 'ebur128=peak=true', '-f', 'null', '-'],
                                capture_output=True, text=True, check=True)
        match = re.findall(r'Peak:\s*([-\d.]+) dBFS', result.stderr)
        if not match:
            raise RuntimeError('FFmpeg did not report a true peak: ' + str(path))
        independent = float(match[-1])
        if independent > -1.0:  # FFmpeg prints only one decimal place.
            raise RuntimeError(f'Independent peak check failed: {path}: {independent}')
        original, sr = read_audio(WORK / 'Originals' / row['path'])
        candidate, _ = read_audio(path)
        error = float(np.max(np.abs(candidate - original * 10 ** (row['gain_db'] / 20))))
        if error > 1.3e-7:
            raise RuntimeError(f'Waveform changed beyond 24-bit quantization: {path}: {error}')
        if digest(path) != row['output_sha256']:
            raise RuntimeError('Candidate changed: ' + str(path))
        return dict(clip=row['name'], ffmpeg_true_peak=independent,
                    max_waveform_error=error, hash_ok=True, timing_ok=True)
    with ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(check, manifest['clips']))
    (WORK / 'verification.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps({'verified': len(results), 'worst_ffmpeg_true_peak': max(r['ffmpeg_true_peak'] for r in results),
                      'maximum_waveform_error': max(r['max_waveform_error'] for r in results)}, indent=2))

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=['analyze', 'render', 'verify'])
    args = parser.parse_args()
    {'analyze': analyze, 'render': render, 'verify': verify}[args.command]()
