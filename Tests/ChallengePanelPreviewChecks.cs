using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Edit-mode check (CI and local editor): builds the real ChallengePanel from an isolated profile in a temporary
// additive scene, renders every tab to Temp/ChallengePanel-<Tab>.png (top-level Temp, uploaded by CI) and compares
// the panel with the MetaProgression views: tiles, ids, XP, progress, bonus, countdown, tier colours and text fit.
// Report: Temp/ChallengePanelPreviewChecks.txt; every line starting with FAIL fails the check.
public static class ChallengePanelPreviewChecks
{
    const string Report = "Temp/ChallengePanelPreviewChecks.txt";
    const int Layer = 31, Budget = 3000;
    static readonly DateTime Now = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc); // a Wednesday
    static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    static readonly ChallengePeriod[] Periods = { ChallengePeriod.Daily, ChallengePeriod.Weekly, ChallengePeriod.Monthly, ChallengePeriod.Lifetime };
    static readonly ChallengePeriod[] Rounds = { ChallengePeriod.Daily, ChallengePeriod.Weekly, ChallengePeriod.Monthly };
    static readonly string[] TabLabels = { "Täglich", "Wöchentlich", "Monatlich", "Langzeit" };
    static readonly string[] TierNames = { "Leicht", "Mittel", "Schwer", "XXL", "Projekt" };
    static readonly Color32[] TierColors = { new(112, 196, 96, 255), new(86, 156, 232, 255), new(170, 108, 230, 255), new(255, 201, 92, 255), new(255, 201, 92, 255) };
    static readonly Color32[] MedalColors = { new(0, 0, 0, 0), new(205, 127, 50, 255), new(196, 204, 212, 255), new(255, 201, 92, 255), new(229, 228, 226, 255) };
    static readonly int[] Subjects = Enumerable.Range(-1, 55).ToArray();
    static readonly Item[] OreOrder = { Item.Coal, Item.Copper, Item.Iron, Item.Silver, Item.OrangeGarnet, Item.Gold, Item.Titanium, Item.Platinum, Item.Tungsten, Item.Diamond, Item.Ruby, Item.Ultronium };
    static readonly Item[] GearItems = { Item.Backpack, Item.LoadBelt, Item.ReinforcedBackpack, Item.HeavyDutyBoots, Item.SpringGreaves, Item.LoadFrame, Item.Exoskeleton, Item.CrystalPendant };
    static readonly HashSet<string> BulkMetrics = new() { "resources", "ore", "gems", "depth", "altars", "torches-placed", "ladders-placed", "crafted", "tools",
        "dynamite", "medkits", "torches-crafted", "ladders-crafted", "exotics", "sales", "run-sales", "sale-single" };
    static readonly HashSet<string> UnitMetrics = new() { "blocks", "layer-blocks", "blasted", "ore-variety", "veins", "trees", "grass", "regions", "cells", "layers",
        "discoveries", "round-trips", "returns", "full-returns", "gear", "upgrades", "artifacts", "artifact", "artifact-variety" };

    static readonly List<string> report = new(), errors = new();
    static readonly Dictionary<string, int> serials = new();
    static int checks, failed;
    static Camera camera; static RenderTexture target; static Texture2D pixels; static bool canRender;

    public static string Main()
    {
        if (Application.isPlaying) return "FAIL: run ChallengePanelPreviewChecks in Edit Mode";
        report.Clear(); errors.Clear(); serials.Clear(); checks = failed = 0;
        Directory.CreateDirectory("Temp");
        string directory = Path.GetFullPath("Temp/ChallengePreview-" + DateTime.UtcNow.Ticks);
        var oldDirectory = MetaProgression.TestDirectory; var oldClock = MetaProgression.ChallengeTestUtc;
        var oldZone = MetaProgression.ChallengeTimeZoneOverride; var oldSettings = MetaProgression.ChallengeSettingsOverride;
        var previousScene = SceneManager.GetActiveScene();
        var open = new List<(Scene scene, bool dirty, int roots)>();
        for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) open.Add((s, s.isDirty, s.rootCount)); }
        var oldActive = RenderTexture.active;
        var scene = default(Scene);
        Application.logMessageReceived += Log;
        try
        {
            MetaProgression.TestDirectory = directory;
            MetaProgression.ChallengeTestUtc = Now;
            MetaProgression.ChallengeTimeZoneOverride = TimeZoneInfo.Utc;
            var settings = new ChallengeSettings();
            settings.weekly.xxlChancePercent = 100; // the weekly tab then shows all four tiers
            MetaProgression.ChallengeSettingsOverride = settings;
            Section("Fixture", Fixture);
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var canvas = Stage();
            ChallengePanel panel = null;
            Section("Panel", () => panel = CreatePanel(canvas));
            if (panel)
            {
                foreach (var period in Periods) Section(period + " tab", () => ShowTab(panel, period, true, "ChallengePanel-" + period));
                Section("Lifetime tab without run", () => { MetaProgression.EndRun(); ShowTab(panel, ChallengePeriod.Lifetime, false, "ChallengePanel-Lifetime-NoRun"); });
                Section("Catalog text widths", () => MeasureCatalog(panel));
                Section("Ledger", () => Ledger("End"));
                Section("Tracking", () => Tracking(panel, canvas));
                Section("Back button", () => CloseWithBack(panel));
            }
        }
        catch (Exception e) { Fail("Unexpected " + Describe(e)); }
        finally
        {
            Application.logMessageReceived -= Log;
            RenderTexture.active = oldActive;
            if (camera) camera.targetTexture = null;
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (target) Object.DestroyImmediate(target);
            if (pixels) Object.DestroyImmediate(pixels);
            camera = null; target = null; pixels = null;
            try { MetaProgression.EndRun(); } catch (Exception e) { Fail("EndRun during cleanup threw " + Describe(e)); }
            MetaProgression.ChallengeSettingsOverride = oldSettings;
            MetaProgression.ChallengeTimeZoneOverride = oldZone;
            MetaProgression.ChallengeTestUtc = oldClock;
            MetaProgression.TestDirectory = oldDirectory; // flushes the isolated profile into its own folder first
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (Exception e) { Info("Temp profile kept: " + e.Message); }
        }
        foreach (var error in errors.Distinct().Take(12))
        {
            bool ours = error.StartsWith("Exception") || error.StartsWith("Assert") || new[] { "Destroy", "Challenge", "MetaProgression", "NullReference",
                "MissingReference", "MissingComponent", "IndexOutOfRange", "ArgumentException", "InvalidOperation", "KeyNotFound" }.Any(k => error.Contains(k));
            if (ours) Fail("Console " + error); else report.Add("WARN console " + error);
        }
        foreach (var (s, dirty, roots) in open)
        {
            if (!s.isLoaded) continue;
            Check(dirty || !s.isDirty, "Scene " + s.name + " stays unmodified");
            Check(s.rootCount == roots, "Scene " + s.name + " keeps its " + roots + " root objects (now " + s.rootCount + ")");
        }
        Check(SceneManager.GetActiveScene() == previousScene, "Active scene restored");
        Check(MetaProgression.TestDirectory == oldDirectory && Nullable.Equals(MetaProgression.ChallengeTestUtc, oldClock)
            && MetaProgression.ChallengeTimeZoneOverride == oldZone && MetaProgression.ChallengeSettingsOverride == oldSettings, "Profile isolation and challenge overrides restored");
        report.Insert(0, "ChallengePanelPreviewChecks " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ": " + checks + " checks, " + failed + " failed");
        File.WriteAllLines(Report, report);
        return failed == 0 ? "PASS " + checks + " checks; images Temp/ChallengePanel-*.png" : "FAIL " + failed + " of " + checks + " checks; see " + Report;
    }

    // ---------- fixture: isolated profile with completed, partial and project progress ----------

    static void Fixture()
    {
        MetaProgression.Profile.reachedMiningLayer = 5; // deepest draw: longest names, largest amounts, XXL eligible
        MetaProgression.BeginRun("preview-run");
        var run = MetaProgression.CurrentRun;
        Check(run != null && run.objectiveIds.Count == 3, "Isolated preview run with three run objectives");
        foreach (var period in Rounds) MetaProgression.GetChallenges(period);
        var rounds = MetaProgression.Profile.challengeRounds;
        Check(rounds.Count == 3 && Rounds.All(p => rounds.Count(r => r != null && r.period == (int)p) == 1), "One stored round per period (" + rounds.Count + " rounds)");
        // Lifetime counters and ledger lines first, so the period picks below already see their side effects.
        Placed("torches-placed", 150); Placed("ladders-placed", 2400);
        MetaProgression.RecordSale(55000);
        MetaProgression.RecordResource(Item.Coal, 2100, Source());
        MetaProgression.RecordCraftedAmount(Item.BridgePart, 40);
        for (int i = 0; i < 12; i++) MetaProgression.RecordMinedBlock(Cell(), 2, true);
        var profile = MetaProgression.Profile;
        Check(LifetimeCounter("torches-placed") == 150 && LifetimeCounter("ladders-placed") == 2400 && LifetimeCounter("sales") == 55000
            && LifetimeCounter("blasted") == 12 && LifetimeCounter("crafted") == 40,
            "Lifetime counters: " + string.Join(", ", profile.lifetimeCounters.Where(c => c != null).Select(c => c.id + "=" + c.value)));
        Check(profile.milestoneAwards.Any(a => a != null && a.id == "ms-torches-1") && profile.milestoneAwards.Any(a => a != null && a.id == "ms-ladders-2")
            && profile.completedMilestones.Contains("lifetime-coal"), "New counter stages and the legacy coal stage are awarded");
        foreach (var period in Rounds)
        {
            var round = Round(period);
            if (round == null) continue;
            var open = Enumerable.Range(0, round.tasks.Count).Where(i => round.tasks[i] != null && !IsProject(round.tasks[i]) && !round.tasks[i].done)
                .OrderBy(i => Cost(round.tasks[i], round.tasks[i].target)).ToList();
            int done = open.Where(i => Cost(round.tasks[i], round.tasks[i].target) <= Budget).DefaultIfEmpty(-1).First();
            if (done >= 0) Drive(period, done, round.tasks[done].target, "complete");
            round = Round(period);
            int partial = open.Where(i => i != done && i < round.tasks.Count && round.tasks[i] != null && !round.tasks[i].done && round.tasks[i].target >= 2
                && round.tasks[i].progress < round.tasks[i].target / 2 && Cost(round.tasks[i], round.tasks[i].target / 2) <= Budget).DefaultIfEmpty(-1).First();
            if (partial >= 0) Drive(period, partial, round.tasks[partial].target / 2, "partial");
            if (period != ChallengePeriod.Monthly) continue;
            round = Round(period);
            int index = round.tasks.FindIndex(t => t != null && IsProject(t));
            var stages = index >= 0 ? round.tasks[index].stageTargets : null;
            if (stages != null && stages.Length > 0 && Cost(round.tasks[index], stages[0]) <= Budget) Drive(period, index, stages[0], "project stage 1");
            else Info("Monthly project stage 1 not driven (no project or too expensive)");
        }
        foreach (var period in Rounds)
        {
            var views = MetaProgression.GetChallenges(period) ?? new MetaChallengeProgress[0];
            Check(views.Any(v => v.completed), period + ": fixture shows a completed task");
            Check(views.Any(v => !v.completed && v.current > 0), period + ": fixture shows a partially done task");
        }
        var project = MetaProgression.GetMonthlyProject();
        Check(project != null && project.stagesDone >= 1 && !project.completed, "Monthly project has stage 1 done (" + (project != null ? project.stagesDone + " stages" : "none") + ")");
        Ledger("Fixture");
    }

    static void Ledger(string when)
    {
        var profile = MetaProgression.Profile;
        long paid = profile.challengeRounds.Where(r => r != null).Sum(r => r.tasks.Where(t => t != null).Sum(t => (long)t.paid) + (r.bonusPaid ? r.bonusXp : 0))
            + profile.milestoneAwards.Where(a => a != null).Sum(a => (long)a.xp);
        Check(paid == profile.periodRewardXp, when + ": periodRewardXp " + profile.periodRewardXp + " equals paid tasks, bonuses and new milestone stages " + paid);
        Check(profile.challengePeriods.Count == 0, when + ": no legacy challengePeriods written");
    }

    static MetaChallengeRound Round(ChallengePeriod period) => MetaProgression.Profile.challengeRounds.Find(r => r != null && r.period == (int)period);
    static MetaChallengeTask Task(ChallengePeriod period, int index) { var round = Round(period); return round != null && index < round.tasks.Count ? round.tasks[index] : null; }
    static bool IsProject(MetaChallengeTask task) => task.tier == (int)ChallengeTier.Project || task.stageTargets != null && task.stageTargets.Length > 0;
    static long LifetimeCounter(string id) => MetaProgression.Profile.lifetimeCounters.FirstOrDefault(c => c != null && c.id == id)?.value ?? 0;
    static int Slots(ChallengePeriodSettings s) => s.countEasy + s.countMedium + s.countHard;
    static int Next(string key) { serials.TryGetValue(key, out int value); serials[key] = value + 1; return value; }
    static string Source() => "preview-" + Next("source");
    static Vector3Int Cell() { int n = Next("cell"); return new Vector3Int(n % 400, -2000 - n / 400, 0); }
    static void Placed(string metric, int add)
    {
        serials.TryGetValue("placed:" + metric, out int total); total += add; serials["placed:" + metric] = total;
        MetaProgression.RecordPlacedCount(metric, total);
    }
    static int Cost(MetaChallengeTask task, int goal) => goal <= task.progress ? 0 : BulkMetrics.Contains(task.metric) ? 1 : UnitMetrics.Contains(task.metric) ? goal - task.progress : int.MaxValue;

    // Feeds the task's metric through the public recorders until its progress reaches goal.
    static bool Drive(ChallengePeriod period, int index, int goal, string what)
    {
        var task = Task(period, index);
        if (task == null) return false;
        string metric = task.metric; int subject = task.subject, start = task.progress;
        for (int step = 0, guard = Math.Min(goal - start, Budget) + 8; step < guard; step++)
        {
            task = Task(period, index);
            if (task == null || task.progress >= goal) break;
            int kind = Step(metric, subject, goal - task.progress, goal);
            if (kind == 0) { Info(period + " fixture: no driver for metric " + metric); return false; }
            if (kind == 2) break;
        }
        task = Task(period, index);
        Info(period + " fixture " + what + ": " + task?.id + " (" + metric + (subject >= 0 ? " " + subject : "") + ") " + start + " -> " + task?.progress + " of " + task?.target);
        return task != null && task.progress >= goal;
    }

    // One recorder call; returns 0 = no driver, 1 = one unit, 2 = whole amount at once.
    static int Step(string metric, int subject, int missing, int goal)
    {
        switch (metric)
        {
            case "blocks": case "layer-blocks": case "blasted":
                MetaProgression.RecordMinedBlock(Cell(), metric == "layer-blocks" && subject > 0 ? subject : 1, metric == "blasted"); return 1;
            case "resources": MetaProgression.RecordResource(Item.Coal, missing, Source()); return 2;
            case "ore": MetaProgression.RecordResource(subject >= 0 ? (Item)subject : Item.Coal, missing, Source()); return 2;
            case "gems": MetaProgression.RecordResource(Item.Diamond, missing, Source()); return 2;
            case "ore-variety": MetaProgression.RecordResource(OreOrder[Next(metric) % OreOrder.Length], 1, Source()); return 1;
            case "veins": MetaProgression.RecordVeinCleared(subject >= 0 ? subject : (int)Item.Iron, Cell(), 3); return 1;
            case "trees": case "grass": MetaProgression.RecordSurfaceHarvest(metric, Source()); return 1;
            case "depth": MetaProgression.RecordDepthReached(goal); return 2;
            case "regions": { int n = Next(metric); MetaProgression.RecordExploration(n % 500, 7000 + n / 500, 60); return 1; }
            case "cells": { int n = Next(metric); MetaProgression.RecordRevealedCell(n % 500, 1 + n / 500); return 1; }
            case "layers": MetaProgression.RecordLayerVisited(1 + Next(metric) % 5); return 1;
            case "discoveries": MetaProgression.RecordDiscovery(Source()); return 1;
            case "altars": MetaProgression.RecordAltarReached(); return 2;
            case "round-trips": MetaProgression.RecordReturn(1000, Math.Min(5, Math.Max(2, subject)), false, 500); return 1;
            case "returns": MetaProgression.RecordReturn(1000, 5, false, 500); return 1;
            case "full-returns": MetaProgression.RecordReturn(1000, 5, true, 500); return 1;
            case "torches-placed": case "ladders-placed": Placed(metric, missing); return 2;
            case "crafted": MetaProgression.RecordCraftedAmount(Item.BridgePart, missing); return 2;
            case "tools": MetaProgression.RecordCraftedAmount(Item.Axe, missing); return 2;
            case "gear": MetaProgression.RecordCraftedAmount(GearItems[Next(metric) % GearItems.Length], 1); return 1;
            case "upgrades":
            {
                string kind = subject == 1 ? "energy-upgrades" : subject == 2 ? "carrying-upgrades" : "pickaxe-upgrades";
                int level = 1 + Next(kind); MetaProgression.RecordCraftUpgrade(kind, level, level + 1); return 1;
            }
            case "dynamite": MetaProgression.RecordCraftedAmount(Item.Dynamite, missing); return 2;
            case "medkits": MetaProgression.RecordCraftedAmount(Item.Medkit, missing); return 2;
            case "torches-crafted": MetaProgression.RecordCraftedAmount(Item.Torche, missing); return 2;
            case "ladders-crafted": MetaProgression.RecordCraftedAmount(Item.Ladder, missing); return 2;
            case "exotics": MetaProgression.RecordCraftedAmount(Item.PercussionHammer, missing, true, missing); return 2;
            case "sales": case "run-sales": MetaProgression.RecordSale(missing); return 2;
            case "sale-single": MetaProgression.RecordSale(goal); return 2;
            case "artifacts": MetaProgression.RecordArtifact(Cell(), "Artifact_01"); return 1;
            case "artifact": MetaProgression.RecordArtifact(Cell(), "Artifact_" + Math.Max(1, subject).ToString("D2")); return 1;
            case "artifact-variety": MetaProgression.RecordArtifact(Cell(), "Artifact_" + (1 + Next(metric) % 10).ToString("D2")); return 1;
            default: return 0;
        }
    }

    // ---------- stage: temporary camera and canvas like the game (1920x1080, match .5) ----------

    static Canvas Stage()
    {
        canRender = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        target = new RenderTexture(1920, 1080, 24) { name = "Challenge Preview Target" };
        pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        camera = new GameObject("Challenge Preview Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true; camera.orthographicSize = 5.4f; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(14, 10, 8, 255);
        camera.cullingMask = 1 << Layer; camera.allowHDR = false; camera.allowMSAA = false; camera.targetTexture = target;
        var root = new GameObject("Challenge Preview Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.layer = Layer;
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        scaler.enabled = false; scaler.enabled = true; // recompute the scale for the camera target
        Canvas.ForceUpdateCanvases();
        var rect = ((RectTransform)root.transform).rect;
        if (rect.width < 1000 || rect.height < 500)
        {
            // Fallback in case the editor does not size a camera canvas from its render target.
            Info("Camera canvas was " + rect.size + "; using a 1920x1080 world-space canvas");
            canvas.renderMode = RenderMode.WorldSpace; ((RectTransform)root.transform).sizeDelta = new Vector2(1920, 1080);
            root.transform.position = Vector3.zero; camera.orthographicSize = 540; Canvas.ForceUpdateCanvases();
        }
        Info("Canvas " + canvas.renderMode + " " + ((RectTransform)root.transform).rect.size + ", graphics " + SystemInfo.graphicsDeviceType);
        return canvas;
    }

    static ChallengePanel CreatePanel(Canvas canvas)
    {
        var create = typeof(ChallengePanel).GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Transform), typeof(Action) }, null);
        if (!Check(create != null, "ChallengePanel.Create(Transform, Action) exists")) return null;
        Check(create.IsPublic, "ChallengePanel.Create is public (spec §9)");
        var panel = create.Invoke(null, new object[] { canvas.transform, null }) as ChallengePanel;
        if (!Check(panel, "Panel created in Edit Mode")) return null;
        var group = panel.GetComponent<CanvasGroup>();
        if (group) group.alpha = 1; // HomeUi.Panel keeps edit-mode panels invisible
        Canvas.ForceUpdateCanvases();
        var layout = panel.transform.Find("Challenges Layout") as RectTransform;
        if (Check(layout, "Challenges Layout present")) { HomeUi.Fit(layout, new Vector2(1600, 980)); Info("Layout scale " + layout.localScale.x.ToString("0.###")); }
        return panel;
    }

    static bool Click(Component panel, string name)
    {
        var button = panel ? panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name) : null;
        if (!Check(button, "Button '" + name + "' present")) return false;
        button.onClick.Invoke(); return true;
    }

    static void Refresh(Component panel)
    {
        Canvas.ForceUpdateCanvases();
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
    }

    static void ShowTab(ChallengePanel panel, ChallengePeriod period, bool runActive, string image)
    {
        if (!Click(panel, "Challenge Tab " + period)) return;
        Refresh(panel);
        CheckTab(panel, period, runActive);
        Render(panel, image);
        RenderEnd(panel, period, image);
    }

    static void Render(Component panel, string name)
    {
        if (!canRender) { report.Add("SKIP render " + name + " (no graphics device)"); return; }
        foreach (var child in panel.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var active = RenderTexture.active; RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
        RenderTexture.active = active;
        File.WriteAllBytes("Temp/" + name + ".png", pixels.EncodeToPNG());
        var colours = new HashSet<int>(); var data = pixels.GetPixels32();
        for (int i = 0; i < data.Length; i += 5) colours.Add(data[i].r << 16 | data[i].g << 8 | data[i].b);
        Check(colours.Count > 50, name + ".png shows the panel (" + colours.Count + " distinct colours)");
        Info("Image Temp/" + name + ".png");
    }

    static void RenderEnd(ChallengePanel panel, ChallengePeriod period, string name)
    {
        var scroll = panel.GetComponentInChildren<ScrollRect>(true);
        if (!scroll || !scroll.content || !scroll.viewport) return;
        if (scroll.content.rect.height <= scroll.viewport.rect.height + 1) { Info(period + ": all tiles visible without scrolling"); return; }
        scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
        var tiles = scroll.content.Cast<Transform>().Where(t => t.gameObject.activeSelf).OfType<RectTransform>().ToList();
        if (tiles.Count > 0)
        {
            var last = tiles.OrderBy(t => RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, t).min.y).First();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, last);
            Check(bounds.min.y >= scroll.viewport.rect.yMin - 1 && bounds.max.y <= scroll.viewport.rect.yMax + 1, period + ": last tile " + last.name + " fully visible after scrolling to the end");
        }
        Render(panel, name + "-End");
        scroll.verticalNormalizedPosition = 1; Canvas.ForceUpdateCanvases();
    }

    // ---------- per-tab assertions ----------

    static void CheckTab(ChallengePanel panel, ChallengePeriod period, bool runActive)
    {
        var settings = MetaProgression.CurrentChallengeSettings;
        var scroll = panel.GetComponentInChildren<ScrollRect>(true);
        if (!Check(scroll && scroll.content && scroll.viewport, period + ": scroll view present")) return;
        var content = scroll.content;
        var views = MetaProgression.GetChallenges(period) ?? new MetaChallengeProgress[0];
        var project = period == ChallengePeriod.Monthly ? MetaProgression.GetMonthlyProject() : null;
        var tiles = content.Cast<Transform>().Where(t => t.gameObject.activeSelf).OfType<RectTransform>().ToList();
        var names = views.Select(v => "Challenge " + v.id).ToList();
        if (project != null) names.Insert(0, "Challenge " + project.id);
        if (project != null) Info(period + " " + project.id + " project '" + project.name + "' " + project.current + "/" + project.target + " " + project.unit + " stages " + project.stagesDone);
        foreach (var v in views) Info(period + " " + v.id + " kind " + v.kind + " tier " + v.tier + (v.kind == 2 ? " medal " + v.medal : "") + " '" + v.name + "' "
            + v.current + "/" + v.target + " " + v.unit + " " + Xp(v.xp) + (v.completed ? " done" : ""));
        Check(tiles.Count == names.Count, period + ": " + tiles.Count + " active tiles for " + names.Count + " views");
        Check(names.Distinct().Count() == names.Count, period + ": view ids are unique");
        Check(new HashSet<string>(tiles.Select(t => t.name)).SetEquals(names), period + ": tiles are named 'Challenge '+id of the views");
        if (!tiles.Select(t => t.name).SequenceEqual(names)) Info(period + ": tile order differs from the view order");
        if (period != ChallengePeriod.Lifetime)
        {
            int slots = Slots(settings.Period(period));
            Check(views.Length == slots, period + ": " + views.Length + " tasks, settings ask for " + slots);
            Check(views.All(v => v.kind == 0), period + ": every view is a period task (kind 0)");
            var round = Round(period);
            if (Check(round != null, period + ": round stored in the profile"))
            {
                var expected = ViewIds(round);
                Check(views.Select(v => v.id).SequenceEqual(expected), period + ": ids follow '<period>-<template>[-subject]' (" + string.Join(", ", expected) + ")");
            }
            if (period == ChallengePeriod.Monthly) CheckProject(content, project, round, settings);
        }
        else CheckLifetime(views, runActive);
        foreach (var view in views) CheckTile(Active(content, "Challenge " + view.id), view, period, settings);
        CheckBonus(panel, period, views, project, settings);
        CheckCountdown(panel, period);
        CheckTabs(panel, period);
        CheckLayout(panel, scroll, tiles, period);
        CheckTexts(panel, period.ToString());
    }

    static List<string> ViewIds(MetaChallengeRound round)
    {
        var ids = new List<string>();
        for (int i = 0; i < round.tasks.Count; i++)
        {
            var task = round.tasks[i];
            if (task == null || IsProject(task)) continue;
            string id = (ChallengePeriod)round.period + "-" + task.id + (task.subject >= 0 ? "-" + task.subject : "");
            ids.Add(ids.Contains(id) ? id + "-" + i : id);
        }
        return ids;
    }

    static void CheckTile(RectTransform tile, MetaChallengeProgress view, ChallengePeriod period, ChallengeSettings settings)
    {
        string label = period + " " + view.id;
        if (!Check(tile, label + ": tile present")) return;
        var iconNode = tile.Find("Icon"); var icon = iconNode ? iconNode.GetComponent<Image>() : null;
        Check(icon && icon.sprite && IconMatches(view.icon, icon.sprite), label + ": icon '" + view.icon + "' shows " + (icon && icon.sprite ? icon.sprite.name : "nothing"));
        Check(!string.IsNullOrEmpty(view.name) && Text(tile, "Name") == view.name, label + ": name '" + Text(tile, "Name") + "' is '" + view.name + "'");
        string progress = Text(tile, "Progress"), counter = N0(view.current) + " / " + N0(view.target);
        Check(progress != null && progress.StartsWith(counter) && progress.Any(char.IsLetter) && (string.IsNullOrEmpty(view.unit) || progress.Contains(view.unit)),
            label + ": progress '" + progress + "' shows " + counter + " " + view.unit);
        Check(Text(tile, "XP Plaque/Reward") == Xp(view.xp), label + ": reward '" + Text(tile, "XP Plaque/Reward") + "' is " + Xp(view.xp));
        var fill = tile.Find("Progress Track/Interior/Progress Fill") as RectTransform;
        float ratio = view.target > 0 ? Mathf.Clamp01((float)view.current / view.target) : 0;
        Check(fill && Mathf.Abs(fill.anchorMax.x - ratio) < .002f, label + ": fill " + (fill ? fill.anchorMax.x.ToString("0.000") : "missing") + " for " + ratio.ToString("0.000"));
        Check(Seal(tile) == view.completed, label + ": completed seal " + (view.completed ? "shown" : "hidden"));
        var rim = Rim(tile);
        var texts = tile.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text ?? "").ToList();
        if (view.kind == 0)
        {
            bool tier = view.tier >= 0 && view.tier <= 3 && period != ChallengePeriod.Lifetime;
            Check(tier && view.xp == settings.Period(period).Xp(view.tier), label + ": XP " + view.xp + " follows the tier " + view.tier + " setting");
            Check(tier && rim != null && Same(rim.color, TierColors[view.tier]), label + ": tier rim " + Rgb(rim) + " is " + (tier ? TierNames[view.tier] : "tier " + view.tier));
            Check(tier && texts.Contains(TierNames[view.tier]), label + ": tier label '" + (tier ? TierNames[view.tier] : "?") + "'");
        }
        else if (view.kind == 2)
        {
            Check(texts.Any(t => t.StartsWith("Stufe ")), label + ": stage label 'Stufe x/y'");
            if (view.medal >= 1 && view.medal <= 4 && rim != null) Check(Same(rim.color, MedalColors[view.medal]), label + ": medal rim " + Rgb(rim) + " for medal " + view.medal);
            else if (view.medal >= 1) Info(label + ": medal " + view.medal + " shown without 'Tier Rim'");
        }
        else if (view.kind == 3) Check(texts.Contains("Run-Ziel"), label + ": run objective label 'Run-Ziel'");
    }

    static void CheckProject(Transform content, MetaChallengeProgress project, MetaChallengeRound round, ChallengeSettings settings)
    {
        if (!Check(project != null, "Monthly: GetMonthlyProject returns the project")) return;
        var task = round?.tasks.FirstOrDefault(t => t != null && IsProject(t));
        Check(task != null && project.id == "Monthly-" + task.id && project.kind == 1 && project.tier == (int)ChallengeTier.Project, "Monthly: project view '" + project.id + "' (kind 1, tier 4)");
        int stages = project.stageTargets?.Length ?? 0;
        if (!Check(stages == 3 && project.stageXp != null && project.stageXp.Length == 3, "Monthly: project has three stages")) return;
        Check(project.stagesDone == project.stageTargets.Count(t => project.current >= t) && project.target == project.stageTargets[stages - 1]
            && project.xp == project.stageXp[Math.Min(project.stagesDone, stages - 1)] && (task == null || project.current == task.progress),
            "Monthly: project view matches the stored task (" + project.current + "/" + project.target + ", " + project.stagesDone + " stages)");
        var tile = Active(content, "Challenge " + project.id);
        if (!Check(tile, "Monthly: project tile present")) return;
        Check(Mathf.Abs(tile.rect.width - 1312) < .5f, "Monthly: project tile is 1312 wide (" + tile.rect.width + ")");
        Check(Text(tile, "Name") == project.name, "Monthly: project name '" + Text(tile, "Name") + "'");
        var icon = tile.Find("Icon") ? tile.Find("Icon").GetComponent<Image>() : null;
        Check(icon && icon.sprite && IconMatches(project.icon, icon.sprite), "Monthly: project icon '" + project.icon + "'");
        var rim = Rim(tile);
        Check(rim != null && Same(rim.color, TierColors[4]), "Monthly: project rim " + Rgb(rim) + " is gold");
        Check(tile.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text != null && t.text.Contains("rojekt")), "Monthly: project tile is labelled as project");
        int previous = 0;
        for (int i = 0; i < stages; i++)
        {
            int stageTarget = project.stageTargets[i];
            string label = "Monthly project stage " + (i + 1);
            var stage = tile.Find("Stage " + (i + 1));
            if (Check(stage, label + ": present"))
            {
                Check(project.stageXp[i] == settings.ProjectStageXp(i), label + ": XP " + project.stageXp[i] + " follows the settings (" + settings.ProjectStageXp(i) + ")");
                Check(Text(stage, "XP Plaque/Reward") == Xp(project.stageXp[i]), label + ": reward '" + Text(stage, "XP Plaque/Reward") + "'");
                float ratio = stageTarget > previous ? Mathf.Clamp01((float)(project.current - previous) / (stageTarget - previous)) : 0;
                var fill = stage.Find("Progress Track/Interior/Progress Fill") as RectTransform;
                Check(fill && Mathf.Abs(fill.anchorMax.x - ratio) < .002f, label + ": segment fill " + (fill ? fill.anchorMax.x.ToString("0.000") : "missing") + " for " + ratio.ToString("0.000"));
                Check(Seal(stage) == (project.current >= stageTarget), label + ": seal " + (project.current >= stageTarget ? "shown" : "hidden"));
            }
            previous = stageTarget;
        }
    }

    static void CheckLifetime(MetaChallengeProgress[] views, bool runActive)
    {
        var profile = MetaProgression.Profile; var lines = ChallengeCatalog.Milestones;
        Check(lines.Count > 0 && views.Count(v => v.kind == 2) == lines.Count, "Lifetime: one tile per milestone line (" + lines.Count + " lines, " + views.Count(v => v.kind == 2) + " shown)");
        Check(views.Where(v => v.kind == 2).Select(v => v.id).SequenceEqual(lines.Select(l => l.id)) && lines.All(l => l.id.StartsWith("line-")), "Lifetime: line ids in catalog order");
        var objectives = views.Where(v => v.kind == 3).Select(v => v.id).ToList();
        var run = MetaProgression.CurrentRun;
        Check(runActive ? run != null && objectives.Count == 3 && objectives.All(run.objectiveIds.Contains) : objectives.Count == 0,
            "Lifetime: " + objectives.Count + " run objectives " + (runActive ? "with" : "without") + " an active run");
        foreach (var line in lines)
        {
            var view = views.FirstOrDefault(v => v.id == line.id);
            if (view == null) continue;
            int reached = line.stages.Count(s => Reached(profile, s));
            var next = line.stages.FirstOrDefault(s => !Reached(profile, s)) ?? line.stages.LastOrDefault();
            Check(view.stage == reached && view.medal == reached && view.stages == line.stages.Length && view.completed == (reached == line.stages.Length)
                && next != null && view.target == next.target && view.xp == next.xp && view.name == line.name && view.unit == line.unit && view.icon == line.icon,
                "Lifetime " + line.id + ": stage " + view.stage + "/" + view.stages + " (expected " + reached + "/" + line.stages.Length + "), target " + view.target + ", " + Xp(view.xp));
        }
        Check(views.Any(v => v.kind == 2 && v.medal == 1) && views.Any(v => v.kind == 2 && v.medal >= 2), "Lifetime: fixture shows Bronze and Silber medals");
    }

    static bool Reached(MetaProfile profile, ChallengeMilestoneStage stage)
        => stage.legacy ? profile.completedMilestones.Contains(stage.id) : profile.milestoneAwards.Any(a => a != null && a.id == stage.id);

    static void CheckBonus(ChallengePanel panel, ChallengePeriod period, MetaChallengeProgress[] views, MetaChallengeProgress project, ChallengeSettings settings)
    {
        var bonus = MetaProgression.ChallengeBonus(period) ?? new MetaChallengeBonus();
        var bar = Named(panel.transform, "Bonus Bar");
        string shown = Text(bar, "Bonus"), expected = BonusText(period, bonus);
        Check(bar && bar.gameObject.activeInHierarchy && shown == expected, period + ": bonus bar '" + shown + "' is '" + expected + "'");
        if (period == ChallengePeriod.Lifetime)
            Check(bonus.required == ChallengeCatalog.Milestones.Sum(l => l.stages.Length) && bonus.done == views.Where(v => v.kind == 2).Sum(v => v.stage) && bonus.xp == 0,
                "Lifetime: bonus counts reached stages " + bonus.done + " / " + bonus.required);
        else
            Check(bonus.required == views.Length + (project != null ? 1 : 0) && bonus.done == views.Count(v => v.completed) + (project != null && project.completed ? 1 : 0)
                && bonus.xp == settings.Period(period).completionBonusXp && bonus.awarded == (bonus.required > 0 && bonus.done == bonus.required),
                period + ": bonus " + bonus.done + " / " + bonus.required + " " + Xp(bonus.xp) + (bonus.awarded ? " awarded" : ""));
        int pips = bar ? bar.Cast<Transform>().Count(t => t.gameObject.activeSelf && t.name.StartsWith("Bonus Pip")) : 0;
        if (period == ChallengePeriod.Lifetime || bonus.required > 16) Check(pips == 0, period + ": no bonus pips (" + pips + ")");
        else if (pips > 0) Check(pips == bonus.required, period + ": " + pips + " bonus pips for " + bonus.required + " tasks");
        else Info(period + ": bonus bar has no children named 'Bonus Pip*'");
    }

    static string BonusText(ChallengePeriod period, MetaChallengeBonus b) =>
        period == ChallengePeriod.Lifetime ? b.done + " / " + b.required + " Stufen erreicht"
        : b.awarded ? "Bonus erhalten · " + Xp(b.xp)
        : b.xp > 0 ? b.done + " / " + b.required + " erledigt → " + Xp(b.xp)
        : b.done + " / " + b.required + " erledigt";

    static void CheckCountdown(ChallengePanel panel, ChallengePeriod period)
    {
        var countdown = panel.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Countdown");
        long end = MetaProgression.ChallengePeriodEnd(period);
        var remaining = MetaProgression.ChallengeRemaining(period);
        if (period == ChallengePeriod.Lifetime)
        {
            Check(!countdown || !countdown.gameObject.activeInHierarchy, "Lifetime: countdown hidden");
            Check(end == 0 && remaining == TimeSpan.Zero, "Lifetime: no period end");
            return;
        }
        var expectedEnd = period == ChallengePeriod.Daily ? new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc)
            : period == ChallengePeriod.Weekly ? new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc) : new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var round = Round(period);
        Check(end == expectedEnd.Ticks && round != null && round.endUtc == end, period + ": round ends " + expectedEnd.ToString("u") + " in UTC (ChallengePeriodEnd " + Ticks(end) + ")");
        Check(remaining.Ticks == end - Now.Ticks, period + ": remaining " + remaining + " matches the period end");
        string expected = period == ChallengePeriod.Daily
            ? $"Neu in {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"Neu in {remaining.Days} T {remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        Check(countdown && countdown.gameObject.activeInHierarchy && countdown.text == expected, period + ": countdown '" + (countdown ? countdown.text : null) + "' is '" + expected + "'");
        Check(period == ChallengePeriod.Daily || countdown && countdown.text.Contains(" T "), period + ": countdown shows days");
        var hourglass = countdown ? countdown.transform.Find("Hourglass") : null;
        Check(hourglass && hourglass.gameObject.activeInHierarchy, period + ": hourglass next to the countdown");
    }

    static void CheckTabs(ChallengePanel panel, ChallengePeriod period)
    {
        var tabs = panel.GetComponentsInChildren<Button>(true).Where(b => b.name.StartsWith("Challenge Tab")).ToList();
        Check(tabs.Count == 4, period + ": four tab buttons (" + string.Join(", ", tabs.Select(t => t.name)) + ")");
        float lastX = float.NegativeInfinity;
        for (int i = 0; i < Periods.Length; i++)
        {
            var tab = tabs.FirstOrDefault(b => b.name == "Challenge Tab " + Periods[i]);
            if (!Check(tab, period + ": tab 'Challenge Tab " + Periods[i] + "' present")) continue;
            var label = tab.GetComponentInChildren<TMP_Text>(true);
            Check(label && label.text == TabLabels[i], period + ": tab " + Periods[i] + " labelled '" + TabLabels[i] + "'");
            var feedback = tab.GetComponent<HomeButtonFeedback>();
            Check(feedback && feedback.primary == (Periods[i] == period), period + ": tab " + Periods[i] + (Periods[i] == period ? " selected" : " not selected"));
            Check(tab.transform.localPosition.x > lastX, period + ": tab " + Periods[i] + " ordered left to right");
            lastX = tab.transform.localPosition.x;
        }
    }

    static void CheckLayout(ChallengePanel panel, ScrollRect scroll, List<RectTransform> tiles, ChallengePeriod period)
    {
        var area = scroll.viewport.rect;
        var bounds = tiles.Select(t => RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, t)).ToList();
        Check(bounds.All(b => b.min.x >= area.xMin - 1 && b.max.x <= area.xMax + 1), period + ": tiles fit the viewport width");
        string overlap = null;
        for (int i = 0; i < bounds.Count && overlap == null; i++)
            for (int j = i + 1; j < bounds.Count; j++)
                if (Math.Min(bounds[i].max.x, bounds[j].max.x) - Math.Max(bounds[i].min.x, bounds[j].min.x) > 1
                    && Math.Min(bounds[i].max.y, bounds[j].max.y) - Math.Max(bounds[i].min.y, bounds[j].min.y) > 1) { overlap = tiles[i].name + " / " + tiles[j].name; break; }
        Check(overlap == null, period + ": tiles do not overlap" + (overlap == null ? "" : " (" + overlap + ")"));
        if (bounds.Count > 0) Check(scroll.content.rect.height + 1 >= bounds.Max(b => b.max.y) - bounds.Min(b => b.min.y), period + ": content height covers every tile");
        var board = panel.transform.Find("Challenges Layout/Board") as RectTransform;
        var root = panel.GetComponentInParent<Canvas>().transform as RectTransform;
        if (Check(board && root, period + ": board present"))
        {
            var corners = new Vector3[4]; board.GetWorldCorners(corners);
            Check(corners.All(c => root.rect.Contains(root.InverseTransformPoint(c))), period + ": board fits the 1920x1080 canvas");
        }
    }

    static void CheckTexts(Component root, string label)
    {
        int count = 0;
        foreach (var text in root.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate(); count++;
            bool size = !text.enableAutoSizing || text.fontSize >= text.fontSizeMin - .01f;
            Check(!text.isTextOverflowing && !text.isTextTruncated && size, label + ": text fits: " + PathOf(text.transform, root.transform) + " '" + text.text + "' size "
                + text.fontSize.ToString("0.#") + " (min " + text.fontSizeMin.ToString("0.#") + "), width " + text.renderedWidth.ToString("0") + " of " + text.rectTransform.rect.width.ToString("0"));
        }
        Check(count > 0, label + ": " + count + " visible texts checked");
    }

    // ---------- catalog strings through the real tile texts ----------

    static void MeasureCatalog(ChallengePanel panel)
    {
        if (!Click(panel, "Challenge Tab Monthly")) return;
        Refresh(panel);
        var content = panel.GetComponentInChildren<ScrollRect>(true).content;
        var monthly = MetaProgression.GetChallenges(ChallengePeriod.Monthly) ?? new MetaChallengeProgress[0];
        var project = MetaProgression.GetMonthlyProject();
        var tile = monthly.Length > 0 ? Active(content, "Challenge " + monthly[0].id) : null;
        var projectTile = project != null ? Active(content, "Challenge " + project.id) : null;
        var drawn = SimulateDraws();
        Check(ChallengeCatalog.Templates.Count > 0 && ChallengeCatalog.Milestones.Count > 0, "Catalog lists " + ChallengeCatalog.Templates.Count + " templates and " + ChallengeCatalog.Milestones.Count + " milestone lines");
        Check(drawn.Count > 0, "Draw simulation produced " + drawn.Count + " tasks");
        var names = new List<string>(); var counters = new List<string>(); var rewards = new List<string>();
        var projectNames = new List<string>(); var projectCounters = new List<string>(); var maxTarget = new Dictionary<string, int>();
        foreach (var task in drawn)
        {
            string unit = string.IsNullOrEmpty(task.unit) ? Safe(() => ChallengeCatalog.Unit(task.id, task.metric, task.subject)) : task.unit;
            string name = string.IsNullOrEmpty(task.name) ? Safe(() => ChallengeCatalog.Name(task.id, task.subject)) : task.name;
            if (IsProject(task))
            {
                projectNames.Add(name); projectCounters.Add(Counter(task.target, task.target, unit));
                foreach (int xp in task.stageXp ?? new int[0]) rewards.Add(Xp(xp));
                continue;
            }
            names.Add(name); counters.Add(Counter(task.target, task.target, unit)); rewards.Add(Xp(task.xp));
            maxTarget[task.id ?? ""] = Math.Max(maxTarget.TryGetValue(task.id ?? "", out int max) ? max : 0, task.target);
        }
        foreach (var template in ChallengeCatalog.Templates)
        {
            if (template == null || template.retired) continue;
            (template.project ? projectNames : names).Add(template.name);
            foreach (int subject in Subjects)
            {
                (template.project ? projectNames : names).Add(Safe(() => ChallengeCatalog.Name(template.id, subject)));
                if (maxTarget.TryGetValue(template.id, out int top)) counters.Add(Counter(top, top, Safe(() => ChallengeCatalog.Unit(template.id, template.metric, subject))));
            }
        }
        foreach (var line in ChallengeCatalog.Milestones)
        {
            if (line == null) continue;
            names.Add(line.name);
            foreach (var stage in line.stages) { counters.Add(Counter(stage.target, stage.target, line.unit)); rewards.Add(Xp(stage.xp)); }
        }
        foreach (var definition in MetaProgressionCatalog.Challenges.Where(d => !d.permanent))
        {
            names.Add(definition.name); rewards.Add(Xp(definition.xp));
            counters.Add(Counter(definition.target, definition.target, Safe(() => ChallengeCatalog.Unit(null, definition.metric, -1))));
        }
        var settings = MetaProgression.CurrentChallengeSettings;
        foreach (var period in Rounds) for (int tier = 0; tier < 4; tier++) if (settings.Period(period).Xp(tier) > 0) rewards.Add(Xp(settings.Period(period).Xp(tier)));
        for (int i = 0; i < 3; i++) rewards.Add(Xp(settings.ProjectStageXp(i)));
        Fit(tile, "Name", names, "Tile name");
        Fit(tile, "Progress", counters, "Tile progress");
        Fit(tile, "XP Plaque/Reward", rewards, "XP plaque");
        var tierText = tile ? tile.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => TierNames.Contains(t.text)) : null;
        if (tierText) FitText(tierText, TierNames.Concat(new[] { "Bronze", "Silber", "Gold", "Platin", "Run-Ziel" }).Concat(Enumerable.Range(1, 4).Select(m => Safe(() => ChallengeCatalog.MedalName(m)))), "Tier label");
        else Info("No tier label found on " + (tile ? tile.name : "the first monthly tile"));
        Fit(projectTile, "Name", projectNames, "Project name");
        Fit(projectTile, "Progress", projectCounters, "Project progress");
        var bonuses = new List<string>();
        foreach (var period in Rounds)
        {
            int required = Slots(settings.Period(period)) + (period == ChallengePeriod.Monthly ? 1 : 0), xp = settings.Period(period).completionBonusXp;
            for (int done = Math.Max(0, required - 1); done <= required; done++) bonuses.Add(BonusText(period, new MetaChallengeBonus { done = done, required = required, xp = xp }));
            bonuses.Add(BonusText(period, new MetaChallengeBonus { done = required, required = required, xp = xp, awarded = true }));
        }
        int total = ChallengeCatalog.Milestones.Sum(l => l?.stages.Length ?? 0);
        bonuses.Add(BonusText(ChallengePeriod.Lifetime, new MetaChallengeBonus { done = total, required = total }));
        Fit(Named(panel.transform, "Bonus Bar"), "Bonus", bonuses, "Bonus bar");
        var countdown = panel.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Countdown");
        if (Check(countdown, "Countdown text found for width checks")) FitText(countdown, new[] { "Neu in 30 T 23:59:59", "Neu in 6 T 23:59:59", "Neu in 23:59:59" }, "Countdown");
    }

    // Draws rounds for every layer at low and high level (XXL forced) to collect the strings and amounts players can see.
    static List<MetaChallengeTask> SimulateDraws()
    {
        var result = new List<MetaChallengeTask>();
        var settings = new ChallengeSettings();
        settings.daily.xxlChancePercent = settings.weekly.xxlChancePercent = 100;
        settings = settings.Sanitized();
        var limits = new[] { 2, 5, 3, 2, 2, 20, 10, 4, 1000, 2, 5, 1 };
        int rounds = 0;
        foreach (int layer in new[] { 1, 2, 3, 4, 5 })
            foreach (int level in new[] { 1, 200 })
                foreach (var period in Rounds)
                {
                    MetaChallengeRound previous = null;
                    int count = period == ChallengePeriod.Daily ? 21 : period == ChallengePeriod.Weekly ? 12 : 8;
                    for (int i = 0; i < count; i++)
                    {
                        var moment = period == ChallengePeriod.Daily ? Now.AddDays(i) : period == ChallengePeriod.Weekly ? Now.AddDays(7 * i) : Now.AddMonths(i);
                        ChallengeEngine.Bounds(period, moment, TimeZoneInfo.Utc, out long start, out long end, out int dayIndex);
                        var context = new ChallengeDrawContext { reachedLayer = layer, level = level, craftLimits = limits, pickaxeHardness = 10 };
                        var round = ChallengeEngine.Draw(period, start, end, dayIndex, context, settings, previous);
                        rounds++;
                        if (round?.tasks != null) result.AddRange(round.tasks.Where(t => t != null));
                        previous = round;
                    }
                }
        Info("Draw simulation: " + result.Count + " tasks from " + rounds + " rounds (layers 1-5, level 1/200, XXL forced)");
        return result;
    }

    static void Fit(Transform root, string path, IEnumerable<string> values, string label)
    {
        var node = root ? root.Find(path) : null;
        var text = node ? node.GetComponent<TMP_Text>() : null;
        if (Check(text, label + ": text object '" + path + "' found for width checks")) FitText(text, values, label);
    }

    static void FitText(TMP_Text text, IEnumerable<string> values, string label)
    {
        string original = text.text; int count = 0, shrunk = 0;
        var widest = new List<(float width, string value, float size)>();
        foreach (var value in values.Where(v => !string.IsNullOrEmpty(v)).Distinct())
        {
            text.text = value; text.ForceMeshUpdate(); count++;
            if (text.enableAutoSizing && text.fontSize < text.fontSizeMax - .01f) shrunk++;
            Check(!text.isTextOverflowing && !text.isTextTruncated, label + " fits '" + value + "' (size " + text.fontSize.ToString("0.#") + ", "
                + text.renderedWidth.ToString("0") + " of " + text.rectTransform.rect.width.ToString("0") + " px)");
            widest.Add((text.renderedWidth, value, text.fontSize));
        }
        text.text = original; text.ForceMeshUpdate();
        Info(label + ": " + count + " strings, " + shrunk + " auto-sized below " + text.fontSizeMax.ToString("0.#") + ", box " + text.rectTransform.rect.width.ToString("0") + " px");
        foreach (var w in widest.OrderByDescending(w => w.width).Take(3)) Info("  widest '" + w.value + "' " + w.width.ToString("0") + " px at size " + w.size.ToString("0.#"));
    }

    // ---------- star marks and HUD tracker (MetaChallengeTracking, ChallengeTrackerHud) ----------
    static void Tracking(ChallengePanel panel, Canvas canvas)
    {
        var rebuild = typeof(ChallengePanel).GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.NonPublic);
        if (!Check(rebuild != null, "ChallengePanel.Rebuild exists") || !Click(panel, "Challenge Tab Daily")) return;
        Refresh(panel);
        var scroll = panel.GetComponentInChildren<ScrollRect>(true);
        var daily = MetaProgression.GetChallenges(ChallengePeriod.Daily);
        foreach (var view in daily)
        {
            var tile = Active(scroll.content, "Challenge " + view.id);
            var star = tile ? tile.Find("Track Star") : null;
            Check(!string.IsNullOrEmpty(view.track) && view.track.StartsWith(view.id + "@"), view.id + ": track key '" + view.track + "' carries the round start");
            Check(view.completed ? star == null : star != null && !star.GetComponentInChildren<ChallengeStar>().Filled, view.id + ": star " + (view.completed ? "hidden (done)" : "shown, empty"));
        }
        var open = daily.Where(v => !v.completed).Concat(MetaProgression.GetChallenges(ChallengePeriod.Weekly).Where(v => !v.completed)).ToList();
        if (!Check(open.Count >= 6, "At least six unfinished daily/weekly tasks to mark (" + open.Count + ")")) return;
        var first = open[0];
        var button = Active(scroll.content, "Challenge " + first.id)?.Find("Track Star")?.GetComponent<Button>();
        if (!Check(button, first.id + ": star button")) return;
        button.onClick.Invoke();
        Check(MetaProgression.IsTracked(first.track), "Star click marks " + first.id);
        rebuild.Invoke(panel, null); Refresh(panel);
        var filled = Active(scroll.content, "Challenge " + first.id)?.Find("Track Star")?.GetComponentInChildren<ChallengeStar>();
        Check(filled && filled.Filled, "Marked tile shows a filled star after the rebuild");
        Render(panel, "ChallengePanel-Daily-Tracked");
        for (int i = 1; i < 6; i++) MetaProgression.SetTracked(open[i].track, true);
        MetaProgression.SetTracked(open[1].track, true); // second mark of the same key is ignored
        var tracked = MetaProgression.TrackedChallenges();
        Check(tracked.Select(v => v.track).SequenceEqual(open.Take(6).Select(v => v.track)), "Six marks kept once each, in marking order");

        // HUD: a stretched 1920×1080 parent like CompactHud; at most four rows in marking order.
        var hud = new GameObject("Tracker HUD", typeof(RectTransform)).GetComponent<RectTransform>();
        hud.SetParent(canvas.transform, false); HomeUi.Stretch(hud);
        var tracker = ChallengeTrackerHud.Attach(hud);
        if (!Check(tracker, "ChallengeTrackerHud attached")) return;
        Check(ChallengeTrackerHud.Attach(hud) == tracker, "Attach twice reuses the tracker");
        tracker.Refresh(); Refresh(tracker);
        Check(tracker.ShownCount == MetaProgression.TrackedShown && HudNames(tracker).SequenceEqual(open.Take(4).Select(v => v.name)),
            "HUD shows the first four marks: " + string.Join(", ", HudNames(tracker)));
        Check(tracker.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "HUD tracker never takes clicks");
        var frame = tracker.transform.Find("Tracker Panel") as RectTransform;
        var corners = new Vector3[4]; frame.GetWorldCorners(corners);
        var screen = new Vector3[4]; hud.GetWorldCorners(screen);
        Check(corners[2].x <= screen[2].x + .01f && corners[0].x > (screen[0].x + screen[2].x) * .5f && corners[0].y >= screen[0].y,
            "HUD tracker sits inside the right half of the screen");
        var overflow = tracker.GetComponentsInChildren<TMP_Text>().Where(t => { t.ForceMeshUpdate(); return t.isTextOverflowing; }).Select(t => t.text).ToList();
        if (overflow.Count > 0) Info("HUD names shortened with ellipsis: " + string.Join(", ", overflow));
        panel.gameObject.SetActive(false);
        Render(hud, "ChallengeTracker-HUD");
        panel.gameObject.SetActive(true);

        // Persistence: marks survive save and reload.
        Check(MetaProgression.Save(), "Profile with marks saved");
        MetaProgression.Reload();
        Check(MetaProgression.Profile.trackedChallenges != null && MetaProgression.Profile.trackedChallenges.SequenceEqual(open.Take(6).Select(v => v.track)), "Marks survive a reload");

        // A finished mark leaves the HUD and the fifth one moves up.
        var round = MetaProgression.Profile.challengeRounds.Find(r => r != null && r.period == (int)ChallengePeriod.Daily);
        var task = round?.tasks.FirstOrDefault(t => t != null && ChallengePeriod.Daily + "-" + t.id + (t.subject >= 0 ? "-" + t.subject : "") == first.id);
        if (Check(task != null, "Task of " + first.id + " found"))
        {
            task.progress = task.target; task.done = true;
            tracker.Refresh(); Refresh(tracker);
            Check(tracker.ShownCount == 4 && HudNames(tracker).SequenceEqual(open.Skip(1).Take(4).Select(v => v.name)),
                "Finished mark drops out, the fifth moves up: " + string.Join(", ", HudNames(tracker)));
        }
        MetaProgression.SetTracked(open[2].track, false);
        Check(!MetaProgression.IsTracked(open[2].track), "Second star click removes the mark");

        // A new day ends the daily marks; weekly marks of the same week stay.
        MetaProgression.ChallengeTestUtc = Now.AddDays(1);
        var left = MetaProgression.TrackedChallenges().Select(v => v.track).ToList();
        Check(left.All(k => k.StartsWith("Weekly-")) && MetaProgression.Profile.trackedChallenges.All(k => k.StartsWith("Weekly-")),
            "Daily marks end with their round: " + string.Join(", ", left));
        MetaProgression.ChallengeTestUtc = Now;
        Object.DestroyImmediate(hud.gameObject);
    }
    static List<string> HudNames(Component tracker) => tracker.GetComponentsInChildren<Transform>()
        .Where(t => t.name.StartsWith("Tracked ")).Select(t => t.Find("Name")?.GetComponent<TMP_Text>()?.text).ToList();

    static void CloseWithBack(ChallengePanel panel)
    {
        // Unity sends no OnDestroy to edit-mode scripts; release the panel's MetaProgression.Changed handler first.
        typeof(ChallengePanel).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(panel, null);
        if (!Click(panel, "Challenges Back")) return;
        Check(!panel, "Back closes and destroys the panel in Edit Mode (DestroyImmediate)");
    }

    // ---------- helpers ----------

    static RectTransform Active(Transform content, string name)
        => content.Cast<Transform>().FirstOrDefault(t => t.gameObject.activeSelf && t.name == name) as RectTransform;
    static Transform Named(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    static string Text(Transform root, string path)
    {
        var node = root ? root.Find(path) : null;
        var text = node ? node.GetComponent<TMP_Text>() : null;
        return text ? text.text : null;
    }
    static bool Seal(Transform tile) { var seal = tile.Find("Completed"); return seal && seal.gameObject.activeSelf; }
    static Graphic Rim(Transform tile)
    {
        var node = tile.Find("Tier Rim");
        var graphic = node ? node.GetComponent<Graphic>() : null;
        return graphic ? graphic : null;
    }
    static bool Same(Color color, Color32 expected) { Color32 c = color; return Math.Abs(c.r - expected.r) <= 2 && Math.Abs(c.g - expected.g) <= 2 && Math.Abs(c.b - expected.b) <= 2; }
    static string Rgb(Graphic graphic) { if (graphic == null) return "missing"; Color32 c = graphic.color; return "(" + c.r + "," + c.g + "," + c.b + ")"; }
    static bool IconMatches(string key, Sprite sprite)
    {
        if (string.IsNullOrEmpty(key) || !sprite) return false;
        if (key.StartsWith("art:")) return sprite.name == "Challenge Art " + key.Substring(4);
        if (key == "artifact") return sprite.name == "ArtifactPoint_Sun";
        if (!key.StartsWith("item:") || !Enum.TryParse<Item>(key.Substring(5), out var item)) return false;
        var catalog = Resources.Load<ItemCatalog>("ItemCatalog");
        var entry = catalog ? catalog.items.FirstOrDefault(e => e && e.item == item && e.icon) : null;
        return entry ? entry.icon == sprite : true;
    }
    static string PathOf(Transform node, Transform root)
    {
        var parts = new List<string>();
        for (var t = node; t && t != root; t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }
    static string N0(long value) => value.ToString("N0", German);
    static string Xp(int xp) => "+" + xp.ToString("N0", German) + " XP";
    static string Counter(long current, long target, string unit) => N0(current) + " / " + N0(target) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
    static string Ticks(long ticks) => ticks > 0 && ticks < DateTime.MaxValue.Ticks ? new DateTime(ticks, DateTimeKind.Utc).ToString("u") : ticks.ToString();
    static string Safe(Func<string> value) { try { return value(); } catch (Exception e) { Fail("Catalog call threw " + Describe(e)); return null; } }

    static void Section(string name, Action body)
    {
        int checksBefore = checks, failedBefore = failed;
        try { body(); }
        catch (Exception e) { Fail(name + " threw " + Describe(e)); }
        report.Add((failed == failedBefore ? "PASS " : "INFO ") + name + ": " + (checks - checksBefore) + " checks" + (failed == failedBefore ? "" : ", " + (failed - failedBefore) + " failed"));
    }
    static string Describe(Exception e)
    {
        while ((e is TargetInvocationException || e is AggregateException) && e.InnerException != null) e = e.InnerException;
        string frame = e.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return e.GetType().Name + ": " + e.Message + (frame.Length > 0 ? " @ " + frame : "");
    }
    static bool Check(bool ok, string label) { checks++; if (!ok) { failed++; report.Add("FAIL " + label); } return ok; }
    static void Fail(string label) => Check(false, label);
    static void Info(string text) => report.Add("INFO " + text);
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + (message ?? "").Split('\n')[0]);
    }
}
