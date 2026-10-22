// Procedural painter for the ladder segment variants (Design/Ladder).
// Build: csc /r:System.Drawing.dll /out:<scratch>\LadderPainter.exe LadderPainter.cs
// Run:   LadderPainter.exe <outputDir>
// Every variant is one square tile (1254 px, like Assets/GameObjects/Map/Ladders/LadderSegment.png).
// Rails depend on y only through tile-periodic noise, so stacked tiles join seamlessly.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

struct Col
{
    public float R, G, B;
    public Col(float r, float g, float b) { R = r; G = g; B = b; }
    public static Col Hex(string h)
    {
        return new Col(Convert.ToInt32(h.Substring(0, 2), 16) / 255f, Convert.ToInt32(h.Substring(2, 2), 16) / 255f, Convert.ToInt32(h.Substring(4, 2), 16) / 255f);
    }
    public static Col Lerp(Col a, Col b, float t) { return new Col(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t); }
    public Col Mul(float k) { return new Col(R * k, G * k, B * k); }
    public Col Add(float k) { return new Col(R + k, G + k, B + k); }
}

class Ramp
{
    public Col D, M, L, S;
    public Ramp(string d, string m, string l, string s) { D = Col.Hex(d); M = Col.Hex(m); L = Col.Hex(l); S = Col.Hex(s); }
    public Col At(float v)
    {
        if (v < 0.5f) return Col.Lerp(D, M, Math.Max(0, v) * 2);
        if (v < 0.85f) return Col.Lerp(M, L, (v - 0.5f) / 0.35f);
        return Col.Lerp(L, S, Math.Min(1, (v - 0.85f) / 0.15f));
    }
}

delegate float Sdf(float x, float y);
delegate Col Mat(float x, float y, float d, float nx, float ny);

class Canvas
{
    public int W, H;
    public float[] R, G, B, A;
    public Canvas(int w, int h) { W = w; H = h; R = new float[w * h]; G = new float[w * h]; B = new float[w * h]; A = new float[w * h]; }

    public void Blend(int x, int y, Col c, float a)
    {
        if (x < 0 || y < 0 || x >= W || y >= H || a <= 0) return;
        int i = y * W + x;
        float ao = A[i], na = a + ao * (1 - a);
        if (na <= 0) return;
        R[i] = (c.R * a + R[i] * ao * (1 - a)) / na;
        G[i] = (c.G * a + G[i] * ao * (1 - a)) / na;
        B[i] = (c.B * a + B[i] * ao * (1 - a)) / na;
        A[i] = na;
    }

    public void Darken(int x, int y, float k)
    {
        if (x < 0 || y < 0 || x >= W || y >= H || k <= 0) return;
        int i = y * W + x;
        float f = 1 - k * A[i];
        R[i] *= f; G[i] *= f; B[i] *= f;
    }

    public void Save(string path)
    {
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        {
            var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[W * H * 4];
            for (int i = 0; i < W * H; i++)
            {
                buf[i * 4 + 0] = B8(B[i]); buf[i * 4 + 1] = B8(G[i]); buf[i * 4 + 2] = B8(R[i]); buf[i * 4 + 3] = B8(A[i]);
                if (buf[i * 4 + 3] == 0) { buf[i * 4] = 0; buf[i * 4 + 1] = 0; buf[i * 4 + 2] = 0; }
            }
            Marshal.Copy(buf, 0, data.Scan0, buf.Length);
            bmp.UnlockBits(data);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    static byte B8(float v) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }
}

static class N
{
    public static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    public static float Smooth(float a, float b, float v) { float t = Clamp01((v - a) / (b - a)); return t * t * (3 - 2 * t); }
    public static float Frac(float v) { return v - (float)Math.Floor(v); }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xffffff) / 16777215f;
        }
    }

    static int Wrap(int v, int p) { if (p <= 0) return v; v %= p; return v < 0 ? v + p : v; }

    // Value noise; px/py > 0 make it periodic in that axis.
    public static float Noise(float x, float y, int px, int py, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int xa = Wrap(x0, px), xb = Wrap(x0 + 1, px), ya = Wrap(y0, py), yb = Wrap(y0 + 1, py);
        float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }

    public static float Fbm(float x, float y, int px, int py, int seed, int oct)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (int o = 0; o < oct; o++)
        {
            sum += Noise(x, y, px, py, seed + o * 17) * amp;
            norm += amp; amp *= 0.5f; x *= 2; y *= 2; px *= 2; py *= 2;
        }
        return sum / norm;
    }

    public static float Box(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = Math.Abs(x - cx) - hx + r, qy = Math.Abs(y - cy) - hy + r;
        float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    public static float Circle(float x, float y, float cx, float cy, float r)
    {
        float dx = x - cx, dy = y - cy;
        return (float)Math.Sqrt(dx * dx + dy * dy) - r;
    }
}

