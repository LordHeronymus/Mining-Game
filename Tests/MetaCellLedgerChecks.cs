using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Edit-mode checks for the cell ledger of the meta progression (MetaCellLedger, Docs/Progression.md "Grösse des Profils"):
// A codec (round trip, known vectors, malformed records, legacy keys), B ledger (oracle, flush and attach, incremental
// flushes, compaction, older-build union, read mode), C integration with MetaProgression (both writer modes through
// CellChunkWriterOverride). Pure parts use System.Random with fixed seeds; every profile lives in
// Temp/MetaCellLedger-<ticks>/<case> (MetaProgression.TestDirectory) and all statics are restored.
// Report: Temp/MetaCellLedgerChecks.txt.
public static class MetaCellLedgerChecks
{
    const string Report = "Temp/MetaCellLedgerChecks.txt";
    const int R = MetaCellLedger.Revealed, M = MetaCellLedger.Mined;
    static readonly int O3 = MetaCellLedger.OreKind(3), O33 = MetaCellLedger.OreKind(33);
    static readonly List<string> lines = new();
    static int passed, failed;
    static string root;
    static readonly DateTime Now = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc); // a Wednesday
    static readonly uint[] scratch = new uint[16];
    // Ledger strings that are no cells: payout and tally markers, other key families, non-cell ore sources.
    static readonly string[] Markers = { "cm:ms-sales-1", "cr:0:123:blocks/-1/0:t", "ct:3:1790000000:0:diligence/-1/0/100/50", "cx:3:1790000000:1790100000:5:0:8",
        "cs:3:1790000000:2:10,20/5,6", "tp:sales:5", "vein:3:1:2", "harvest:trees:a", "gear-crafted:35", "artifact-mined:1:2", "craft-upgrade:pickaxe-upgrades:2",
        "3:replay-coal-1", "0:a" };

    public static object Main()
    {
        lines.Clear(); passed = failed = 0;
        Directory.CreateDirectory("Temp");
        if (Application.isPlaying) return Finish("FAIL Edit Mode only: stop Play Mode before running MetaCellLedgerChecks");
        if (MetaProgression.CurrentRun != null) return Finish("FAIL a run is active; run MetaCellLedgerChecks without a loaded world");
        string previousDirectory = MetaProgression.TestDirectory;
        var previousClock = MetaProgression.ChallengeTestUtc;
        var previousSettings = MetaProgression.ChallengeSettingsOverride;
        var previousZone = MetaProgression.ChallengeTimeZoneOverride;
        var previousWriter = MetaProgression.CellChunkWriterOverride;
        root = Path.GetFullPath("Temp/MetaCellLedger-" + DateTime.UtcNow.Ticks);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try {
            Section("A1 round trip", CodecRoundTrip);
            Section("A2 known vectors", CodecVectors);
            Section("A3 malformed records", CodecMalformed);
            Section("A4 legacy parser", LegacyParser);
            Section("B1 oracle", LedgerOracle);
            Section("B2 flush and attach", FlushAndAttach);
            Section("B3 incremental flushes", IncrementalFlushes);
            Section("B4 compaction", Compaction);
            Section("B5 older-build union", OlderBuildUnion);
            Section("B6 read mode", ReadMode);
            Section("C1 recording and identity", RecordingIdentity);
            Section("C2 persistence", Persistence);
            Section("C3 replay identity", ReplayIdentity);
            Section("C4 legacy world save", LegacyWorldSave);
            Section("C5 legacy profile migration", LegacyProfileMigration);
            Section("C6 rescue", Rescue);
            Section("C7 downgrade cycle", DowngradeCycle);
            Section("C8 read-only step", ReadOnlyStep);
            Section("C9 forward compatibility", ForwardCompatibility);
            Section("C10 flush paths", FlushPaths);
            Section("C11 CopyRun parity", CopyRunParity);
            Section("C12 tp-marker interleaving", TallyMarkerInterleaving);
            Section("C13 size", Size);
        } finally {
            try { MetaProgression.EndRun(); } catch (Exception e) { Check(false, "cleanup: EndRun threw " + Describe(e)); }
            MetaProgression.CellChunkWriterOverride = previousWriter;
            try { MetaProgression.TestDirectory = previousDirectory; } catch (Exception e) { Check(false, "cleanup: restoring TestDirectory threw " + Describe(e)); }
            MetaProgression.ChallengeTestUtc = previousClock;
            MetaProgression.ChallengeSettingsOverride = previousSettings;
            MetaProgression.ChallengeTimeZoneOverride = previousZone;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception e) { lines.Add("INFO temporary profiles kept in " + root + ": " + e.Message); }
        }
        Check(MetaProgression.TestDirectory == previousDirectory && MetaProgression.ChallengeTestUtc == previousClock && MetaProgression.ChallengeSettingsOverride == previousSettings &&
            MetaProgression.ChallengeTimeZoneOverride == previousZone && MetaProgression.CellChunkWriterOverride == previousWriter, "cleanup: statics restored");
        Check(watch.Elapsed.TotalSeconds < 60, "cleanup: runtime below 60 s (" + watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
        return Finish(null);
    }

    static string Finish(string abort)
    {
        if (abort != null) { failed++; lines.Insert(0, abort); }
        string summary = failed == 0 ? "PASS " + passed + ": cell ledger (codec, ledger, integration in both writer modes)"
            : "FAIL " + failed + " of " + (passed + failed) + " cell ledger checks";
        File.WriteAllText(Report, summary + "\n" + string.Join("\n", lines) + "\n");
        if (failed == 0) return summary;
        // The CI live status only keeps the returned text (about 2500 characters), so list every failure briefly.
        string list = string.Join(" | ", lines.Where(line => line.StartsWith("FAIL ")).Select(line => line.Length > 165 ? line.Substring(5, 160) + "…" : line.Substring(5)));
        return summary + ": " + (list.Length > 2300 ? list.Substring(0, 2300) + " …" : list);
    }

    // ---------- helpers ----------
    static void Check(bool ok, string label)
    {
        if (ok) passed++; else failed++;
        lines.Add((ok ? "PASS " : "FAIL ") + label.Replace('\n', ' ').Replace('\r', ' '));
    }
    static void Info(string text) => lines.Add("INFO " + text);
    static void Section(string name, Action body) { try { body(); } catch (Exception e) { Check(false, name + ": aborted by " + Describe(e)); } }
    static string Describe(Exception e)
    {
        while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null) e = e.InnerException;
        var frame = new System.Diagnostics.StackTrace(e, false).GetFrames()?.FirstOrDefault(f => f.GetMethod()?.DeclaringType != null);
        return e.GetType().Name + ": " + e.Message + (frame != null ? " at " + frame.GetMethod().DeclaringType.Name + "." + frame.GetMethod().Name : "");
    }
    static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
    static string Cell(int kind, int x, int y) => "(" + Inv(kind) + ", " + Inv(x) + ", " + Inv(y) + ")";
    // Report-safe spelling: control and non-ASCII characters (culture minus signs, bidi marks) as \uXXXX.
    static string Show(string text) => text == null ? "null" : string.Concat(text.Select(ch => ch < 32 || ch > 126 ? "\\u" + ((int)ch).ToString("X4", CultureInfo.InvariantCulture) : ch.ToString()));
    static string Example(string first) => first != null ? " (first: " + Show(first) + ")" : "";
    static MetaCellLedger Empty() => MetaCellLedger.Attach(new List<string>(), null, true);
    static uint[] Fill(uint value) { var rows = new uint[16]; for (int r = 0; r < 16; r++) rows[r] = value; return rows; }
    static string Pair(int index) => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"[index >> 6].ToString() + "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"[index & 63];
    static bool RecordChar(char ch) => ch == '#' || ch == ':' || ch == '-' || ch == '_' || (ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
    static bool IsRecord(string text) => !string.IsNullOrEmpty(text) && text[0] == '#' && MetaCellLedger.TryDecode(text, scratch, out _, out _, out _);
    // The 64-byte layout B[4r + k] = rows[r] >> 8k in the base64url spelling of the "b" payload.
    static string Base64Url(uint[] rows)
    {
        var bytes = new byte[64];
        for (int r = 0; r < 16; r++) for (int k = 0; k < 4; k++) bytes[4 * r + k] = (byte)((rows[r] >> (8 * k)) & 0xFFu);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    static void RandomRows(System.Random random, int count, uint[] rows, int[] pool)
    {
        Array.Clear(rows, 0, 16);
        for (int i = 0; i < 512; i++) pool[i] = i;
        for (int i = 0; i < count; i++) {
            int j = i + random.Next(512 - i), swap = pool[i]; pool[i] = pool[j]; pool[j] = swap;
            rows[pool[i] >> 5] |= 1u << (pool[i] & 31);
        }
    }
    // Sets count random cells of one chunk in the ledger and the oracle.
    static void FillChunk(MetaCellLedger ledger, HashSet<(int, int, int)> oracle, System.Random random, int kind, int cx, int cy, int count)
    {
        var rows = new uint[16]; RandomRows(random, count, rows, new int[512]);
        for (int i = 0; i < 512; i++) {
            if ((rows[i >> 5] & (1u << (i & 31))) == 0) continue;
            int cellX = cx * 32 + (i & 31), cellY = cy * 16 + (i >> 5);
            ledger.Add(kind, cellX, cellY); oracle.Add((kind, cellX, cellY));
        }
    }
    static (int, int) FreeCell(HashSet<(int, int, int)> oracle, int kind, int cx, int cy)
    {
        for (int i = 0; i < 512; i++) if (!oracle.Contains((kind, cx * 32 + (i & 31), cy * 16 + (i >> 5)))) return (cx * 32 + (i & 31), cy * 16 + (i >> 5));
        throw new InvalidOperationException("chunk is full");
    }
    // Cells a list stands for: decodable records plus in-domain legacy keys (what Attach folds).
    static HashSet<(int, int, int)> Members(IEnumerable<string> list)
    {
        var cells = new HashSet<(int, int, int)>(); var rows = new uint[16];
        foreach (string text in list) {
            if (string.IsNullOrEmpty(text)) continue;
            if (text[0] == '#') {
                if (!MetaCellLedger.TryDecode(text, rows, out int kind, out int cx, out int cy)) continue;
                for (int i = 0; i < 512; i++) if ((rows[i >> 5] & (1u << (i & 31))) != 0) cells.Add((kind, cx * 32 + (i & 31), cy * 16 + (i >> 5)));
            } else if (MetaCellLedger.TryParseLegacy(text, out int k, out int x, out int y) && MetaCellLedger.InDomain(k, x, y)) cells.Add((k, x, y));
        }
        return cells;
    }
    // Decodable records of the list; unique = no (kind, cx, cy) has two records.
    static int RecordCount(List<string> list, out bool unique)
    {
        var heads = new HashSet<(int, int, int)>(); var rows = new uint[16]; int records = 0; unique = true;
        foreach (string text in list)
            if (!string.IsNullOrEmpty(text) && text[0] == '#' && MetaCellLedger.TryDecode(text, rows, out int kind, out int cx, out int cy)) { records++; unique &= heads.Add((kind, cx, cy)); }
        return records;
    }
    static bool HasLegacyCell(List<string> list) => list.Any(text => MetaCellLedger.TryParseLegacy(text, out int k, out int x, out int y) && MetaCellLedger.InDomain(k, x, y));
    // The string rules every older build applies to a run ledger (MetaProgression.ValidStrings, 2,000,000 entries, 160 chars).
    static bool LegacyValid(List<string> list) => list.Count <= 2000000 && list.All(text => !string.IsNullOrWhiteSpace(text) && text.Length <= 160) && new HashSet<string>(list).Count == list.Count;
    // Exact membership: the same chunks, and every cell of every chunk agrees with the oracle.
    static bool SameCells(MetaCellLedger ledger, HashSet<(int, int, int)> oracle, out string difference)
    {
        difference = null;
        var heads = new HashSet<(int, int, int)>(oracle.Select(cell => (cell.Item1, cell.Item2 >> 5, cell.Item3 >> 4)));
        if (ledger.ChunkCount != heads.Count) { difference = "chunks " + ledger.ChunkCount + " instead of " + heads.Count; return false; }
        foreach (var (kind, cx, cy) in heads)
            for (int i = 0; i < 512; i++) {
                int x = cx * 32 + (i & 31), y = cy * 16 + (i >> 5);
                if (ledger.Contains(kind, x, y) != oracle.Contains((kind, x, y))) { difference = "cell " + Cell(kind, x, y); return false; }
            }
        return true;
    }
    // Spelling of a cell key as an older build wrote it; variant picks the minus sign and the mined prefix.
    static string LegacyKey(int kind, int x, int y, int variant) =>
        (kind == R ? "revealed:" : kind == M ? MetaCellLedger.MinedPrefixes[variant % MetaCellLedger.MinedPrefixes.Length] : Inv(kind - MetaCellLedger.OreBase) + ":") + Spell(x, variant) + ":" + Spell(y, variant);
    static string Spell(int value, int variant) => value >= 0 ? Inv(value) : (variant % 3 == 0 ? "-" : variant % 3 == 1 ? "\u2212" : "\u200E-") + Inv(-(long)value);
    static List<(int, int, int)> RandomCells(System.Random random, int count, int[] kinds, int width, int depth)
    {
        var cells = new List<(int, int, int)>(count);
        for (int i = 0; i < count; i++) {
            int kind = kinds[random.Next(kinds.Length)];
            // Revealed cells are only recorded for x >= 0 and depth > 0 (RecordRevealedCell).
            cells.Add(kind == R ? (kind, random.Next(0, width), random.Next(1, depth)) : (kind, random.Next(-width, width), random.Next(-depth, depth)));
        }
        return cells;
    }

    // ---------- A. codec ----------
    static void CodecRoundTrip()
    {
        int[] counts = { 1, 2, 41, 42, 43, 200, 469, 470, 471, 511, 512 };
        int[] kinds = { R, M, MetaCellLedger.OreKind(0), O3, MetaCellLedger.OreKind(MetaCellLedger.MaxItem) };
        var places = new[] { (0, 0), (-1, -1), (-32768, -65536), (32767, 65535), (2812, -3) };
        var random = new System.Random(4711); var pool = new int[512]; var rows = new uint[16]; var back = new uint[16];
        int cases = 0, bitmaps = 0, decodeErrors = 0, letterErrors = 0, lengthErrors = 0, charErrors = 0, unstable = 0, base64Errors = 0; string first = null;
        foreach (int count in counts) foreach (int kind in kinds) foreach (var (cx, cy) in places) {
            RandomRows(random, count, rows, pool); var before = (uint[])rows.Clone();
            string record = MetaCellLedger.Encode(kind, cx, cy, rows); cases++;
            string payload = record.Substring(record.LastIndexOf(':') + 1);
            char letter = count == 512 ? 'f' : count <= MetaCellLedger.SparseMax ? 's' : 512 - count <= MetaCellLedger.SparseMax ? 'i' : 'b';
            if (!(MetaCellLedger.TryDecode(record, back, out int decodedKind, out int decodedX, out int decodedY) && decodedKind == kind && decodedX == cx && decodedY == cy && back.SequenceEqual(rows))) { decodeErrors++; first ??= record; }
            if (payload.Length == 0 || payload[0] != letter) { letterErrors++; first ??= Inv(count) + " cells -> " + record; }
            if (record.Length > MetaCellLedger.MaxRecordLength) { lengthErrors++; first ??= record; }
            if (!record.All(RecordChar)) { charErrors++; first ??= record; }
            if (!rows.SequenceEqual(before) || MetaCellLedger.Encode(kind, cx, cy, before) != record) { unstable++; first ??= record; }
            if (letter == 'b') { bitmaps++; if (payload.Substring(1) != Base64Url(rows)) { base64Errors++; first ??= record; } }
        }
        Check(cases == 275 && decodeErrors == 0, "A1 round trip: " + cases + " records decode to the same kind, cx, cy and rows" + Example(first));
        Check(letterErrors == 0, "A1 round trip: payload s for up to 42 cells, b for 43..469, i for 470..511, f for 512" + Example(first));
        Check(lengthErrors == 0 && charErrors == 0, "A1 round trip: every record is at most 110 chars of [#:-0-9A-Za-z_]" + Example(first));
        Check(unstable == 0, "A1 round trip: encoding is deterministic and leaves the rows untouched" + Example(first));
        Check(bitmaps == 75 && base64Errors == 0, "A1 round trip: all " + bitmaps + " b payloads equal Convert.ToBase64String of the byte layout (url alphabet, no padding)" + Example(first));
        string longest = MetaCellLedger.Encode(MetaCellLedger.OreKind(MetaCellLedger.MaxItem), -32768, -65536, Fill(0x55555555u));
        Check(longest.Length == MetaCellLedger.MaxRecordLength, "A1 round trip: the longest possible record has exactly " + MetaCellLedger.MaxRecordLength + " chars (" + longest.Length + ")");
        bool threw = false;
        try { MetaCellLedger.Encode(M, 0, 0, new uint[16]); } catch (ArgumentException) { threw = true; }
        Check(threw, "A1 round trip: Encode refuses an empty chunk (empty chunks are never written)");
    }

    static void CodecVectors()
    {
        var mined = new uint[16]; mined[-40 & 15] = 1u << (90001 & 31);
        string single = MetaCellLedger.Encode(M, 90001 >> 5, -40 >> 4, mined);
        Check(single == "#1M:2812:-3:sER", "A2 vectors: the single mined cell (90001, -40) is #1M:2812:-3:sER (" + single + ")");
        var coalRows = new uint[16]; coalRows[0] = 1u;
        string coal = MetaCellLedger.Encode(MetaCellLedger.OreKind((int)Item.Coal), -1, -2, coalRows);
        Check(MetaCellLedger.OreKind((int)Item.Coal) == 5 && coal == "#1O3:-1:-2:sAA", "A2 vectors: coal at (-32, -32) is #1O3:-1:-2:sAA (" + coal + ")");
        string full = MetaCellLedger.Encode(R, 3, 10, Fill(uint.MaxValue));
        Check(full == "#1R:3:10:f", "A2 vectors: the full chunk R(3, 10) is #1R:3:10:f (" + full + ")");

        // The same records through the ledger: cell -> chunk mapping with floor division of negative coordinates.
        var ledger = Empty();
        ledger.Add(M, 90001, -40); ledger.Add(MetaCellLedger.OreKind((int)Item.Coal), -32, -32);
        for (int x = 96; x < 128; x++) for (int depth = 160; depth < 176; depth++) ledger.Add(R, x, depth);
        var list = new List<string>(); ledger.Flush(list);
        Check(list.SequenceEqual(new[] { "#1M:2812:-3:sER", "#1O3:-1:-2:sAA", "#1R:3:10:f" }), "A2 vectors: Flush of these cells writes exactly the three records in order of first change (" + string.Join(" ", list) + ")");

        var rows = Fill(0xDEADBEEFu);
        bool decoded = MetaCellLedger.TryDecode("#1M:2812:-3:sER", rows, out int kind, out int cx, out int cy);
        Check(decoded && kind == M && cx == 2812 && cy == -3 && rows[8] == 1u << 17 && Enumerable.Range(0, 16).All(r => r == 8 || rows[r] == 0),
            "A2 vectors: #1M:2812:-3:sER decodes to bit 17 of row 8 only (TryDecode clears the rows first)");

        // Further hand-checked vectors (cross-checked with Python's base64): i, checkerboard, first and last 43 cells.
        var allButFirst = Fill(uint.MaxValue); allButFirst[0] = 0xFFFFFFFEu;
        var firstCells = new uint[16]; firstCells[0] = uint.MaxValue; firstCells[1] = 0x7FFu;
        var lastCells = new uint[16]; lastCells[14] = 0xFFE00000u; lastCells[15] = uint.MaxValue;
        var expected = new (string record, uint[] rows)[] {
            ("#1R:0:0:iAA", allButFirst),
            ("#1M:0:0:b" + new string('V', 85) + "Q", Fill(0x55555555u)),
            ("#1M:0:0:b______8H" + new string('A', 78), firstCells),
            ("#1M:0:0:b" + new string('A', 77) + "OD______w", lastCells) };
        foreach (var (record, cells) in expected) {
            string encoded = MetaCellLedger.Encode(record[2] == 'R' ? R : M, 0, 0, cells);
            bool back = MetaCellLedger.TryDecode(record, rows, out _, out _, out _) && rows.SequenceEqual(cells);
            Check(encoded == record && back, "A2 vectors: " + record.Substring(0, Math.Min(record.Length, 24)) + "… (" + record.Length + " chars) encodes and decodes exactly");
        }
        // The pad bits of the last b digit are ignored; its two data bits are the low bits of byte 63 (row 15, bits 24-25).
        bool pad = MetaCellLedger.TryDecode("#1M:0:0:b" + new string('A', 85) + "P", rows, out _, out _, out _) && rows.All(row => row == 0);
        bool top = MetaCellLedger.TryDecode("#1M:0:0:b" + new string('A', 85) + "w", rows, out _, out _, out _) && rows[15] == 0x03000000u && rows.Take(15).All(row => row == 0);
        Check(pad && top, "A2 vectors: the last b digit carries 2 data bits (byte 63 = row 15, bits 24-25) and 4 ignored pad bits");
        bool lenient = MetaCellLedger.TryDecode("#1M:0:0:sH_AAH_", rows, out _, out _, out _) && rows[0] == 1u && rows[15] == 0x80000000u && rows.Skip(1).Take(14).All(row => row == 0);
        Check(lenient, "A2 vectors: s payloads are read in any order and with duplicates (lenient reader)");
    }

    static void CodecMalformed()
    {
        string bitmap = MetaCellLedger.Encode(M, 0, 0, Fill(0x55555555u));
        string sparse43 = "#1M:0:0:s" + string.Concat(Enumerable.Range(0, 43).Select(Pair));
        var malformed = new List<string> {
            "#1M:1:2:", "#1M:a:2:f", "#1Z:0:0:f", "#2M:0:0:f",
            "#1M:01:0:f", "#1M:-0:0:f", "#1M:+1:0:f",
            "#1M:32768:0:f", "#1M:0:65536:f", "#1O65536:0:0:f", "#1O03:0:0:f",
            "#1M:0:0:fx", "#1M:0:0:s", "#1M:0:0:sA", "#1M:0:0:sIA",
            sparse43, "#1M:0:0:i",
            "#1M:0:0:b" + new string('A', 85), "#1M:0:0:b" + new string('A', 87),
            bitmap.Substring(0, 20) + "+" + bitmap.Substring(21), bitmap.Substring(0, 20) + "/" + bitmap.Substring(21), bitmap.Substring(0, bitmap.Length - 1) + "=",
            "#1M:0:0:q", "#1M:0:0",
            // further edges: empty and short strings, ranges, case, spacing, other digits, opaque forward records
            null, "", "#", "#1", "#1M", "#1M:", "#1M:0", "#1M:0:", "#1M:0:0:", "#1M:-32769:0:f", "#1M:0:-65537:f", "#1m:0:0:f", "#1O:0:0:f", "#1O-1:0:0:f",
            "#1M:0:0:f ", " #1M:0:0:f", "#1M::0:f", "#1M:0:0:sAAA", "#1M:0:0:iIA", "#1M:0:0:s" + new string('A', 86), "#1M:\u0661:0:f", "#1M:0:0:s\u0410A",
            "#1M:0:0:b!!", "#2M:0:0:Zfuture", "#1M:0:0:" + bitmap.Substring(bitmap.LastIndexOf(':') + 1) + "A" };
        var rows = new uint[16]; int accepted = 0, threw = 0; string first = null;
        foreach (string record in malformed) {
            try { if (MetaCellLedger.TryDecode(record, rows, out _, out _, out _)) { accepted++; first ??= record; } }
            catch (Exception e) { threw++; first ??= record + " threw " + e.GetType().Name; }
        }
        Check(accepted == 0 && threw == 0, "A3 malformed: TryDecode rejects all " + malformed.Count + " malformed records without throwing" + Example(first));
        bool nullRows;
        try { nullRows = !MetaCellLedger.TryDecode("#1M:0:0:f", null, out _, out _, out _); } catch (Exception) { nullRows = false; }
        Check(nullRows, "A3 malformed: TryDecode without a row buffer returns false and does not throw");
        string[] boundaries = { "#1M:-32768:-65536:f", "#1M:32767:65535:f", "#1O65535:0:0:f", "#1O0:0:0:f", "#1R:0:0:sAAAA", "#1M:0:0:iAAAA", "#1M:0:0:s" + string.Concat(Enumerable.Range(470, 42).Select(Pair)) };
        string rejected = boundaries.FirstOrDefault(record => !MetaCellLedger.TryDecode(record, rows, out _, out _, out _));
        Check(rejected == null, "A3 malformed: the range boundaries and 42 indices are accepted" + Example(rejected));
    }

    static void LegacyParser()
    {
        var accepted = new (string key, int kind, int x, int y)[] {
            ("revealed:5:7", R, 5, 7),
            ("mined:-3:-40", M, -3, -40), ("mined:-3:\u221240", M, -3, -40), ("mined:-3:\u200E-40", M, -3, -40), ("mined:-3:\u200F\u221240", M, -3, -40), ("mined:-3:\u061C-40", M, -3, -40),
            ("mined:\u2212" + "3:-40", M, -3, -40), ("l2-block:4:-9", M, 4, -9), ("l3-block:4:-9", M, 4, -9), ("xxl-layer:4:4:-9", M, 4, -9), ("xxl-layer:5:4:-9", M, 4, -9), ("surface-block:4:-9", M, 4, -9),
            ("12:5:-100", 14, 5, -100), ("0:0:0", MetaCellLedger.OreBase, 0, 0), ("65535:-1048576:1048575", MetaCellLedger.OreBase + 65535, -1048576, 1048575) };
        string first = null;
        foreach (var (key, kind, x, y) in accepted)
            if (!(MetaCellLedger.TryParseLegacy(key, out int k, out int px, out int py) && k == kind && px == x && py == y && MetaCellLedger.InDomain(k, px, py) && MetaCellLedger.LooksLikeCellKey(key))) first ??= key;
        Check(first == null, "A4 legacy: " + accepted.Length + " keys of every family and minus spelling parse to the expected (kind, x, y) in the domain" + Example(first));

        string[] rejected = { "mined:007:1", "mined:+1:2", "mined: 1:2", "mined:1:2:3", "mined:-0:1", "mined:1:", "12:replay-coal-1", "a", "007:1:2",
            "vein:2:1:1", "tp:sales:5", "cr:0:123:blocks/-1/0:t", "cm:ms-sales-1", "harvest:trees:a", "artifact-mined:1:2", "craft-upgrade:pickaxe-upgrades:2", "gear-crafted:35", "#1M:0:0:f",
            // further edges: a mark without a sign, double signs, int overflow, missing parts, case, unknown layer prefix
            "mined:\u200E1:2", "mined:1:2\u200E", "mined:--1:2", "mined:-\u22121:2", "mined:2147483648:0", "mined:1:-2147483649", "mined:12345678901:0", "mined:1:2 ",
            "revealed:5", "revealed:", "mined:", "Mined:1:2", "xxl-layer:3:1:2", "1:2", "1::2", "1:2:", ":1:2", "", null };
        first = rejected.FirstOrDefault(key => MetaCellLedger.TryParseLegacy(key, out _, out _, out _));
        Check(first == null, "A4 legacy: " + rejected.Length + " non-canonical keys, markers and other families stay strings" + Example(first));

        string[] outside = { "mined:1048576:0", "70000:1:1", "mined:0:-1048577", "65536:0:0", "mined:2147483647:-2147483648" };
        first = outside.FirstOrDefault(key => !MetaCellLedger.TryParseLegacy(key, out int k, out int px, out int py) || MetaCellLedger.InDomain(k, px, py));
        Check(first == null, "A4 legacy: mined:1048576:0, 70000:1:1 and other far keys parse but are outside the domain" + Example(first));

        string[] cellLike = { "mined:007:1", "revealed:1:2:3", "3:05:7" }, notCellLike = { "12:replay-coal-1", "vein:2:1:1", "tp:sales:5", "a", "3:replay-coal-1", "0:a", "cr:0:123:blocks/-1/0:t", "#1M:0:0:f", "", null };
        Check(cellLike.All(MetaCellLedger.LooksLikeCellKey) && !notCellLike.Any(MetaCellLedger.LooksLikeCellKey), "A4 legacy: LooksLikeCellKey is true for mined:007:1, revealed:1:2:3, 3:05:7 and false for ore sources, markers and records");

        var legacyMined = typeof(MetaProgression).GetField("LegacyMinedKeys", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as string[];
        Check(legacyMined != null && MetaCellLedger.MinedPrefixes.SequenceEqual(new[] { "mined:" }.Concat(legacyMined)), "A4 legacy: MinedPrefixes is \"mined:\" plus MetaProgression.LegacyMinedKeys");
        Check(MetaCellLedger.OreKind(-1) == -1 && MetaCellLedger.OreKind(65536) == -1 && MetaCellLedger.OreKind(0) == 2 && MetaCellLedger.OreKind(65535) == 65537 &&
            MetaCellLedger.InDomain(65537, -1048576, 1048575) && !MetaCellLedger.InDomain(65538, 0, 0) && !MetaCellLedger.InDomain(-1, 0, 0), "A4 legacy: OreKind and InDomain bounds");
    }

    // ---------- B. ledger ----------
    static void LedgerOracle()
    {
        var random = new System.Random(20261021); var ledger = Empty(); var oracle = new HashSet<(int, int, int)>();
        int[] kinds = { R, M, O3, O33 };
        int adds = 0, addErrors = 0, repeatErrors = 0, containErrors = 0, probes = 0; string first = null;
        void Put(int putKind, int putX, int putY)
        {
            adds++;
            if (ledger.Add(putKind, putX, putY) != oracle.Add((putKind, putX, putY))) { addErrors++; first ??= "add " + Cell(putKind, putX, putY); }
        }
        for (int i = 0; i < 200000; i++) Put(kinds[random.Next(kinds.Length)], random.Next(-300, 300), random.Next(-1200, 1201));
        int far = MetaCellLedger.Limit - 1, edge = MetaCellLedger.Limit;
        for (int i = 0; i < 1000; i++) Put(kinds[random.Next(kinds.Length)], random.Next(-far, far + 1), random.Next(-far, far + 1));
        foreach (int kind in kinds) { Put(kind, -edge, -edge); Put(kind, far, far); Put(kind, -edge, far); Put(kind, far, -edge); }
        Put(MetaCellLedger.OreKind(MetaCellLedger.MaxItem), -edge, far);
        var members = oracle.ToList();
        for (int i = 0; i < 1000; i++) { var (k, x, y) = members[random.Next(members.Count)]; if (ledger.Add(k, x, y)) repeatErrors++; }
        Check(addErrors == 0 && repeatErrors == 0, "B1 oracle: " + adds + " Add calls agree with a HashSet (true only for new cells) and 1000 repeats return false" + Example(first));

        int otherOre = MetaCellLedger.OreKind(4);
        foreach (var (k, x, y) in oracle) {
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) {
                probes++;
                if (ledger.Contains(k, x + dx, y + dy) != oracle.Contains((k, x + dx, y + dy))) { containErrors++; first ??= "contains " + Cell(k, x + dx, y + dy); }
            }
            foreach (int other in kinds) if (other != k) { probes++; if (ledger.Contains(other, x, y) != oracle.Contains((other, x, y))) { containErrors++; first ??= "contains " + Cell(other, x, y); } }
            probes++; if (ledger.Contains(otherOre, x, y)) { containErrors++; first ??= "contains " + Cell(otherOre, x, y); }
        }
        int[] probeKinds = { R, M, O3, O33, otherOre, -1, MetaCellLedger.OreBase + MetaCellLedger.MaxItem + 1 };
        for (int i = 0; i < 50000; i++) {
            int k = probeKinds[random.Next(probeKinds.Length)];
            int x = i % 10 == 0 ? random.Next(-edge - 4, edge + 4) : random.Next(-400, 400), y = i % 10 == 0 ? random.Next(-edge - 4, edge + 4) : random.Next(-1300, 1301);
            probes++;
            if (ledger.Contains(k, x, y) != oracle.Contains((k, x, y))) { containErrors++; first ??= "probe " + Cell(k, x, y); }
        }
        Check(containErrors == 0, "B1 oracle: Contains agrees with the HashSet for all " + oracle.Count + " members, their neighbours, other kinds and 50k random probes (" + probes + " probes)" + Example(first));
        int chunks = oracle.Select(cell => (cell.Item1, cell.Item2 >> 5, cell.Item3 >> 4)).Distinct().Count();
        Check(ledger.ChunkCount == chunks, "B1 oracle: one chunk per occupied (kind, x >> 5, y >> 4) (" + ledger.ChunkCount + " of " + chunks + ")");

        var outside = new (int, int, int)[] {
            (M, edge, 0), (M, -edge - 1, 0), (M, 0, edge), (M, 0, -edge - 1), (R, int.MinValue, int.MaxValue), (O3, int.MaxValue, int.MinValue),
            (-1, 0, 0), (MetaCellLedger.OreBase + MetaCellLedger.MaxItem + 1, 0, 0), (MetaCellLedger.OreKind(70000), 0, 0) };
        int before = ledger.ChunkCount, outsideErrors = 0;
        foreach (var (k, x, y) in outside) if (MetaCellLedger.InDomain(k, x, y) || ledger.Add(k, x, y) || ledger.Contains(k, x, y)) outsideErrors++;
        Check(outsideErrors == 0 && ledger.ChunkCount == before, "B1 oracle: out-of-domain Add calls return false and record nothing");

        var list = new List<string>();
        bool flushed = ledger.Flush(list);
        int records = RecordCount(list, out bool unique);
        Check(flushed && records == list.Count && records == chunks && unique && list.All(text => text.Length <= MetaCellLedger.MaxRecordLength) && Members(list).SetEquals(oracle),
            "B1 oracle: the flushed records (" + records + ", far and edge chunks included) decode to exactly the oracle");
    }

    static void FlushAndAttach()
    {
        var random = new System.Random(77); var ledger = Empty(); var oracle = new HashSet<(int, int, int)>();
        // Every payload form, negative chunks, every kind; then scattered cells.
        FillChunk(ledger, oracle, random, R, 0, 0, 512); FillChunk(ledger, oracle, random, M, -1, -3, 500); FillChunk(ledger, oracle, random, O3, 2, 5, 200);
        FillChunk(ledger, oracle, random, O33, -2, -1, 7); FillChunk(ledger, oracle, random, M, 2812, -3, 43);
        foreach (var (k, x, y) in RandomCells(random, 3000, new[] { R, M, O3, O33 }, 200, 400)) { ledger.Add(k, x, y); oracle.Add((k, x, y)); }
        var list = new List<string>(Markers);
        bool flushed = ledger.Flush(list);
        int records = RecordCount(list, out bool unique);
        Check(flushed && list.Take(Markers.Length).SequenceEqual(Markers) && list.Count == Markers.Length + ledger.ChunkCount && records == ledger.ChunkCount && unique,
            "B2 flush: the markers stay first and in order, followed by exactly one record per chunk (" + records + " records)");
        Check(new HashSet<string>(list).Count == list.Count && list.All(text => text.Length <= MetaCellLedger.MaxRecordLength), "B2 flush: the list is unique and every record has at most 110 chars");
        Check(Members(list).SetEquals(oracle), "B2 flush: the records hold exactly the added cells");
        var flushedList = new List<string>(list);
        Check(!ledger.Flush(list) && list.SequenceEqual(flushedList), "B2 flush: a Flush without changes returns false and leaves the list alone");

        var copy = new List<string>(list); var others = new HashSet<string>();
        var attached = MetaCellLedger.Attach(copy, others, true);
        bool same = SameCells(attached, oracle, out string difference);
        Check(attached.Writes && same, "B2 attach: the attached ledger has identical membership" + Example(difference));
        Check(copy.SequenceEqual(Markers) && others.SetEquals(Markers) && attached.Folded == 0 && !attached.StringCells, "B2 attach: only the markers stay in the list (in order) and in others; Folded is 0");
        bool restored = attached.Flush(copy) && copy.Count == list.Count && Enumerable.Range(0, list.Count).All(i => ReferenceEquals(copy[i], list[i]));
        Check(restored, "B2 attach: the next Flush restores the same record strings in the same order (same instances, no unchanged chunk re-encoded)");

        string old = list.First(text => text.StartsWith("#1O33:-2:-1:", StringComparison.Ordinal));
        var (freeX, freeY) = FreeCell(oracle, O33, -2, -1);
        bool added = attached.Add(O33, freeX, freeY); oracle.Add((O33, freeX, freeY));
        bool replaced = attached.Flush(copy) && !copy.Contains(old) && copy.Count == list.Count && copy[copy.Count - 1].StartsWith("#1O33:-2:-1:", StringComparison.Ordinal) &&
            copy.Take(copy.Count - 1).SequenceEqual(list.Where(text => text != old));
        Check(added && replaced && Members(copy).SetEquals(oracle), "B2 flush: a changed chunk replaces only its own record (appended), all other strings stay");
    }

    static void IncrementalFlushes()
    {
        var random = new System.Random(5150);
        var list = new List<string>(Markers); var expectedOthers = new List<string>(Markers);
        var ledger = MetaCellLedger.Attach(list, new HashSet<string>(), true); var oracle = new HashSet<(int, int, int)>();
        int[] kinds = { R, M, O3 };
        int flushes = 0, errors = 0, markers = 0; string first = null;
        for (int i = 1; i <= 500; i++) {
            int kind = kinds[random.Next(kinds.Length)], x = random.Next(-70, 70), y = random.Next(-40, 40);
            if (ledger.Add(kind, x, y) != oracle.Add((kind, x, y))) { errors++; first ??= "add " + Cell(kind, x, y); }
            if (i % 7 == 0) { string marker = "probe-marker:" + Inv(markers++); list.Add(marker); expectedOthers.Add(marker); }
            if (i % 11 == 0 && expectedOthers.Count > 0) {
                // Like RaisePeak's RemoveAt of an old "tp:" marker between two flushes.
                string gone = expectedOthers[random.Next(expectedOthers.Count)];
                expectedOthers.Remove(gone); list.RemoveAt(list.LastIndexOf(gone));
            }
            if (i % 50 != 0) continue;
            ledger.Flush(list); flushes++;
            int records = RecordCount(list, out bool unique);
            if (!unique || records != ledger.ChunkCount) { errors++; first ??= "flush " + flushes + ": " + records + " records for " + ledger.ChunkCount + " chunks"; }
            if (!Members(list).SetEquals(oracle)) { errors++; first ??= "flush " + flushes + ": membership differs"; }
            if (!list.Where(text => !IsRecord(text)).SequenceEqual(expectedOthers)) { errors++; first ??= "flush " + flushes + ": non-record strings changed"; }
            if (new HashSet<string>(list).Count != list.Count) { errors++; first ??= "flush " + flushes + ": duplicate strings"; }
        }
        Check(flushes == 10 && errors == 0, "B3 incremental: 500 Add calls with markers appended and removed, 10 flushes: always one record per chunk, exact membership, other strings unchanged" + Example(first));
        Check(SameCells(ledger, oracle, out string difference), "B3 incremental: the live ledger matches the oracle" + Example(difference));
    }

    static void Compaction()
    {
        // Two differing records of the chunk M(0, 0), plus a legacy key in the same chunk.
        var partA = new uint[16]; partA[0] = 0x0000FFFFu; partA[3] = 1u;
        var partB = new uint[16]; partB[0] = 0xFFFF0000u; partB[15] = 0x80000000u;
        string recordA = MetaCellLedger.Encode(M, 0, 0, partA), recordB = MetaCellLedger.Encode(M, 0, 0, partB);
        string[] legacy = { "revealed:5:7", "revealed:40:300", "mined:-3:-40", "mined:-3:\u221240", "mined:-4:\u200E-41", "mined:-5:\u200F\u221242", "mined:-6:\u061C-43",
            "l2-block:6:-41", "l3-block:7:-41", "xxl-layer:4:8:-900", "xxl-layer:5:9:-1000", "surface-block:10:40", "mined:1:1", "1:12:-100", "2:12:-100", "3:-33:\u2212100", "33:7:-650", "0:0:0" };
        // Kept verbatim: markers, opaque records, an out-of-domain and an unfoldable cell key.
        var kept = new List<string>(Markers) { "#2M:0:0:f", "#1M:0:0:b!!", "mined:1048576:0", "mined:007:1" };
        var input = new List<string>();
        for (int i = 0; i < Math.Max(kept.Count, legacy.Length); i++) {
            if (i < kept.Count) input.Add(kept[i]);
            if (i < legacy.Length) input.Add(legacy[i]);
            if (i == 2) input.Add(recordA);
            if (i == 9) input.Add(recordB);
        }
        var expected = Members(input);
        Check(MetaCellLedger.NeedsCompaction(input), "B4 compaction: NeedsCompaction finds the legacy keys and the duplicate record");
        var list = new List<string>(input);
        bool compacted = MetaCellLedger.Compact(list);
        int records = RecordCount(list, out bool unique);
        Check(compacted, "B4 compaction: Compact of the mixed list returns true");
        Check(list.Where(text => !IsRecord(text)).SequenceEqual(kept), "B4 compaction: markers, opaque records (#2M:0:0:f, #1M:0:0:b!!), mined:1048576:0 and mined:007:1 stay byte-identical in their order");
        Check(!HasLegacyCell(list), "B4 compaction: no in-domain legacy cell key remains");
        Check(unique && records == expected.Select(cell => (cell.Item1, cell.Item2 >> 5, cell.Item3 >> 4)).Distinct().Count() && Members(list).SetEquals(expected),
            "B4 compaction: one record per chunk (" + records + ") holding exactly the folded cells (both minus spellings of (-3, -40) are one cell)");
        var rows = new uint[16]; string merged = list.FirstOrDefault(text => text.StartsWith("#1M:0:0:", StringComparison.Ordinal) && IsRecord(text));
        bool merging = merged != null && MetaCellLedger.TryDecode(merged, rows, out _, out _, out _) && rows[0] == uint.MaxValue && rows[1] == 2u && rows[3] == 1u && rows[15] == 0x80000000u &&
            Enumerable.Range(0, 16).All(r => r == 0 || r == 1 || r == 3 || r == 15 || rows[r] == 0);
        Check(merging, "B4 compaction: the duplicated chunk M(0, 0) holds the OR of both records and mined:1:1 (" + Show(merged) + ")");
        Check(LegacyValid(list), "B4 compaction: the result passes the legacy ValidStrings rules (non-blank, at most 160 chars, unique)");
        var once = new List<string>(list);
        Check(!MetaCellLedger.Compact(list) && list.SequenceEqual(once), "B4 compaction: a second Compact returns false and leaves the list unchanged");
        Info("B4 compaction: NeedsCompaction after compaction = " + MetaCellLedger.NeedsCompaction(list) + " (the opaque #1M:0:0:b!! shares the header of M(0, 0); Compact still reports false, so no write loop)");
        var clean = new List<string>(Markers) { recordA };
        var cleanCopy = new List<string>(clean);
        Check(!MetaCellLedger.NeedsCompaction(clean) && !MetaCellLedger.Compact(clean) && clean.SequenceEqual(cleanCopy), "B4 compaction: a list without legacy keys or duplicate records needs no compaction and stays untouched");
    }

    static void OlderBuildUnion()
    {
        var random = new System.Random(31337); int[] kinds = { R, M, O3, O33 };
        var cellsA = RandomCells(random, 400, kinds, 120, 300);
        var cellsB = RandomCells(random, 300, kinds, 120, 300).Concat(cellsA.Take(100)).ToList();
        var cellsC = RandomCells(random, 100, kinds, 120, 300).Concat(cellsA.Skip(200).Take(25)).Concat(cellsB.Take(25)).ToList();
        List<string> Legacy(IEnumerable<(int, int, int)> cells, int offset) => cells.Select((cell, i) => LegacyKey(cell.Item1, cell.Item2, cell.Item3, i + offset)).ToList();
        var listA = new List<string>(Markers); Union(listA, Legacy(cellsA, 0));
        var listAB = new List<string>(Markers); Union(listAB, Legacy(cellsA, 0)); Union(listAB, Legacy(cellsB, 1));
        bool compactedA = MetaCellLedger.Compact(listA), compactedAB = MetaCellLedger.Compact(listAB);
        // An older build string-unions both ledgers (MergeLedger) and appends its own legacy keys.
        var union = new List<string>(listA); Union(union, listAB); Union(union, Legacy(cellsC, 2));
        var expected = new HashSet<(int, int, int)>(cellsA.Concat(cellsB).Concat(cellsC));
        var attached = MetaCellLedger.Attach(new List<string>(union), new HashSet<string>(), true);
        bool same = SameCells(attached, expected, out string difference);
        Check(compactedA && compactedAB && same, "B5 union: after Attach the membership is A ∪ B ∪ C (" + expected.Count + " cells)" + Example(difference));
        bool compacted = MetaCellLedger.Compact(union);
        int records = RecordCount(union, out bool unique);
        Check(compacted && unique && !HasLegacyCell(union) && Members(union).SetEquals(expected) && union.Where(text => !IsRecord(text)).SequenceEqual(Markers),
            "B5 union: Compact leaves one record per chunk (" + records + "), the same cells and the markers in order");
    }

    static void ReadMode()
    {
        var partA = new uint[16]; partA[2] = 0xF0u; var partB = new uint[16]; partB[2] = 0x0Fu; partB[9] = 1u;
        var list = new List<string>(Markers) {
            "revealed:5:7", "mined:-3:-40", "mined:-3:\u221240", "xxl-layer:5:9:-1000", "1:12:-100", "33:7:-650",
            MetaCellLedger.Encode(O3, -4, 7, partA), MetaCellLedger.Encode(O3, -4, 7, partB), MetaCellLedger.Encode(R, 0, 0, Fill(uint.MaxValue)),
            "#2M:0:0:Zfuture", "#1M:0:0:b!!", "mined:1048576:0", "3:05:7" };
        var before = new List<string>(list);
        var readOthers = new HashSet<string>();
        var reader = MetaCellLedger.Attach(list, readOthers, false);
        Check(!reader.Writes && list.Count == before.Count && Enumerable.Range(0, list.Count).All(i => ReferenceEquals(list[i], before[i])), "B6 read mode: Attach leaves the list sequence-identical");
        var writeList = new List<string>(before); var writeOthers = new HashSet<string>();
        var writer = MetaCellLedger.Attach(writeList, writeOthers, true);
        var expected = Members(before);
        bool readSame = SameCells(reader, expected, out string readDifference), writeSame = SameCells(writer, expected, out string writeDifference);
        Check(readSame && writeSame, "B6 read mode: the same membership as write mode" + Example(readDifference ?? writeDifference));
        Check(readOthers.SetEquals(writeOthers) && readOthers.Contains("#2M:0:0:Zfuture") && readOthers.Contains("mined:1048576:0") && !readOthers.Contains("revealed:5:7") &&
            reader.StringCells && writer.StringCells && reader.Folded == writer.Folded, "B6 read mode: others, StringCells (3:05:7) and Folded agree with write mode");
        var (freeX, freeY) = FreeCell(expected, M, -1, -3);
        bool added = reader.Add(M, freeX, freeY) && reader.Contains(M, freeX, freeY) && !reader.Add(M, freeX, freeY);
        Check(added && !reader.Flush(list) && list.SequenceEqual(before), "B6 read mode: a new cell is recorded in memory, Flush returns false and the list stays unchanged");
        var plain = MetaCellLedger.Attach(new List<string>(Markers) { "mined:1:2" }, null, false);
        Check(!plain.StringCells, "B6 read mode: canonical keys and markers alone do not switch on StringCells");
    }
    static void Union(List<string> target, IEnumerable<string> source)
    {
        var seen = new HashSet<string>(target);
        foreach (string entry in source) if (seen.Add(entry)) target.Add(entry);
    }

    // ---------- C. integration (MetaProgression in isolated profiles) ----------
    [Serializable] sealed class Envelope { public int version = 1; public string payload, sha256; }
    static MetaProfile P => MetaProgression.Profile;
    static MetaRunState Run => MetaProgression.CurrentRun;
    static string Tag(string section, bool writes) => section + (writes ? " write" : " read");
    // Runs one case in read mode (Step 1 default) and in write mode (Step 2), each in its own profile.
    static void BothModes(string section, Action<bool> body)
    {
        foreach (bool writes in new[] { false, true }) {
            try { body(writes); } catch (Exception e) { Check(false, Tag(section, writes) + ": aborted by " + Describe(e)); }
        }
    }
    // Small rounds (as ChallengeReworkChecks.S1), so the probe task fits into the monthly round (at most 16 tasks).
    static ChallengeSettings Settings()
    {
        var settings = new ChallengeSettings { diligenceDays = 2, diligenceDailyTasks = 1 };
        settings.daily.countEasy = 2; settings.daily.countMedium = 1; settings.daily.countHard = 1;
        settings.weekly.countEasy = 1; settings.weekly.countMedium = 2; settings.weekly.countHard = 1;
        settings.monthly.countEasy = 1; settings.monthly.countMedium = 1; settings.monthly.countHard = 0;
        return settings;
    }
    // Isolated profile for one case; writes sets CellChunkWriterOverride (null = the build default).
    static void Fresh(string name, bool? writes)
    {
        MetaProgression.EndRun();
        MetaProgression.TestDirectory = Path.Combine(root, name.Replace(' ', '-'));
        MetaProgression.ChallengeTestUtc = Now;
        MetaProgression.ChallengeSettingsOverride = Settings();
        MetaProgression.ChallengeTimeZoneOverride = TimeZoneInfo.Utc;
        MetaProgression.CellChunkWriterOverride = writes;
        Check(MetaProgression.CurrentRun == null && MetaProgression.TotalXp == 0 && MetaProgression.Profile.runs.Count == 0, name + ": starts with an isolated empty profile");
    }
    // Probe task appended to the monthly round to observe credits (as in ChallengeReworkChecks).
    static MetaChallengeTask Probe(string metric, int mode = 0, int subject = -1, bool atLeast = false) => new() {
        id = "probe-" + metric + (subject >= 0 ? "-" + subject : ""), name = "Probe", unit = "Probe", metric = metric, mode = mode, subject = subject, atLeast = atLeast,
        tier = 0, target = 1000000000, xp = 0 };
    static MetaChallengeRound Month() { MetaProgression.GetChallenges(ChallengePeriod.Monthly); return P.challengeRounds?.Find(r => r != null && r.period == (int)ChallengePeriod.Monthly); }
    static long Pr(string id) => P.challengeRounds?.Find(r => r != null && r.period == (int)ChallengePeriod.Monthly)?.tasks?.Find(t => t != null && t.id == id)?.progress ?? -1;
    // A revealed cell leaves no trace but the "cells" metric, so a probe task counts it. Call it with a run (no menu redraw).
    static bool AddProbe()
    {
        var month = Month(); var probe = Probe("cells");
        if (month == null) return false;
        if (!month.tasks.Exists(t => t != null && t.id == probe.id)) month.tasks.Add(probe);
        return month.tasks.Count <= 16 && Pr(probe.id) >= 0;
    }
    // Every credit a cell event can give: revealed cells (probe), mined cells (CreditedBlocks) and ore pieces.
    static long Credits() => Math.Max(0, Pr("probe-cells")) + MetaProgression.CreditedBlocks + (Run?.resources.Sum(r => (long)r.count) ?? 0);
    static int Pieces(Item item) => Run?.resources.Find(r => r != null && r.item == (int)item)?.count ?? 0;
    static Vector3Int V(int x, int y) => new(x, y, 0);
    static Item OreItem(int kind) => (Item)(kind - MetaCellLedger.OreBase);
    // Records one cell through its gameplay API (ore: one piece through the Vector3Int overload).
    static void Record((int kind, int x, int y) cell, bool blasted = false)
    {
        if (cell.kind == R) MetaProgression.RecordRevealedCell(cell.x, cell.y);
        else if (cell.kind == M) MetaProgression.RecordMinedBlock(V(cell.x, cell.y), 1, blasted);
        else MetaProgression.RecordResource(OreItem(cell.kind), 1, V(cell.x, cell.y));
    }
    static bool Has((int kind, int x, int y) cell) => cell.kind == R ? MetaProgression.HasRevealedCell(cell.x, cell.y)
        : cell.kind == M ? MetaProgression.HasMinedBlock(V(cell.x, cell.y)) : MetaProgression.HasMinedOre(OreItem(cell.kind), V(cell.x, cell.y));
    // count distinct cells that are not in used yet (and marks them there).
    static List<(int, int, int)> NewCells(System.Random random, int count, int[] kinds, int width, int depth, HashSet<(int, int, int)> used)
    {
        var result = new List<(int, int, int)>(count);
        while (result.Count < count) foreach (var cell in RandomCells(random, 1, kinds, width, depth)) if (used.Add(cell)) result.Add(cell);
        return result;
    }
    static void WaitForSave()
    {
        var timeout = DateTime.UtcNow.AddSeconds(20);
        while (MetaProgression.IsSaving) {
            MetaProgression.PumpSaves();
            if (DateTime.UtcNow > timeout) throw new TimeoutException("profile writer did not finish");
            System.Threading.Thread.Sleep(1);
        }
    }
    static string Sha(string text) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", ""); }
    // The profile file as a build writes it (uppercase SHA-256), without backup and journal.
    static void WriteEnvelope(string payload)
    {
        Directory.CreateDirectory(MetaProgression.DirectoryPath);
        File.WriteAllText(MetaProgression.ProfilePath, JsonUtility.ToJson(new Envelope { payload = payload, sha256 = Sha(payload) }));
        foreach (string extra in new[] { ".bak", ".tmp" }) if (File.Exists(MetaProgression.ProfilePath + extra)) File.Delete(MetaProgression.ProfilePath + extra);
    }
    static Envelope ReadEnvelope() => JsonUtility.FromJson<Envelope>(File.ReadAllText(MetaProgression.ProfilePath));
    static List<string> DiskLedger(string runId) => JsonUtility.FromJson<MetaProfile>(ReadEnvelope().payload).runs.Find(r => r.runId == runId)?.resourceSources ?? new List<string>();
    // The integrity formula ValidateProfile enforces on every load.
    static long Formula(MetaProfile p) => p.periodRewardXp + p.runs.Sum(r => (long)Math.Floor(r.progressXp + r.efficiencyXp) + r.challengeXp) +
        p.completedMilestones.Sum(id => (long)(MetaProgressionCatalog.Challenge(id)?.xp ?? 0));
    static string Ms(double milliseconds) => milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
    static string FirstDifference(string a, string b)
    {
        int i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        int start = Math.Max(0, i - 30);
        return Show(a.Substring(start, Math.Min(70, a.Length - start))) + " / " + Show(b.Substring(start, Math.Min(70, b.Length - start)));
    }

    // C1: every cell kind, both identities of an ore cell, a string source and an out-of-domain cell.
    static void RecordingIdentity() => BothModes("C1", RecordingIdentityIn);
    static void RecordingIdentityIn(bool writes)
    {
        string name = Tag("C1", writes);
        Fresh(name, writes);
        MetaProgression.BeginRun("c1-world");
        Check(AddProbe() && MetaProgression.WritesCellChunks == writes, name + ": probe task in the monthly round, WritesCellChunks follows the override");
        long cells = Pr("probe-cells");
        MetaProgression.RecordRevealedCell(5, 7);
        bool once = Pr("probe-cells") == cells + 1;
        MetaProgression.RecordRevealedCell(5, 7);
        Check(once && Pr("probe-cells") == cells + 1 && MetaProgression.HasRevealedCell(5, 7) && !MetaProgression.HasRevealedCell(6, 7),
            name + ": RecordRevealedCell(5, 7) counts one cell, a repeat nothing; HasRevealedCell is true");
        var mined = V(90001, -40); int blocks = MetaProgression.CreditedBlocks;
        MetaProgression.RecordMinedBlock(mined, 2, false);
        once = MetaProgression.CreditedBlocks == blocks + 1;
        MetaProgression.RecordMinedBlock(mined, 2, true);
        Check(once && MetaProgression.CreditedBlocks == blocks + 1 && MetaProgression.HasMinedBlock(mined) && !MetaProgression.HasMinedBlock(V(90002, -40)),
            name + ": RecordMinedBlock((90001, -40)) credits one block, again blasted nothing; HasMinedBlock is true");
        var ore = V(12, -100); int iron = Pieces(Item.Iron), gold = Pieces(Item.Gold);
        MetaProgression.RecordResource(Item.Iron, 1, ore);
        once = Pieces(Item.Iron) == iron + 1;
        MetaProgression.RecordResource(Item.Iron, 1, "12:-100");
        bool same = Pieces(Item.Iron) == iron + 1;
        MetaProgression.RecordResource(Item.Gold, 1, "12:-100");
        Check(once && same && Pieces(Item.Gold) == gold + 1, name + ": iron at (12, -100) credits once (the cell and \"12:-100\" are one identity), gold at the same cell credits");
        Check(MetaProgression.HasMinedOre(Item.Iron, ore) && MetaProgression.HasMinedOre(Item.Gold, ore) && !MetaProgression.HasMinedOre(Item.Coal, ore),
            name + ": HasMinedOre is true for iron and gold and false for coal at (12, -100)");
        int coal = Pieces(Item.Coal);
        MetaProgression.RecordResource(Item.Coal, 1, "replay-coal-1"); MetaProgression.RecordResource(Item.Coal, 1, "replay-coal-1");
        Check(Pieces(Item.Coal) == coal + 1, name + ": the string source replay-coal-1 credits once");
        // Session-culture spelling, as the old code wrote cell keys.
        var far = V(2000000, -5); string farKey = "mined:" + far.x + ":" + far.y; blocks = MetaProgression.CreditedBlocks;
        MetaProgression.RecordMinedBlock(far, 1, false); MetaProgression.RecordMinedBlock(far, 1, true);
        Check(MetaProgression.CreditedBlocks == blocks + 1 && MetaProgression.HasMinedBlock(far) && Run.resourceSources.Contains(farKey),
            name + ": the out-of-domain cell (2000000, -5) credits once and keeps the string key " + Show(farKey));
        var live = Run.resourceSources;
        if (writes) {
            Check(!HasLegacyCell(live) && live.Contains("3:replay-coal-1"), name + ": the live list holds no in-domain legacy cell key, but 3:replay-coal-1");
            var snapshot = MetaProgression.CaptureRunState().resourceSources;
            Check(snapshot.Contains("#1M:2812:-3:sER") && snapshot.Any(k => k.StartsWith("#1R:0:0:", StringComparison.Ordinal)) &&
                snapshot.Any(k => k.StartsWith("#1O1:0:-7:", StringComparison.Ordinal)) && snapshot.Any(k => k.StartsWith("#1O2:0:-7:", StringComparison.Ordinal)),
                name + ": CaptureRunState holds #1M:2812:-3:sER and the records #1R:0:0:, #1O1:0:-7: (iron) and #1O2:0:-7: (gold)");
        } else {
            var expected = new[] { "revealed:5:7", "mined:" + mined.x + ":" + mined.y, "1:" + ore.x + ":" + ore.y, "2:12:-100", farKey };
            var cellKeys = live.Where(k => MetaCellLedger.TryParseLegacy(k, out _, out _, out _)).ToList();
            Check(cellKeys.Count == expected.Length && expected.All(live.Contains) && live.Contains("3:replay-coal-1") && !live.Any(k => k.StartsWith("#", StringComparison.Ordinal)),
                name + ": the live list holds exactly the old keys revealed:5:7, mined:90001:-40, 1:12:-100, 2:12:-100, mined:2000000:-5 and 3:replay-coal-1, no record (" +
                string.Join(" ", cellKeys.Select(Show)) + ")");
        }
    }

    // C2: the saved file stays readable for older builds; after a reload every C1 cell is still guarded.
    static void IdentityEvents()
    {
        MetaProgression.RecordRevealedCell(5, 7);
        MetaProgression.RecordMinedBlock(V(90001, -40), 2, false); MetaProgression.RecordMinedBlock(V(90001, -40), 2, true);
        MetaProgression.RecordResource(Item.Iron, 1, V(12, -100)); MetaProgression.RecordResource(Item.Iron, 1, "12:-100"); MetaProgression.RecordResource(Item.Gold, 1, "12:-100");
        MetaProgression.RecordResource(Item.Coal, 1, "replay-coal-1"); MetaProgression.RecordMinedBlock(V(2000000, -5), 1, false);
    }
    static void Persistence() => BothModes("C2", PersistenceIn);
    static void PersistenceIn(bool writes)
    {
        string name = Tag("C2", writes), id = "c2-world";
        Fresh(name, writes);
        MetaProgression.BeginRun(id);
        bool probe = AddProbe();
        IdentityEvents();
        long credits = Credits();
        Check(probe && MetaProgression.Save() && !MetaProgression.IsSaving && MetaProgression.LastError == null, name + ": Save succeeds");
        var envelope = ReadEnvelope(); var disk = JsonUtility.FromJson<MetaProfile>(envelope.payload);
        var ledger = disk.runs.Find(r => r.runId == id)?.resourceSources;
        Check(envelope.version == 1 && envelope.sha256 == Sha(envelope.payload) && ledger != null && disk.runs.All(r => LegacyValid(r.resourceSources)),
            name + ": the saved runs pass the legacy ValidateRun string rules (at most 2,000,000 unique non-blank strings of at most 160 chars)");
        if (ledger != null && writes) {
            int records = RecordCount(ledger, out bool unique);
            Check(records > 0 && unique && !HasLegacyCell(ledger) && ledger.Where(k => k.StartsWith("#", StringComparison.Ordinal)).All(IsRecord),
                name + ": the file holds " + records + " records, one per chunk, and no in-domain legacy cell key");
        } else if (ledger != null) Check(ledger.Contains("revealed:5:7") && !ledger.Any(k => k.StartsWith("#", StringComparison.Ordinal)), name + ": the file holds the legacy keys and no record (reader-only step)");
        MetaProgression.Reload();
        MetaProgression.BeginRun(id);
        var cells = new (int, int, int)[] { (R, 5, 7), (M, 90001, -40), (MetaCellLedger.OreKind((int)Item.Iron), 12, -100), (MetaCellLedger.OreKind((int)Item.Gold), 12, -100) };
        Check(MetaProgression.LastError == null && cells.All(c => Has(c)) && MetaProgression.HasMinedBlock(V(2000000, -5)) && Pr("probe-cells") >= 0 && Credits() == credits,
            name + ": after Reload and BeginRun every C1 cell is still recorded (the out-of-domain one too)");
        IdentityEvents();
        Check(Credits() == credits, name + ": repeating the C1 events after the reload credits nothing");
    }

    // C3: replaying older snapshots in any order never credits a recorded cell again and never forgets one.
    static void ReplayIdentity() => BothModes("C3", ReplayIdentityIn);
    static void ReplayIdentityIn(bool writes)
    {
        string name = Tag("C3", writes), id = "c3-world";
        Fresh(name, writes);
        MetaProgression.BeginRun(id);
        bool probe = AddProbe();
        var s0 = MetaProgression.CaptureRunState();
        var random = new System.Random(303); var used = new HashSet<(int, int, int)>();
        List<(int, int, int)> NewSet() => NewCells(random, 100, new[] { R }, 300, 600, used).Concat(NewCells(random, 100, new[] { M }, 300, 600, used))
            .Concat(NewCells(random, 50, new[] { O3, MetaCellLedger.OreKind((int)Item.Iron) }, 300, 600, used)).ToList();
        long Replay(IEnumerable<(int, int, int)> cells) { long before = Credits(); foreach (var cell in cells) Record(cell); return Credits() - before; }
        var a = NewSet();
        long gainA = Replay(a);
        Check(probe && gainA == a.Count && a.All(c => Has(c)), name + ": set A (100 revealed, 100 mined, 50 ore cells) credits each cell once (" + gainA + ")");
        long xp = MetaProgression.TotalXp;
        MetaProgression.BeginRun(id, s0);
        Check(MetaProgression.TotalXp >= xp, name + ": BeginRun with the older snapshot S0 does not lower TotalXp");
        Check(Replay(a) == 0, name + ": re-recording A after loading S0 credits nothing");
        var b = NewSet();
        long gainB = Replay(b), again = Replay(b);
        Check(gainB == b.Count && again == 0, name + ": the new distinct set B credits each cell exactly once (" + gainB + ", repeat " + again + ")");
        var s1 = MetaProgression.CaptureRunState();
        var both = a.Concat(b).ToList();
        MetaProgression.BeginRun(id, s0);
        Check(both.All(c => Has(c)) && Replay(both) == 0, name + ": after loading S0 again A and B stay guarded");
        MetaProgression.BeginRun(id, s1);
        Check(Replay(both) == 0, name + ": after loading S1 nothing is credited");
        Check(MetaProgression.TotalXp == Formula(P), name + ": TotalXp equals the integrity formula");
    }

    // C4: a world save of an older build with 30,000 legacy keys.
    static void LegacyWorldSave() => BothModes("C4", LegacyWorldSaveIn);
    static void LegacyWorldSaveIn(bool writes)
    {
        string name = Tag("C4", writes), id = "c4-world";
        Fresh(name, writes);
        MetaProgression.BeginRun(id);
        bool probe = AddProbe();
        var snapshot = MetaProgression.CaptureRunState();
        var cells = NewCells(new System.Random(404), 30000, new[] { R, M, O3, O33 }, 400, 900, new HashSet<(int, int, int)>());
        // Every mined prefix and minus spelling (LegacyKey variant).
        var keys = cells.Select((cell, i) => LegacyKey(cell.Item1, cell.Item2, cell.Item3, i)).ToList();
        snapshot.resourceSources.AddRange(keys);
        int count = snapshot.resourceSources.Count; long xp = MetaProgression.TotalXp, credits = Credits();
        Check(probe && !cells.Any(c => Has(c)), name + ": the 30,000 cells are not recorded yet");
        MetaProgression.BeginRun(id, snapshot);
        Check(MetaProgression.TotalXp == xp && Credits() == credits, name + ": BeginRun with 30,000 legacy keys (every family, both minus spellings) keeps the XP");
        Check(cells.All(c => Has(c)), name + ": all 30,000 cells count as recorded");
        foreach (var cell in cells) Record(cell);
        Check(Credits() == credits, name + ": recording them credits nothing");
        Check(snapshot.resourceSources.Count == count, name + ": the snapshot is not mutated");
        var live = new HashSet<string>(Run.resourceSources);
        if (writes) {
            Check(!keys.Any(live.Contains), name + ": the live list holds none of the 30,000 keys");
            Check(MetaProgression.Save() && !HasLegacyCell(DiskLedger(id)), name + ": the saved file holds no legacy cell key");
        } else Check(keys.All(live.Contains), name + ": the live list keeps all 30,000 keys verbatim (reader-only step)");
    }

    // C5 (write mode): a legacy profile is compacted at load and written at once.
    static void LegacyProfileMigration()
    {
        const string name = "C5 write", id = "c5-world";
        Fresh(name, true);
        // An older build's profile: one run with 300,000 legacy cell keys of every family and spelling, three non-cell strings, consistent XP.
        var cells = new List<(int, int, int)>(300000);
        for (int x = 0; x < 300; x++) for (int depth = 1; depth <= 500; depth++) cells.Add((R, x, depth));
        for (int x = 0; x < 300; x++) for (int y = -1; y >= -400; y--) cells.Add((M, x, y));
        for (int x = 0; x < 300; x++) for (int y = -401; y >= -500; y--) cells.Add((O3, x, y));
        string[] others = { "tp:sales:5", "vein:3:1:2", "harvest:trees:a" };
        var legacy = new MetaRunState { runId = id, progressXp = 1234.5 };
        for (int i = 0; i < cells.Count; i++) {
            if (i % 100000 == 0) legacy.resourceSources.Add(others[i / 100000]);
            legacy.resourceSources.Add(LegacyKey(cells[i].Item1, cells[i].Item2, cells[i].Item3, i));
        }
        WriteEnvelope(JsonUtility.ToJson(new MetaProfile { revision = 3, totalXp = 1234, runs = { legacy } }));
        long oldSize = new FileInfo(MetaProgression.ProfilePath).Length;
        MetaProgression.Reload();
        long total = MetaProgression.TotalXp;
        bool saving = MetaProgression.IsSaving;
        var ledger = P.runs.Find(r => r.runId == id)?.resourceSources ?? new List<string>();
        Check(MetaProgression.LastError == null && total == 1234, name + ": the legacy profile with 300,000 cell keys loads with its XP (" + total + ")");
        Check(ledger.Count < 5000 && !HasLegacyCell(ledger) && ledger.Where(k => !IsRecord(k)).SequenceEqual(others) && Members(ledger).SetEquals(cells),
            name + ": the run is compacted to " + ledger.Count + " strings with every cell, the non-cell strings stay in order");
        Check(saving, name + ": the compacted profile is written at once (IsSaving)");
        WaitForSave();
        long newSize = new FileInfo(MetaProgression.ProfilePath).Length;
        Check(MetaProgression.LastError == null && newSize * 20 < oldSize, name + ": the file shrinks below 5 % (" + Inv(oldSize) + " -> " + Inv(newSize) + " bytes)");
        MetaProgression.Reload();
        total = MetaProgression.TotalXp;
        Check(!MetaProgression.IsSaving && total == 1234 && MetaProgression.LastError == null, name + ": a second Reload writes nothing (no second compaction)");
        MetaProgression.BeginRun(id);
        bool probe = AddProbe();
        var random = new System.Random(505);
        var sample = Enumerable.Range(0, 1000).Select(_ => cells[random.Next(cells.Count)]).ToList();
        long credits = Credits();
        Check(probe && sample.All(c => Has(c)), name + ": after BeginRun 1,000 sampled cells are recorded");
        foreach (var cell in sample) Record(cell);
        Check(Credits() == credits, name + ": recording them credits nothing");
    }

    // C6: runs older builds reject (more than 2,000,000 strings) are accepted, so their profile can be compacted.
    static void Rescue()
    {
        var state = new MetaRunState { runId = "c6-rescue" };
        state.resourceSources.Capacity = 2000001;
        for (int i = 0; i <= 2000000; i++) state.resourceSources.Add("rescue:" + Inv(i));
        Check(MetaProgression.ValidateRunState(state), "C6 rescue: ValidateRunState accepts a run with 2,000,001 unique strings (older builds reject it)");
        var limit = typeof(MetaProgression).GetField("MaxLedgerEntries", BindingFlags.NonPublic | BindingFlags.Static);
        Check(limit != null && limit.IsLiteral && (int)limit.GetValue(null) == 6000000, "C6 rescue: MetaProgression.MaxLedgerEntries is 6,000,000");
    }

    // C7 (write mode): new -> old -> new loses nothing; the older build's stale records and legacy keys are folded at load.
    static void DowngradeCycle()
    {
        const string name = "C7 write", id = "c7-world";
        Fresh(name, true);
        MetaProgression.BeginRun(id);
        bool probe = AddProbe();
        var random = new System.Random(707); var used = new HashSet<(int, int, int)>(); int[] kinds = { R, M, O3 };
        var first = NewCells(random, 200, kinds, 100, 200, used);
        foreach (var cell in first) Record(cell);
        var older = MetaProgression.CaptureRunState();
        var second = NewCells(random, 200, kinds, 100, 200, used);
        foreach (var cell in second) Record(cell);
        long xp = MetaProgression.TotalXp;
        MetaProgression.EndRun();
        // What an older build writes: it string-unions the stale records of an older world save into the profile run, adds its own
        // legacy keys (bind re-adds revealed cells) for one recorded and one new cell, drops the tallies and raises the revision.
        var payload = JsonUtility.FromJson<MetaProfile>(ReadEnvelope().payload);
        var stored = payload.runs.Find(r => r.runId == id);
        var added = new[] { second.First(c => c.Item1 == M), NewCells(random, 1, new[] { M }, 100, 200, used)[0], second.First(c => c.Item1 == R), NewCells(random, 1, new[] { R }, 100, 200, used)[0] };
        Union(stored.resourceSources, older.resourceSources);
        foreach (var (kind, x, y) in added) stored.resourceSources.Add((kind == M ? "mined:" : "revealed:") + Inv(x) + ":" + Inv(y));
        RecordCount(stored.resourceSources, out bool unique);
        foreach (var state in payload.runs) state.tallies.Clear();
        payload.revision++;
        WriteEnvelope(JsonUtility.ToJson(payload));
        MetaProgression.Reload();
        long total = MetaProgression.TotalXp;
        Check(probe && !unique && MetaProgression.LastError == null && total == xp, name + ": the older build's profile (stale records, legacy keys, no tallies) loads with unchanged XP");
        WaitForSave();
        var ledger = DiskLedger(id);
        RecordCount(ledger, out bool once);
        var all = first.Concat(second).Concat(added).ToList();
        Check(once && !HasLegacyCell(ledger) && Members(ledger).SetEquals(all), name + ": after the save the file holds one record per chunk with every cell and no legacy cell key");
        MetaProgression.BeginRun(id);
        long credits = Credits();
        Check(all.All(c => Has(c)), name + ": after BeginRun the union of all cells is guarded, including the new cells of the older build");
        foreach (var cell in all) Record(cell);
        Check(Credits() == credits, name + ": recording them again credits nothing");
    }

    // C8: a reader-only build (Step 1) on a profile with chunk records keeps them verbatim and adds legacy keys.
    static void ReadOnlyStep()
    {
        const string name = "C8", id = "c8-world";
        Fresh(name + " write", true);
        MetaProgression.BeginRun(id);
        bool probe = AddProbe();
        var random = new System.Random(808); var used = new HashSet<(int, int, int)>();
        var cells = NewCells(random, 300, new[] { R, M, O3 }, 100, 200, used);
        foreach (var cell in cells) Record(cell);
        long xp = MetaProgression.TotalXp;
        MetaProgression.EndRun();
        var records = DiskLedger(id).Where(IsRecord).ToList();
        Check(probe && records.Count > 0, name + ": the profile was written with " + records.Count + " chunk records");
        MetaProgression.CellChunkWriterOverride = false;
        MetaProgression.Reload();
        long total = MetaProgression.TotalXp;
        Check(total == xp && !MetaProgression.IsSaving && !MetaProgression.WritesCellChunks && MetaProgression.LastError == null, name + ": with the writer off, Reload neither compacts nor writes");
        MetaProgression.BeginRun(id);
        long credits = Credits();
        Check(cells.All(c => Has(c)), name + ": the cells of the records are guarded in read mode");
        foreach (var cell in cells) Record(cell);
        bool repeat = Credits() == credits;
        var extra = NewCells(random, 1, new[] { M }, 100, 200, used)[0];
        string key = "mined:" + extra.Item2 + ":" + extra.Item3;
        Record(extra);
        Check(repeat && Credits() == credits + 1 && Run.resourceSources.Contains(key), name + ": repeats credit nothing; a new cell credits and appends its legacy key " + Show(key));
        Check(MetaProgression.Save(), name + ": Save in read mode");
        var saved = DiskLedger(id);
        Check(records.All(saved.Contains) && saved.Count(IsRecord) == records.Count && saved.Contains(key), name + ": the file keeps every original record verbatim, plus the legacy key");
        xp = MetaProgression.TotalXp;
        MetaProgression.EndRun();
        MetaProgression.CellChunkWriterOverride = true;
        MetaProgression.Reload();
        total = MetaProgression.TotalXp;
        bool saving = MetaProgression.IsSaving;
        WaitForSave();
        var compacted = DiskLedger(id);
        RecordCount(compacted, out bool unique);
        Check(total == xp && saving && unique && !HasLegacyCell(compacted) && Members(compacted).SetEquals(cells.Append(extra)),
            name + ": with the writer on, Reload folds the legacy key into one record per chunk holding every cell");
    }

    // C9: unknown and malformed "#" strings of later builds survive every path verbatim.
    static void ForwardCompatibility() => BothModes("C9", ForwardCompatibilityIn);
    static void ForwardCompatibilityIn(bool writes)
    {
        string name = Tag("C9", writes), id = "c9-world";
        Fresh(name, writes);
        MetaProgression.BeginRun(id);
        MetaProgression.RecordMinedBlock(V(40, -40), 1, false);
        var snapshot = MetaProgression.CaptureRunState();
        string[] opaque = { "#2M:0:0:Zfuture", "#1M:0:0:b!!", "#1M:0:0:sIA" };
        snapshot.resourceSources.AddRange(opaque);
        bool Kept(List<string> list) => list != null && opaque.All(list.Contains);
        MetaProgression.BeginRun(id, snapshot);
        bool begun = Kept(Run.resourceSources);
        bool saved = MetaProgression.Save() && Kept(DiskLedger(id));
        MetaProgression.Reload();
        bool reloaded = Kept(P.runs.Find(r => r.runId == id)?.resourceSources) && MetaProgression.LastError == null;
        MetaProgression.BeginRun(id);
        var capture = MetaProgression.CaptureRunState();
        Check(begun && saved && reloaded && Kept(capture.resourceSources) && MetaProgression.ValidateRunState(capture) && MetaProgression.HasMinedBlock(V(40, -40)),
            name + ": #2M:0:0:Zfuture, #1M:0:0:b!! and #1M:0:0:sIA survive BeginRun, Save, Reload, BeginRun and CaptureRunState verbatim, without rejection (" +
            begun + "/" + saved + "/" + reloaded + ")");
    }

    // C10 (write mode): every path that serializes or copies the run carries a cell recorded just before it.
    static void FlushPaths()
    {
        const string name = "C10 write", id = "c10-world";
        Fresh(name, true);
        MetaProgression.BeginRun(id);
        int next = 0;
        Vector3Int NextCell() { next++; return V(60000 + 37 * next, -50 - next); }
        bool Holds(List<string> list, Vector3Int cell) => list != null && Members(list).Contains((M, cell.x, cell.y));
        WaitForSave();
        var cell1 = NextCell(); MetaProgression.RecordMinedBlock(cell1, 1, false);
        bool requested = MetaProgression.RequestSave(); WaitForSave();
        Check(requested && Holds(DiskLedger(id), cell1), name + ": RequestSave writes the record of a cell recorded just before");
        var cell2 = NextCell(); MetaProgression.RecordMinedBlock(cell2, 1, false);
        Check(MetaProgression.Save() && Holds(DiskLedger(id), cell2), name + ": Save writes it");
        var cell3 = NextCell(); MetaProgression.RecordMinedBlock(cell3, 1, false);
        var snapshot = MetaProgression.CaptureRunState();
        Check(Holds(snapshot.resourceSources, cell3), name + ": CaptureRunState holds it");
        var cell4 = NextCell(); MetaProgression.RecordMinedBlock(cell4, 1, false);
        MetaProgression.EndRun();
        Check(Holds(P.runs.Find(r => r.runId == id)?.resourceSources, cell4) && Holds(DiskLedger(id), cell4), name + ": EndRun leaves it in Profile.runs and in the file");
        MetaProgression.BeginRun(id);
        Check(MetaProgression.HasMinedBlock(cell4), name + ": BeginRun(id) still guards it");
        var cell5 = NextCell(); MetaProgression.RecordMinedBlock(cell5, 1, false);
        MetaProgression.BeginRun("c10-other"); MetaProgression.BeginRun(id);
        Check(MetaProgression.HasMinedBlock(cell5), name + ": BeginRun(\"other\") then BeginRun(id) still guards it");
        // Write-blocked: a newer build replaced the profile. The run still records into its ledger and reaches the world save.
        MetaProgression.EndRun();
        File.WriteAllText(MetaProgression.ProfilePath, "{\"version\":999,\"payload\":\"future\",\"sha256\":\"future\"}");
        MetaProgression.Reload();
        MetaProgression.BeginRun(id, snapshot);
        var cell6 = NextCell(); MetaProgression.RecordMinedBlock(cell6, 1, false);
        var blocked = MetaProgression.CaptureRunState();
        Check(MetaProgression.ProfileWarning != null && Holds(blocked.resourceSources, cell6) && Holds(blocked.resourceSources, cell3), name + ": write-blocked, CaptureRunState still holds the new cell");
        var cell7 = NextCell(); MetaProgression.RecordMinedBlock(cell7, 1, false);
        MetaProgression.BeginRun("c10-other"); MetaProgression.BeginRun(id);
        Check(MetaProgression.HasMinedBlock(cell7) && MetaProgression.HasMinedBlock(cell6), name + ": write-blocked, BeginRun(\"other\") then BeginRun(id) still guards it (flushed before the blocked write returns)");
    }

    // C11: CopyRun copies every MetaRunState field and shares no list with the live run.
    static readonly string[] RunFields = { "version", "runId", "loadout", "settings", "maxDepth", "activeSeconds", "recentWorkXp", "workWindowSeconds", "progressXp", "efficiencyXp",
        "earnedXp", "challengeXp", "milestoneXp", "ended", "won", "resources", "resourceSources", "regions", "discoveries", "crafts",
        "exoticCrafts", "objectiveIds", "completedObjectives", "milestones", "tallies", "rewardXp" };
    static void CopyRunParity()
    {
        var fields = typeof(MetaRunState).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(field => field.Name).ToList();
        var added = fields.Except(RunFields).ToList(); var gone = RunFields.Except(fields).ToList();
        Check(added.Count == 0 && gone.Count == 0, "C11 CopyRun: MetaRunState has exactly the 26 known fields; copy a new one in MetaProgression.CopyRun and list it here (new: " +
            string.Join(", ", added) + "; gone: " + string.Join(", ", gone) + ")");
        BothModes("C11", CopyRunParityIn);
    }
    // A non-default value for a MetaRunState field that CopyRun and Sanitized() keep as it is; null = no sample for this type.
    static object Sample(Type type, string field, int index)
    {
        if (field == "runId") return "c11-world";
        if (type == typeof(int)) return 100 + index;
        if (type == typeof(long)) return 1000L + index;
        if (type == typeof(double)) return 10.5 + index;
        if (type == typeof(bool)) return true;
        if (type == typeof(string)) return "c11-" + field;
        if (type == typeof(List<string>)) return new List<string> { "c11-" + field };
        if (type == typeof(List<int>)) return new List<int> { 40 + index };
        if (type == typeof(List<MetaResourceCount>)) return new List<MetaResourceCount> { new MetaResourceCount { item = (int)Item.Gold, count = 7 } };
        if (type == typeof(List<MetaRunTally>)) return new List<MetaRunTally> { new MetaRunTally { id = "c11", value = 3, peak = 9 } };
        if (type == typeof(MetaLoadout)) return new MetaLoadout { ranks = new List<MetaUpgradeRank> { new MetaUpgradeRank { id = "health", rank = 2 } },
            healthMultiplier = 1.25f, energyMultiplier = 1.5f, movementMultiplier = 1.125f, jumpMultiplier = 1.0625f, miningEnergyMultiplier = .75f,
            startMoney = 50, startingTorches = 3, startingLadders = 4, buildReachBonus = .5f, carryCapacityBonus = 2.5f };
        if (type == typeof(MetaProgressionSettings)) return new MetaProgressionSettings { xpMultiplier = 2f, depthStep = 30, depthBaseXp = 31f, depthIncrementXp = 6f,
            depthMaxXp = 151f, explorationRegionSize = 16, explorationCellThreshold = 30, explorationXp = 17f, resourceXpMultiplier = 1.5f, discoveryXp = 181f,
            efficiencyMaxBonus = .125f, efficiencyReferenceXpPerMinute = 101f, depthXpMaxFactor = 2.5f, depthXpFullDepth = 800, resourceXpExponent = .75f };
        return null;
    }
    static void CopyRunParityIn(bool writes)
    {
        string name = Tag("C11", writes);
        Fresh(name, writes);
        MetaProgression.BeginRun("c11-world");
        var live = Run; var unknown = new List<string>(); int index = 0;
        foreach (var field in typeof(MetaRunState).GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            object value = Sample(field.FieldType, field.Name, ++index);
            if (value == null) unknown.Add(field.Name); else field.SetValue(live, value);
        }
        string copied = JsonUtility.ToJson(MetaProgression.CaptureRunState()), json = JsonUtility.ToJson(Run);
        Check(unknown.Count == 0 && copied == json, name + ": CaptureRunState (CopyRun) serializes exactly like the live run with every field set" +
            (unknown.Count > 0 ? " (no sample value for " + string.Join(", ", unknown) + ")" : copied == json ? "" : " (" + FirstDifference(copied, json) + ")"));
        var snapshot = MetaProgression.CaptureRunState();
        json = JsonUtility.ToJson(Run);
        foreach (var list in new[] { snapshot.resourceSources, snapshot.regions, snapshot.discoveries, snapshot.objectiveIds, snapshot.completedObjectives, snapshot.milestones }) {
            list[0] = "c11-changed"; list.Add("c11-added");
        }
        snapshot.crafts[0]++; snapshot.crafts.Add(1); snapshot.exoticCrafts[0]++; snapshot.exoticCrafts.Add(2);
        snapshot.resources[0].count++; snapshot.resources.Add(new MetaResourceCount { item = (int)Item.Silver, count = 1 });
        snapshot.tallies[0].value++; snapshot.tallies[0].peak++; snapshot.tallies.Add(new MetaRunTally { id = "c11-added", value = 1, peak = 1 });
        snapshot.loadout.ranks[0].rank++; snapshot.loadout.ranks.Add(new MetaUpgradeRank { id = "energy", rank = 1 }); snapshot.loadout.healthMultiplier += 1f;
        snapshot.settings.xpMultiplier += 1f;
        Check(JsonUtility.ToJson(Run) == json, name + ": mutating the snapshot's lists, resources, tallies, loadout and settings leaves the live run unchanged");
    }

    // C12: cell events between tally markers that RaisePeak removes from the middle of the list.
    static void TallyMarkerInterleaving() => BothModes("C12", TallyMarkerInterleavingIn);
    static void TallyMarkerInterleavingIn(bool writes)
    {
        string name = Tag("C12", writes);
        Fresh(name, writes);
        MetaProgression.BeginRun("c12-world");
        var random = new System.Random(1212); var oracle = new HashSet<(int, int, int)>(); int[] kinds = { R, M, O3 };
        int saves = 0, errors = 0; string first = null;
        for (int i = 1; i <= 500; i++) {
            foreach (var cell in RandomCells(random, 1, kinds, 40, 40)) { Record(cell); oracle.Add(cell); }
            if (i % 2 == 0) MetaProgression.RecordSale(1);
            if (i % 3 == 0) MetaProgression.RecordCraftedAmount(Item.BridgePart, 1);
            if (i % 50 != 0) continue;
            MetaProgression.RequestSave(); WaitForSave(); saves++;
            var list = Run.resourceSources;
            int records = RecordCount(list, out bool unique);
            int chunks = oracle.Select(cell => (cell.Item1, cell.Item2 >> 5, cell.Item3 >> 4)).Distinct().Count();
            string problem = new HashSet<string>(list).Count != list.Count ? "duplicate strings"
                : !Members(list).SetEquals(oracle) ? "membership differs from the oracle"
                : writes && (!unique || records != chunks || HasLegacyCell(list)) ? records + " records for " + chunks + " chunks"
                : !writes && records != 0 ? records + " records in read mode"
                : TallyProblem(list);
            if (problem != null) { errors++; first ??= "event " + i + ": " + problem; }
        }
        Check(saves == 10 && errors == 0, name + ": 500 cell events between sales and crafts (tp marker RemoveAt) and 10 saves: the live list stays unique, " +
            (writes ? "one record per chunk, " : "") + "exact membership, one tp marker per tally" + Example(first));
    }
    // null when every tally with a peak has exactly one "tp:<id>:<peak>" marker and no other marker of its id.
    static string TallyProblem(List<string> list)
    {
        foreach (var tally in Run.tallies) {
            if (tally == null || tally.id == "tallies" || tally.peak <= 0) continue;
            string prefix = "tp:" + tally.id + ":";
            var markers = list.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k.LastIndexOf(':') == prefix.Length - 1).ToList();
            if (markers.Count != 1 || markers[0] != prefix + Inv(tally.peak)) return "tally " + tally.id + ": " + markers.Count + " markers for peak " + tally.peak;
        }
        return null;
    }

    // C13 (write mode): a fully explored 600 x 1200 world stays small; NOTE lines report the timings (no limits).
    static void Size()
    {
        const string name = "C13 write", id = "c13-world";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // Every revealed cell, 60 % mined in tunnel bands (rows 0-29 of every 50), 10 % ore of 6 kinds.
        var ledger = Empty(); var random = new System.Random(1313);
        int[] ores = { (int)Item.Coal, (int)Item.Copper, (int)Item.Iron, (int)Item.Silver, (int)Item.Gold, (int)Item.Platinum };
        int revealed = 0, mined = 0, ore = 0;
        for (int x = 0; x < 600; x++)
            for (int depth = 1; depth < 1200; depth++) {
                ledger.Add(R, x, depth); revealed++;
                if (depth / 10 % 5 < 3) { ledger.Add(M, x, -depth); mined++; }
                if (random.Next(10) == 0) { ledger.Add(MetaCellLedger.OreKind(ores[random.Next(ores.Length)]), x, -depth); ore++; }
            }
        double build = watch.Elapsed.TotalMilliseconds;
        var records = new List<string>();
        watch.Restart(); ledger.Flush(records); double flush = watch.Elapsed.TotalMilliseconds;
        Check(revealed == 719400 && records.Count == ledger.ChunkCount && records.Count <= 8 * 1425, name + ": the world needs " + records.Count + " records (at most (2 + 6) x 1,425)");
        var copy = new List<string>(records);
        watch.Restart(); MetaCellLedger.Attach(copy, new HashSet<string>(), true); double attach = watch.Elapsed.TotalMilliseconds;

        Fresh(name, true);
        MetaProgression.BeginRun(id);
        var snapshot = MetaProgression.CaptureRunState();
        snapshot.resourceSources.AddRange(records);
        watch.Restart(); MetaProgression.BeginRun(id, snapshot); double begin = watch.Elapsed.TotalMilliseconds;
        MetaProgression.RecordMinedBlock(V(0, -45), 1, false); // rows 40-49 are no tunnel: a new cell, so Save writes
        watch.Restart(); bool saved = MetaProgression.Save(); double save = watch.Elapsed.TotalMilliseconds;
        var onDisk = DiskLedger(id); long size = new FileInfo(MetaProgression.ProfilePath).Length;
        int diskRecords = RecordCount(onDisk, out bool unique);
        Check(saved && unique && diskRecords <= 8 * 1425 && size < 1500000 && onDisk.All(k => k.Length <= MetaCellLedger.MaxRecordLength),
            name + ": the saved run holds " + diskRecords + " records (at most 11,400), the file has " + Inv(size) + " bytes (under 1.5 MB), every string at most 110 chars");
        MetaProgression.RecordMinedBlock(V(1, -45), 1, false);
        watch.Restart(); MetaProgression.RequestSave(); double request = watch.Elapsed.TotalMilliseconds;
        WaitForSave();
        MetaProgression.RecordMinedBlock(V(2, -45), 1, false);
        watch.Restart(); var capture = MetaProgression.CaptureRunState(); double capturing = watch.Elapsed.TotalMilliseconds;
        Check(capture.resourceSources.Count(IsRecord) == diskRecords && MetaProgression.HasMinedBlock(V(2, -45)) && MetaProgression.HasRevealedCell(599, 1199) && !MetaProgression.HasMinedBlock(V(3, -45)),
            name + ": the capture holds the same chunks and the ledger answers lookups");
        // The same world as legacy keys (what older builds wrote): the one-time compaction at load.
        var legacy = new List<string>(revealed + mined + ore); var rows = new uint[16];
        foreach (string record in records) {
            if (!MetaCellLedger.TryDecode(record, rows, out int kind, out int cx, out int cy)) continue;
            for (int i = 0; i < 512; i++) if ((rows[i >> 5] & (1u << (i & 31))) != 0) legacy.Add(LegacyKey(kind, cx * 32 + (i & 31), cy * 16 + (i >> 5), 0));
        }
        int legacyCount = legacy.Count;
        watch.Restart(); bool compacted = MetaCellLedger.Compact(legacy); double compact = watch.Elapsed.TotalMilliseconds;
        Check(compacted && new HashSet<string>(legacy).SetEquals(records), name + ": Compact of the equivalent " + Inv(legacyCount) + " legacy keys gives exactly the same records");
        lines.Add("NOTE " + name + ": " + Inv(revealed) + " revealed, " + Inv(mined) + " mined, " + Inv(ore) + " ore cells (built in " + Ms(build) + "); Flush " + Ms(flush) +
            ", Attach " + Ms(attach) + ", BeginRun with the snapshot " + Ms(begin) + ", RequestSave (main thread) " + Ms(request) + ", Save " + Ms(save) +
            ", CaptureRunState " + Ms(capturing) + ", Compact of " + Inv(legacyCount) + " legacy keys " + Ms(compact));
    }
}
