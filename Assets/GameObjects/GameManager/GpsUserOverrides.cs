#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Eigene GPS-/F1-Werte im Ordner des Benutzers ("Mining Game", Docs/GPS-Eigene-Werte.md). Sie landen nicht im versionierten
// GpsProfile.asset, sondern in UserSettings/GpsOverrides.json (nicht versioniert) und werden beim Laden über die Standardwerte
// des Profils gelegt. Das Profil ändert sich dort nur bewusst über Branch/PR, und main lässt sich immer aktualisieren.
// Gespeichert wird nur, was vom Standard abweicht, bis hinunter zum einzelnen Objekt-/Listenfeld: Neue Standardwerte auf main
// kommen an, solange derselbe Wert nicht selbst überschrieben ist.
public static class GpsUserOverrides
{
    public const string UserFolder = "Mining Game";
    // path: Feldname, darunter Kindnamen mit "/"; arrays: Länge jeder Liste auf dem Weg, damit verschobene Einträge nicht falsch treffen.
    [Serializable] public sealed class Entry { public string record, path; public List<int> arrays = new(); public GpsValue value; }
    [Serializable] sealed class FileData { public int version = 1; public string savedUtc; public List<Entry> entries = new(); }

    // Checks schalten die Schicht ausserhalb des Benutzerordners ein und schreiben in eine temporäre Datei.
    public static string TestPath;
    public static bool? TestActive;
    // Nur auf dem Hauptthread abfragen (Application.dataPath); das Lesen von Profiltexten läuft auch im Hintergrund.
    static string projectRoot;
    static string ProjectRoot => projectRoot ??= Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
    public static bool Active => TestActive ?? Path.GetFileName(ProjectRoot) == UserFolder;
    public static string FilePath => TestPath ?? Path.Combine(ProjectRoot, "UserSettings", "GpsOverrides.json");
    // Lautstärke bleibt in den EditorPrefs (GpsSettings), sie gehört nie in die Überschreibungen.
    public static readonly string[] Personal = { nameof(GpsPreferences.masterVolume), nameof(GpsPreferences.musicVolume) };

    static List<Entry> entries;
    static HashSet<string> roots;
    public static event Action Changed;
    // Vom Editor gesetzt (GpsDefaultsProposal): eigene Werte als neue Standardwerte auf einen Branch/PR stellen.
    public static Action Propose;
    public static string LastMessage { get; set; }
    // Während main aktualisiert wird (MainAutoUpdate): neu übernommene Werte stehen noch im geladenen Profil und dürfen
    // beim Speichern nicht als "gleich wie Standard" wegfallen; gespeichert wird dann zusätzlich zu den bisherigen Einträgen.
    public static bool Hold;
    public static void Notify(string message) { LastMessage = message; Changed?.Invoke(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { entries = null; roots = null; }

    public static IReadOnlyList<Entry> Entries { get { EnsureLoaded(); return entries; } }
    public static int Count => Entries.Count;
    public static bool IsOverridden(string record, string field) { EnsureLoaded(); return roots.Contains(record + "\n" + field); }
    public static IEnumerable<(string record, string field)> OverriddenRoots() =>
        Entries.Select(entry => (entry.record, entry.path.Split('/')[0])).Distinct();

    static void EnsureLoaded()
    {
        if (entries != null) return;
        entries = new List<Entry>();
        try
        {
            if (File.Exists(FilePath))
                entries = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath))?.entries?.Where(entry => entry?.value != null &&
                    !string.IsNullOrEmpty(entry.record) && !string.IsNullOrEmpty(entry.path)).ToList() ?? new List<Entry>();
        }
        catch (Exception ex)
        {
            // Defekte Datei nicht überschreiben, sondern beiseitelegen: Die Werte lassen sich von Hand retten.
            string aside = FilePath + ".defekt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(FilePath, aside); } catch { }
            Debug.LogWarning("Eigene GPS-Werte nicht lesbar, beiseitegelegt als " + aside + ": " + ex.Message);
        }
        RebuildRoots();
    }
    static void RebuildRoots() => roots = new HashSet<string>(entries.Select(entry => entry.record + "\n" + entry.path.Split('/')[0]));

