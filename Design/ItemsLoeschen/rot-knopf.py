"""Roter Loeschknopf: SelectionButtons-v2 mit ganz rot gefuellter Holzflaeche (auch in den Pfeilspitzen).
Aufruf an der Projektwurzel: python3 -I Design/ItemsLoeschen/rot-knopf.py"""
from PIL import Image, ImageDraw, ImageFilter
import numpy as np

SRC = 'Assets/Resources/Homescreen/SelectionButtons-v2.png'
OUT = 'Assets/Resources/Homescreen/SelectionButtons-Red.png'
X0, W, H, STEP = 45, 1958, 240, 235          # Knopfbereich wie in HomeUi.Sprite (Referenz 2048 x 768)
TONES = [(156, 28, 18), (176, 36, 22), (206, 52, 30)]   # normal, hover, gewaehlt

src = Image.open(SRC).convert('RGBA'); assert src.size == (2048, 768)
a = np.asarray(src).astype(float); out = a.copy()
# Holzflaeche des ersten Knopfs: dunkle Pixel (wenig Gruen), zusammenhaengend ab der Mitte
bar = a[25:25 + H, X0:X0 + W]
dark = Image.fromarray((((bar[..., 1] < 50) & (bar[..., 3] > 200)) * 255).astype('uint8')).convert('RGB')
ImageDraw.floodfill(dark, (W // 2, H // 2), (255, 0, 0))
mask = Image.fromarray(((np.asarray(dark) == [255, 0, 0]).all(-1) * 255).astype('uint8'))
mask = mask.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.MinFilter(9)).filter(ImageFilter.GaussianBlur(1.2))
m = np.asarray(mask).astype(float)[..., None] / 255
for state, tone in enumerate(TONES):
    y0 = 25 + STEP * state
    reg = a[y0:y0 + H, X0:X0 + W, :3]
    lum = reg.mean(-1)
    k = lum / max(1, lum[m[..., 0] > .5].mean())     # Maserung bleibt sichtbar
    red = np.array(tone)[None, None, :] * np.clip(.4 + .6 * k, .25, 1.7)[..., None]
    out[y0:y0 + H, X0:X0 + W, :3] = reg * (1 - m) + red * m
Image.fromarray(out.clip(0, 255).astype('uint8')).save(OUT)
print('geschrieben:', OUT)
