"""Conservative offline checks of common overlaps using Unity-decoded samples."""
import json
import math
from pathlib import Path
import audio_mastering as m

manifest = json.loads((m.WORK / 'manifest.json').read_text(encoding='utf-8'))
imports = {r['guid']: r for r in json.loads((m.WORK / 'unity-decoded.json').read_text())}
tunings = {r['guid']: r for r in json.loads((m.WORK / 'gps-after.json').read_text())}
rows = {Path(r['destination']).stem: r for r in manifest['clips']}
cache = {}
RATE, SECONDS = 48000, 8

def audio(name, pitch_random=0):
    key = (name, pitch_random)
    if key in cache:
        return cache[key]
    r = rows[name]
    info, tuning = imports[r['guid']], tunings[r['guid']]
    a = m.np.fromfile(m.ROOT / ('Temp/AudioMasteringDecoded/' + r['guid'] + '.f32'), dtype='<f4').reshape(-1, info['channels'])
    sr = info['rate']
    if len(a) > sr * SECONDS:
        # Use the loudest one-second region and its following tail as the bed.
        seconds = len(a) // sr
        energies = m.np.mean(a[:seconds * sr].reshape(seconds, sr, -1).astype('float64') ** 2, axis=(1, 2))
        start = min(int(m.np.argmax(energies)) * sr, len(a) - sr * SECONDS)
        a = a[start:start + sr * SECONDS]
    pitch = tuning['pitch'] * (1 + tuning['pitchSpread'] * pitch_random)
    ratio = RATE / (sr * pitch)
    positions = m.np.arange(min(round(len(a) * ratio), RATE * SECONDS)) / ratio
    # Match Unity's source speed; same phase offset across stereo channels.
    a = m.np.stack([m.np.interp(positions, m.np.arange(len(a)), a[:, c]) for c in range(a.shape[1])], axis=1)
    if a.shape[1] == 1:
        a = m.np.repeat(a, 2, axis=1)
    a *= tuning['volume'] * (1 + tuning['volumeSpread'])
    cache[key] = a
    return a

scenarios = []
for i in range(1, 7):
    scenarios.append((f'Dirt hit {i} + break + underground', [(f'DirtHit_{i:02}', 0), ('ClayBreak_L1L2', 0), ('UndergroundClay', 0)]))
scenarios += [
    ('Stone hit + break + cave', [('PickaxeStoneHitL3Plus', 0), ('StoneBreak_L3', 0), ('CaveAmbience', 0)]),
    ('Ore hit + break + cave', [('PickaxeHitL3Plus', 0), ('OreBreakV3', 0), ('CaveAmbience', 0)]),
    ('Loading hit + music', [('LoadingPickaxeHit', 0), ('LoadingYogaAmbience', 0)]),
    ('Home music + two distant hits + water', [('HomescreenAmbience', 0), ('HomeDistantPickaxe08', .3), ('HomeDistantPickaxe06', 1), ('HomeWaterdrop02', 1.2)]),
    ('Crafting overlap + collect', [('Crafting2', 0), ('Crafting2', rows['Crafting2']['after']['duration'] * .6), ('Ding4', rows['Crafting2']['after']['duration'] - .3)]),
    ('Fall hurt + bones + heartbeat', [('Hurt', 0), ('BoneBreaking_01', 0), ('SingleHeartBeat', 0)]),
]
results = []
for name, tracks in scenarios:
    for pitch in (-1, 0, 1):
        mix = m.np.zeros((RATE * SECONDS, 2))
        for clip, delay in tracks:
            data = audio(clip, pitch)
            start = max(0, int(delay * RATE))
            length = min(len(data), len(mix) - start)
            if length > 0:
                mix[start:start + length] += data[:length]
        results.append(dict(scenario=name, pitch_extreme=pitch, true_peak=m.true_peak(mix)))
(m.WORK / 'mix-checks.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(json.dumps({'scenarios': len(results), 'worst_peak': max(r['true_peak'] for r in results),
                  'over_zero': [r for r in results if r['true_peak'] >= 0]}, indent=2))
