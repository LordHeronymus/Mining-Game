using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CaveGenerationSettings
{
    public bool enabled;
    [Min(0)] public int minimumDepth = 35;
    [Range(0, 100)] public int walkerCount = 14;
    [Range(0f, 1f)] public float walkerStartRandomness = .25f;
    [Min(1)] public int minimumWalkerLength = 70;
    [Min(1)] public int maximumWalkerLength = 190;
    [Range(.5f, 12f)] public float minimumTunnelRadius = 1.5f;
    [Range(.5f, 12f)] public float maximumTunnelRadius = 3.75f;
    [Range(0f, 100f)] public float caveAversionPercent = 35f;
    [Range(0f, 1f)] public float directionChange = .2f;
    [Range(0f, 1f)] public float verticalityIndex = 1f;
    [Range(0f, 10f)] public float branchChancePercent = 1.1f;
    [Range(0f, 100f)] public float splitBranchChanceFactorPercent = 50f;
    public AnimationCurve branchChanceByDepth = AnimationCurve.Linear(0f, .7f, 1f, 1.35f);
    [Range(0f, 100f)] public float chamberChancePercent = 35f;
    [Range(1f, 20f)] public float minimumChamberRadius = 4f;
    [Range(1f, 20f)] public float maximumChamberRadius = 8f;
    public AnimationCurve densityByDepth = AnimationCurve.Linear(0f, .25f, 1f, 1f);
}

public static class CaveGenerator
{
    const uint Stream = 0xCA7E51u;
    static readonly float[] AversionTurnOffsets = { -.9f, -.45f, .45f, .9f };

    struct Walker
    {
        public Vector2 position;
        public float angle;
        public float radius;
        public int steps;
        public float branchChanceMultiplier;
        public float splitTurnRemaining;
        public int splitTurnStepsRemaining;
    }

    struct StableRandom
    {
        uint state;

        public StableRandom(int seed, int width, int height)
        {
            state = OreVeins.Hash(seed, width, height, Stream);
            if (state == 0) state = 0x9e3779b9u;
        }

        public uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        public float Value() => (NextUInt() & 0x00ffffffu) / 16777215f;
        public float Range(float minimum, float maximum) => Mathf.Lerp(minimum, maximum, Value());
        public int Range(int minimum, int maximumExclusive) => minimum >= maximumExclusive
            ? minimum : minimum + (int)(NextUInt() % (uint)(maximumExclusive - minimum));
        public bool Chance(float probability) => Value() < Mathf.Clamp01(probability);
    }