class Painter
{
    public const int S = 1254;
    public Canvas C = new Canvas(S, S);
    public float Outline = 13;
    public Col Ink = Col.Hex("1c110a");
    // Light from the upper left (image y points down).
    public const float LX = -0.55f, LY = -0.83f;

    public void Fill(Sdf sdf, Mat mat, float x0, float y0, float x1, float y1)
    {
        int ix0 = Math.Max(0, (int)x0 - 2), iy0 = Math.Max(0, (int)y0 - 2);
        int ix1 = Math.Min(S - 1, (int)x1 + 2), iy1 = Math.Min(S - 1, (int)y1 + 2);
        for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = sdf(px, py);
                if (d > 1) continue;
                float cov = N.Clamp01(0.5f - d);
                float inner = N.Clamp01(-(d + Outline) + 0.5f);
                Col col = Ink;
                if (inner > 0)
                {
                    float nx = sdf(px + 1, py) - sdf(px - 1, py), ny = sdf(px, py + 1) - sdf(px, py - 1);
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny);
                    if (nl > 1e-5f) { nx /= nl; ny /= nl; } else { nx = 0; ny = 0; }
                    col = Col.Lerp(Ink, mat(px, py, d + Outline, nx, ny), inner);
                }
                C.Blend(x, y, col, cov);
            }
    }

    // Soft drop shadow, drawn below the parts so it only shows on the wall behind the ladder.
    public void Shadow(Sdf sdf, float ox, float oy, float soft, float alpha, float x0, float y0, float x1, float y1)
    {
        int ix0 = Math.Max(0, (int)(x0 + ox - soft) - 2), iy0 = Math.Max(0, (int)(y0 + oy - soft) - 2);
        int ix1 = Math.Min(S - 1, (int)(x1 + ox + soft) + 2), iy1 = Math.Min(S - 1, (int)(y1 + oy + soft) + 2);
        for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                float d = sdf(x + 0.5f - ox, y + 0.5f - oy);
                float a = alpha * N.Smooth(soft, -soft, d);
                C.Blend(x, y, new Col(0.03f, 0.02f, 0.01f), a);
            }
    }

    // Contact shadow cast onto parts that are already painted (rails below a rung).
    public void ShadeOnto(Sdf sdf, float ox, float oy, float soft, float k, float x0, float y0, float x1, float y1)
    {
        int ix0 = Math.Max(0, (int)(x0 + ox - soft) - 2), iy0 = Math.Max(0, (int)(y0 + oy - soft) - 2);
        int ix1 = Math.Min(S - 1, (int)(x1 + ox + soft) + 2), iy1 = Math.Min(S - 1, (int)(y1 + oy + soft) + 2);
        for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
                C.Darken(x, y, k * N.Smooth(soft, -soft, sdf(x + 0.5f - ox, y + 0.5f - oy)));
    }

    // Lighting of the bevelled rim of a flat part: 1 = lit edge, 0 = interior.
    public static float Rim(float d, float nx, float ny, float width)
    {
        float edge = N.Clamp01(1 + d / width);
        return edge * (nx * LX + ny * LY);
    }

    // Painted wood. u runs across the board (0..1), v along it in pixels; tile-periodic in v when periodic is true.
    public static float WoodValue(float u, float v, int seed, bool periodic, float grainLines, float crackAmount)
    {
        const int P = 16;
        float vv = v / S * P;
        int py = periodic ? P : 0;
        float warp = N.Fbm(u * 2.5f, vv * 0.5f, 0, periodic ? P / 2 : 0, seed + 3, 3);
        float body = N.Fbm(u * 3.5f, vv * 1.0f, 0, py, seed, 4);
        float streak = N.Fbm(u * 18 + warp * 4, vv * 0.25f, 0, periodic ? P / 4 : 0, seed + 9, 3);
        float g = N.Frac(u * grainLines + warp * 1.8f);
        float line = N.Smooth(0.07f, 0.0f, Math.Abs(g - 0.5f) - 0.0f) ;
        float crackField = N.Noise(u * 9, vv * 2.2f, 0, periodic ? P * 2 : 0, seed + 31);
        float crack = N.Smooth(0.86f, 0.93f, crackField) * N.Smooth(0.12f, 0.0f, Math.Abs(N.Frac(u * 5 + warp) - 0.5f) - 0.05f);
        float value = 0.42f * body + 0.58f * streak;
        value = 0.25f + value * 0.75f;
        value -= line * 0.22f;
        value -= crack * crackAmount;
        // Slight posterisation gives the painted, banded look of the building art.
        float q = (float)Math.Round(value * 7) / 7;
        return value + (q - value) * 0.35f;
    }

    public static Col Dome(float x, float y, float cx, float cy, float r, Ramp ramp)
    {
        float dx = (x - cx) / r, dy = (y - cy) / r;
        float rr = dx * dx + dy * dy;
        float nz = (float)Math.Sqrt(Math.Max(0, 1 - rr));
        float diff = -dx * 0.5f - dy * 0.65f + nz * 0.57f;
        float spec = (float)Math.Pow(N.Clamp01(diff), 14);
        return ramp.At(N.Clamp01(0.42f + diff * 0.45f + spec * 0.35f));
    }

    // Rivets and studs get a thinner ink line so the metal stays visible.
    public void Rivet(float cx, float cy, float r, Ramp ramp)
    {
        float keep = Outline;
        Outline = Math.Max(4, r * 0.24f);
        float inner = r - Outline;
        Fill((x, y) => N.Circle(x, y, cx, cy, r), (x, y, d, nx, ny) => Dome(x, y, cx, cy, inner, ramp), cx - r, cy - r, cx + r, cy + r);
        Outline = keep;
    }

    // Pyramid stud like on the surface buildings.
    public void Stud(float cx, float cy, float r, Ramp ramp)
    {
        float keep = Outline;
        Outline = Math.Max(5, r * 0.18f);
        Fill((x, y) => (Math.Abs(x - cx) + Math.Abs(y - cy) - r) * 0.7071f,
            (x, y, d, nx, ny) =>
            {
                float dx = x - cx, dy = y - cy;
                float inner = Math.Abs(dx) + Math.Abs(dy);
                float face;
                if (inner < r * 0.18f) face = 0.97f;
                else if (Math.Abs(dx) > Math.Abs(dy)) face = dx < 0 ? 0.78f : 0.38f;
                else face = dy < 0 ? 0.88f : 0.3f;
                return ramp.At(face);
            }, cx - r, cy - r, cx + r, cy + r);
        Outline = keep;
    }

    // Flat metal plate with bevelled, light-catching rim and fine brushed noise.
    public Mat Plate(Ramp ramp, float bevel, float baseValue, int seed)
    {
        return (x, y, d, nx, ny) =>
        {
            float n = N.Fbm(x / 60f, y / 22f, 0, 0, seed, 3);
            float spots = N.Smooth(0.68f, 0.8f, N.Fbm(x / 35f, y / 35f, 0, 0, seed + 5, 2));
            float v = baseValue + (n - 0.5f) * 0.16f - spots * 0.12f + Rim(d, nx, ny, bevel) * 0.38f;
            // Gentle top-to-bottom sheen.
            return ramp.At(N.Clamp01(v));
        };
    }
}

