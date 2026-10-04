using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Presentation of an already committed run ledger. Never awards experience.</summary>
public sealed class ExpeditionResultPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    public bool AnimationComplete { get; private set; }
    public long EarnedXp { get; private set; }
    public long StartXp { get; private set; }
    public long FinalXp { get; private set; }
    RectTransform layout;
    ProgressionTimeline timeline;
    TextMeshProUGUI points;
    Button spend, home;
    float previousScale;
    bool leaving;
    static readonly CultureInfo German=CultureInfo.GetCultureInfo("de-DE");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()=>IsOpen=false;

    public static ExpeditionResultPanel Show(Transform parent)
    {
        if (!Application.isPlaying || !parent || LoadingProgress.Active || RunNavigation.IsTransitioning) return null;
        var existing=FindFirstObjectByType<ExpeditionResultPanel>(); if(existing) return existing;
        var run=MetaProgression.CurrentRun; if(run==null || !run.ended) return null;
        var root=HomeUi.Panel("Expedition Result",parent);
        var panel=root.gameObject.AddComponent<ExpeditionResultPanel>();
        panel.FinalXp=MetaProgression.TotalXp; panel.EarnedXp=Math.Max(0,run.earnedXp);
        panel.StartXp=Math.Max(0,panel.FinalXp-panel.EarnedXp);
        panel.Build(); return panel;
    }
    void Build()
    {
        IsOpen=true; previousScale=Time.timeScale; Time.timeScale=0;
        GameplayInputBlocker.SetBlocked(this,true); transform.SetAsLastSibling();
        var shade=HomeUi.Image("Scrim",transform,Vector2.zero,Vector2.zero); HomeUi.Stretch(shade.rectTransform);
        shade.color=new Color(0,0,0,.84f); shade.raycastTarget=true;
        layout=HomeUi.Rect("Result Layout",transform,Vector2.zero,new Vector2(1740,960));
        ProgressionArt.Text("Result Title",layout,"Expedition beendet",new Vector2(0,410),new Vector2(1500,72),61);
        ProgressionArt.Text("Earned XP",layout,"+"+EarnedXp.ToString("N0",German)+" XP",new Vector2(0,349),new Vector2(1200,48),39).color=ProgressionArt.Gold;
        HomeUi.Image("Board Backing",layout,new Vector2(0,-23),new Vector2(1640,610)).color=new Color32(23,12,7,255);
        var board=HomeUi.Image("Board",layout,new Vector2(0,-23),new Vector2(1700,665),"Panel"); board.pixelsPerUnitMultiplier=1.4f;
        timeline=ProgressionTimeline.Create(layout,new Vector2(0,10),StartXp,true,MetaProgressionCatalog.LevelForXp(StartXp));
        points=ProgressionArt.Text("Earned Points",layout,"",new Vector2(0,-291),new Vector2(1430,43),30);
        points.color=ProgressionArt.Gold;
        spend=HomeUi.Button("Spend Points",layout,"Punkte verteilen",new Vector2(-254,-421),new Vector2(465,79),()=>Leave(true));
        home=HomeUi.Button("Main Menu",layout,"Hauptmenü",new Vector2(254,-421),new Vector2(465,79),()=>Leave(false));
        Fit(); ProgressionArt.Navigation(transform);
        EventSystem.current?.SetSelectedGameObject(home.gameObject);
        StartCoroutine(Animate());
    }
    IEnumerator Animate()
    {
        // Unscaled and bounded: pauses, zero XP, long runs and level 1000 all terminate.
        float duration=EarnedXp>0?3.2f:0;
        for(float elapsed=0;elapsed<duration;elapsed+=Time.unscaledDeltaTime) {
            float t=Mathf.Clamp01(elapsed/duration); double eased=t*t*(3-2*t);
            long xp=StartXp+(long)((FinalXp-StartXp)*eased);
            timeline.SetExperience(xp); RefreshPoints(xp); yield return null;
        }
        CompleteAnimation();
    }
    public void CompleteAnimation()
    {
        if(AnimationComplete) return;
        StopAllCoroutines(); timeline.SetExperience(FinalXp,true); RefreshPoints(FinalXp); AnimationComplete=true;
    }
    void RefreshPoints(long xp)
    {
        int from=MetaProgressionCatalog.LevelForXp(StartXp), to=MetaProgressionCatalog.LevelForXp(xp);
        int power=MetaProgressionCatalog.EarnedPowerPoints(to)-MetaProgressionCatalog.EarnedPowerPoints(from);
        int comfort=MetaProgressionCatalog.EarnedComfortPoints(to)-MetaProgressionCatalog.EarnedComfortPoints(from);
        points.text=power>0?"+"+power+(power==1?" Powerup-Punkt":" Powerup-Punkte"):"";
        if(comfort>0) points.text+=(power>0?"  ·  ":"")+"+"+comfort+(comfort==1?" Komfortpunkt":" Komfortpunkte");
    }
    void Leave(bool upgrades)
    {
        if(leaving || RunNavigation.IsTransitioning || GameSaveSystem.IsBusy) return;
        CompleteAnimation(); leaving=true;
        MetaProgressionPanel.OpenUpgradesOnHome=upgrades;
        GetComponent<CanvasGroup>().interactable=false;
        AudioManager.Instance?.StopGameOverMusic();
        RunNavigation.MainMenu();
    }
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Escape)) { RunPauseMenu.ConsumeInput(); if(!AnimationComplete) CompleteAnimation(); }
    }
    void Fit()=>HomeUi.Fit(layout,new Vector2(1820,1020));
    void OnRectTransformDimensionsChange()=>Fit();
    void OnDestroy()
    {
        IsOpen=false; GameplayInputBlocker.SetBlocked(this,false);
        if(!RunNavigation.IsTransitioning && !LoadingProgress.Active) Time.timeScale=previousScale;
    }
}