    public static bool[] Generate(CaveGenerationSettings settings, int seed, int width, int height,
        int ignoredBorderPadding = 0, Func<int, int, bool> reserved = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("Map dimensions must be positive.");
        var result = new bool[checked(width * height)];
        if (settings == null || !settings.enabled || settings.walkerCount <= 0) return result;

        int minimumDepth = Mathf.Clamp(settings.minimumDepth, 0, height - 1);
        float minimumRadius = Mathf.Clamp(Mathf.Min(settings.minimumTunnelRadius,
            settings.maximumTunnelRadius), .5f, 12f);
        float maximumRadius = Mathf.Clamp(Mathf.Max(settings.minimumTunnelRadius,
            settings.maximumTunnelRadius), minimumRadius, 12f);
        // Keep the legacy border argument for existing callers; cave tunnels may now reach side borders.
        if (minimumDepth >= height - 1) return result;

        int minimumLength = Mathf.Max(1, Mathf.Min(settings.minimumWalkerLength, settings.maximumWalkerLength));
        int maximumLength = Mathf.Max(minimumLength, Mathf.Max(settings.minimumWalkerLength,
            settings.maximumWalkerLength));
        int roots = Mathf.Clamp(settings.walkerCount, 0, 100);
        if (roots == 0) return result;

        var depthWeights = BuildDepthWeights(settings.densityByDepth, minimumDepth, height);
        float totalDepthWeight = depthWeights[depthWeights.Length - 1];
        if (totalDepthWeight <= 0f) return result;

        var random = new StableRandom(seed, width, height);
        var horizontalRanks = new int[roots];
        for (int i = 0; i < roots; i++) horizontalRanks[i] = i;
        for (int i = roots - 1; i > 0; i--)
        {
            int swap = random.Range(0, i + 1);
            int rank = horizontalRanks[i];
            horizontalRanks[i] = horizontalRanks[swap];
            horizontalRanks[swap] = rank;
        }

        float randomness = Mathf.Clamp01(settings.walkerStartRandomness);
        for (int i = 0; i < roots; i++)
        {
            Vector2 start = ChooseStartPosition(result, width, height, maximumRadius,
                minimumDepth, roots, i, horizontalRanks[i], randomness, settings.caveAversionPercent,
                depthWeights, totalDepthWeight, ref random);
            int depth = Mathf.RoundToInt(start.y);
            float depth01 = Depth01(depth, minimumDepth, height);
            float direction = random.Chance(.5f) ? 0f : Mathf.PI;
            direction += random.Range(-.65f, .65f);
            var walkers = new Queue<Walker>();
            walkers.Enqueue(new Walker
            {
                position = start,
                angle = direction,
                radius = random.Range(minimumRadius, maximumRadius) * Mathf.Lerp(.9f, 1.12f, depth01),
                steps = Mathf.Max(1, Mathf.RoundToInt(random.Range(minimumLength, maximumLength + 1))),
                branchChanceMultiplier = 1f
            });
            int createdWalkers = 1;
            const int maximumWalkersPerSystem = 4;
            while (walkers.Count > 0)
            {
                Walker walker = walkers.Dequeue();
                int queuedBefore = walkers.Count;
                Walk(ref walker, settings, result, width, height, minimumDepth,
                    minimumRadius, maximumRadius, reserved, walkers,
                    maximumWalkersPerSystem - createdWalkers, ref random);
                createdWalkers += walkers.Count - queuedBefore;
            }
        }

        Smooth(result, width, height, minimumDepth, reserved);
        return result;
    }

    static float[] BuildDepthWeights(AnimationCurve curve, int minimumDepth, int height)
    {
        var cumulativeWeights = new float[height - minimumDepth];
        float total = 0f;
        for (int depth = minimumDepth; depth < height; depth++)
        {
            float value = curve == null ? 1f : curve.Evaluate(Depth01(depth, minimumDepth, height));
            if (Finite(value) && value > 0f) total += value;
            cumulativeWeights[depth - minimumDepth] = total;
        }
        return cumulativeWeights;
    }

