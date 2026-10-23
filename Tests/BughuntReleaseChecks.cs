using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Release builds (R2-release-0/1), Edit Mode:
// - link.xml preserves every engine member GPS reaches only by name (catalog, shipped records and nested value types),
//   so IL2CPP does not strip them and reject every GPS document
// - a release build keeps only the personal volumes of a player file: F1 values of a development build on the same PC,
//   imported debug files and old full copies no longer hide the shipped profile
// Reads files and works on copies of the shipped document; nothing in the project, the profile or the player data changes.
public static class BughuntReleaseChecks
{
    const string LinkPath = "Assets/GameObjects/GameManager/link.xml", SettingsPath = "Assets/GameObjects/GameManager/GpsSettings.cs";
    static readonly List<string> lines = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL " + label); lines.Add("PASS " + label); }
    static GpsDocument Copy(GpsDocument doc) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(doc));
    static GpsValue Field(GpsDocument doc, Type type, string name) =>
        doc.records.First(record => record.type == type.AssemblyQualifiedName).fields.Find(field => field.name == name);

    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode erwartet.");
        lines.Clear();
        try
        {
            LinkChecks();
            ReleaseFilterChecks();
            File.WriteAllLines("Temp/BughuntReleaseChecks.txt", lines.Append("PASS (" + lines.Count + ")"));
            return "PASS (" + lines.Count + ")";
        }
        catch (Exception ex) { File.WriteAllLines("Temp/BughuntReleaseChecks.txt", lines.Append("FAIL " + ex)); throw; }
    }

    // Value kinds GpsCodec stores without reflecting into the type (same order as GpsCodec.KindOf).
    static bool IsLeaf(Type type) => typeof(Object).IsAssignableFrom(type) || type.IsEnum || type == typeof(bool) || type == typeof(string) ||
        type == typeof(AnimationCurve) || type == typeof(Color) || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) ||
        type == typeof(Vector2Int) || type == typeof(Vector3Int) || type.IsPrimitive || type == typeof(decimal);
    static Type Element(Type type) => type.IsArray ? type.GetElementType() : typeof(IList).IsAssignableFrom(type) ? type.GetGenericArguments()[0] : null;
    static bool Engine(Type type) => type.Assembly.GetName().Name != "Assembly-CSharp";

    // R2-release-0: every member that GPS reads or writes by name outside Assembly-CSharp must be kept by link.xml.
    static void LinkChecks()
    {
        Check(File.Exists(LinkPath), "link.xml vorhanden");
        var root = XDocument.Load(LinkPath).Root;
        var allAssemblies = new HashSet<string>(root.Elements("assembly").Where(a => (string)a.Attribute("preserve") == "all").Select(a => (string)a.Attribute("fullname")));
        var kept = new HashSet<(string assembly, string type, string member)>();
        var wholeTypes = new HashSet<(string assembly, string type)>();
        foreach (var assembly in root.Elements("assembly"))
            foreach (var type in assembly.Elements("type"))
            {
                string assemblyName = (string)assembly.Attribute("fullname"), typeName = (string)type.Attribute("fullname");
                if ((string)type.Attribute("preserve") == "all") wholeTypes.Add((assemblyName, typeName));
                foreach (var member in type.Elements().Where(e => e.Name == "property" || e.Name == "field"))
                    kept.Add((assemblyName, typeName, (string)member.Attribute("name")));
                // A typo in link.xml would keep nothing; every entry names an existing member of the right assembly.
                var resolved = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName)?.GetType(typeName);
                Check(resolved != null, "link.xml-Typ existiert: " + assemblyName + "/" + typeName);
                foreach (var member in type.Elements().Where(e => e.Name == "property" || e.Name == "field"))
                {
                    string name = (string)member.Attribute("name");
                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    Check(member.Name == "property" ? resolved.GetProperty(name, flags) != null : resolved.GetField(name, flags) != null,
                        "link.xml-Mitglied existiert: " + typeName + "." + name);
                }
            }
        Check(allAssemblies.Contains("Assembly-CSharp"), "Assembly-CSharp bleibt vollständig erhalten");

        var needed = new HashSet<(Type type, string member)>();
        var visited = new HashSet<Type>();
        void Walk(Type type)
        {
            if (type == null || IsLeaf(type) || !visited.Add(type)) return;
            if (Element(type) is Type element) { Walk(element); return; }
            foreach (var field in GpsCodec.SerializableFields(type))
            {
                if (Engine(type)) needed.Add((type, field.Name));
                Walk(field.FieldType);
            }
        }
        void Member(Type owner, string name)
        {
            var memberType = GpsCodec.MemberType(owner, name);
            if (!Engine(owner)) { Walk(memberType); return; }
            Check(memberType != null, "Engine-Feld der GPS existiert im Editor: " + owner.Name + "." + name);
            needed.Add((owner, name));
            Walk(memberType);
        }
        foreach (var section in GpsSchema.Sections)
            foreach (var spec in section.fields) Member(section.source, spec.name);
        // Shipped records and the whole preference/test objects are stored too, not only the catalog entries.
        foreach (var record in GpsSettings.ShippedDocument().records)
        {
            var type = GpsCodec.ResolveType(record.type);
            Check(type != null, "Profiltyp auflösbar: " + record.type);
            foreach (var field in record.fields) Member(type, field.name);
        }
        Walk(typeof(GpsPreferences)); Walk(typeof(GameplayTestSettingsData));

        Check(needed.Contains((typeof(Rigidbody2D), "mass")) && needed.Contains((typeof(Camera), "orthographicSize")),
            "Durchsicht findet die Engine-Mitglieder des Katalogs (" + string.Join(", ", needed.Select(n => n.type.Name + "." + n.member)) + ")");
        foreach (var (type, member) in needed)
        {
            string assembly = type.Assembly.GetName().Name, name = type.FullName;
            Check(allAssemblies.Contains(assembly) || wholeTypes.Contains((assembly, name)) || kept.Contains((assembly, name, member)),
                "link.xml bewahrt " + assembly + "/" + name + "." + member);
        }
    }

    // R2-release-1: the release filter keeps the volumes and drops every other value of the player file.
    static void ReleaseFilterChecks()
    {
        var method = typeof(GpsSettings).GetMethod("KeepPersonalPreferences", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Check(method != null, "Release-Filter für persönliche Werte vorhanden");
        var shipped = GpsSettings.ShippedDocument();
        var loaded = Copy(shipped);
        float master = Mathf.Approximately(shipped.preferences.masterVolume, .3f) ? .4f : .3f;
        float music = Mathf.Approximately(shipped.preferences.musicVolume, .6f) ? .7f : .6f;
        loaded.preferences.masterVolume = master; loaded.preferences.musicVolume = music;
        loaded.preferences.startingResources.money += 1234;
        loaded.preferences.autosaveIntervalMinutes = shipped.preferences.autosaveIntervalMinutes + 5;
        loaded.preferences.metaProgression.xpMultiplier = shipped.preferences.metaProgression.xpMultiplier + 2;
        loaded.tests.infiniteMoney = !shipped.tests.infiniteMoney;
        Field(loaded, typeof(PlayerBaseStats), "miningSpeed").number += .25;
        var result = (GpsDocument)method.Invoke(null, new object[] { loaded, Copy(shipped) });
        Check(Mathf.Approximately(result.preferences.masterVolume, master) && Mathf.Approximately(result.preferences.musicVolume, music),
            "Release übernimmt Gesamt- und Musiklautstärke aus der Spielerdatei");
        var expected = Copy(shipped); expected.preferences.masterVolume = master; expected.preferences.musicVolume = music;
        Check(JsonUtility.ToJson(result) == JsonUtility.ToJson(expected),
            "Release verwirft Startgeld, Autosave, XP-Tempo, Testschalter und Datensatzwerte aus der Spielerdatei");

        // The filter runs for every player file in release builds, not only for old full copies; development builds keep F1 values.
        // Whitespace is removed first, so reformatting the loading block does not break the check.
        string source = Regex.Replace(File.ReadAllText(SettingsPath), @"\s+", "");
        int start = source.IndexOf("#if!UNITY_EDITORboolexisting", StringComparison.Ordinal);
        int end = start < 0 ? -1 : source.IndexOf("#else", start, StringComparison.Ordinal);
        Check(start >= 0 && end > start, "Ladeblock für Builds gefunden");
        string block = source.Substring(start, end - start);
        int import = block.IndexOf("GpsLegacyImport.Import(", StringComparison.Ordinal);
        int filter = block.IndexOf("KeepPersonalPreferences(document,ShippedDocument())", StringComparison.Ordinal);
        int release = block.IndexOf("#if!DEVELOPMENT_BUILD", StringComparison.Ordinal);
        Check(import >= 0 && filter > import && release >= 0 && release < filter && block.IndexOf("#endif", release, StringComparison.Ordinal) > filter,
            "Release-Filter läuft nach dem Altimport und nur ausserhalb von Development-Builds");
        string before = block.Substring(0, filter);
        Check(before.Count(c => c == '{') == before.Count(c => c == '}'), "Release-Filter gilt für jede Spielerdatei, nicht nur für alte Vollkopien");
        Check(block.IndexOf("GpsFileStore.Save(FilePath,document)", filter, StringComparison.Ordinal) > filter, "Gefilterter Stand wird zurückgeschrieben");
    }
}