static class Program
{
    const int S = Painter.S;

    static readonly Ramp Iron = new Ramp("17181b", "4a4d53", "8d9096", "d8dade");
    static readonly Ramp Brass = new Ramp("5a3306", "c48a1c", "f2c449", "fff3bd");
    static readonly Ramp HoneyWood = new Ramp("4a2a12", "9a6331", "d39a55", "f2cf93");
    static readonly Ramp DarkOak = new Ramp("2a170b", "5f3a1e", "93623a", "c08b5a");
    static readonly Ramp WarmWood = new Ramp("3a2211", "7b4f2a", "b57f48", "e0b07a");
    static readonly Ramp GreyWood = new Ramp("2b231d", "5f5144", "958370", "c8b8a0");
    static readonly Ramp Hemp = new Ramp("3c2810", "8a6532", "c9a263", "efd9a6");

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(dir);
        PaintA().C.Save(Path.Combine(dir, "LadderSegment_A_Grubenleiter.png"));
        PaintB().C.Save(Path.Combine(dir, "LadderSegment_B_Messing.png"));
        PaintC().C.Save(Path.Combine(dir, "LadderSegment_C_Seilleiter.png"));
        PaintD().C.Save(Path.Combine(dir, "LadderSegment_D_Schutzleisten.png"));
        Console.WriteLine("ok");
    }

    // Square rail spanning the whole tile height (no rounding at the tile borders).
    static Sdf Rail(float cx, float hw) { return (x, y) => Math.Abs(x - cx) - hw; }

    static Mat RailWood(float cx, float hw, Ramp ramp, int seed, float grain, float crack, float roundness)
    {
        return (x, y, d, nx, ny) =>
        {
            float u = (x - (cx - hw)) / (2 * hw);
            float v = Painter.WoodValue(u, y, seed, true, grain, crack);
            float t = u * 2 - 1;
            // Flat face with chamfered sides, or a rounded beam.
            float side = roundness > 0 ? (-t * 0.22f) * roundness + ((float)Math.Sqrt(Math.Max(0, 1 - t * t)) - 0.8f) * 0.3f * roundness : 0;
            v += side + Painter.Rim(d, nx, ny, 20) * 0.32f;
            return ramp.At(N.Clamp01(v));
        };
    }

    static Mat RungWood(float cy, float hh, Ramp ramp, int seed, float grain, float crack, float cylinder)
    {
        return (x, y, d, nx, ny) =>
        {
            float u = (y - (cy - hh)) / (2 * hh);
            float v = Painter.WoodValue(u, x + seed * 31, seed, false, grain, crack);
            float t = u * 2 - 1;
            float shade = cylinder > 0
                ? (-t * 0.33f + ((float)Math.Sqrt(Math.Max(0, 1 - t * t)) - 0.7f) * 0.35f) * cylinder
                : 0;
            v += shade + Painter.Rim(d, nx, ny, 18) * 0.3f;
            return ramp.At(N.Clamp01(v));
        };
    }

    // A: heavy mine ladder, dark oak, iron plates with four rivets, iron bands at the tile seam.
    static Painter PaintA()
    {
        var p = new Painter { Outline = 14 };
        float lx = 272, rx = 982, hw = 78;
        float[] rungs = { S * 0.25f, S * 0.75f };
        float rh = 62;
        foreach (var cx in new[] { lx, rx }) { float c = cx; p.Shadow(Rail(c, hw), 16, 0, 14, 0.38f, c - hw, 0, c + hw, S); }
        foreach (var cy in rungs) { float c = cy; p.Shadow((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f, rh, 6), 16, 20, 14, 0.38f, lx, c - rh, rx, c + rh); }
        foreach (var cx in new[] { lx, rx })
        {
            float c = cx;
            p.Fill(Rail(c, hw), RailWood(c, hw, DarkOak, (int)c, 5.5f, 0.32f, 0), c - hw, 0, c + hw, S);
        }
        foreach (var cy in rungs)
        {
            float c = cy;
            p.ShadeOnto((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f + hw, rh, 6), 0, 26, 18, 0.45f, lx - hw, c - rh, rx + hw, c + rh);
            p.Fill((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f, rh, 8), RungWood(c, rh, DarkOak, (int)c, 4.5f, 0.3f, 0), lx, c - rh, rx, c + rh);
            foreach (var sx in new[] { lx, rx })
            {
                float s = sx;
                float ph = 96, pw = hw + 22;
                p.ShadeOnto((x, y) => N.Box(x, y, s, c, pw, ph, 14), 8, 14, 12, 0.4f, s - pw, c - ph, s + pw, c + ph);
                p.Fill((x, y) => N.Box(x, y, s, c, pw, ph, 14), p.Plate(Iron, 22, 0.45f, (int)(s + c)), s - pw, c - ph, s + pw, c + ph);
                foreach (var ox in new[] { -1f, 1f })
                    foreach (var oy in new[] { -1f, 1f })
                        p.Rivet(s + ox * (pw - 32), c + oy * (ph - 32), 22, Iron);
                p.Rivet(s, c, 26, Iron);
            }
        }
        // Iron band around each rail at the tile seam (drawn at y = 0 and y = S so both halves meet).
        foreach (var cx in new[] { lx, rx })
            foreach (var by in new[] { 0f, (float)S })
            {
                float c = cx, b = by;
                p.ShadeOnto((x, y) => N.Box(x, y, c, b, hw + 8, 30, 6), 0, 14, 10, 0.4f, c - hw - 8, b - 30, c + hw + 8, b + 30);
                p.Fill((x, y) => N.Box(x, y, c, b, hw + 10, 30, 6), p.Plate(Iron, 14, 0.42f, (int)c + 77), c - hw - 10, b - 30, c + hw + 10, b + 30);
                p.Rivet(c, b == 0 ? 0 + 0.01f : S - 0.01f, 15, Iron);
            }
        return p;
    }

    // B: planed honey wood, round dowel rungs, brass rosettes with pyramid studs and brass ferrules.
    static Painter PaintB()
    {
        var p = new Painter { Outline = 13 };
        float lx = 276, rx = 978, hw = 62;
        float[] rungs = { S * 0.25f, S * 0.75f };
        float rh = 44;
        foreach (var cx in new[] { lx, rx }) { float c = cx; p.Shadow(Rail(c, hw), 14, 0, 14, 0.36f, c - hw, 0, c + hw, S); }
        foreach (var cy in rungs) { float c = cy; p.Shadow((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f, rh, rh), 14, 18, 14, 0.36f, lx, c - rh, rx, c + rh); }
        foreach (var cx in new[] { lx, rx })
        {
            float c = cx;
            p.Fill(Rail(c, hw), RailWood(c, hw, HoneyWood, (int)c + 5, 4.0f, 0.12f, 0.7f), c - hw, 0, c + hw, S);
        }
        foreach (var cy in rungs)
        {
            float c = cy;
            p.ShadeOnto((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f + hw, rh, rh), 0, 22, 16, 0.4f, lx - hw, c - rh, rx + hw, c + rh);
            p.Fill((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f - hw + 10, rh, rh * 0.35f), RungWood(c, rh, HoneyWood, (int)c + 1, 3.0f, 0.08f, 1f), lx + hw - 10, c - rh, rx - hw + 10, c + rh);
            foreach (var sx in new[] { lx, rx })
            {
                float s = sx, dir = sx < S / 2 ? 1 : -1;
                // Brass ferrule on the dowel next to the rail.
                float fx = s + dir * (hw + 34);
                p.Fill((x, y) => N.Box(x, y, fx, c, 22, rh + 8, 8), (x, y, d, nx, ny) =>
                {
                    float t = (y - c) / (rh + 8);
                    float v = 0.6f - t * 0.35f + ((float)Math.Sqrt(Math.Max(0, 1 - t * t)) - 0.7f) * 0.4f + Painter.Rim(d, nx, ny, 10) * 0.2f;
                    return Brass.At(N.Clamp01(v));
                }, fx - 22, c - rh - 8, fx + 22, c + rh + 8);
                // Brass rosette on the rail.
                float pw = hw + 18, ph = 84;
                p.ShadeOnto((x, y) => N.Box(x, y, s, c, pw, ph, 30), 8, 14, 12, 0.4f, s - pw, c - ph, s + pw, c + ph);
                p.Fill((x, y) => N.Box(x, y, s, c, pw, ph, 30), p.Plate(Brass, 20, 0.5f, (int)(s + c)), s - pw, c - ph, s + pw, c + ph);
                p.Stud(s, c, 38, Brass);
                p.Rivet(s, c - ph + 26, 13, Brass);
                p.Rivet(s, c + ph - 26, 13, Brass);
            }
        }
        // Small brass rivets on the rails between the rungs.
        foreach (var cx in new[] { lx, rx })
            foreach (var ry in new[] { 0f, S * 0.5f, (float)S })
                p.Rivet(cx, ry, 17, Brass);
        return p;
    }

    // C: rope ladder, twisted hemp ropes with round wooden rungs lashed on.
    static Painter PaintC()
    {
        var p = new Painter { Outline = 11 };
        float lx = 270, rx = 984, hw = 40;
        float[] rungs = { S * 0.25f, S * 0.75f };
        float rh = 46;
        float period = S / 22f;
        Func<float, Sdf> ropeSdf = cx => (x, y) =>
        {
            float f = N.Frac((y - (x - cx) * 0.9f) / period);
            float bulge = (float)Math.Sin(Math.PI * f);
            return Math.Abs(x - cx) - hw * (0.9f + 0.1f * bulge);
        };
        Func<float, Mat> ropeMat = cx => (x, y, d, nx, ny) =>
        {
            float t = (x - cx) / hw;
            float f = N.Frac((y - (x - cx) * 0.9f) / period);
            float strand = (float)Math.Sin(Math.PI * f);
            float fibre = N.Fbm((x - cx) / 6f + y / 9f, (y - (x - cx) * 0.9f) / 4f, 0, 0, 11, 2);
            float cyl = -t * 0.25f + ((float)Math.Sqrt(Math.Max(0, 1 - t * t)) - 0.7f) * 0.3f;
            float v = 0.26f + strand * 0.42f + cyl + (fibre - 0.5f) * 0.18f - N.Smooth(0.14f, 0.0f, Math.Min(f, 1 - f)) * 0.2f;
            v += (strand - 0.5f) * (-t) * 0.1f;
            return Hemp.At(N.Clamp01(v));
        };
        foreach (var cx in new[] { lx, rx }) { float c = cx; p.Shadow(ropeSdf(c), 14, 0, 12, 0.36f, c - hw, 0, c + hw, S); }
        foreach (var cy in rungs) { float c = cy; p.Shadow((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f + 46, rh, rh), 14, 18, 14, 0.36f, lx - 46, c - rh, rx + 46, c + rh); }
        // Rungs sit behind the ropes and poke out on both sides.
        foreach (var cy in rungs)
        {
            float c = cy;
            p.Fill((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f + 46, rh, rh * 0.45f), RungWood(c, rh, WarmWood, (int)c + 7, 3.5f, 0.15f, 1f), lx - 46, c - rh, rx + 46, c + rh);
        }
        foreach (var cx in new[] { lx, rx }) { float c = cx; p.Fill(ropeSdf(c), ropeMat(c), c - hw, 0, c + hw, S); }
        // Lashing: three wraps across each rope/rung junction plus a diagonal cross.
        foreach (var cy in rungs)
            foreach (var sx in new[] { lx, rx })
            {
                float c = cy, s = sx;
                p.ShadeOnto((x, y) => N.Box(x, y, s, c, hw + 20, 52, 14), 0, 12, 10, 0.4f, s - hw - 20, c - 52, s + hw + 20, c + 52);
                foreach (var oy in new[] { -36f, 0f, 36f })
                {
                    float wy = c + oy;
                    p.Fill((x, y) => N.Box(x, y, s, wy + (x - s) * 0.12f, hw + 22, 16, 15), (x, y, d, nx, ny) =>
                    {
                        float t = (y - wy - (x - s) * 0.12f) / 16f;
                        float f = N.Frac((x + (y - wy) * 1.1f) / 18f);
                        float strand = (float)Math.Sin(Math.PI * f);
                        float v = 0.3f + strand * 0.38f - t * 0.2f + ((float)Math.Sqrt(Math.Max(0, 1 - t * t)) - 0.7f) * 0.3f;
                        return Hemp.At(N.Clamp01(v));
                    }, s - hw - 24, wy - 30, s + hw + 24, wy + 30);
                }
            }
        return p;
    }

    // D: weathered grey timber with iron guard strips along the rails and iron nosing on worn rungs.
    static Painter PaintD()
    {
        var p = new Painter { Outline = 13 };
        float lx = 274, rx = 980, hw = 64;
        float[] rungs = { S * 0.25f, S * 0.75f };
        float rh = 54;
        float gw = 20; // guard strip half width
        foreach (var cx in new[] { lx, rx }) { float c = cx; p.Shadow(Rail(c, hw + 10), 14, 0, 14, 0.36f, c - hw - 10, 0, c + hw + 10, S); }
        foreach (var cy in rungs) { float c = cy; p.Shadow((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f, rh, 6), 14, 18, 14, 0.36f, lx, c - rh, rx, c + rh); }
        foreach (var cx in new[] { lx, rx })
        {
            float c = cx;
            p.Fill(Rail(c, hw), RailWood(c, hw, GreyWood, (int)c + 13, 6.5f, 0.45f, 0), c - hw, 0, c + hw, S);
        }
        foreach (var cy in rungs)
        {
            float c = cy;
            p.ShadeOnto((x, y) => N.Box(x, y, S / 2f, c, (rx - lx) / 2f + hw, rh, 6), 0, 24, 18, 0.45f, lx - hw, c - rh, rx + hw, c + rh);
            // Worn rung: the middle of the step is hollowed out and polished lighter.
            Sdf rung = (x, y) => Math.Max(N.Box(x, y, S / 2f, c, (rx - lx) / 2f, rh, 8), -N.Circle(x, y, S / 2f, c - rh - 1180, 1196));
            Mat wood = RungWood(c, rh, GreyWood, (int)c + 2, 5.0f, 0.4f, 0);
            p.Fill(rung, (x, y, d, nx, ny) =>
            {
                float wear = N.Smooth(260, 60, Math.Abs(x - S / 2f));
                Col col = wood(x, y, d, nx, ny);
                return Col.Lerp(col, Col.Lerp(col, Col.Hex("c9b89a"), 0.45f), wear * 0.55f);
            }, lx, c - rh, rx, c + rh);
            // Iron nosing strip on the front-bottom edge of the step.
            float sy = c + rh - 16;
            p.Fill((x, y) => N.Box(x, y, S / 2f, sy, (rx - lx) / 2f - hw - 6, 16, 5), p.Plate(Iron, 10, 0.42f, (int)c), lx + hw, sy - 16, rx - hw, sy + 16);
            foreach (var rxp in new[] { S / 2f - 220, S / 2f, S / 2f + 220 }) p.Rivet(rxp, sy, 14, Iron);
            // Iron strap brackets wrapping the rung ends onto the rails.
            foreach (var sx in new[] { lx, rx })
            {
                float s = sx;
                p.ShadeOnto((x, y) => N.Box(x, y, s, c, hw + 6, rh + 22, 8), 6, 12, 10, 0.4f, s - hw - 6, c - rh - 22, s + hw + 6, c + rh + 22);
                p.Fill((x, y) => N.Box(x, y, s, c, hw + 6, rh + 22, 8), p.Plate(Iron, 16, 0.4f, (int)(s * 3 + c)), s - hw - 6, c - rh - 22, s + hw + 6, c + rh + 22);
                p.Rivet(s - hw * 0.45f, c - rh + 4, 18, Iron);
                p.Rivet(s + hw * 0.45f, c + rh - 4, 18, Iron);
            }
        }
        // Guard strips along the outer edge of each rail, riveted every quarter tile (rivets stay inside the tile).
        foreach (var cx in new[] { lx, rx })
        {
            float c = cx, gx = cx < S / 2 ? cx - hw + gw - 2 : cx + hw - gw + 2;
            p.Fill((x, y) => Math.Abs(x - gx) - gw, (x, y, d, nx, ny) =>
            {
                float n = N.Fbm(x / 40f, y / S * 24, 0, 24, 41, 3);
                float rust = N.Smooth(0.62f, 0.78f, N.Fbm(x / 30f, y / S * 18, 0, 18, 43, 3));
                Col iron = Iron.At(N.Clamp01(0.42f + (n - 0.5f) * 0.2f + Painter.Rim(d, nx, ny, 8) * 0.35f));
                return Col.Lerp(iron, Col.Hex("6b3a1c"), rust * 0.55f);
            }, gx - gw, 0, gx + gw, S);
            foreach (var ry in new[] { S * 0.125f, S * 0.375f, S * 0.625f, S * 0.875f }) p.Rivet(gx, ry, 17, Iron);
        }
        return p;
    }
}
