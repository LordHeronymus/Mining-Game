using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Pipeline run_script: invoke MetaProgressionUiChecks.Main in the main menu.
// This UI-only suite never changes XP, ranks, recipes, gameplay saves or profile paths.
public static class MetaProgressionUiChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible) throw new Exception("Start in the main menu.");
        if (GameplayInputBlocker.IsBlocked || MetaProgressionPanel.IsOpen) throw new Exception("Close other panels before running the checks.");
        new GameObject("Meta Progression UI Checks").AddComponent<MetaProgressionUiProbe>();
        return "Started progression UI checks";
    }
}

public sealed class MetaProgressionUiProbe : MonoBehaviour
{
    const string Report = "Temp/MetaProgressionUiChecks.txt";
    readonly List<string> passed = new();
    MetaProgressionPanel panel;
    long originalXp;
    int[] originalRanks;
    bool finished;

    void Check(bool value, string description)
    {
        if (!value) throw new Exception(description);
        passed.Add(description); File.WriteAllLines(Report, passed);
    }
    Button Button(string name) => panel.GetComponentsInChildren<Button>(true).First(b => b.name == name);
    IEnumerator Start()
    {
        originalXp = MetaProgression.TotalXp;
        originalRanks = MetaProgressionCatalog.Upgrades.Select(u => MetaProgression.GetRank(u.id)).ToArray();
        var routines = new Stack<IEnumerator>(); routines.Push(Run());
        while (routines.Count > 0)
        {
            bool more = false; object current = null; Exception failure = null;
            try { more = routines.Peek().MoveNext(); if (more) current = routines.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) { File.AppendAllText(Report, "\nFAIL: " + failure); Cleanup(); yield break; }
            if (!more) { routines.Pop(); continue; }
            if (current is IEnumerator nested) routines.Push(nested); else yield return current;
        }
        File.AppendAllText(Report, "\nPASS: " + passed.Count + " checks"); Cleanup();
    }

