#ifndef MINING_PAINTED_ORE_INCLUDED
#define MINING_PAINTED_ORE_INCLUDED

// Painted ore tiles carry a tile colour alpha of 254/255. Ultronium uses alpha below .99,
// ordinary ores exactly 1, so the three cases never overlap.
half PaintedOreMarker(half alpha)
{
    return step(.99h, alpha) * step(alpha, .999h);
}

#endif
