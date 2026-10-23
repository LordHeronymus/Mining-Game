using System;
using System.Collections.Generic;

// Replay ledger of the revealed, mined and ore cells of one run (Docs/Progression.md "Grösse des Profils"). Instead of one
// "mined:x:y" string per cell, a run keeps one "#1" record per kind and 32x16 chunk in MetaRunState.resourceSources; older
// builds keep, validate, union and write back these records like any other ledger string, and a string union of records is
// the bitwise OR of their cells. record = "#1" kind ":" cx ":" cy ":" payload, kind "R" (revealed), "M" (mined) or "O<item>",
// payload "f" (all 512 cells), "s"/"i" + 1..42 base64url index pairs (exactly these / all but these) or "b" + 86 base64url
// digits (bitmap, Convert.ToBase64String bit order). A "#" string that does not decode is opaque: kept verbatim, never a
// reason to reject. Pure C# without UnityEngine; only the main thread touches an instance, workers only see string lists.
public sealed class MetaCellLedger
{
    public const int Revealed = 0, Mined = 1, OreBase = 2, MaxItem = 65535, Limit = 1 << 20, SparseMax = 42, MaxRecordLength = 110;
    // Every prefix RecordMinedBlock treats as "already mined" ("mined:" + MetaProgression.LegacyMinedKeys).
    public static readonly string[] MinedPrefixes = { "mined:", "l2-block:", "l3-block:", "xxl-layer:4:", "xxl-layer:5:", "surface-block:" };
    const string Digits = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    sealed class Chunk
    {
        public int kind, cx, cy, count;
        public readonly uint[] rows = new uint[16];   // cell (x, y) is bit x & 31 of rows[y & 15]
        public string encoded;                        // record of the current bits, null = changed
        public string listed;                         // record now in the live list (write mode), null = none
        public bool queued;
    }
    readonly Dictionary<long, Chunk> chunks = new();
    readonly List<Chunk> changed = new();          // write mode only; emission order = first change
    long cachedKey = long.MinValue; Chunk cached;  // one-entry cache, also caches misses
    public bool Writes { get; }
    // An in-domain cell-like key that cannot be folded (unknown culture minus, leading zeros): the old string lookup stays active.
    public bool StringCells { get; private set; }
    public int ChunkCount => chunks.Count;
    public int Folded { get; private set; }        // legacy keys, duplicate and empty records absorbed by Attach
    MetaCellLedger(bool writes) { Writes = writes; }

    public static int OreKind(int item) => item >= 0 && item <= MaxItem ? OreBase + item : -1;
    public static bool InDomain(int kind, int x, int y) => kind >= 0 && kind <= OreBase + MaxItem && x >= -Limit && x < Limit && y >= -Limit && y < Limit;
    static long Key(int kind, int cx, int cy) => ((long)kind << 34) | ((long)(cy + 65536) << 16) | (long)(cx + 32768);

    // Arithmetic shifts floor negative coordinates: cx = x >> 5, cy = y >> 4.
    Chunk Find(int kind, int x, int y, bool create)
    {
        int cx = x >> 5, cy = y >> 4; long key = Key(kind, cx, cy);
        if (key != cachedKey || (cached == null && create)) {
            if (!chunks.TryGetValue(key, out var c) && create) chunks.Add(key, c = new Chunk { kind = kind, cx = cx, cy = cy });
            cachedKey = key; cached = c;
        }
        return cached;
    }
    public bool Contains(int kind, int x, int y)
    {
        if (!InDomain(kind, x, y)) return false;
        var c = Find(kind, x, y, false);
        return c != null && (c.rows[y & 15] & (1u << (x & 31))) != 0;
    }
    // true = newly set. Out of domain: false and nothing recorded (callers check InDomain first).
    public bool Add(int kind, int x, int y)
    {
        if (!InDomain(kind, x, y)) return false;
        var c = Find(kind, x, y, true); uint bit = 1u << (x & 31);
        if ((c.rows[y & 15] & bit) != 0) return false;
        c.rows[y & 15] |= bit; c.count++; c.encoded = null; Queue(c); return true;
    }
    void Queue(Chunk c) { if (Writes && !c.queued) { c.queued = true; changed.Add(c); } }