    public static void Reload() { entries = null; EnsureLoaded(); Changed?.Invoke(); }

    public static void Write(List<Entry> list)
    {
        EnsureLoaded();
        string json = JsonUtility.ToJson(new FileData { savedUtc = DateTime.UtcNow.ToString("o"), entries = list }, true);
        // Gleiche Einträge: Datei und .bak bleiben, wie sie sind (sonst ersetzte schon ein Klick ohne Änderung die Sicherung).
        if (File.Exists(FilePath) && Unchanged(File.ReadAllText(FilePath), list)) { entries = list; RebuildRoots(); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
        entries = list; RebuildRoots();
        Changed?.Invoke();
    }
    // Vergleicht mit dem Zeitstempel der vorhandenen Datei, damit nur die Einträge zählen.
    static bool Unchanged(string text, List<Entry> list)
    {
        try { return JsonUtility.ToJson(new FileData { savedUtc = JsonUtility.FromJson<FileData>(text)?.savedUtc, entries = list }, true) == text; }
        catch { return false; }
    }

    // ---------- Anwenden und Vergleichen ----------

    // Legt die Überschreibungen über doc. stale: Einträge, deren Stelle es so nicht mehr gibt (umgebaute Liste, entferntes Feld).
    public static int Apply(GpsDocument doc, GpsProfile profile, IEnumerable<Entry> list, List<Entry> stale = null)
    {
        int applied = 0;
        foreach (var group in list.GroupBy(entry => (entry.record, field: entry.path.Split('/')[0])))
        {
            var root = Root(doc, profile, group.Key.record, group.Key.field, out var owner);
            if (root == null) { stale?.AddRange(group); continue; }
            var before = root.Copy();
            int count = 0;
            foreach (var entry in group)
                if (Replace(root, entry)) count++; else stale?.Add(entry);
            if (count == 0) continue;
            // Später enger gefasste Grenzen klemmen den eigenen Wert, statt ihn zu verwerfen.
            GpsSettings.ClampBounds(root, GpsSchema.Spec(owner, root.name));
            if (!GpsSettings.IsValidOverride(owner, root) && !(DropUnresolved(group.ToList(), root, profile) && GpsSettings.IsValidOverride(owner, root)))
            {
                stale?.AddRange(group.Where(entry => !stale.Contains(entry)));
                Store(doc, profile, owner, before);
                continue;
            }
            Store(doc, profile, owner, root);
            applied += count;
        }
        return applied;
    }

    // Eine ganz gespeicherte Liste kann Einträge für Assets enthalten, die es nur lokal gab (z. B. ein nicht versionierter Clip):
    // nur diese Einträge fallen weg, nicht die ganze Liste mit allen übrigen eigenen Werten.
    static bool DropUnresolved(List<Entry> group, GpsValue root, GpsProfile profile)
    {
        if (!profile || root.kind != GpsValueKind.Array || group.Count != 1 || group[0].path.IndexOf('/') >= 0) return false;
        int removed = root.children.RemoveAll(child => Unresolved(child, profile));
        if (removed == 0) return false;
        for (int i = 0; i < root.children.Count; i++) root.children[i].name = i.ToString();
        Debug.LogWarning("Eigener GPS-Wert " + group[0].record + " / " + group[0].path + ": " + removed + " Listeneintrag/-einträge ohne Asset entfernt (alte Datei: " + FilePath + ".bak)");
        return true;
    }
    static bool Unresolved(GpsValue node, GpsProfile profile) =>
        node.kind == GpsValueKind.Reference && !string.IsNullOrEmpty(node.text) && !profile.Resolve(node.text) || node.children.Any(child => Unresolved(child, profile));

    // Was in current vom baseline abweicht, so fein wie möglich (gleiche Objekt-/Listenstruktur → einzelne Kinder).
    public static List<Entry> Diff(GpsDocument current, GpsDocument baseline, GpsProfile profile)
    {
        var result = new List<Entry>();
        foreach (var record in current.records)
        {
            var other = baseline.records.Find(item => item.key == record.key);
            if (other == null) continue;
            foreach (var field in record.fields)
            {
                var otherField = other.fields.Find(item => item.name == field.name);
                if (otherField != null) DiffNode(record.key, field, otherField, field.name, new List<int>(), result);
            }
        }
        foreach (var (key, type) in Virtual)
            foreach (var spec in GpsSchema.Fields(type))
            {
                if (key == "preferences" && Personal.Contains(spec.name)) continue;
                var a = VirtualNode(current, profile, key, spec.name); var b = VirtualNode(baseline, profile, key, spec.name);
                if (a != null && b != null) DiffNode(key, a, b, spec.name, new List<int>(), result);
            }
        return result;
    }

    // Vereinigt zwei Listen; bei gleicher Stelle gewinnt preferred. Einträge, die eine Stelle von preferred enthalten
    // oder in ihr liegen, fallen weg, damit nie zwei Einträge dieselbe Stelle setzen.
    public static List<Entry> Merge(IEnumerable<Entry> preferred, IEnumerable<Entry> other)
    {
        var result = preferred.ToList();
        foreach (var entry in other)
            if (!result.Any(kept => kept.record == entry.record && Overlaps(kept.path, entry.path))) result.Add(entry);
        return result;
    }
    static bool Overlaps(string a, string b) => a == b || a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);

