#ifndef MINING_ULTRONIUM_PULSE_INCLUDED
#define MINING_ULTRONIUM_PULSE_INCLUDED

float UltroniumPulse(float2 cell, float timeSeconds, float pulsesPerMinute)
{
    if (pulsesPerMinute <= 0) return 1;
    float cycle = timeSeconds * pulsesPerMinute / 60;
    float seed = cell.x * .6180339 + cell.y * .41421356;
    float phase = cycle + seed
        + .18 * sin(TWO_PI * (cycle * .19 + seed * 1.7))
        + .07 * sin(TWO_PI * (cycle * .43 + seed * .4));
    float crest = .5 + .5 * cos(TWO_PI * phase);
    return .55 + .65 * crest * crest;
}

#endif
