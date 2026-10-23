#!/usr/bin/env python3
"""Vorher/Nachher-Bilder dünner Erzadern aus den VEIN/CORE-Zeilen von Tests/ThinVeinMediumChecks.cs.

Setzt die echten Spiel-Sprites zusammen (Gestein der Ebene, gemalte Erz-Tiles small/medium/rich mit derselben
Variantenwahl wie MapGenerator: OreVeins.Hash(seed, x, y, 0x5678) % Anzahl). Ohne Beleuchtung und Terrain-Shader;
die gerenderten Standbilder aus dem Cloud-Lauf liegen im Artefakt (Temp/ThinVeinMedium/Vein_*.png).

  python3 Design/OreVeins/MediumInThinVeins/compose_previews.py veins.txt [Zielordner]
"""
import re
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
CELL = 48  # Pixel pro Kachel in der Vorschau
M = 0xffffffff
BLOCKS = {'IronOre': 'Iron', 'CopperOre': 'Copper', 'GoldOre': 'Gold', 'SilverOre': 'Silver', 'PlatinumOre': 'Platinum',
          'Coal': 'Coal', 'DiamondOre': 'Diamond', 'UltroniumOre': 'Ultronium', 'TitaniumOre': 'Titanium',
          'TungstenOre': 'Wolfram', 'OrangeGarnetOre': 'OrangeGranat', 'MythrilOre': 'Mythril', 'EmeraldOre': 'Emerald',
          'RubyOre': 'Ruby'}
NAMES = {'IronOre': 'Eisen', 'CopperOre': 'Kupfer', 'GoldOre': 'Gold', 'SilverOre': 'Silber', 'PlatinumOre': 'Platin',
         'Coal': 'Kohle', 'DiamondOre': 'Diamant', 'UltroniumOre': 'Ultronium', 'TitaniumOre': 'Titan',
         'TungstenOre': 'Wolfram', 'OrangeGarnetOre': 'Granat', 'MythrilOre': 'Mythril', 'EmeraldOre': 'Smaragd',
         'RubyOre': 'Rubin'}
# Gestein je Ebene (GPS-Layer 1–5): Erde, Stein, Gestein Ebene 2–4.
STONES = {1: 'Assets/GameObjects/Map/Blocks/Dirt/Dirt.asset', 2: 'Assets/GameObjects/Map/Blocks/Stone.asset',
          3: 'Assets/GameObjects/Map/Blocks/LayerStones/Stone_Layer2.asset',
          4: 'Assets/GameObjects/Map/Blocks/LayerStones/Stone_Layer3.asset',
          5: 'Assets/GameObjects/Map/Blocks/LayerStones/Stone_Layer4.asset'}

_guids = None


def guid_path(guid):
    global _guids
    if _guids is None:
        _guids = {}
        for meta in (ROOT / 'Assets' / 'GameObjects' / 'Map').rglob('*.meta'):
            match = re.search(r'guid: ([0-9a-f]{32})', meta.read_text(encoding='utf-8', errors='ignore')[:300])
            if match: _guids[match.group(1)] = meta.with_suffix('')
    return _guids[guid]


def refs(text, key):
    block = re.search(key + r':\n((?:  - \{[^\n]*\n)+)', text)
    return re.findall(r'guid: ([0-9a-f]{32})', block.group(1)) if block else []


def sprite_of(tile_asset):
    text = Path(tile_asset).read_text(encoding='utf-8')
    match = re.search(r'm_(?:Default)?Sprite: \{fileID: -?\d+, guid: ([0-9a-f]{32})', text)
    return Image.open(guid_path(match.group(1))).convert('RGBA')


def ore_hash(seed, x, y, stream):
    v = (seed & M) ^ ((x * 0x9e3779b9) & M) ^ ((y * 0x85ebca6b) & M) ^ (stream & M)
    v &= M
    v ^= v >> 16; v = (v * 0x7feb352d) & M
    v ^= v >> 15; v = (v * 0x846ca68b) & M
    return v ^ (v >> 16)


_cache = {}