    IEnumerator Run()
    {
        var home = Object.FindFirstObjectByType<MainMenuController>();
        var entrance = home.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Progression");
        // Main menu layout is attached to ScreenCanvas, not the scene controller.
        if (!entrance) entrance = GameObject.Find("ScreenCanvas").GetComponentsInChildren<Button>(true).First(b => b.name == "Progression");
        Check(entrance.GetComponentInChildren<TMP_Text>().text.Contains("Level " + MetaProgression.Level), "Home entry exposes current level");
        entrance.onClick.Invoke(); yield return null;
        panel = Object.FindFirstObjectByType<MetaProgressionPanel>();
        Check(panel && MetaProgressionPanel.IsOpen && !panel.IsReadOnly, "Home opens editable progression");
        Check(panel.ShowingOverview, "Home first displays progression timeline");
        Button("Open Upgrades").onClick.Invoke(); yield return null;
        Check(Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Profile modal pauses and blocks gameplay");
        Check(MetaProgressionPanel.Show(panel.transform.parent) == panel, "Repeated open reuses one modal");
        var group = panel.GetComponent<CanvasGroup>();
        Check(group.alpha == 1 && group.interactable && group.blocksRaycasts, "Runtime root is visible and interactive");
        var board = panel.transform.Find("Upgrades Layout/Board").GetComponent<Image>();
        Check(board.material && board.material.shader.name.Contains("PanelWoodTone"), "Progression shares dark panel wood shader");
        var scroll = panel.GetComponentInChildren<ScrollRect>();
        Check(scroll && scroll.content.rect.height > scroll.viewport.rect.height && scroll.verticalScrollbar, "All upgrades are scrollable with a scrollbar");
        foreach (var definition in MetaProgressionCatalog.Upgrades)
        {
            var buy = Button("Buy " + definition.id);
            Check(buy && buy.interactable == (MetaProgression.GetRank(definition.id) < definition.maxRank &&
                (definition.comfort ? MetaProgression.AvailableComfortPoints : MetaProgression.AvailablePowerPoints) >= definition.Cost(MetaProgression.GetRank(definition.id)) && MetaProgression.CanEdit),
                definition.id + " spend state matches current balance and cap");
        }
        foreach (var button in panel.GetComponentsInChildren<Button>())
        {
            var feedback = button.GetComponent<HomeButtonFeedback>();
            Check(feedback && !feedback.animateScale && feedback.normalPart == "SaveButton" && feedback.activePart == "SaveActive", button.name + " uses stationary shared button styling");
            if (button.IsInteractable()) Check(button.navigation.mode == Navigation.Mode.Explicit &&
                button.navigation.selectOnDown.transform.IsChildOf(panel.transform) && button.navigation.selectOnUp.transform.IsChildOf(panel.transform), button.name + " keyboard navigation stays inside modal");
        }
        var tab = Button("Back");
        var position = tab.GetComponent<RectTransform>().anchoredPosition;
        tab.GetComponent<HomeButtonFeedback>().OnPointerEnter(new PointerEventData(EventSystem.current));
        yield return new WaitForSecondsRealtime(.4f);
        Check(position == tab.GetComponent<RectTransform>().anchoredPosition && tab.transform.localScale == Vector3.one, "Paused hover preserves tab geometry");
        tab.GetComponent<HomeButtonFeedback>().OnPointerExit(new PointerEventData(EventSystem.current));
        yield return Capture("Upgrades");
        tab.onClick.Invoke(); yield return null;
        Check(panel.ShowingOverview, "Upgrades Back returns to the timeline");
        Check(!panel.transform.Find("Progression Layout") && !panel.GetComponentsInChildren<Button>(true).Any(b => b.name.StartsWith("Tab ")), "Old combined progression panel and all tabs are absent");
        Button("Open Herausforderungen").onClick.Invoke(); yield return null;
        var challengesPanel = Object.FindFirstObjectByType<ChallengePanel>();
        Check(challengesPanel && !panel.ShowingOverview && !panel.transform.Find("Upgrades Layout").gameObject.activeSelf,
            "Timeline entry opens a separate challenge panel");
        var challengeScroll = challengesPanel.GetComponentInChildren<ScrollRect>();
        Check(challengeScroll.verticalNormalizedPosition > .999f, "Challenges open at the top");
        var challengeRoot=(RectTransform)panel.transform;
        challengeRoot.anchorMin=challengeRoot.anchorMax=new Vector2(.5f,.5f);
        foreach (var size in new[]{new Vector2(1920,1080),new Vector2(1280,1024),new Vector2(2560,1080)}) {
            challengeRoot.sizeDelta=size; yield return null;
            var frame=(RectTransform)challengesPanel.transform.Find("Challenges Layout/Board");
            var corners=new Vector3[4];frame.GetWorldCorners(corners);
            Check(corners.All(c=>challengeRoot.rect.Contains(challengeRoot.InverseTransformPoint(c))), "Challenge board fits "+size);
        }
        HomeUi.Stretch(challengeRoot); yield return null;
        foreach (var challenge in MetaProgression.GetChallenges())
        {
            var row = challengeScroll.content.Find("Challenge " + challenge.id);
            Check(row && row.Find("Progress").GetComponent<TMP_Text>().text.Any(char.IsLetter), challenge.id + " counter names the objective unit");
            Check(row.Find("Icon").GetComponent<Image>().sprite && !row.GetComponentsInChildren<TMP_Text>().Any(t => t.isTextOverflowing), challenge.id + " icon and tile text fit");
        }
        Check(Time.timeScale == 0 && GameplayInputBlocker.IsBlocked, "Challenge panel preserves parent pause and blocker");
        yield return Capture("Challenges");
        challengeScroll.verticalNormalizedPosition=0; yield return null;
        var lastChallenge=(RectTransform)challengeScroll.content.Find("Challenge "+MetaProgression.GetChallenges().Last().id);
        Check(challengeScroll.viewport.rect.Contains(challengeScroll.viewport.InverseTransformPoint(lastChallenge.TransformPoint(lastChallenge.rect.center))), "Last challenge tile reachable by scrolling");
        Button("Challenges Back").onClick.Invoke(); yield return null;
        Check(panel.ShowingOverview && !Object.FindFirstObjectByType<ChallengePanel>(), "Challenge Back returns directly to the timeline");
        Button("Open Baupläne").onClick.Invoke(); yield return null;
        var blueprintsPanel=Object.FindFirstObjectByType<BlueprintCollectionPanel>();
        Check(blueprintsPanel && !panel.ShowingOverview && !panel.transform.Find("Upgrades Layout").gameObject.activeSelf, "Blueprints open in their own panel from timeline");
        var blueprintScroll=blueprintsPanel.GetComponentInChildren<ScrollRect>();
        Check(blueprintScroll.verticalNormalizedPosition>.999f, "Blueprint collection opens at top");
        foreach (var recipe in ExoticCatalog.Recipes)
        {
            var row = blueprintScroll.content.Find("Blueprint " + recipe.exoticId);
            Check(row && row.GetComponentInChildren<ExoticSealGraphic>(), recipe.exoticId + " has a crystalline exotic seal");
            Check(row.Find("Unlock Level").GetComponent<TMP_Text>().text == "Level " + recipe.metaUnlockLevel, recipe.exoticId + " shows exact level gate");
        }
        int essentials = ExoticCatalog.AllRecipes.Count(r => r && r.output && !r.exotic);
        Check(essentials > 0 && blueprintScroll.content.Cast<Transform>().Count(t => t.name.StartsWith("Essential ")) == essentials, "All essential recipes are listed from the build catalog");
        foreach(var tile in blueprintScroll.content.Cast<Transform>().Where(t=>t.name.StartsWith("Essential ") || t.name.StartsWith("Blueprint "))) {
            Check(tile.Find("Icon").GetComponent<Image>().sprite && !tile.GetComponentsInChildren<TMP_Text>().Any(t=>t.isTextOverflowing), "Blueprint icon and text fit: "+tile.name);
        }
        challengeRoot.anchorMin=challengeRoot.anchorMax=new Vector2(.5f,.5f);
        foreach(var size in new[]{new Vector2(1920,1080),new Vector2(1280,1024),new Vector2(2560,1080)}) {
            challengeRoot.sizeDelta=size;yield return null;
            var frame=(RectTransform)blueprintsPanel.transform.Find("Blueprints Layout/Board");var corners=new Vector3[4];frame.GetWorldCorners(corners);
            Check(corners.All(c=>challengeRoot.rect.Contains(challengeRoot.InverseTransformPoint(c))), "Blueprint board fits "+size);
        }
        HomeUi.Stretch(challengeRoot);yield return null;
        yield return Capture("Blueprints");
        blueprintScroll.verticalNormalizedPosition=0;yield return null;
        var finalTile=(RectTransform)blueprintScroll.content.GetChild(blueprintScroll.content.childCount-1);
        Check(blueprintScroll.viewport.rect.Contains(blueprintScroll.viewport.InverseTransformPoint(finalTile.TransformPoint(finalTile.rect.center))), "Last essential blueprint is reachable");
        Button("Blueprints Back").onClick.Invoke();yield return null;
        Check(panel.ShowingOverview && !Object.FindFirstObjectByType<BlueprintCollectionPanel>() && GameplayInputBlocker.IsBlocked, "Blueprints Back returns to paused timeline");
        Button("Open Upgrades").onClick.Invoke(); yield return null;
        scroll.verticalNormalizedPosition = 0; yield return null;
        var last = Button("Buy " + MetaProgressionCatalog.Upgrades.Last().id).GetComponent<RectTransform>();
        Check(scroll.viewport.rect.Contains(scroll.viewport.InverseTransformPoint(last.TransformPoint(last.rect.center))), "Last comfort upgrade is reachable at scroll bottom");
        var root = (RectTransform)panel.transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1440, 1080), new Vector2(2560, 1080) })
        {
            root.sizeDelta = size; yield return null;
            var frame = (RectTransform)board.transform;
            var corners = new Vector3[4]; frame.GetWorldCorners(corners);
            Check(corners.All(c => root.rect.Contains(root.InverseTransformPoint(c))), "Board stays within " + size.x + "x" + size.y);
        }
        HomeUi.Stretch(root);
        Button("Back").onClick.Invoke(); yield return null;
        Check(panel.ShowingOverview, "Detail back returns to timeline overview");
        Button("Overview Back").onClick.Invoke(); yield return null;
        Check(!MetaProgressionPanel.IsOpen && Time.timeScale == 1 && !GameplayInputBlocker.IsBlocked, "Close releases only the profile modal pause and blocker");
        panel = MetaProgressionPanel.Show(GameObject.Find("ScreenCanvas").transform, true); yield return null;
        Check(panel.IsReadOnly && panel.GetComponentsInChildren<Button>().All(b => !b.name.StartsWith("Buy ")) && !Button("Refund Upgrades").gameObject.activeSelf,
            "Run view exposes no purchase or refund actions");
        Check(panel.transform.Find("Upgrades Layout/Run Experience"), "Run view shows earned run XP");
        panel.Close(); yield return null;
        Check(MetaProgression.TotalXp == originalXp && MetaProgressionCatalog.Upgrades.Select(u => MetaProgression.GetRank(u.id)).SequenceEqual(originalRanks), "UI navigation preserves XP and all purchased ranks");
    }

    IEnumerator Capture(string name)
    {
        yield return new WaitForSecondsRealtime(.55f);
        Canvas.ForceUpdateCanvases(); yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot("Temp/MetaProgression-" + name + ".png");
        yield return null; yield return null;
    }
    void Cleanup()
    {
        if (finished) return;
        finished = true;
        if (panel) panel.Close();
        Destroy(gameObject);
    }
    void OnDestroy() { if (!finished && panel) panel.Close(); }
}