    // Ledger of one list. Write mode removes the folded cells and records from the list (the next Flush appends one record per
    // chunk); read mode leaves the list untouched. others receives every string that is not folded (markers, opaque records,
    // out-of-domain and unfoldable cell keys), in list order.
    public static MetaCellLedger Attach(List<string> list, HashSet<string> others, bool writes)
    {
        var ledger = new MetaCellLedger(writes); var rows = new uint[16];
        var kept = writes ? new List<string>(list.Count) : null;
        int kind, x, y, cx, cy;
        foreach (string key in list) {
            if (!string.IsNullOrEmpty(key)) {
                if (key[0] == '#') { if (TryDecode(key, rows, out kind, out cx, out cy)) { ledger.Merge(kind, cx, cy, rows, key); continue; } }
                else if (TryParseLegacy(key, out kind, out x, out y)) { if (InDomain(kind, x, y)) { ledger.Folded++; ledger.Add(kind, x, y); continue; } }
                else if (LooksLikeCellKey(key)) ledger.StringCells = true;   // e.g. an unknown culture minus: old string lookup stays active
            }
            others?.Add(key); kept?.Add(key);
        }
        if (writes) { list.Clear(); list.AddRange(kept); }   // non-cell keys in original order; Flush appends the chunks
        return ledger;
    }
    void Merge(int kind, int cx, int cy, uint[] rows, string text)
    {
        int count = 0; for (int r = 0; r < 16; r++) count += PopCount(rows[r]);
        if (count == 0) { Folded++; return; }                      // all-zero record: dropped
        long key = Key(kind, cx, cy);
        if (!chunks.TryGetValue(key, out var c)) {
            c = new Chunk { kind = kind, cx = cx, cy = cy, count = count, encoded = text, listed = Writes ? null : text };
            Array.Copy(rows, c.rows, 16); chunks.Add(key, c); if (key == cachedKey) cached = c;
            Queue(c); return;                                      // single unchanged record: reused verbatim, no re-encode
        }
        Folded++; bool grew = false;                               // second record of one chunk (older-build union, world save)
        for (int r = 0; r < 16; r++) { uint add = rows[r] & ~c.rows[r]; if (add != 0) { c.rows[r] |= add; c.count += PopCount(add); grew = true; } }
        if (grew) { c.encoded = null; Queue(c); }
    }

    // Write mode only: replaces the record of every changed chunk in the live list (removal by value, so no slots are kept).
    // true = the list changed. Never call it while the list is being enumerated.
    public bool Flush(List<string> list)
    {
        if (!Writes || changed.Count == 0) return false;
        HashSet<string> stale = null;
        foreach (var c in changed) if (c.listed != null) (stale ??= new HashSet<string>(StringComparer.Ordinal)).Add(c.listed);
        if (stale != null) list.RemoveAll(s => !string.IsNullOrEmpty(s) && s[0] == '#' && stale.Contains(s));
        foreach (var c in changed) { c.queued = false; c.encoded ??= Encode(c.kind, c.cx, c.cy, c.rows); list.Add(c.encoded); c.listed = c.encoded; }
        changed.Clear(); return true;
    }

    // Headers only: an in-domain legacy cell key or two records with the same (kind, cx, cy).
    public static bool NeedsCompaction(List<string> list)
    {
        HashSet<long> heads = null;
        foreach (string s in list) {
            if (string.IsNullOrEmpty(s)) continue; char c = s[0];
            if (c == '#') { if (TryParseHeader(s, out int k, out int cx, out int cy, out _) && !(heads ??= new HashSet<long>()).Add(Key(k, cx, cy))) return true; }
            else if ((c == 'r' || c == 'm' || c == 'l' || c == 'x' || c == 's' || (c >= '0' && c <= '9')) && TryParseLegacy(s, out int kind, out int x, out int y) && InDomain(kind, x, y)) return true;
        }
        return false;
    }
    // Folds legacy cell keys and duplicate records of the list into one record per chunk. true only if something was folded,
    // so an opaque string with a valid header (kept verbatim) can never cause a write loop.
    public static bool Compact(List<string> list)
    {
        if (!NeedsCompaction(list)) return false;
        var ledger = Attach(list, null, true); ledger.Flush(list); return ledger.Folded > 0;
    }