def ore_sprites(ore, level):
    key = (ore, level)
    if key not in _cache:
        text = (ROOT / 'Assets/GameObjects/Map/Blocks' / (BLOCKS[ore] + '.asset')).read_text(encoding='utf-8')
        group = {'s': 'smallOre', 'm': 'mediumOre', 'r': 'richOre'}[level]
        _cache[key] = [sprite_of(guid_path(g)) for g in refs(text, group)]
    return _cache[key]


def stone_sprite(layer):
    key = ('stone', layer)
    if key not in _cache:
        text = (ROOT / STONES[layer]).read_text(encoding='utf-8')
        _cache[key] = sprite_of(guid_path(refs(text, 'variants')[0]))
    return _cache[key]


def draw_grid(rows, ore, layer, seed, x0, y0):
    h, w = len(rows), len(rows[0])
    pad = 1
    image = Image.new('RGBA', ((w + 2 * pad) * CELL, (h + 2 * pad) * CELL), (13, 13, 15, 255))
    stone = stone_sprite(layer).resize((CELL, CELL), Image.LANCZOS)
    for gy in range(h + 2 * pad):
        for gx in range(w + 2 * pad):
            image.alpha_composite(stone, (gx * CELL, gy * CELL))
    for gy, row in enumerate(rows):
        for gx, level in enumerate(row):
            if level == '.': continue
            variants = ore_sprites(ore, level)
            sprite = variants[ore_hash(seed, x0 + gx, y0 + gy, 0x5678) % len(variants)]
            size = round(sprite.width / 418 * CELL)  # 418 px des gemalten Sprites sind eine Kachel
            tile = sprite.resize((size, size), Image.LANCZOS)
            cx, cy = (gx + pad + .5) * CELL, (gy + pad + .5) * CELL
            image.alpha_composite(tile, (round(cx - size / 2), round(cy - size / 2)))
    return image


def label(image, text):
    draw = ImageDraw.Draw(image)
    try: font = ImageFont.truetype('DejaVuSans.ttf', 18)
    except OSError: font = ImageFont.load_default()
    draw.rectangle((0, 0, image.width, 26), fill=(0, 0, 0, 170))
    draw.text((6, 3), text, fill=(255, 255, 255, 255), font=font)


def main(path, out=OUT):
    out = Path(out); out.mkdir(parents=True, exist_ok=True)
    pairs = []
    for line in Path(path).read_text(encoding='utf-8').splitlines():
        parts = line.split()
        if len(parts) != 7 or parts[0] not in ('VEIN', 'CORE'): continue
        kind, seed, ore, layer, origin, before, after = parts
        x0, y0 = map(int, origin.split(','))
        rows_before, rows_after = before.split('/'), after.split('/')
        cells = sum(r.count('s') + r.count('m') + r.count('r') for r in rows_before)
        added = sum(r.count('m') for r in rows_after) - sum(r.count('m') for r in rows_before)
        left = draw_grid(rows_before, ore, int(layer[1:]), int(seed), x0, y0)
        right = draw_grid(rows_after, ore, int(layer[1:]), int(seed), x0, y0)
        caption = f"{NAMES.get(ore, ore)}, Ebene {layer[1:]}, {cells} Blöcke: vorher | nachher (+{added} Medium)"
        if kind == 'CORE': caption += ' – Schalter Rich-Kern'
        gap = CELL // 2
        pair = Image.new('RGBA', (left.width + gap + right.width, left.height + 28), (24, 24, 28, 255))
        pair.alpha_composite(left, (0, 28)); pair.alpha_composite(right, (left.width + gap, 28))
        label(pair, caption)
        pairs.append((kind, pair))
    if not pairs: sys.exit('Keine VEIN/CORE-Zeilen gefunden.')
    for index, (kind, pair) in enumerate(pairs, 1):
        pair.convert('RGB').save(out / f'Vergleich_{index:02d}_{"Rich-Kern" if kind == "CORE" else "Duenn"}.png')
    width = max(p.width for _, p in pairs)
    sheet = Image.new('RGB', (width, sum(p.height + 8 for _, p in pairs)), (24, 24, 28))
    y = 0
    for _, pair in pairs:
        sheet.paste(pair.convert('RGB'), (0, y)); y += pair.height + 8
    sheet.save(out / 'Uebersicht.png')
    print(f'{len(pairs)} Vergleiche nach {out}')


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else OUT / 'veins.txt', sys.argv[2] if len(sys.argv) > 2 else OUT)
