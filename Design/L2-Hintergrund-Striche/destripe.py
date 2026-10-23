# Entfernt senkrechte Striche aus einem Hintergrundbild (Layer 2, 23.10.2026).
# Aufruf: python3 destripe.py LowerSandstone.png LowerSandstone_Clean.png 201 6
import sys, numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter1d, median_filter
src, dst = sys.argv[1], sys.argv[2]
V = int(sys.argv[3]) if len(sys.argv) > 3 else 81
im = Image.open(src); mode = im.mode
a = np.asarray(im.convert('RGBA')).astype(np.float32)
rgb = a[..., :3]
def stripes(x, axis_hp, axis_v):
    hp = x - gaussian_filter1d(x, 2.0, axis=axis_hp, mode='wrap')
    size = [1, 1, 1]; size[axis_v] = V
    return median_filter(hp, size=tuple(size), mode='wrap')
out = rgb.copy(); sv = np.zeros_like(rgb)
for _ in range(int(sys.argv[4]) if len(sys.argv) > 4 else 4):
    d = stripes(out, 1, 0)   # vertical lines
    out -= d; sv += d
for n, s in (('vertikal', sv),):
    e = np.abs(s).mean(2)
    print(n, 'mittel %.3f  max %.1f  Pixel>1: %d' % (e.mean(), e.max(), (e > 1).sum()))
a[..., :3] = np.clip(np.rint(out), 0, 255)
res = Image.fromarray(a.astype(np.uint8), 'RGBA')
res = res.convert(mode) if mode != 'RGBA' else res
res.save(dst, optimize=True)
