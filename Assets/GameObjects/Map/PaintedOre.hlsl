#ifndef MINING_PAINTED_ORE_INCLUDED
#define MINING_PAINTED_ORE_INCLUDED

// Painted ore tiles carry a tile colour alpha of 254/255 (lit like stone) or 253/255 (lit and
// pulsing glow, ultronium). Old ultronium uses alpha below .99, ordinary ores exactly 1,
// so the cases never overlap.
half PaintedOreMarker(half alpha)
{
    return step(.99h, alpha) * step(alpha, .999h);
}

// 1 = painted, 2 = painted and glowing, 0 = not painted.
half PaintedOreKind(half alpha)
{
    return PaintedOreMarker(alpha) * (1 + step(alpha, .9941h));
}

#endif
