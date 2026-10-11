import json
import urllib.request
from pathlib import Path
import numpy as np
import soundfile as sf

out = Path('Design/Tiefenhall/HomeAmbience/L1')
out.mkdir(parents=True, exist_ok=True)
source = out / 'Wind-ElevenLabs-original.mp3'
if not source.exists():
    url = json.loads(Path('Temp/wind-source-download.json').read_text())['url']
    urllib.request.urlretrieve(url, source)
x, sr = sf.read(source, always_2d=True)
freq = np.fft.rfftfreq(len(x), 1 / sr)
# Smooth high shelf: retain body of wind, suppress bright granular hiss.
t = np.clip((np.log2(np.maximum(freq, 1)) - np.log2(900)) / np.log2(3200 / 900), 0, 1)
t = t * t * (3 - 2 * t)
gain = 10 ** (-20 * t / 20)
gain *= 1 / np.sqrt(1 + (freq / 4200) ** 12)
y = np.fft.irfft(np.fft.rfft(x, axis=0) * gain[:, None], n=len(x), axis=0)
# Short seam repair, leaving the rest of the original wind untouched.
k = min(int(sr * .012), len(y) // 4)
edge = (y[0] + y[-1]) / 2
w = (.5 + .5 * np.cos(np.linspace(0, np.pi, k)))[:, None]
y[:k] += w * (edge - y[0])
y[-k:] += w[::-1] * (edge - y[-1])
assert np.max(np.abs(y)) < 1
sf.write(out / 'Wind-L1-soft-loop-30s.wav', y, sr, subtype='PCM_24')
minute = np.tile(y, (2, 1))
sf.write(out / 'Wind-L1-soft-loop-60s.wav', minute, sr, subtype='PCM_24')
specx = np.abs(np.fft.rfft(x, axis=0)) ** 2
specy = np.abs(np.fft.rfft(y, axis=0)) ** 2
def change(lo, hi):
    b = (freq >= lo) & (freq < hi)
    return float(10 * np.log10(specy[b].sum() / specx[b].sum()))
report = dict(sampleRate=sr, channels=x.shape[1], sourceSeconds=len(x)/sr,
              outputSeconds=len(minute)/sr, peak=float(np.max(np.abs(y))),
              windBodyChangeDb=change(80, 900), highHissChangeDb=change(3200, 12000),
              seamDelta=float(np.max(np.abs(y[0] - y[-1]))),
              processing='Original ElevenLabs recording; circular FFT high-shelf -20dB above 3.2kHz, soft lowpass 4.2kHz, 12ms seam repair; repeated twice.')
assert abs(report['outputSeconds'] - 60) < .1
assert report['highHissChangeDb'] < -19
assert abs(report['windBodyChangeDb']) < .2
(out / 'Wind-cleanup-report.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
