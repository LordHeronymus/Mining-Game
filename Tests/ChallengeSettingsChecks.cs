using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Edit-mode checks for the GPS section "Herausforderungen" (GpsPreferences.challenges, ChallengeSettings).
// Writes go to a temporary GpsProfile and Temp/; GpsProfile.asset and the GPS statics are restored.
// Report: Temp/ChallengeSettingsChecks.txt (one PASS/FAIL line per check).
public static class ChallengeSettingsChecks
{
    const string Report = "Temp/ChallengeSettingsChecks.txt";
    static readonly List<string> lines = new();
    static int passed, failed;
    static readonly FieldInfo ChallengesField = typeof(GpsPreferences).GetField("challenges", BindingFlags.Public | BindingFlags.Instance);

    static void Check(bool ok, string label)
    {
        if (ok) passed++; else failed++;
        lines.Add((ok ? "PASS " : "FAIL ") + label.Replace('\n', ' ').Replace('\r', ' '));
    }
    static void Group(string name, Action body) { try { body(); } catch (Exception e) { Check(false, name + ": aborted by " + Describe(e)); } }
    static string Describe(Exception e)
    {
        while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null) e = e.InnerException;
        var frame = new System.Diagnostics.StackTrace(e, false).GetFrames()?.FirstOrDefault(f => f.GetMethod()?.DeclaringType != null);
        return e.GetType().Name + ": " + e.Message + (frame != null ? " at " + frame.GetMethod().DeclaringType.Name + "." + frame.GetMethod().Name : "");
    }
    // Reflection keeps this file compilable while GpsPreferences.challenges is still being added.
    static ChallengeSettings Of(GpsPreferences preferences) => preferences == null ? null : ChallengesField?.GetValue(preferences) as ChallengeSettings;
    static GpsDocument Copy(GpsDocument document) => JsonUtility.FromJson<GpsDocument>(JsonUtility.ToJson(document));
    static string Json(object value) => value == null ? "null" : JsonUtility.ToJson(value);
    static GpsValue Child(GpsValue node, string path)
    {
        foreach (string part in path.Split('.')) node = node?.children?.Find(child => child.name == part);
        return node;
    }

    public static object Main()
    {
        lines.Clear(); passed = failed = 0;
        Directory.CreateDirectory("Temp");
        GpsSettings.EnsureLoaded();
        var statics = typeof(GpsSettings).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Where(f => !f.IsLiteral && !f.IsInitOnly).ToArray();
        var savedStatics = statics.Select(f => f.GetValue(null)).ToArray();
        var asset = GpsSettings.Profile;
        string assetJson = asset ? asset.documentJson : null;
        var original = Copy(GpsSettings.Document);
        var previousOverride = MetaProgression.ChallengeSettingsOverride;
        string directory = Path.GetFullPath("Temp/ChallengeSettingsChecks-" + DateTime.UtcNow.Ticks);
        GpsProfile temporary = null;
        bool assetUntouched = true;
        try {
            temporary = ScriptableObject.CreateInstance<GpsProfile>();
            temporary.assets = asset ? asset.assets.ToList() : new List<GpsAssetReference>();
            temporary.documentJson = assetJson;
            GpsSettings.UseProfile(temporary, Copy(original));
            Group("catalog", Catalog);
            Group("defaults", Defaults);
            Group("json", JsonDefaults);
            Group("sanitized", Sanitized);
            Group("set value", () => SetValues(temporary, assetJson));
            Group("overrides file", () => OverridesFile(original, directory));
        } catch (Exception e) {
            Check(false, "setup: aborted by " + Describe(e));
        } finally {
            assetUntouched = !asset || asset.documentJson == assetJson;
            for (int i = 0; i < statics.Length; i++) try { statics[i].SetValue(null, savedStatics[i]); } catch (Exception) { }
            if (asset && asset.documentJson != assetJson) asset.documentJson = assetJson;
            if (temporary) Object.DestroyImmediate(temporary);
            MetaProgression.ChallengeSettingsOverride = previousOverride;
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (Exception) { }
        }
        Check(assetUntouched, "hygiene: GpsProfile.asset documentJson never written");
        Check(ReferenceEquals(GpsSettings.Profile, asset), "hygiene: GPS statics restored to the shipped profile");
        string summary = failed == 0 ? "PASS " + passed + ": GPS challenge settings (catalog, defaults, JSON, Sanitized, bounds, overrides)"
            : "FAIL " + failed + " of " + (passed + failed) + " GPS challenge settings checks";
        File.WriteAllText(Report, summary + "\n" + string.Join("\n", lines) + "\n");
        return failed == 0 ? summary : summary + "; first: " + lines.First(line => line.StartsWith("FAIL ")).Substring(5);
    }

    static void Catalog()
    {
        Check(ChallengesField != null && ChallengesField.FieldType == typeof(ChallengeSettings), "catalog: GpsPreferences.challenges is a public ChallengeSettings field");
        Check(GpsSchema.Fields(typeof(GpsPreferences)).Count(f => f.name == "challenges") == 1, "catalog: GpsSchema.Fields(GpsPreferences) lists challenges once");
        var sections = GpsSchema.Sections;
        int challenge = sections.FindIndex(s => s.source == typeof(GpsPreferences) && s.fields.Any(f => f.name == "challenges"));
        int meta = sections.FindIndex(s => s.source == typeof(GpsPreferences) && s.fields.Any(f => f.name == "metaProgression"));
        Check(challenge >= 0 && sections.Count(s => s.source == typeof(GpsPreferences) && s.fields.Any(f => f.name == "challenges")) == 1, "catalog: exactly one section holds challenges");
        if (challenge < 0) return;
        var section = sections[challenge];
        Check(section.tab == "Upgrades" && section.title == "Herausforderungen" && GpsSchema.Tabs.Contains(section.tab) && string.IsNullOrEmpty(section.role),
            "catalog: section Upgrades / Herausforderungen (" + section.tab + " / " + section.title + ")");
        Check(meta >= 0 && challenge == meta + 1, "catalog: section directly follows Metaprogression (" + meta + " -> " + challenge + ")");
        var spec = section.fields.First(f => f.name == "challenges");
        Check(!string.IsNullOrWhiteSpace(spec.label) && spec.label != spec.name && !spec.label.Contains('ß') && !spec.hideInUi && spec.applyTime == GpsApplyTime.Immediately,
            "catalog: visible field with a German label without ß and the default apply time (" + spec.label + ")");
        var problems = new List<string>();
        foreach (var type in new[] { typeof(ChallengeSettings), typeof(ChallengePeriodSettings) })
            foreach (var field in GpsCodec.SerializableFields(type)) {
                var label = field.GetCustomAttribute<InspectorNameAttribute>();
                if (label == null || string.IsNullOrWhiteSpace(label.displayName) || label.displayName.Contains('ß')) problems.Add(type.Name + "." + field.Name + " label");
                bool numeric = field.FieldType == typeof(int) || field.FieldType == typeof(float);
                if (numeric && field.GetCustomAttribute<RangeAttribute>() == null) problems.Add(type.Name + "." + field.Name + " range");
                if (!numeric && field.FieldType != typeof(ChallengePeriodSettings)) problems.Add(type.Name + "." + field.Name + " type " + field.FieldType.Name);
                if (new[] { "version", "amount", "money", "volume", "name", "enabled", "item", "scale" }.Contains(field.Name)) problems.Add(type.Name + "." + field.Name + " reserved GPS child name");
            }
        Check(problems.Count == 0, "catalog: every setting has [InspectorName] (no ß) and numbers have [Range] (" + string.Join(", ", problems) + ")");
        var outside = OutOfRange(new ChallengeSettings());
        Check(outside.Count == 0, "catalog: all code defaults lie inside their [Range] (" + string.Join(", ", outside) + ")");
    }

    static List<string> OutOfRange(ChallengeSettings settings)
    {
        var result = new List<string>();
        void Scan(object target, string prefix)
        {
            if (target == null) { result.Add(prefix + " null"); return; }
            foreach (var field in GpsCodec.SerializableFields(target.GetType())) {
                object value = field.GetValue(target);
                if (value is ChallengePeriodSettings nested) { Scan(nested, prefix + field.Name + "."); continue; }
                var range = field.GetCustomAttribute<RangeAttribute>();
                double number = value is int i ? i : value is float f ? f : double.NaN;
                if (range != null && (double.IsNaN(number) || number < range.min || number > range.max)) result.Add(prefix + field.Name + "=" + value);
            }
        }
        Scan(settings, "");
        return result;
    }

    static void PeriodDefaults(string label, ChallengePeriodSettings p, int easy, int medium, int hard, int xxl, int countEasy, int countMedium, int countHard, float chance, int bonus)
    {
        Check(p != null && p.xpEasy == easy && p.xpMedium == medium && p.xpHard == hard && (xxl < 0 || p.xpXxl == xxl) &&
            p.countEasy == countEasy && p.countMedium == countMedium && p.countHard == countHard && (chance < 0 || p.xxlChancePercent == chance) && p.completionBonusXp == bonus,
            label + ": XP " + easy + "/" + medium + "/" + hard + (xxl >= 0 ? "/" + xxl : "") + ", counts " + countEasy + "/" + countMedium + "/" + countHard +
            (chance >= 0 ? ", XXL " + chance + "%" : "") + ", bonus " + bonus + " (" + Json(p) + ")");
    }
    static void ConceptDefaults(string label, ChallengeSettings c)
    {
        if (c == null) { Check(false, label + ": challenges is null"); return; }
        PeriodDefaults(label + " daily", c.daily, 300, 600, 1200, 3000, 3, 3, 1, 5f, 1000);
        PeriodDefaults(label + " weekly", c.weekly, 1500, 3000, 5000, 10000, 3, 3, 2, -1f, 5000);
        PeriodDefaults(label + " monthly", c.monthly, 5000, 10000, 20000, -1, 2, 2, 2, -1f, 15000);
        Check(c.projectStage1Xp == 5000 && c.projectStage2Xp == 10000 && c.projectStage3Xp == 20000, label + ": project stages 5000/10000/20000");
        Check(c.diligenceDays == 5 && c.diligenceDailyTasks == 3, label + ": diligence 5 days with 3 daily tasks");
        Check(c.tripMinNewBlocks == 10 && c.fullBagCapKg == 150, label + ": trip minimum 10 new blocks, full-bag reference capacity 150 kg");
    }

    static void Defaults()
    {
        ConceptDefaults("defaults new ChallengeSettings()", new ChallengeSettings());
        ConceptDefaults("defaults new GpsPreferences()", Of(new GpsPreferences()));
        var a = new ChallengeSettings(); var b = new ChallengeSettings();
        a.daily.xpEasy = 1; a.weekly.countHard = 9;
        Check(!ReferenceEquals(a.daily, b.daily) && b.daily.xpEasy == 300 && b.weekly.countHard == 2 && ChallengeSettings.Defaults(ChallengePeriod.Weekly).countHard == 2,
            "defaults: every instance owns its period objects (no shared defaults)");
        var defaults = new ChallengeSettings();
        Check(Json(defaults.Sanitized()) == Json(defaults), "defaults: Sanitized() leaves the defaults unchanged");
        Check(Of(GpsSettings.Document.preferences) != null, "defaults: the loaded GPS document provides challenges");
        Check(GpsSettings.ValidateDocument(GpsSettings.Document, out string error), "defaults: current GPS document validates (" + error + ")");
        var copy = Copy(GpsSettings.Document);
        ChallengesField?.SetValue(copy.preferences, new ChallengeSettings());
        Check(ChallengesField != null && GpsSettings.ValidateDocument(copy, out error), "defaults: a document with code defaults validates (" + error + ")");
        var outside = OutOfRange(Of(GpsSettings.Document.preferences));
        Check(outside.Count == 0, "defaults: shipped challenge values lie inside their ranges (" + string.Join(", ", outside) + ")");
    }

    static void JsonDefaults()
    {
        var empty = Of(JsonUtility.FromJson<GpsPreferences>("{}"));
        ConceptDefaults("json {}", empty);
        Check(empty != null && Json(empty) == Json(new ChallengeSettings()), "json: {} yields exactly the code defaults");
        var document = JsonUtility.FromJson<GpsDocument>("{\"preferences\":{}}");
        Check(Of(document.preferences)?.weekly?.xpEasy == 1500, "json: a document without challenges gets the code defaults");
        var partial = Of(JsonUtility.FromJson<GpsPreferences>("{\"challenges\":{\"weekly\":{\"xpEasy\":1}}}"));
        Check(partial?.weekly?.xpEasy == 1, "json: a stored nested value is read");
        Check(partial?.weekly != null && partial.weekly.countHard == 2 && partial.weekly.xpMedium == 3000 && partial.weekly.completionBonusXp == 5000,
            "json: missing nested sub-fields keep the weekly defaults (" + Json(partial?.weekly) + ")");
        Check(partial != null && partial.daily?.xpEasy == 300 && partial.daily.countHard == 1 && partial.monthly?.completionBonusXp == 15000 &&
            partial.projectStage3Xp == 20000 && partial.diligenceDays == 5, "json: missing sibling objects and fields keep their defaults");
        var top = Of(JsonUtility.FromJson<GpsPreferences>("{\"challenges\":{\"diligenceDays\":4}}"));
        Check(top != null && top.diligenceDays == 4 && top.diligenceDailyTasks == 3 && top.daily?.countEasy == 3 && top.monthly?.xpHard == 20000,
            "json: a top-level challenge value alone keeps every period default");
        var changed = new GpsPreferences();
        var settings = Of(changed);
        if (settings != null) { settings.monthly.xpHard = 22222; settings.growthPercentPerLayer = 12.5f; }
        var restored = Of(JsonUtility.FromJson<GpsPreferences>(JsonUtility.ToJson(changed)));
        Check(settings != null && Json(restored) == Json(settings), "json: changed challenge settings survive a JSON round trip");
    }

    static void Sanitized()
    {
        var bad = new ChallengeSettings {
            daily = null, projectStage1Xp = -5, projectStage2Xp = int.MaxValue, projectStage3Xp = 7, diligenceDays = 50, diligenceDailyTasks = 0, xxlMinLayer = 0,
            growthPercentPerLayer = float.NaN, growthPercentPer10Levels = float.PositiveInfinity, maxGrowthPercent = -1f, returnMinDepth = 0, fullBagMinDepth = 5000,
            fullBagPercent = 1000f, fullBagGainPercent = float.NegativeInfinity, fullBagCapKg = 1, tripMinNewBlocks = -1
        };
        bad.weekly.xpEasy = -1; bad.weekly.xpXxl = 2000000; bad.weekly.countHard = 99; bad.weekly.countEasy = -3;
        bad.weekly.xxlChancePercent = float.NaN; bad.weekly.completionBonusXp = 2000000000; bad.monthly.xxlChancePercent = 500f;
        var result = bad.Sanitized();
        Check(result != null && !ReferenceEquals(result, bad) && !ReferenceEquals(result.weekly, bad.weekly), "sanitized: returns a new instance");
        Check(bad.weekly.xpEasy == -1 && bad.daily == null && float.IsNaN(bad.growthPercentPerLayer), "sanitized: the input stays unchanged");
        Check(result.daily != null && Json(result.daily) == Json(ChallengeSettings.Defaults(ChallengePeriod.Daily)), "sanitized: a missing period falls back to its defaults");
        Check(result.weekly.xpEasy == 0 && result.weekly.xpXxl == 1000000 && result.weekly.countHard == 10 && result.weekly.countEasy == 0 &&
            result.weekly.xxlChancePercent == 5f && result.weekly.completionBonusXp == 1000000 && result.monthly.xxlChancePercent == 100f,
            "sanitized: period values clamped, NaN chance falls back (" + Json(result.weekly) + ")");
        Check(result.projectStage1Xp == 0 && result.projectStage2Xp == 1000000 && result.projectStage3Xp == 7 && result.diligenceDays == 7 &&
            result.diligenceDailyTasks == 1 && result.xxlMinLayer == 1, "sanitized: project, diligence and XXL layer clamped");
        Check(result.growthPercentPerLayer == 10f && result.growthPercentPer10Levels == 2f && result.maxGrowthPercent == 0f,
            "sanitized: NaN/Infinity growth falls back to defaults, negatives clamp to 0");
        Check(result.returnMinDepth == 1 && result.fullBagMinDepth == 1200 && result.fullBagPercent == 300f && result.fullBagGainPercent == 50f &&
            result.fullBagCapKg == 10 && result.tripMinNewBlocks == 0, "sanitized: trip values clamped (" + Json(result) + ")");
        var outside = OutOfRange(result);
        Check(outside.Count == 0, "sanitized: every sanitized value lies inside its GPS range (" + string.Join(", ", outside) + ")");
        var huge = new ChallengeSettings { maxGrowthPercent = 1e30f, growthPercentPerLayer = -1e30f };
        huge.daily.xxlChancePercent = float.NegativeInfinity;
        var hugeResult = huge.Sanitized();
        Check(hugeResult.maxGrowthPercent == 500f && hugeResult.growthPercentPerLayer == 0f && hugeResult.daily.xxlChancePercent == 5f, "sanitized: huge and infinite floats handled");
    }

    static void SetValues(GpsProfile temporary, string assetJson)
    {
        var node = GpsSettings.GetValue("preferences", "challenges");
        Check(node != null && node.kind == GpsValueKind.Object && Child(node, "daily.xpEasy") != null, "set value: GetValue exposes challenges as a nested object");
        if (node == null) return;
        string before = Json(Of(GpsSettings.Preferences));
        var cases = new (string path, double value)[] {
            ("daily.xxlChancePercent", 150), ("daily.countEasy", 11), ("weekly.xpEasy", -1), ("monthly.completionBonusXp", 1000001), ("diligenceDays", 8),
            ("diligenceDailyTasks", 0), ("xxlMinLayer", 6), ("tripMinNewBlocks", 201), ("fullBagCapKg", 5), ("growthPercentPerLayer", 501), ("daily.xpHard", double.NaN)
        };
        foreach (var (path, value) in cases) {
            var copy = node.Copy(); var child = Child(copy, path);
            if (child == null) { Check(false, "set value: child " + path + " missing"); continue; }
            child.number = value;
            bool accepted = GpsSettings.SetValue("preferences", copy, out string error);
            Check(!accepted, "set value: out-of-range " + path + "=" + value + " rejected (" + error + ")");
        }
        Check(Json(Of(GpsSettings.Preferences)) == before, "set value: rejected edits leave the live settings unchanged");
        var valid = node.Copy();
        Child(valid, "daily.xpEasy").number = 450; Child(valid, "weekly.countHard").number = 4; Child(valid, "diligenceDays").number = 0;
        bool ok = GpsSettings.SetValue("preferences", valid, out string message);
        var live = Of(GpsSettings.Preferences);
        Check(ok && live != null && live.daily.xpEasy == 450 && live.weekly.countHard == 4 && live.diligenceDays == 0, "set value: in-range nested edits apply (" + message + ")");
        if (!Application.isPlaying) Check(temporary.documentJson != assetJson, "set value: edit-mode write-through lands in the temporary profile");
        if (string.IsNullOrWhiteSpace(MetaProgression.TestDirectory)) {
            var current = MetaProgression.CurrentChallengeSettings;
            Check(current != null && current.daily.xpEasy == 450 && current.weekly.countHard == 4 && current.diligenceDays == 0,
                "set value: MetaProgression.CurrentChallengeSettings reads GPS when not isolated");
            MetaProgression.ChallengeSettingsOverride = new ChallengeSettings { diligenceDays = 7 };
            MetaProgression.ChallengeSettingsOverride.daily.xpEasy = 7;
            current = MetaProgression.CurrentChallengeSettings;
            Check(current != null && current.daily.xpEasy == 450 && current.diligenceDays == 0, "set value: ChallengeSettingsOverride is ignored without an isolated TestDirectory");
            Check(!ReferenceEquals(MetaProgression.CurrentChallengeSettings, live), "set value: consumers get a sanitized copy, not the live GPS object");
        }
    }

    static void OverridesFile(GpsDocument original, string directory)
    {
        var baseline = Copy(original);
        var data = Copy(original);
        var settings = Of(data.preferences);
        Check(settings != null, "overrides file: document has challenges");
        if (settings == null) return;
        settings.weekly.xpHard = 4321; settings.diligenceDays = 4;
        string path = Path.Combine(directory, "gps-settings.json");
        GpsFileStore.Save(path, data, baseline);
        string stored = File.ReadAllText(path);
        var loaded = GpsFileStore.Load(path, Copy(original), out string warning);
        var result = Of(loaded.preferences);
        Check(warning == null && result != null && result.weekly.xpHard == 4321 && result.diligenceDays == 4 && result.daily.xpEasy == Of(original.preferences).daily.xpEasy,
            "overrides file: a challenge override survives the player overrides file (" + warning + ")");
        Check(stored.Contains("\"challenges\""), "overrides file: only the changed preference object is stored as an override");
    }
}
