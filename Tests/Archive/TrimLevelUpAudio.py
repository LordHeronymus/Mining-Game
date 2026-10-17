"""Keep the second event of the selected ElevenLabs variation, without regeneration."""
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "Temp/audio-tools"))
import soundfile as sf

folder = Path(__file__).resolve().parent.parent / "Assets/AC Audio/Archive/LevelUp"
source = folder / "LevelUp-ElevenLabs-V2-original.mp3"
target = folder / "LevelUp-ElevenLabs-V2-cut.wav"
audio, rate = sf.read(source, always_2d=True)
cut_seconds = 0.795
start = round(cut_seconds * rate)
edited = audio[start:].copy()
fade = round(0.005 * rate)
edited[:fade] *= np.linspace(0, 1, fade)[:, None]
edited[-fade:] *= np.linspace(1, 0, fade)[:, None]
sf.write(target, edited, rate, subtype="PCM_24")
decoded, decoded_rate = sf.read(target, always_2d=True)
assert decoded_rate == rate and decoded.shape == edited.shape
assert np.max(np.abs(decoded[fade:-fade] - audio[start + fade:-fade])) < 1.3e-7
assert np.max(np.abs(decoded[0])) == 0 and np.max(np.abs(decoded[-1])) == 0
assert np.max(np.abs(decoded)) <= 1
details = {
    "provider": "ElevenLabs",
    "model": "eleven_text_to_sound_v2",
    "flow_id": "n4GN2ViEZmTeRL3gTQ5J",
    "generation_id": "e7D4EDcXpIwC0O2cb6NC",
    "selected_variation": 2,
    "source": source.name,
    "output": target.name,
    "removed_seconds": cut_seconds,
    "duration_seconds": len(decoded) / rate,
    "sample_rate": rate,
    "channels": decoded.shape[1],
    "boundary_fade_seconds": 0.005,
    "unchanged_interior_verified": True,
}
(folder / "LevelUp-ElevenLabs-V2-cut.json").write_text(json.dumps(details, indent=2) + "\n", encoding="utf-8")
print(json.dumps(details, indent=2))
