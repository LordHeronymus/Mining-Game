import sys, numpy as np
from PIL import Image
from scipy import ndimage as nd
src, dst = sys.argv[1], sys.argv[2]
DX, DY = 1, -3                      # Haken: 1 px nach rechts, 3 px nach oben
X0, Y0, W, H = 703, 715, 262, 253   # Siegel (ChallengeArt index 7)
CX, CY, R = 127.41, 127.62, 76.45   # Innenkreis im Ausschnitt
atlas = Image.open(src).convert('RGBA'); full = np.array(atlas).astype(float)
a = full[Y0:Y0+H, X0:X0+W].copy(); L = a[..., :3].mean(2)
yy, xx = np.mgrid[0:H, 0:W]
m = (L > 110) & (np.hypot(xx-128, yy-128) < 64)
lab, n = nd.label(m); sizes = nd.sum(m, lab, range(1, n+1))
body = nd.binary_fill_holes(nd.binary_closing(lab == 1+np.argmax(sizes), iterations=2))
# Abstand zum verschobenen Haken (Ziel-Koordinaten)
moved = np.zeros_like(body); moved[max(0,DY):H+min(0,DY), max(0,DX):W+min(0,DX)] = body[max(0,-DY):H-max(0,DY), max(0,-DX):W-max(0,DX)]
dist = nd.distance_transform_edt(~moved)
alpha = np.clip((16 - dist) / 4, 0, 1)                     # voll bis 12 px, weich bis 16 px
rd = np.hypot(xx-CX, yy-CY); rs = np.hypot(xx-DX-CX, yy-DY-CY)
alpha *= np.clip((R - 6 - np.maximum(rd, rs)) / 4, 0, 1)  # Rand des Innenkreises nie anfassen
shifted = np.roll(np.roll(a, DY, 0), DX, 1)
out = a*(1-alpha[..., None]) + shifted*alpha[..., None]
out[..., 3] = a[..., 3]
full[Y0:Y0+H, X0:X0+W] = out
Image.fromarray(np.round(full).astype(np.uint8), 'RGBA').save(dst, optimize=True)