    // ---------- codec ----------
    // Canonical record of a non-empty chunk (writer rule: 512 cells "f", up to 42 "s", up to 42 missing "i", else "b").
    public static string Encode(int kind, int cx, int cy, uint[] rows)
    {
        if (rows == null || rows.Length < 16) throw new ArgumentException("A chunk has 16 rows.", nameof(rows));
        if (kind < 0 || kind > OreBase + MaxItem || cx < -32768 || cx > 32767 || cy < -65536 || cy > 65535) throw new ArgumentOutOfRangeException(nameof(kind), "Chunk outside the record domain.");
        int count = 0; for (int r = 0; r < 16; r++) count += PopCount(rows[r]);
        if (count == 0) throw new ArgumentException("Empty chunks are never written.", nameof(rows));
        var text = new char[MaxRecordLength]; int n = 0;
        text[n++] = '#'; text[n++] = '1';
        if (kind == Revealed) text[n++] = 'R'; else if (kind == Mined) text[n++] = 'M'; else { text[n++] = 'O'; Append(text, ref n, kind - OreBase); }
        text[n++] = ':'; Append(text, ref n, cx); text[n++] = ':'; Append(text, ref n, cy); text[n++] = ':';
        if (count == 512) text[n++] = 'f';
        else if (count <= SparseMax || 512 - count <= SparseMax) {
            bool sparse = count <= SparseMax; text[n++] = sparse ? 's' : 'i';
            for (int i = 0; i < 512; i++) {
                bool set = ((rows[i >> 5] >> (i & 31)) & 1u) != 0;   // i = 32 * r + b
                if (set == sparse) { text[n++] = Digits[i >> 6]; text[n++] = Digits[i & 63]; }
            }
        } else {
            // 64 bytes B[4r + k] = rows[r] >> 8k, standard base64 bit order without padding: 86 digits, the last one
            // carries 2 data bits and 4 zero pad bits.
            text[n++] = 'b'; int acc = 0, bits = 0;
            for (int j = 0; j < 64; j++) {
                acc = (acc << 8) | (int)((rows[j >> 2] >> (8 * (j & 3))) & 0xFFu); bits += 8;
                while (bits >= 6) { bits -= 6; text[n++] = Digits[(acc >> bits) & 63]; }
                acc &= (1 << bits) - 1;
            }
            if (bits > 0) text[n++] = Digits[(acc << (6 - bits)) & 63];
        }
        return new string(text, 0, n);
    }
    // Strict syntax, lenient payload ("s"/"i" in any order with duplicates, "b" with any count, pad bits ignored).
    // Clears rows first; false for every opaque string; never throws.
    public static bool TryDecode(string s, uint[] rows, out int kind, out int cx, out int cy)
    {
        if (rows == null || rows.Length < 16) { kind = -1; cx = cy = 0; return false; }
        Array.Clear(rows, 0, 16);
        if (TryParseHeader(s, out kind, out cx, out cy, out int at) && DecodePayload(s, at, rows)) return true;
        Array.Clear(rows, 0, 16); kind = -1; cx = cy = 0; return false;
    }
    static bool DecodePayload(string s, int at, uint[] rows)
    {
        char letter = s[at++]; int length = s.Length - at;
        if (letter == 'f') {
            if (length != 0) return false;
            for (int r = 0; r < 16; r++) rows[r] = uint.MaxValue;
            return true;
        }
        if (letter == 's' || letter == 'i') {
            if (length < 2 || length > 2 * SparseMax || (length & 1) != 0) return false;
            bool inverse = letter == 'i';
            if (inverse) for (int r = 0; r < 16; r++) rows[r] = uint.MaxValue;
            for (; at < s.Length; at += 2) {
                int hi = Value(s[at]), lo = Value(s[at + 1]);
                if (hi < 0 || hi > 7 || lo < 0) return false;
                int i = 64 * hi + lo;
                if (inverse) rows[i >> 5] &= ~(1u << (i & 31)); else rows[i >> 5] |= 1u << (i & 31);
            }
            return true;
        }
        if (letter != 'b' || length != 86) return false;
        int acc = 0, bits = 0, j = 0;
        for (; at < s.Length; at++) {
            int v = Value(s[at]);
            if (v < 0) return false;
            acc = (acc << 6) | v; bits += 6;
            if (bits >= 8) { bits -= 8; if (j < 64) rows[j >> 2] |= (uint)((acc >> bits) & 0xFF) << (8 * (j & 3)); j++; acc &= (1 << bits) - 1; }
        }
        return j == 64;
    }
    // "#1" kind ":" cx ":" cy ":" with strict canonical ASCII integers; at least one payload character must follow.
    static bool TryParseHeader(string s, out int kind, out int cx, out int cy, out int payloadStart)
    {
        kind = -1; cx = cy = 0; payloadStart = 0;
        if (s == null || s.Length < 9 || s[0] != '#' || s[1] != '1') return false;
        int at = 3, k;
        if (s[2] == 'R') k = Revealed;
        else if (s[2] == 'M') k = Mined;
        else if (s[2] == 'O' && ParseInt(s, ref at, false, false, 0, MaxItem, out int item)) k = OreBase + item;
        else return false;
        if (at >= s.Length || s[at++] != ':' || !ParseInt(s, ref at, true, false, -32768, 32767, out int x) || at >= s.Length || s[at++] != ':'
            || !ParseInt(s, ref at, true, false, -65536, 65535, out int y) || at >= s.Length || s[at++] != ':' || at >= s.Length) return false;
        kind = k; cx = x; cy = y; payloadStart = at; return true;
    }
    // Legacy cell key, syntax only (the caller checks InDomain): "revealed:" + cell, a MinedPrefixes entry + cell, or a
    // canonical unsigned item + ":" + cell (items above MaxItem give kind -1, which is never in the domain).
    public static bool TryParseLegacy(string key, out int kind, out int x, out int y)
    {
        kind = -1; x = y = 0;
        if (string.IsNullOrEmpty(key)) return false;
        if (key[0] >= '0' && key[0] <= '9') {
            int at = 0;
            if (!ParseInt(key, ref at, false, false, 0, int.MaxValue, out int item) || at >= key.Length || key[at] != ':' || !TryParseCell(key, at + 1, out int ox, out int oy)) return false;
            kind = OreKind(item); x = ox; y = oy; return true;
        }
        int start = CellPrefix(key, out int k);
        if (start == 0 || !TryParseCell(key, start, out int cellX, out int cellY)) return false;
        kind = k; x = cellX; y = cellY; return true;
    }
    // Exactly int ":" int up to the end of text, as older builds formatted cells with the session culture: each int "0" or
    // [1-9][0-9]{0,9} in int range, optionally signed by '-' or U+2212, the sign optionally preceded by U+200E, U+200F or
    // U+061C. No "-0", "+", whitespace or leading zeros.
    public static bool TryParseCell(string text, int start, out int x, out int y)
    {
        x = y = 0;
        if (text == null || start < 0 || start >= text.Length) return false;
        int at = start;
        if (!ParseInt(text, ref at, true, true, int.MinValue, int.MaxValue, out int vx) || at >= text.Length || text[at++] != ':'
            || !ParseInt(text, ref at, true, true, int.MinValue, int.MaxValue, out int vy) || at != text.Length) return false;
        x = vx; y = vy; return true;
    }
    // A key an older build may have written for a cell: "revealed:" or a mined prefix, or an ASCII digit first and exactly
    // two ':' ("3:replay-coal-1", "0:a", "vein:..." and the markers are not cell-like).
    public static bool LooksLikeCellKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key[0] >= '0' && key[0] <= '9') { int colons = 0; foreach (char c in key) if (c == ':' && ++colons > 2) return false; return colons == 2; }
        return CellPrefix(key, out _) > 0;
    }

    // Length of the "revealed:" or mined prefix the key starts with, 0 = none.
    static int CellPrefix(string key, out int kind)
    {
        kind = Revealed; if (StartsWith(key, "revealed:")) return 9;
        kind = Mined; foreach (string prefix in MinedPrefixes) if (StartsWith(key, prefix)) return prefix.Length;
        kind = -1; return 0;
    }
    static bool StartsWith(string key, string prefix) => key.Length >= prefix.Length && key[0] == prefix[0] && string.CompareOrdinal(key, 0, prefix, 0, prefix.Length) == 0;
    // Canonical decimal at s[at]: "0" or [1-9][0-9]{0,9} within [min, max]; signed: an optional '-' (culture: also U+2212,
    // optionally after U+200E/U+200F/U+061C). Rejects "-0", "+", whitespace and leading zeros; advances at past the digits.
    static bool ParseInt(string s, ref int at, bool signed, bool culture, long min, long max, out int value)
    {
        value = 0;
        if (culture && at < s.Length && (s[at] == '\u200E' || s[at] == '\u200F' || s[at] == '\u061C')) {
            at++; if (at >= s.Length || (s[at] != '-' && s[at] != '\u2212')) return false;
        }
        bool negative = signed && at < s.Length && (s[at] == '-' || (culture && s[at] == '\u2212'));
        if (negative) at++;
        if (at >= s.Length || s[at] < '0' || s[at] > '9') return false;
        long v = 0;
        if (s[at] == '0') { at++; if (negative || (at < s.Length && s[at] >= '0' && s[at] <= '9')) return false; }
        else for (int digits = 0; at < s.Length && s[at] >= '0' && s[at] <= '9'; at++) { if (++digits > 10) return false; v = v * 10 + (s[at] - '0'); }
        if (negative) v = -v;
        if (v < min || v > max) return false;
        value = (int)v; return true;
    }
    static void Append(char[] text, ref int n, int value)
    {
        long v = value; if (v < 0) { text[n++] = '-'; v = -v; }
        int start = n;
        do { text[n++] = (char)('0' + (int)(v % 10)); v /= 10; } while (v != 0);
        Array.Reverse(text, start, n - start);
    }
    static int Value(char c) => c >= 'A' && c <= 'Z' ? c - 'A' : c >= 'a' && c <= 'z' ? c - 'a' + 26 : c >= '0' && c <= '9' ? c - '0' + 52 : c == '-' ? 62 : c == '_' ? 63 : -1;
    // SWAR popcount (System.Numerics.BitOperations is not available in Unity's profile).
    static int PopCount(uint v)
    {
        unchecked {
            v -= (v >> 1) & 0x55555555u;
            v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
            return (int)((((v + (v >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
        }
    }
}
