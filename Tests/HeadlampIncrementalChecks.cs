// Run with Unity Pipeline eval_file; compares incremental light against full propagation.
void Check(bool ok, string message) { if (!ok) throw new System.Exception(message); }
const int width = 80, height = 80;
var solid = new bool[width * height];
for (int y = 0; y < height; y++) solid[y * width + 40] = true;
var smooth = new SmoothHeadlampField(width, height, solid, 1, 1, 6);
float[] Reference(float x, float y, float intensity, bool active)
{
    var result = new float[solid.Length];
    if (!active || intensity <= 0) return result;
    var field = new TorchLightField(width, height, solid, 1, 1, 6);
    int fx = Mathf.FloorToInt(x), fy = Mathf.FloorToInt(y);
    int cx = Mathf.RoundToInt(x), cy = Mathf.RoundToInt(y);
    bool Wall(int xx, int yy) => xx < 0 || xx >= width || yy < 0 || yy >= height || solid[yy * width + xx];
    var weights = new float[4];
    float sum = 0;
    for (int i = 0; i < 4; i++)
    {
        int ax = fx + (i & 1), ay = fy + (i >> 1);
        float weight = ((i & 1) == 0 ? 1 - (x - fx) : x - fx) * ((i & 2) == 0 ? 1 - (y - fy) : y - fy);
        if (Wall(ax, ay) || (ax != cx && ay != cy && Wall(cx, ay) && Wall(ax, cy))) weight = 0;
        weights[i] = weight; sum += weight;
    }
    if (sum <= 0) return result;
    for (int i = 0; i < 4; i++)
    {
        var source = new[] { new TorchLightField.Source(fx + (i & 1), fy + (i >> 1), intensity) };
        foreach (int index in field.Rebuild(source)) result[index] += field.Get(index) * weights[i] / sum;
    }
    return result;
}
int comparisons = 0;
float maximumDifference = 0;
void Compare(float x, float y, float intensity = 1, bool active = true)
{
    foreach (int index in smooth.Rebuild(x, y, intensity, active)) { }
    var expected = Reference(x, y, intensity, active);
    for (int i = 0; i < expected.Length; i++)
    {
        float difference = Mathf.Abs(smooth.Get(i) - expected[i]);
        maximumDifference = Mathf.Max(maximumDifference, difference);
        Check(difference < .0001f, "Light differs at " + i + ": " + difference);
    }
    comparisons++;
}
void Set(int x, int y, bool value)
{
    smooth.SetSolid(x, y, value); solid[y * width + x] = value;
}
Compare(37.3f, 33.4f);
Check(!smooth.SetSolid(79, 79, true), "Distant terrain unnecessarily invalidated headlamp light.");
solid[79 * width + 79] = true;
Compare(37.3f, 33.4f);
Check(smooth.Get(43, 33) == 0, "Light leaked through intact wall.");
Set(40, 33, false); Compare(37.3f, 33.4f);
Check(smooth.Get(43, 33) > 0, "Opening did not spread light in the same update.");
Set(40, 33, true); Compare(37.3f, 33.4f);
Check(smooth.Get(43, 33) == 0, "Closing left light behind wall.");
// Multiple edits before a rebuild, including diagonal dependencies and source cells.
for (int y = 30; y < 37; y++) Set(40, y, false);
Compare(37.3f, 33.4f);
Set(38, 34, true); Set(38, 33, true); Compare(37.3f, 33.4f);
Set(38, 34, false); Set(38, 33, false); Compare(37.3f, 33.4f);
var random = new System.Random(97531);
for (int n = 0; n < 120; n++)
{
    for (int j = 0; j < 4; j++)
    {
        int x = random.Next(30, 49), y = random.Next(26, 44);
        Set(x, y, !solid[y * width + x]);
    }
    Compare(37.3f, 33.4f);
}
for (int n = 0; n < 100; n++)
{
    for (int j = 0; j < 3; j++)
    {
        int x = random.Next(width), y = random.Next(height);
        Set(x, y, !solid[y * width + x]);
    }
    Compare(35 + n % 7 + .23f, 31 + n % 5 + .41f, n % 3 == 0 ? .8f : 1);
}
Compare(38.3f, 33.4f, 1, false); Compare(38.3f, 33.4f, 1, true);
Compare(-.3f, .3f); Compare(79.3f, 79.4f);

// Isolate repeated nearby mining from source movement and GPU uploads.
const int size = 128;
var benchmarkSolid = new bool[size * size];
benchmarkSolid[64 * size + 64] = true;
var fast = new SmoothHeadlampField(size, size, benchmarkSolid, 1, 1, 10);
var full = new SmoothHeadlampField(size, size, benchmarkSolid, 1, 1, 10);
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var cacheField = typeof(SmoothHeadlampField).GetField("samples", flags);
var terrainDirty = typeof(SmoothHeadlampField).GetField("terrainDirty", flags);
void ForceFull(SmoothHeadlampField field)
{
    foreach (var cache in (System.Array)cacheField.GetValue(field))
        cache.GetType().GetField("valid").SetValue(cache, false);
    terrainDirty.SetValue(field, true);
}
var incrementalTimes = new System.Collections.Generic.List<double>();
var fullTimes = new System.Collections.Generic.List<double>();
for (int run = 0; run < 9; run++)
{
    foreach (var field in new[] { fast, full })
    {
        field.SetSolid(64, 64, true);
        foreach (int index in field.Rebuild(61.3f, 63.4f, 1, true)) { }
        field.SetSolid(64, 64, false);
    }
    ForceFull(full);
    var watch = System.Diagnostics.Stopwatch.StartNew();
    foreach (int index in fast.Rebuild(61.3f, 63.4f, 1, true)) { }
    watch.Stop(); incrementalTimes.Add(watch.Elapsed.TotalMilliseconds);
    watch.Restart();
    foreach (int index in full.Rebuild(61.3f, 63.4f, 1, true)) { }
    watch.Stop(); fullTimes.Add(watch.Elapsed.TotalMilliseconds);
    for (int i = 0; i < size * size; i++) Check(Mathf.Abs(fast.Get(i) - full.Get(i)) < .0001f, "Benchmark light mismatch.");
}
incrementalTimes.Sort(); fullTimes.Sort();
return new { passed = true, comparisons, maximumDifference,
    incrementalMedianMs = incrementalTimes[4], fullMedianMs = fullTimes[4] };
