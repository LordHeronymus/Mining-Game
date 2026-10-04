using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Presentation only: listens to committed XP, never changes rewards, input or time scale.
public sealed class GameplayLevelUpPresentation : MonoBehaviour
{
    public static GameplayLevelUpPresentation Instance { get; private set; }
    public bool IsShowing { get; private set; }
    public int FromLevel { get; private set; }
    public int ToLevel { get; private set; }
    public int AudioPlayCount { get; private set; }
    public float Elapsed => elapsed;
    const float Duration = 2.8f;
    MetaProfile observedProfile;
    MetaRunState observedRun;
    int observedLevel, pendingFrom, pendingTo;
    CanvasGroup group;
    RectTransform medal;
    TextMeshProUGUI number, reward;
    LevelUpGlowGraphic backGlow, frontGlow;
    LevelUpPresentationAssets assets;
    AudioSource sound;
    float elapsed, totalElapsed, replayFade, soundWait, visualWait;
    bool soundPaused, previewing, soundPending;

    public static bool Preview()
    {
        if(!Application.isPlaying || LoadingProgress.Active || RunNavigation.IsTransitioning)return false;
        Install();
        var view=Instance;
        view.Observe();view.Clear();view.previewing=true;
        view.pendingTo=Mathf.Clamp(MetaProgression.Level+1,2,MetaProgressionCatalog.MaxLevel);
        view.pendingFrom=view.pendingTo-1;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()=>Instance=null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if(Instance)return;
        var root=new GameObject("Gameplay Level Up",typeof(RectTransform));
        DontDestroyOnLoad(root);root.AddComponent<GameplayLevelUpPresentation>();
    }
    void Awake()
    {
        if(Instance && Instance!=this){Destroy(gameObject);return;}
        Instance=this;
        assets=Resources.Load<LevelUpPresentationAssets>("Progression/LevelUpPresentation");
        if(assets && assets.sound)assets.sound.LoadAudioData();
        var canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
        var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        group=gameObject.AddComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=false;group.interactable=false;
        medal=HomeUi.Rect("Level Up Medallion",transform,new Vector2(0,-263),new Vector2(360,360));
        medal.anchorMin=medal.anchorMax=new Vector2(.5f,1);
        backGlow=Glow("Gold aura",false);
        var art=HomeUi.Image("Medallion",medal,Vector2.zero,new Vector2(360,360));art.sprite=assets?assets.medallion:null;art.preserveAspect=true;
        var title=ProgressionArt.Text("Title",medal,"LEVEL\nAUFSTIEG",new Vector2(0,66),new Vector2(218,62),26);
        title.textWrappingMode=TextWrappingModes.Normal;title.lineSpacing=-12;
        number=ProgressionArt.Text("Level",medal,"",new Vector2(0,-4),new Vector2(245,116),99);
        number.fontSizeMin=62;
        HomeUi.Image("Divider",medal,new Vector2(0,-53),new Vector2(116,1.3f)).color=ProgressionArt.Gold;
        reward=ProgressionArt.Text("Reward",medal,"",new Vector2(0,-74),new Vector2(210,52),19);
        reward.textWrappingMode=TextWrappingModes.Normal;reward.fontSizeMin=14;reward.lineSpacing=-8;
        frontGlow=Glow("Orbit and sparks",true);
        frontGlow.transform.SetSiblingIndex(2);
        sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;sound.loop=false;sound.spatialBlend=0;
        sound.ignoreListenerPause=false;
    }
    LevelUpGlowGraphic Glow(string name,bool front)
    {
        var rect=HomeUi.Rect(name,medal,Vector2.zero,new Vector2(450,450));
        var graphic=rect.gameObject.AddComponent<LevelUpGlowGraphic>();graphic.front=front;graphic.raycastTarget=false;return graphic;
    }
    void OnEnable(){MetaProgression.Changed+=Observe;SceneManager.sceneLoaded+=SceneLoaded;}
    void OnDisable(){MetaProgression.Changed-=Observe;SceneManager.sceneLoaded-=SceneLoaded;Clear();}
    void SceneLoaded(Scene scene,LoadSceneMode mode){Clear();observedRun=null;observedProfile=null;}
    bool InRun => Application.isPlaying && SceneManager.GetActiveScene().name==RunNavigation.GameScene &&
        !LoadingProgress.Active && !RunNavigation.IsTransitioning && MetaProgression.CurrentRun!=null && !MetaProgression.CurrentRun.ended;
    void Observe()
    {
        if(previewing)
        {
            if(!Application.isPlaying || LoadingProgress.Active || RunNavigation.IsTransitioning){Clear();return;}
            // A real XP gain takes precedence over the decorative preview.
            if(InRun && ReferenceEquals(MetaProgression.Profile,observedProfile) && ReferenceEquals(MetaProgression.CurrentRun,observedRun) && MetaProgression.Level>observedLevel)Clear();
            else return;
        }
        if(!InRun){Clear();observedRun=null;observedProfile=null;return;}
        var profile=MetaProgression.Profile;var run=MetaProgression.CurrentRun;int level=MetaProgression.Level;
        if(!ReferenceEquals(profile,observedProfile)||!ReferenceEquals(run,observedRun))
        {Clear();observedProfile=profile;observedRun=run;observedLevel=level;return;}
        if(level<observedLevel){Clear();observedLevel=level;return;}
        if(level==observedLevel)return;
        if(IsShowing)
        {
            ToLevel=level;RefreshReward();elapsed=Mathf.Min(elapsed,Duration-.95f);
        }
        else {if(pendingTo==0)pendingFrom=observedLevel;pendingTo=level;}
        observedLevel=level;
    }
    void Update()
    {
        Observe();if(!previewing && !InRun)return;
        bool obscured=GameplayInputBlocker.IsBlocked || Time.timeScale<=0 || GameOverPanel.IsOpen || GameVictoryPanel.IsOpen;
        if(obscured)
        {
            group.alpha=0;if(sound.isPlaying){sound.Pause();soundPaused=true;}return;
        }
        if(soundPaused){sound.UnPause();soundPaused=false;}
        if(sound.clip)sound.volume=AudioManager.TunedVolume(sound.clip,1f,sound);
        if(!IsShowing && pendingTo>0)
        {
            // Fade a previous long tail before replaying on the same source; never stack booms.
            if(sound.isPlaying && replayFade<.12f)
            {replayFade+=Time.unscaledDeltaTime;sound.volume*=1-Mathf.Clamp01(replayFade/.12f);return;}
            Begin();
        }
        float step=Time.unscaledDeltaTime;
        if(soundPending){soundWait-=step;if(soundWait<=0)PlaySound();}
        if(!IsShowing){if(previewing && pendingTo==0 && !soundPending && !sound.isPlaying)previewing=false;return;}
        elapsed+=step;totalElapsed+=step;
        if(elapsed<0){group.alpha=0;return;}
        float impact=assets?Mathf.Clamp(assets.impactSeconds,.04f,.4f):.16f;
        float intro=Mathf.Clamp01(elapsed/impact);
        float settling=Mathf.Clamp01((elapsed-impact)/.28f);
        float scale=elapsed<impact?Mathf.Lerp(.68f,1.07f,intro*intro):Mathf.Lerp(1.07f,1f,1-Mathf.Pow(1-settling,3));
        float fade=Mathf.Clamp01((Duration-elapsed)/.45f);
        group.alpha=Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/.12f))*fade;
        medal.localScale=Vector3.one*scale;
        medal.anchoredPosition=new Vector2(0,-263+Mathf.Lerp(12,0,intro)+(1-fade)*10);
        backGlow.Animate(elapsed,impact);frontGlow.Animate(elapsed,impact);
        if(elapsed>=Duration || totalElapsed>=6f+visualWait){IsShowing=false;group.alpha=0;}
    }
    void Begin()
    {
        FromLevel=pendingFrom;ToLevel=pendingTo;pendingFrom=pendingTo=0;
        float offset=GpsSettings.Preferences.levelUpAnimationOffsetSeconds;
        if(float.IsNaN(offset)||float.IsInfinity(offset))offset=0;
        offset=Mathf.Clamp(offset,-5,5);
        visualWait=Mathf.Max(0,offset);soundWait=Mathf.Max(0,-offset);
        elapsed=-visualWait;totalElapsed=replayFade=0;IsShowing=true;RefreshReward();
        sound.Stop();soundPending=false;soundPaused=false;
        if(assets && assets.sound)
        {
            sound.clip=assets.sound;sound.pitch=AudioManager.TunedPitch(sound.clip,1,sound);
            sound.volume=AudioManager.TunedVolume(sound.clip,1,sound);
            if(soundWait>0)soundPending=true;else PlaySound();
        }
    }
    void PlaySound(){soundPending=false;sound.Play();AudioPlayCount++;}
    void RefreshReward()
    {
        number.text=ToLevel.ToString();
        int power=MetaProgressionCatalog.EarnedPowerPoints(ToLevel)-MetaProgressionCatalog.EarnedPowerPoints(FromLevel);
        int comfort=MetaProgressionCatalog.EarnedComfortPoints(ToLevel)-MetaProgressionCatalog.EarnedComfortPoints(FromLevel);
        reward.text=power>0?"+"+power+(power==1?" Powerup-Punkt":" Powerup-Punkte"):"";
        if(comfort>0)reward.text+=(power>0?"\n":"")+"+"+comfort+(comfort==1?" Komfortpunkt":" Komfortpunkte");
    }
    void Clear()
    {
        IsShowing=false;pendingFrom=pendingTo=0;elapsed=totalElapsed=replayFade=soundWait=visualWait=0;soundPaused=previewing=soundPending=false;
        if(group)group.alpha=0;if(sound)sound.Stop();
    }
    void OnDestroy(){if(Instance==this)Instance=null;}
}
