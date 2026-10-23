# Schneidet den Haken (mit Kontur, Schatten und etwas Untergrund) aus dem Siegel (ChallengeArt Teil 7) als eigene Ebene
# fuer den Versatz in F1 (UI -> Challenge-Siegel). Aufruf: python haken-ebene.py <ChallengeAtlas.png> <ChallengeSealCheck.png>
import sys, numpy as np
from PIL import Image
from scipy import ndimage as nd
X0, Y0, W, H = 703, 715, 262, 253   # Siegel im Atlas (1536x1024, oben links)
CX, CY, R = 127.41, 127.62, 76.45   # Innenkreis im Ausschnitt
a = np.array(Image.open(sys.argv[1]).convert('RGBA'))[Y0:Y0+H, X0:X0+W].astype(float)
L = a[..., :3].mean(2); yy, xx = np.mgrid[0:H, 0:W]
m = (L > 110) & (np.hypot(xx-128, yy-128) < 64)
lab, n = nd.label(m); sizes = nd.sum(m, lab, range(1, n+1))
body = nd.binary_fill_holes(nd.binary_closing(lab == 1+np.argmax(sizes), iterations=2))
dist = nd.distance_transform_edt(~body)
alpha = np.clip((18 - dist) / 4, 0, 1)                                  # voll bis 14 px um den Haken, weich bis 18 px
alpha *= np.clip((R - 6 - np.hypot(xx-CX, yy-CY)) / 4, 0, 1)            # Rand des Innenkreises bleibt frei
out = a.copy(); out[..., 3] = np.round(alpha * a[..., 3])
Image.fromarray(out.astype(np.uint8), 'RGBA').save(sys.argv[2], optimize=True)