    static int ChooseDepth(float[] cumulativeWeights, float totalWeight, int minimumDepth, float quantile)
    {
        float target = Mathf.Clamp01(quantile) * totalWeight;
        if (target >= totalWeight) target = totalWeight * .999999f;
        int low = 0, high = cumulativeWeights.Length - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (cumulativeWeights[middle] > target) high = middle;
            else low = middle + 1;
        }
        return minimumDepth + low;
    }

    static Vector2 ChooseStartPosition(bool[] caves, int width, int height,
        float maximumRadius, int minimumDepth, int rootCount, int rootIndex, int horizontalRank,
        float randomness, float aversionPercent, float[] depthWeights, float totalDepthWeight,
        ref StableRandom random)
    {
        float regularX = (horizontalRank + .5f) / rootCount;
        float xQuantile = Mathf.Lerp(regularX, random.Value(), randomness);
        float depthQuantile = (rootIndex + Mathf.Lerp(.5f, random.Value(), randomness)) / rootCount;
        Vector2 preferred = StartPositionAtQuantiles(xQuantile, depthQuantile, width,
            minimumDepth, depthWeights, totalDepthWeight);

        float strength = Mathf.Clamp01(aversionPercent / 100f);
        if (rootIndex == 0 || strength <= 0f) return preferred;

        int searchRadius = Mathf.Clamp(Mathf.CeilToInt(maximumRadius * 6f + 12f), 24, 64);
        float preferredClearance = StartClearance(caves, width, height, preferred, searchRadius);
        if (preferredClearance >= searchRadius) return preferred;

        Vector2 best = preferred;
        float bestClearance = preferredClearance;
        for (int candidate = 0; candidate < 12; candidate++)
        {
            float localX = (horizontalRank + random.Range(.05f, .95f)) / rootCount;
            float alternateX = Mathf.Lerp(localX, random.Value(), randomness);
            float alternateDepth = (rootIndex + random.Range(.05f, .95f)) / rootCount;
            Vector2 position = StartPositionAtQuantiles(alternateX, alternateDepth, width,
                minimumDepth, depthWeights, totalDepthWeight);
            float clearance = StartClearance(caves, width, height, position, searchRadius);
            if (clearance <= bestClearance) continue;
            best = position;
            bestClearance = clearance;
        }

        float improvement = (bestClearance - preferredClearance) / searchRadius;
        float chooseChance = 1f - Mathf.Exp(-8f * strength * improvement);
        return random.Chance(chooseChance) ? best : preferred;
    }

    static Vector2 StartPositionAtQuantiles(float xQuantile, float depthQuantile, int width,
        int minimumDepth, float[] depthWeights, float totalDepthWeight)
    {
        float x = Mathf.Lerp(0f, width - 1f, Mathf.Clamp01(xQuantile));
        int depth = ChooseDepth(depthWeights, totalDepthWeight, minimumDepth, depthQuantile);
        return new Vector2(x, depth);
    }

    static float StartClearance(bool[] caves, int width, int height, Vector2 position, int searchRadius)
    {
        int centerX = Mathf.RoundToInt(position.x), centerY = Mathf.RoundToInt(position.y);
        int closestSquared = searchRadius * searchRadius;
        for (int dy = -searchRadius; dy <= searchRadius; dy++)
        {
            int y = centerY + dy;
            if (y < 0 || y >= height) continue;
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int distanceSquared = dx * dx + dy * dy;
                if (distanceSquared >= closestSquared) continue;
                int x = centerX + dx;
                if (x < 0 || x >= width || !caves[y * width + x]) continue;
                closestSquared = distanceSquared;
                if (closestSquared == 0) return 0f;
            }
        }
        return Mathf.Sqrt(closestSquared);
    }

    static void Walk(ref Walker walker, CaveGenerationSettings settings, bool[] result, int width,
        int height, int minimumDepth, float minimumRadius, float maximumRadius,
        Func<int, int, bool> reserved, Queue<Walker> walkers, int remainingWalkerSlots,
        ref StableRandom random)
    {
        float radiusTarget = walker.radius;
        bool exitedSide = false;
        for (int step = 0; step < walker.steps; step++)
        {
            if (walker.position.x < 0f || walker.position.x >= width)
            {
                exitedSide = true;
                break;
            }

            int depth = Mathf.RoundToInt(walker.position.y);
            float depth01 = Depth01(depth, minimumDepth, height);
            Carve(result, width, height, walker.position, walker.radius, walker.angle,
                minimumDepth, reserved, ref random);

            float depthMultiplier = settings.branchChanceByDepth == null
                ? 1f : settings.branchChanceByDepth.Evaluate(depth01);
            if (float.IsNaN(depthMultiplier) || float.IsInfinity(depthMultiplier)) depthMultiplier = 0f;
            float branchChance = Mathf.Clamp(settings.branchChancePercent, 0f, 10f) / 100f *
                Mathf.Max(0f, depthMultiplier) * Mathf.Max(0f, walker.branchChanceMultiplier);
            if (remainingWalkerSlots > 0 && walker.splitTurnStepsRemaining <= 0 &&
                step > 10 && step < walker.steps - 12 &&
                random.Chance(branchChance))
            {
                TryCarveChamber(settings, result, width, height, walker.position, walker.angle, depth01,
                    minimumDepth, reserved, ref random);
                float splitTurn = random.Range(.45f, .8f) * (random.Chance(.5f) ? 1f : -1f);
                int childSteps = Mathf.Max(12,
                    Mathf.RoundToInt((walker.steps - step) * random.Range(.3f, .62f)));
                int splitTurnSteps = Mathf.Max(1, Mathf.Min(childSteps,
                    Mathf.Min(walker.steps - step, random.Range(16, 27))));
                walker.splitTurnRemaining -= splitTurn * .25f;
                walker.splitTurnStepsRemaining = Mathf.Max(walker.splitTurnStepsRemaining, splitTurnSteps);
                walkers.Enqueue(new Walker
                {
                    position = walker.position,
                    angle = walker.angle,
                    radius = Mathf.Max(minimumRadius, walker.radius * random.Range(.58f, .82f)),
                    steps = childSteps,
                    branchChanceMultiplier = walker.branchChanceMultiplier *
                        Mathf.Clamp(settings.splitBranchChanceFactorPercent, 0f, 100f) / 100f,
                    splitTurnRemaining = splitTurn * .75f,
                    splitTurnStepsRemaining = splitTurnSteps
                });
                remainingWalkerSlots--;
            }

            bool applyingSplitTurn = walker.splitTurnStepsRemaining > 0;
            ApplySplitTurn(ref walker);
            float turnStrength = Mathf.Clamp01(settings.directionChange);
            walker.angle += random.Range(-turnStrength, turnStrength);
            if (walker.position.y < minimumDepth + maximumRadius + 2f) walker.angle += .12f;
            else if (walker.position.y > height - maximumRadius - 3f) walker.angle -= .12f;
            if (!applyingSplitTurn)
                walker.angle = ApplyCaveAversion(walker.position, walker.angle,
                    maximumRadius, settings.caveAversionPercent, result, width, height, ref random);

            if (step % random.Range(8, 18) == 0)
                radiusTarget = random.Range(minimumRadius, maximumRadius) * Mathf.Lerp(.9f, 1.12f, depth01);
            walker.radius = Mathf.Lerp(walker.radius, radiusTarget, .075f);
            Vector2 direction = new Vector2(
                Mathf.Cos(walker.angle),
                Mathf.Sin(walker.angle) * Mathf.Clamp01(settings.verticalityIndex));
            if (direction.sqrMagnitude < .000001f)
                direction = new Vector2(Mathf.Cos(walker.angle) >= 0f ? 1f : -1f, 0f);
            else
                direction.Normalize();
            walker.position += direction * .72f;

            if (walker.position.x < 0f || walker.position.x >= width)
            {
                exitedSide = true;
                break;
            }
            if (walker.position.y < minimumDepth + 1f || walker.position.y >= height - 1f)
            {
                walker.angle = -walker.angle;
                walker.position.y = Mathf.Clamp(walker.position.y, minimumDepth + 1f, height - 2f);
            }
        }

        if (!exitedSide)
        {
            int endDepth = Mathf.RoundToInt(walker.position.y);
            TryCarveChamber(settings, result, width, height, walker.position, walker.angle,
                Depth01(endDepth, minimumDepth, height), minimumDepth, reserved, ref random);
        }
    }

    static void ApplySplitTurn(ref Walker walker)
    {
        if (walker.splitTurnStepsRemaining <= 0) return;
        float turn = walker.splitTurnRemaining / walker.splitTurnStepsRemaining;
        walker.angle += turn;
        walker.splitTurnRemaining -= turn;
        walker.splitTurnStepsRemaining--;
        if (walker.splitTurnStepsRemaining == 0) walker.splitTurnRemaining = 0f;
    }

    static float ApplyCaveAversion(Vector2 position, float angle, float maximumRadius,
        float aversionPercent, bool[] caves, int width, int height,
        ref StableRandom random)
    {
        float strength = Mathf.Clamp01(aversionPercent / 100f);
        if (strength <= 0f) return angle;

        float currentPressure = CavePressure(position, angle, maximumRadius, caves, width, height);
        if (currentPressure <= .015f) return angle;

        float bestPressure = currentPressure;
        float bestOffset = 0f;
        for (int i = 0; i < AversionTurnOffsets.Length; i++)
        {
            float offset = AversionTurnOffsets[i];
            float pressure = CavePressure(position, angle + offset, maximumRadius, caves, width, height);
            if (pressure < bestPressure)
            {
                bestPressure = pressure;
                bestOffset = offset;
            }
        }

        float improvement = (currentPressure - bestPressure) / Mathf.Max(currentPressure, .05f);
        float turnChance = strength * Mathf.Clamp01(improvement * 1.5f);
        return random.Chance(turnChance) ? angle + bestOffset : angle;
    }

    static float CavePressure(Vector2 position, float angle, float maximumRadius,
        bool[] caves, int width, int height)
    {
        float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
        float detectionRadius = maximumRadius + .75f;
        int reach = Mathf.CeilToInt(detectionRadius);
        float occupied = 0f;
        int samples = 0;
        int firstDistance = Mathf.Max(8, Mathf.CeilToInt(maximumRadius * 2f + 2f));
        for (int distance = firstDistance; distance <= firstDistance + 6; distance += 3)
        {
            float centerX = position.x + cosine * distance;
            float centerY = position.y + sine * distance;
            int cellX = Mathf.RoundToInt(centerX);
            int cellY = Mathf.RoundToInt(centerY);
            for (int dy = -reach; dy <= reach; dy += 2)
                for (int dx = -reach; dx <= reach; dx += 2)
                {
                    if (dx * dx + dy * dy > detectionRadius * detectionRadius) continue;
                    int x = cellX + dx, y = cellY + dy;
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;
                    samples++;
                    if (caves[y * width + x]) occupied++;
                }
        }
        return samples > 0 ? occupied / samples : 0f;
    }

    static void TryCarveChamber(CaveGenerationSettings settings, bool[] result, int width, int height,
        Vector2 position, float angle, float depth01, int minimumDepth,
        Func<int, int, bool> reserved, ref StableRandom random)
    {
        float chance = Mathf.Clamp(settings.chamberChancePercent, 0f, 100f) / 100f;
        if (!random.Chance(chance)) return;

        float minimumRadius = Mathf.Max(1f, Mathf.Min(settings.minimumChamberRadius,
            settings.maximumChamberRadius));
        float maximumRadius = Mathf.Max(minimumRadius, Mathf.Max(settings.minimumChamberRadius,
            settings.maximumChamberRadius));
        float radius = random.Range(minimumRadius, maximumRadius) * Mathf.Lerp(.9f, 1.15f, depth01);
        Carve(result, width, height, position, radius, angle, minimumDepth, reserved, ref random);
    }

    static void Carve(bool[] result, int width, int height, Vector2 center, float radius, float angle,
        int minimumDepth, Func<int, int, bool> reserved, ref StableRandom random)
    {
        float alongRadius = radius * random.Range(1f, 1.35f);
        float acrossRadius = radius * random.Range(.82f, 1.08f);
        int reach = Mathf.CeilToInt(Mathf.Max(alongRadius, acrossRadius) + 1f);
        int centerX = Mathf.RoundToInt(center.x), centerY = Mathf.RoundToInt(center.y);
        float cosine = Mathf.Cos(angle), sine = Mathf.Sin(angle);
        for (int y = centerY - reach; y <= centerY + reach; y++)
            for (int x = centerX - reach; x <= centerX + reach; x++)
            {
                if (x < 0 || x >= width || y < minimumDepth || y >= height ||
                    (reserved != null && reserved(x, y))) continue;
                float dx = x + .5f - center.x, dy = y + .5f - center.y;
                float along = dx * cosine + dy * sine;
                float across = -dx * sine + dy * cosine;
                float distance = along * along / (alongRadius * alongRadius) +
                    across * across / (acrossRadius * acrossRadius);
                float edge = .88f + (OreVeins.Hash(centerX * 397 ^ centerY, x, y, Stream) & 255u) / 255f * .24f;
                if (distance <= edge) result[y * width + x] = true;
            }
    }

    static void Smooth(bool[] caves, int width, int height, int minimumDepth,
        Func<int, int, bool> reserved)
    {
        var source = (bool[])caves.Clone();
        for (int y = minimumDepth; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (reserved != null && reserved(x, y)) { caves[index] = false; continue; }
                int neighbors = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height && source[ny * width + nx]) neighbors++;
                    }
                if (source[index] && neighbors <= 1) caves[index] = false;
                else if (!source[index] && neighbors >= 7) caves[index] = true;
            }
    }

    static float Depth01(int depth, int minimumDepth, int height) =>
        Mathf.InverseLerp(minimumDepth, Mathf.Max(minimumDepth + 1, height - 1), depth);

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