    static void DiffNode(string record, GpsValue a, GpsValue b, string path, List<int> arrays, List<Entry> result)
    {
        if (Same(a, b)) return;
        bool nested = a.kind == b.kind && (a.kind == GpsValueKind.Object || a.kind == GpsValueKind.Array) &&
            a.children.Count == b.children.Count && a.children.Select(child => child.name).SequenceEqual(b.children.Select(child => child.name));
        if (!nested) { result.Add(new Entry { record = record, path = path, arrays = arrays.ToList(), value = a.Copy() }); return; }
        var inner = a.kind == GpsValueKind.Array ? arrays.Append(a.children.Count).ToList() : arrays;
        for (int i = 0; i < a.children.Count; i++) DiffNode(record, a.children[i], b.children[i], path + "/" + a.children[i].name, inner, result);
    }

    public static bool Same(GpsValue a, GpsValue b)
    {
        if (a == null || b == null) return a == b;
        if (a.kind != b.kind || a.name != b.name) return false;
        switch (a.kind)
        {
            case GpsValueKind.Boolean: return a.flag == b.flag;
            case GpsValueKind.Text: case GpsValueKind.Reference: return (a.text ?? "") == (b.text ?? "");
            case GpsValueKind.Vector: return Close(a.vector.x, b.vector.x) && Close(a.vector.y, b.vector.y) && Close(a.vector.z, b.vector.z) && Close(a.vector.w, b.vector.w);
            case GpsValueKind.Color: return Close(a.color.r, b.color.r) && Close(a.color.g, b.color.g) && Close(a.color.b, b.color.b) && Close(a.color.a, b.color.a);
            case GpsValueKind.Curve:
                var x = a.curve ?? new GpsCurve(); var y = b.curve ?? new GpsCurve();
                if (x.preWrap != y.preWrap || x.postWrap != y.postWrap || x.keys.Count != y.keys.Count) return false;
                for (int i = 0; i < x.keys.Count; i++)
                {
                    var p = x.keys[i]; var q = y.keys[i];
                    if (!Close(p.time, q.time) || !Close(p.value, q.value) || !Close(p.inTangent, q.inTangent) || !Close(p.outTangent, q.outTangent) ||
                        !Close(p.inWeight, q.inWeight) || !Close(p.outWeight, q.outWeight) || p.weightedMode != q.weightedMode) return false;
                }
                return true;
            case GpsValueKind.Object: case GpsValueKind.Array:
                if (a.children.Count != b.children.Count) return false;
                for (int i = 0; i < a.children.Count; i++) if (!Same(a.children[i], b.children[i])) return false;
                return true;
            case GpsValueKind.Number: return Close(a.number, b.number);
            default: return a.number == b.number;
        }
    }
    // Unitys JSON-Leser rundet lange Kommazahlen nicht immer gleich ("0.45176199078559878" vs. "0.4517619907855988"
    // nach erneutem Speichern). Solche Unterschiede in der letzten Stelle sind keine eigenen Werte.
    static bool Close(double a, double b) => a == b || Math.Abs(a - b) <= 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b));
    static bool Close(float a, float b) => a == b || Math.Abs(a - b) <= 1e-6f * Math.Max(Math.Abs(a), Math.Abs(b));

    // Lesbarer Wert für Protokoll und PR-Text.
    public static string Describe(GpsValue node)
    {
        if (node == null) return "–";
        switch (node.kind)
        {
            case GpsValueKind.Boolean: return node.flag ? "an" : "aus";
            case GpsValueKind.Text: case GpsValueKind.Reference: return string.IsNullOrEmpty(node.text) ? "–" : node.text;
            case GpsValueKind.Vector: return "(" + string.Join("; ", new[] { node.vector.x, node.vector.y, node.vector.z, node.vector.w }.Select(value => Number(value))) + ")";
            case GpsValueKind.Color: return "#" + ColorUtility.ToHtmlStringRGBA(node.color);
            case GpsValueKind.Curve: return "Kurve (" + (node.curve?.keys.Count ?? 0) + " Punkte)";
            case GpsValueKind.Object: case GpsValueKind.Array: return node.kind == GpsValueKind.Array ? "Liste (" + node.children.Count + ")" : "Objekt";
            default: return Number(node.number);
        }
    }
    static string Number(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    public static GpsValue Find(GpsDocument doc, GpsProfile profile, Entry entry)
    {
        var parts = entry.path.Split('/');
        var node = Root(doc, profile, entry.record, parts[0], out _);
        for (int i = 1; node != null && i < parts.Length; i++) node = node.children.Find(child => child.name == parts[i]);
        return node;
    }

    static readonly (string key, Type type)[] Virtual = { ("preferences", typeof(GpsPreferences)), ("tests", typeof(GameplayTestSettingsData)) };
    static object VirtualTarget(GpsDocument doc, string key) => key == "preferences" ? doc.preferences : key == "tests" ? doc.tests : null;
    static GpsValue VirtualNode(GpsDocument doc, GpsProfile profile, string key, string field)
    {
        var target = VirtualTarget(doc, key);
        var type = target == null ? null : GpsCodec.MemberType(target.GetType(), field);
        return type == null ? null : GpsCodec.Read(field, type, GpsCodec.Get(target, field), profile ? profile.Key : _ => null);
    }
    static GpsValue Root(GpsDocument doc, GpsProfile profile, string record, string field, out GpsRecord owner)
    {
        var target = VirtualTarget(doc, record);
        if (target != null)
        {
            if (record == "preferences" && Personal.Contains(field)) { owner = null; return null; }
            var node = VirtualNode(doc, profile, record, field);
            owner = new GpsRecord { key = record, type = target.GetType().AssemblyQualifiedName, name = "", fields = node == null ? new List<GpsValue>() : new List<GpsValue> { node } };
            return node;
        }
        owner = doc.records.Find(item => item.key == record);
        return owner?.fields.Find(item => item.name == field);
    }
    static void Store(GpsDocument doc, GpsProfile profile, GpsRecord owner, GpsValue root)
    {
        var target = VirtualTarget(doc, owner.key);
        if (target != null) { GpsCodec.Set(target, root.name, GpsCodec.Write(root, profile ? profile.Resolve : _ => null)); return; }
        int index = owner.fields.FindIndex(item => item.name == root.name);
        if (index >= 0) owner.fields[index] = root;
    }
    static bool Replace(GpsValue root, Entry entry)
    {
        var parts = entry.path.Split('/');
        GpsValue parent = null, node = root;
        int array = 0;
        for (int i = 1; i < parts.Length; i++)
        {
            if (node.kind == GpsValueKind.Array && (array >= entry.arrays.Count || entry.arrays[array++] != node.children.Count)) return false;
            parent = node; node = node.children.Find(child => child.name == parts[i]);
            if (node == null) return false;
        }
        if (node.kind != entry.value.kind) return false;
        var value = entry.value.Copy(); value.name = node.name;
        if (parent == null)
        {
            // Wurzel: in place überschreiben, damit Store das Feld im Datensatz ersetzt.
            node.type = value.type; node.text = value.text; node.number = value.number; node.flag = value.flag; node.vector = value.vector;
            node.color = value.color; node.curve = value.curve; node.children = value.children;
        }
        else parent.children[parent.children.IndexOf(node)] = value;
        return true;
    }

    // ---------- GpsProfile.asset als Text lesen (für Versionen aus Git, ohne Unity-Import) ----------

    // Holt documentJson aus dem YAML eines GpsProfile.asset. Unity schreibt den Text als YAML-String in doppelten
    // Anführungszeichen mit Zeilenumbrüchen (Falten); die werden hier nach YAML 1.1 aufgelöst.
    public static GpsDocument ParseProfileYaml(string yaml)
    {
        string json = ExtractScalar(yaml, "documentJson");
        return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<GpsDocument>(json);
    }
    public static string ExtractScalar(string yaml, string key)
    {
        if (yaml == null) return null;
        string marker = "\n  " + key + ": ";
        int start = yaml.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        int i = start + marker.Length;
        if (i >= yaml.Length) return null;
        if (yaml[i] != '"')
        {
            int end = yaml.IndexOf('\n', i);
            return (end < 0 ? yaml.Substring(i) : yaml.Substring(i, end - i)).TrimEnd('\r', ' ');
        }
        i++;
        var text = new StringBuilder();
        int keep = 0; // alles bis hier ist maskiert und darf beim Falten nicht als Leerraum entfernt werden
        while (i < yaml.Length)
        {
            char c = yaml[i];
            if (c == '"') return text.ToString();
            if (c == '\\')
            {
                char e = i + 1 < yaml.Length ? yaml[i + 1] : '\0';
                i += 2;
                switch (e)
                {
                    case 'n': text.Append('\n'); break;
                    case 't': case '\t': text.Append('\t'); break;
                    case 'r': text.Append('\r'); break;
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case '0': text.Append('\0'); break;
                    case 'a': text.Append('\a'); break;
                    case 'b': text.Append('\b'); break;
                    case 'v': text.Append('\v'); break;
                    case 'f': text.Append('\f'); break;
                    case 'e': text.Append('\x1b'); break;
                    case ' ': text.Append(' '); break;
                    case '_': text.Append((char)0x00A0); break;
                    case 'N': text.Append((char)0x0085); break;
                    case 'L': text.Append((char)0x2028); break;
                    case 'P': text.Append((char)0x2029); break;
                    case 'x': text.Append((char)Convert.ToInt32(yaml.Substring(i, 2), 16)); i += 2; break;
                    case 'u': text.Append((char)Convert.ToInt32(yaml.Substring(i, 4), 16)); i += 4; break;
                    case 'U': text.Append(char.ConvertFromUtf32(Convert.ToInt32(yaml.Substring(i, 8), 16))); i += 8; break;
                    case '\r': case '\n':
                        // Maskierter Zeilenumbruch: ohne Leerzeichen weiter, Einrückung der Folgezeile fällt weg.
                        if (e == '\r' && i < yaml.Length && yaml[i] == '\n') i++;
                        while (i < yaml.Length && (yaml[i] == ' ' || yaml[i] == '\t')) i++;
                        break;
                    default: text.Append(e); break;
                }
                keep = text.Length;
                continue;
            }
            if (c == '\r' || c == '\n')
            {
                // Faltung: Leerraum am Zeilenende weg, Zeilenumbruch → Leerzeichen, jede Leerzeile → \n.
                int trim = text.Length;
                while (trim > keep && (text[trim - 1] == ' ' || text[trim - 1] == '\t')) trim--;
                text.Length = trim;
                int breaks = 0;
                while (i < yaml.Length && (yaml[i] == '\r' || yaml[i] == '\n' || yaml[i] == ' ' || yaml[i] == '\t'))
                {
                    if (yaml[i] == '\n') breaks++;
                    i++;
                }
                text.Append(breaks <= 1 ? " " : new string('\n', breaks - 1));
                keep = text.Length;
                continue;
            }
            text.Append(c); i++;
        }
        return null; // kein schliessendes Anführungszeichen
    }
}
#endif
