using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MapGenerator))]
public sealed class UltroniumAltarChamber : MonoBehaviour
{
    static UltroniumAltarChamber completingAltar;
    static UltroniumAltarChamber ambienceAltar;
    public static bool VictorySequenceActive => completingAltar && completingAltar.completing;
    public static float StandardLayerAmbienceGain => ambienceAltar ? ambienceAltar.standardAmbienceGain : 1f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { completingAltar=null; ambienceAltar=null; }
    [SerializeField] bool chamberEnabled = true;
    [SerializeField, HideInInspector] bool hasLayout;
    [SerializeField, HideInInspector] Vector3Int chamberOrigin;
    [SerializeField, HideInInspector] int depositedUltronium;
    [SerializeField, HideInInspector] int stateSeed;
    [SerializeField, HideInInspector] bool victoryPresented;
    [Min(.1f)] public float chargeTransitionSeconds = 1f;
    [Min(.1f)] public float victoryDelaySeconds = 2.2f;
    public UltroniumChamberLayout Layout => hasLayout ? new UltroniumChamberLayout(chamberOrigin) : default;
    public int DepositedUltronium => depositedUltronium;
    public int RequiredUltronium => StatsManager.Instance ? Mathf.Max(1,StatsManager.Instance.ultroniumRequiredToWin) : 50;
    public float Charge => Mathf.Clamp01(depositedUltronium/(float)RequiredUltronium);
    public float DisplayedCharge => shownCharge;
    public Texture2D ChamberLightMask { get; private set; }
    public Vector4 ChamberLightRect { get; private set; }
    public Vector4 ChamberLightSize => ChamberLightMask ? new Vector4(ChamberLightMask.width,ChamberLightMask.height,0,0) : Vector4.zero;
    public bool IsCompleting => completing;
    public Vector3 AltarPosition => map ? map.Terrain.CellToWorld(chamberOrigin)+new Vector3(CellSize*.5f,0f,0f) : Vector3.zero;
    public Vector4 LightSource => root && hasLayout ? new Vector4(AltarPosition.x,AltarPosition.y+CellSize*1.35f,CellSize*14f,maskIntensity) : Vector4.zero;
    public float CellSize => map ? Mathf.Abs(map.Terrain.CellToWorld(Vector3Int.right).x-map.Terrain.CellToWorld(Vector3Int.zero).x) : 1f;

    MapGenerator map;
    GameObject root;
    readonly List<UnityEngine.Object> owned = new();
    readonly List<Material> materials = new();
    Light2D altarLight;
    ParticleSystem sparks;
    UnityEngine.Rendering.Volume bloomVolume;
    CanvasGroup buttonGroup;
    Button depositButton;
    Image depositImage;
    Image depositActiveImage;
    Material depositGlowMaterial;
    Outline depositOutline;
    RectTransform depositButtonRect;
    Vector2 depositButtonBasePosition;
    RectTransform progressTextRect;
    Vector2 progressTextBasePosition;
    ParticleSystem buttonMotes;
    Light2D buttonLight;
    TextMeshProUGUI buttonText, progressText;
    PlayerMovement player;
    float shownCharge, completionTime, maskIntensity, transitionFrom, transitionTarget, transitionTime, buttonGlow;
    AudioClip altarAmbienceClip, chargeUpClip, fullChargeClip;
    readonly AudioSource[] altarAmbienceSources = new AudioSource[2];
    int altarAmbienceCurrent;
    bool altarAmbienceRunning, altarAmbienceTransitioning;
    double altarAmbienceNextStart, altarAmbienceTransitionStart;
    float altarAmbienceGain, standardAmbienceGain = 1f;
    bool audioClipsLoaded;
    bool depositing, completing;

    [Serializable] public struct SaveState
    {
        public int seed, x, y, deposited;
        public bool placed, victoryShown;
    }
    public SaveState CaptureState() => new SaveState {seed=stateSeed,x=chamberOrigin.x,y=chamberOrigin.y,
        deposited=depositedUltronium,placed=hasLayout,victoryShown=victoryPresented};
    public void RestoreState(SaveState state)
    {
        GameplayInputBlocker.SetBlocked(this,false);
        if(completingAltar==this) completingAltar=null;
        stateSeed=state.seed; chamberOrigin=new Vector3Int(state.x,state.y,0); hasLayout=state.placed;
        depositedUltronium=Mathf.Clamp(state.deposited,0,RequiredUltronium); victoryPresented=state.victoryShown;
        completing=false; completionTime=0; shownCharge=transitionFrom=transitionTarget=Charge; transitionTime=0;
        RebuildVisuals();
    }
    void OnEnable()
    {
        ambienceAltar=this;
        map=GetComponent<MapGenerator>();
        map.Generated+=RebuildVisuals;
        if(map.IsGenerated) RebuildVisuals();
    }
    void OnDisable()
    {
        if(ambienceAltar==this) ambienceAltar=null;
        StopAltarAmbience();
        altarAmbienceGain=0f; standardAmbienceGain=1f;
        completing=false;
        if(completingAltar==this) completingAltar=null;
        if(map) map.Generated-=RebuildVisuals;
        GameplayInputBlocker.SetBlocked(this,false);
        ReleaseVisuals();
    }
    public UltroniumChamberLayout ChooseLayout(int seed, int width, int height)
    {
        if(!map) map=GetComponent<MapGenerator>();
        var borders=GetComponent<MapWorldBorders>();
        return chamberEnabled ? UltroniumChamberLayout.Choose(seed,width,height,map.layers,borders?borders.sidePaddingCells:0) : default;
    }
    public void PrepareGeneration(int seed, int width, int height)
    {
        var layout=ChooseLayout(seed,width,height);
        hasLayout=layout.valid; chamberOrigin=layout.origin; stateSeed=seed;
        ResetChargeForNewRun();
    }
    public void ResetChargeForNewRun()
    {
        depositedUltronium=0; shownCharge=transitionFrom=transitionTarget=transitionTime=0; completing=false; completionTime=0; victoryPresented=false;
        GameplayInputBlocker.SetBlocked(this,false);
    }
    public bool Protects(Vector3Int cell) => Layout.IsShell(cell);
    public bool CanInteract
    {
        get
        {
            if(!Application.isPlaying || !hasLayout || !map || map.IsGenerationStreaming || completing ||
                victoryPresented || GameplayInputBlocker.IsBlocked || !StatsManager.Instance ||
                StatsManager.Instance.Health<=0 || StatsManager.Instance.HasWon) return false;
            if(!player) player=FindFirstObjectByType<PlayerMovement>();
            if(!player) return false;
            var cell=map.Terrain.WorldToCell(player.transform.position);
            return Layout.IsOpen(cell) && Mathf.Abs(player.transform.position.x-AltarPosition.x)<=CellSize*3.2f &&
                player.transform.position.y>=AltarPosition.y-.2f && player.transform.position.y<=AltarPosition.y+CellSize*2.5f;
        }
    }
    int Available()
    {
        var inventory=InventoryManager.Instance;
        if(!inventory) return 0;
        int total=0;
        foreach(var entry in inventory.GetSnapshot())
            if(entry.Key && entry.Key.item==Item.Ultronium) total=(int)Math.Min(int.MaxValue,(long)total+entry.Value);
        return total;
    }
    public bool TryDeposit()
    {
        if(depositing || !CanInteract || depositedUltronium>=RequiredUltronium) return false;
        var inventory=InventoryManager.Instance;
        if(!inventory) return false;
        depositing=true;
        int transferred=0;
        try
        {
            // Snapshot the keys because removal notifies listeners and mutates inventory.
            var items=new List<ItemSO>();
            foreach(var entry in inventory.GetSnapshot()) if(entry.Key && entry.Key.item==Item.Ultronium) items.Add(entry.Key);
            foreach(var item in items)
            {
                int amount=Mathf.Min(inventory.GetCount(item),Mathf.Max(0,RequiredUltronium-depositedUltronium));
                if(amount<=0 || !inventory.TryRemove(item,amount)) continue;
                depositedUltronium+=amount; transferred+=amount;
            }
            if(transferred<=0) return false;
            LoadAudioClips();
            AudioClip depositClip=depositedUltronium>=RequiredUltronium ? fullChargeClip : chargeUpClip;
            if(depositClip) AudioManager.Instance?.PlayClip(depositClip,1f,0f);
            if(sparks) sparks.Emit(12);
            BeginCompletionIfReady();
            return true;
        }
        finally { depositing=false; }
    }
    void BeginCompletionIfReady()
    {
        if(completing || victoryPresented || depositedUltronium<RequiredUltronium || !StatsManager.Instance || StatsManager.Instance.Health<=0) return;
        completing=true; completionTime=0;
        completingAltar=this;
        GameplayInputBlocker.SetBlocked(this,true);
        AudioManager.Instance?.SetLowHealthHeartbeat(false,true);
    }
    void Update()
    {
        if(Application.isPlaying) UpdateAltarAmbience();
        if(!root || !hasLayout) return;
        if(Application.isPlaying)
        {
            if(!Mathf.Approximately(transitionTarget,Charge)) {transitionFrom=shownCharge;transitionTarget=Charge;transitionTime=0;}
            transitionTime+=Time.deltaTime;
            shownCharge=Mathf.Lerp(transitionFrom,transitionTarget,Mathf.SmoothStep(0,1,transitionTime/Mathf.Max(.1f,chargeTransitionSeconds)));
            if(depositedUltronium>=RequiredUltronium && !victoryPresented) BeginCompletionIfReady();
            if(completing && Time.timeScale>0)
            {
                completionTime+=Time.deltaTime;
                if(completionTime>=victoryDelaySeconds && StatsManager.Instance && StatsManager.Instance.CompleteAltarVictory(this))
                {
                    victoryPresented=true; completing=false;
                    GameplayInputBlocker.SetBlocked(this,false);
                }
            }
        }
        else shownCharge=Charge;
        int available=buttonGroup ? Available() : 0;
        bool canOffer=Application.isPlaying && available>0 && depositedUltronium<RequiredUltronium &&
            !completing && !victoryPresented && StatsManager.Instance &&
            StatsManager.Instance.Health>0 && !StatsManager.Instance.HasWon;
        buttonGlow=Mathf.MoveTowards(buttonGlow,canOffer?1f:0f,Time.unscaledDeltaTime*2.5f);
        if(buttonGroup)
        {
            bool canInteract=CanInteract;
            buttonGroup.alpha=1f;
            buttonGroup.blocksRaycasts=buttonGroup.interactable=canInteract;
            depositButton.interactable=canInteract && canOffer;
            UpdateDepositButtonAnimation();
            buttonText.text="Ultronium abgeben";
            progressText.text=depositedUltronium+" / "+RequiredUltronium;
        }
        ApplyChargeVisuals();
        UpdateButtonEffects();
    }
    void LoadAudioClips()
    {
        if(audioClipsLoaded) return;
        audioClipsLoaded=true;
        altarAmbienceClip=Resources.Load<AudioClip>("Audio/UltroniumAltarAmbience");
        chargeUpClip=Resources.Load<AudioClip>("Audio/UltroniumChargeUp");
        fullChargeClip=Resources.Load<AudioClip>("Audio/UltroniumFullCharge");
    }
    void UpdateAltarAmbience()
    {
        ambienceAltar=this;
        LoadAudioClips();
        if(!hasLayout || !chamberEnabled || !map || !map.Terrain)
        {
            altarAmbienceGain=0f; standardAmbienceGain=1f;
            StopAltarAmbience();
            return;
        }
        if(!player) player=FindFirstObjectByType<PlayerMovement>();
        float proximity=0f;
        if(player)
        {
            float cellSize=Mathf.Max(.01f,CellSize);
            float distanceInCells=Vector2.Distance(player.transform.position,AltarPosition)/cellSize;
            proximity=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(10f,5f,distanceInCells));
        }
        float crossfadeAngle=proximity*Mathf.PI*.5f;
        altarAmbienceGain=Mathf.Sin(crossfadeAngle);
        standardAmbienceGain=Mathf.Clamp01(Mathf.Cos(crossfadeAngle));
        if(!altarAmbienceClip || altarAmbienceGain<=0f)
        {
            StopAltarAmbience();
            return;
        }
        EnsureAltarAmbienceSources();
        double now=AudioSettings.dspTime;
        float crossfade=AltarAmbienceLoopCrossfade;
        if(!altarAmbienceRunning)
        {
            altarAmbienceCurrent=0;
            var first=altarAmbienceSources[altarAmbienceCurrent];
            first.pitch=AudioManager.TunedPitch(altarAmbienceClip,1f);
            first.volume=0f;
            first.Play();
            altarAmbienceNextStart=now+AltarAmbienceSegmentDuration(first)-crossfade;
            var next=altarAmbienceSources[1-altarAmbienceCurrent];
            next.pitch=AudioManager.TunedPitch(altarAmbienceClip,1f);
            next.volume=0f;
            next.PlayScheduled(altarAmbienceNextStart);
            altarAmbienceRunning=true;
        }
        if(!altarAmbienceTransitioning && now>=altarAmbienceNextStart)
        {
            altarAmbienceTransitioning=true;
            altarAmbienceTransitionStart=altarAmbienceNextStart;
        }
        float loopBlend=altarAmbienceTransitioning ? Mathf.SmoothStep(0f,1f,
            Mathf.Clamp01((float)(now-altarAmbienceTransitionStart)/Mathf.Max(.01f,crossfade))) : 0f;
        float ambienceVolume=AudioManager.TunedAmbienceVolume(altarAmbienceClip,
            altarAmbienceGain*AudioManager.GetAmbienceVolume(AmbienceType.Cave));
        float angle=loopBlend*Mathf.PI*.5f;
        altarAmbienceSources[altarAmbienceCurrent].volume=ambienceVolume*Mathf.Cos(angle);
        altarAmbienceSources[1-altarAmbienceCurrent].volume=ambienceVolume*Mathf.Sin(angle);
        if(altarAmbienceTransitioning && loopBlend>=1f)
        {
            altarAmbienceSources[altarAmbienceCurrent].Stop();
            altarAmbienceCurrent=1-altarAmbienceCurrent;
            altarAmbienceTransitioning=false;
            altarAmbienceNextStart=altarAmbienceTransitionStart+
                AltarAmbienceSegmentDuration(altarAmbienceSources[altarAmbienceCurrent])-crossfade;
            var next=altarAmbienceSources[1-altarAmbienceCurrent];
            next.pitch=AudioManager.TunedPitch(altarAmbienceClip,1f);
            next.volume=0f;
            next.PlayScheduled(altarAmbienceNextStart);
        }
    }
    float AltarAmbienceLoopCrossfade => altarAmbienceClip
        ? Mathf.Min(5f,altarAmbienceClip.length*.25f) : .01f;
    float AltarAmbienceSegmentDuration(AudioSource source) =>
        altarAmbienceClip.length/Mathf.Max(.1f,Mathf.Abs(source.pitch));
    void EnsureAltarAmbienceSources()
    {
        for(int i=0;i<altarAmbienceSources.Length;i++)
        {
            if(altarAmbienceSources[i]) continue;
            var source=gameObject.AddComponent<AudioSource>();
            source.playOnAwake=false;
            source.loop=false;
            source.spatialBlend=0f;
            source.volume=0f;
            source.clip=altarAmbienceClip;
            altarAmbienceSources[i]=source;
        }
    }
    void StopAltarAmbience()
    {
        foreach(var source in altarAmbienceSources)
        {
            if(!source) continue;
            source.Stop();
            source.volume=0f;
        }
        altarAmbienceRunning=altarAmbienceTransitioning=false;
    }
    void ApplyChargeVisuals()
    {
        float q=shownCharge;
        float pulse=1f+(Mathf.Sin(Time.time*Mathf.Lerp(1.5f,3.5f,q))*.5f+.5f)*Mathf.Lerp(.05f,.28f,q);
        float flare=completing ? Mathf.Sin(Mathf.Clamp01(completionTime/victoryDelaySeconds)*Mathf.PI)*1.4f : 0f;
        float power=(.1f+q*.45f+Mathf.Pow(q,4f)*1.1f)*pulse+flare;
        maskIntensity=Mathf.Lerp(.7f,2.4f,q)+flare;
        foreach(var material in materials)
        {
            material.SetFloat("_Charge",q); material.SetFloat("_Power",power);
            material.SetVector("_AltarOrigin",new Vector4(AltarPosition.x,AltarPosition.y+CellSize*1.35f,CellSize,0));
        }
        if(altarLight)
        {
            altarLight.intensity=power*1.5f;
            altarLight.pointLightInnerRadius=CellSize*Mathf.Lerp(.6f,2.5f,q);
            altarLight.pointLightOuterRadius=CellSize*Mathf.Lerp(3f,12f,q);
        }
        if(sparks)
        {
            var emission=sparks.emission; emission.rateOverTime=Mathf.Lerp(.6f,23f,q*q);
            var main=sparks.main; main.startSpeed=new ParticleSystem.MinMaxCurve(.25f+q*.4f,.5f+q*1.2f);
            main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,2f+q*1.3f);
            main.startColor=new Color(1f,.25f,1.9f,Mathf.Lerp(.3f,.85f,q));
        }
        if(bloomVolume) bloomVolume.weight=Camera.main && Vector2.Distance(Camera.main.transform.position,AltarPosition)<CellSize*25f
            ? Mathf.Clamp01(Mathf.Max(q+flare,buttonGlow*.8f)) : 0f;
    }
    public void RebuildVisuals()
    {
        if(!map) map=GetComponent<MapGenerator>();
        ReleaseVisuals();
        if(!hasLayout || !chamberEnabled) return;
        var shader=Resources.Load<Shader>("UltroniumAltar/Chamber");
        var wall=Resources.Load<Texture2D>("UltroniumAltar/ChamberWall");
        var stone=Resources.Load<Texture2D>("UltroniumAltar/SanctuaryStone");
        var altar=Resources.Load<Texture2D>("UltroniumAltar/Altar");
        if(!shader || !wall || !stone || !altar) return;
        root=new GameObject("Ultronium altar chamber (generated)");
        root.hideFlags=HideFlags.DontSave;
        root.transform.SetParent(transform,false);
        var terrainRenderer=map.Terrain.GetComponent<TilemapRenderer>();
        int layer=terrainRenderer.sortingLayerID, order=terrainRenderer.sortingOrder;
        BuildBackdrop(MakeMaterial(shader,wall,0),layer,order-2);
        BuildShellMesh(MakeMaterial(shader,stone,1),layer,order+2);
        // Separate object behind the player; the altar does not block either passage.
        float width=CellSize*6.2f, height=CellSize*3.9f;
        BuildQuad("Ultronium altar",AltarPosition+Vector3.up*(height*.5f-.20f*CellSize),new Vector2(width,height),MakeMaterial(shader,altar,2),layer,order+3);
        BuildQuad("Basin glow",AltarPosition+Vector3.up*CellSize*1.58f,new Vector2(CellSize*2.5f,CellSize*1.2f),MakeMaterial(shader,Texture2D.whiteTexture,3),layer,order+4);
        BuildQuad("Rising energy",AltarPosition+Vector3.up*CellSize*2.7f,new Vector2(CellSize*1.65f,CellSize*2.4f),MakeMaterial(shader,Texture2D.whiteTexture,5),layer,order+4);
        var lightObject=new GameObject("Altar Light"); lightObject.transform.SetParent(root.transform,false);
        lightObject.transform.position=AltarPosition+Vector3.up*CellSize*1.35f;
        altarLight=lightObject.AddComponent<Light2D>(); altarLight.lightType=Light2D.LightType.Point;
        altarLight.color=new Color(.65f,.14f,1f); altarLight.shadowsEnabled=false;
        BuildSparks(layer,order+5);
        BuildLightMask();
        var volumeObject=new GameObject("Altar bloom"); volumeObject.transform.SetParent(root.transform,false);
        bloomVolume=volumeObject.AddComponent<UnityEngine.Rendering.Volume>(); bloomVolume.isGlobal=true; bloomVolume.priority=5;
        var profile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>(); owned.Add(profile);
        var bloom=profile.Add<Bloom>(true); owned.Add(bloom); bloom.intensity.Override(.45f); bloom.threshold.Override(1.1f); bloom.scatter.Override(.65f);
        bloomVolume.sharedProfile=profile;
        if(Application.isPlaying) BuildButton();
        shownCharge=transitionFrom=transitionTarget=Charge; transitionTime=0; ApplyChargeVisuals();
    }
    Material MakeMaterial(Shader shader,Texture texture,int mode)
    {
        var material=new Material(shader){hideFlags=HideFlags.DontSave};
        material.mainTexture=texture; material.SetFloat("_Mode",mode);
        owned.Add(material); materials.Add(material); return material;
    }
    void BuildBackdrop(Material material,int layer,int order)
    {
        var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
        float width=UltroniumChamberLayout.HalfWidth*2+1;
        foreach(var cell in Layout.Bounds.allPositionsWithin)
        {
            if(!Layout.IsOpen(cell)) continue;
            Vector3 a=map.Terrain.CellToWorld(cell), b=map.Terrain.CellToWorld(cell+new Vector3Int(1,1,0));
            float x=(cell.x-chamberOrigin.x+UltroniumChamberLayout.HalfWidth)/width;
            float y=(cell.y-chamberOrigin.y)/(float)UltroniumChamberLayout.Height;
            float dx=1f/width, dy=1f/UltroniumChamberLayout.Height;
            int n=vertices.Count;
            vertices.Add(root.transform.InverseTransformPoint(a)); vertices.Add(root.transform.InverseTransformPoint(new Vector3(b.x,a.y,a.z)));
            vertices.Add(root.transform.InverseTransformPoint(b)); vertices.Add(root.transform.InverseTransformPoint(new Vector3(a.x,b.y,a.z)));
            uv.Add(new Vector2(x,y)); uv.Add(new Vector2(x+dx,y));
            uv.Add(new Vector2(x+dx,y+dy)); uv.Add(new Vector2(x,y+dy));
            triangles.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
        }
        MakeMesh("Chamber backdrop",vertices.ToArray(),uv.ToArray(),triangles.ToArray(),material,layer,order);
    }
    void BuildShellMesh(Material material,int layer,int order)
    {
        var vertices=new List<Vector3>(); var uv=new List<Vector2>(); var triangles=new List<int>();
        foreach(var cell in Layout.Bounds.allPositionsWithin)
        {
            if(!Layout.IsShell(cell)) continue;
            Vector3 a=map.Terrain.CellToWorld(cell), b=map.Terrain.CellToWorld(cell+new Vector3Int(1,1,0));
            float x=cell.x-chamberOrigin.x, y=cell.y-chamberOrigin.y;
            Vector2 u0=new Vector2(x/3f,y/3f);
            Vector2 u1=u0+Vector2.one/3f;
            int n=vertices.Count;
            vertices.Add(root.transform.InverseTransformPoint(a)); vertices.Add(root.transform.InverseTransformPoint(new Vector3(b.x,a.y,a.z)));
            vertices.Add(root.transform.InverseTransformPoint(b)); vertices.Add(root.transform.InverseTransformPoint(new Vector3(a.x,b.y,a.z)));
            uv.Add(u0); uv.Add(new Vector2(u1.x,u0.y)); uv.Add(u1); uv.Add(new Vector2(u0.x,u1.y));
            triangles.AddRange(new[]{n,n+2,n+1,n,n+3,n+2});
        }
        MakeMesh("Indestructible shell",vertices.ToArray(),uv.ToArray(),triangles.ToArray(),material,layer,order);
    }
    void BuildQuad(string name,Vector3 center,Vector2 size,Material material,int layer,int order)
    {
        Vector3 a=center-new Vector3(size.x,size.y,0)*.5f,b=center+new Vector3(size.x,size.y,0)*.5f;
        var vertices=new[]{a,new Vector3(b.x,a.y,a.z),b,new Vector3(a.x,b.y,a.z)};
        for(int i=0;i<4;i++) vertices[i]=root.transform.InverseTransformPoint(vertices[i]);
        MakeMesh(name,vertices,new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},new[]{0,2,1,0,3,2},material,layer,order);
    }
    void MakeMesh(string name,Vector3[] vertices,Vector2[] uv,int[] triangles,Material material,int layer,int order)
    {
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(root.transform,false);
        var mesh=new Mesh{name=name,hideFlags=HideFlags.DontSave,vertices=vertices,uv=uv,triangles=triangles}; mesh.RecalculateBounds();
        owned.Add(mesh); go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.sortingLayerID=layer; renderer.sortingOrder=order;
    }
    void BuildSparks(int layer,int order)
    {
        var go=new GameObject("Altar sparks"); go.transform.SetParent(root.transform,false);
        go.transform.position=AltarPosition+Vector3.up*CellSize*1.6f;
        sparks=go.AddComponent<ParticleSystem>(); sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=sparks.main; main.loop=true; main.playOnAwake=false; main.maxParticles=160;
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.startSize=new ParticleSystem.MinMaxCurve(.025f*CellSize,.075f*CellSize);
        var shape=sparks.shape; shape.shapeType=ParticleSystemShapeType.Box; shape.scale=new Vector3(CellSize*.7f,.05f,.01f);
        var velocity=sparks.velocityOverLifetime; velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.World;
        velocity.x=new ParticleSystem.MinMaxCurve(-.12f,.12f); velocity.y=new ParticleSystem.MinMaxCurve(.3f,.7f); velocity.z=new ParticleSystem.MinMaxCurve(0f,0f);
        var color=sparks.colorOverLifetime; color.enabled=true;
        var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.6f,.1f,1f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(0,1)}); color.color=gradient;
        var renderer=sparks.GetComponent<ParticleSystemRenderer>(); renderer.sortingLayerID=layer; renderer.sortingOrder=order;
        renderer.sharedMaterial=MakeMaterial(Resources.Load<Shader>("UltroniumAltar/Chamber"),Texture2D.whiteTexture,4);
        if(Application.isPlaying) sparks.Play();
    }
    void BuildButton()
    {
        var go=new GameObject("Altar interaction",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(GraphicRaycaster));
        go.transform.SetParent(root.transform,false);
        go.transform.position=AltarPosition+Vector3.up*CellSize*4.4f; go.transform.localScale=Vector3.one*(CellSize/110f);
        var canvas=go.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=Camera.main;
        canvas.sortingLayerName="UI"; canvas.sortingOrder=200;
        var rect=(RectTransform)go.transform; rect.sizeDelta=new Vector2(280,140);
        buttonGroup=go.GetComponent<CanvasGroup>(); buttonGroup.alpha=0;
        var button=new GameObject("Deposit",typeof(RectTransform),typeof(Image),typeof(Button)); button.transform.SetParent(go.transform,false);
        var br=(RectTransform)button.transform; br.sizeDelta=new Vector2(250,82); br.anchoredPosition=new Vector2(0,-20);
        depositButtonRect=br;
        depositButtonBasePosition=br.anchoredPosition;
        depositImage=button.GetComponent<Image>();
        depositImage.sprite=LoadLargestSprite("UltroniumAltar/DepositButtonFrame");
        depositImage.type=Image.Type.Simple;
        depositImage.preserveAspect=true;
        depositImage.color=Color.white;
        depositOutline=button.AddComponent<Outline>();
        depositOutline.effectColor=new Color(.68f,.28f,1f,.68f);
        depositOutline.effectDistance=new Vector2(1.5f,-1.5f);
        var active=new GameObject("Charged frame",typeof(RectTransform),typeof(Image));
        active.transform.SetParent(button.transform,false);
        var activeRect=(RectTransform)active.transform;
        activeRect.anchorMin=Vector2.zero; activeRect.anchorMax=Vector2.one;
        activeRect.offsetMin=Vector2.zero; activeRect.offsetMax=Vector2.zero;
        depositActiveImage=active.GetComponent<Image>();
        depositActiveImage.sprite=LoadLargestSprite("UltroniumAltar/DepositButtonFrameActive");
        depositActiveImage.type=Image.Type.Simple;
        depositActiveImage.preserveAspect=true;
        depositActiveImage.raycastTarget=false;
        depositActiveImage.color=new Color(1f,1f,1f,0f);
        var glowShader=Resources.Load<Shader>("UltroniumAltar/DepositButtonGlow");
        if(glowShader)
        {
            depositGlowMaterial=new Material(glowShader){hideFlags=HideFlags.DontSave};
            depositActiveImage.material=depositGlowMaterial;
            owned.Add(depositGlowMaterial);
        }
        depositButton=button.GetComponent<Button>();
        depositButton.targetGraphic=depositImage;
        depositButton.transition=Selectable.Transition.ColorTint;
        depositButton.colors=new ColorBlock
        {
            normalColor=Color.white,
            highlightedColor=new Color(1f,.88f,1f,1f),
            pressedColor=new Color(.78f,.58f,.94f,1f),
            selectedColor=Color.white,
            disabledColor=new Color(.58f,.52f,.66f,.82f),
            colorMultiplier=1f,
            fadeDuration=.12f
        };
        depositButton.onClick.AddListener(()=>TryDeposit());
        buttonText=Label(button.transform,"Label",new Vector2(190,46),Vector2.zero,18);
        buttonText.fontStyle=FontStyles.Bold;
        buttonText.outlineWidth=.14f;
        buttonText.outlineColor=new Color(.18f,.055f,.3f,1f);
        progressText=Label(go.transform,"Charge",new Vector2(200,28),new Vector2(0,55),19);
        progressTextRect=progressText.rectTransform;
        progressTextBasePosition=progressTextRect.anchoredPosition;
        progressText.fontStyle=FontStyles.Bold;
        progressText.outlineWidth=.12f;
        progressText.outlineColor=new Color(.12f,.035f,.2f,1f);
        BuildButtonEffects();
    }
    static Sprite LoadLargestSprite(string path)
    {
        Sprite result=null;
        foreach(var sprite in Resources.LoadAll<Sprite>(path))
            if(sprite && (!result || sprite.rect.width*sprite.rect.height>result.rect.width*result.rect.height))
                result=sprite;
        return result;
    }
    void BuildButtonEffects()
    {
        var lightObject=new GameObject("Deposit button light");
        lightObject.transform.SetParent(root.transform,false);
        buttonLight=lightObject.AddComponent<Light2D>();
        buttonLight.lightType=Light2D.LightType.Point;
        buttonLight.color=new Color(.95f,.16f,1f);
        buttonLight.intensity=0f;
        buttonLight.shadowsEnabled=false;
        buttonLight.pointLightInnerRadius=CellSize*.4f;
        buttonLight.pointLightOuterRadius=CellSize*2.7f;

        var motesObject=new GameObject("Deposit button motes");
        motesObject.transform.SetParent(root.transform,false);
        buttonMotes=motesObject.AddComponent<ParticleSystem>();
        buttonMotes.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=buttonMotes.main;
        main.loop=true; main.playOnAwake=false; main.maxParticles=48;
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.startLifetime=new ParticleSystem.MinMaxCurve(.7f,1.45f);
        main.startSpeed=0f;
        main.startSize=new ParticleSystem.MinMaxCurve(.025f*CellSize,.07f*CellSize);
        main.startColor=Color.white;
        var emission=buttonMotes.emission; emission.rateOverTime=0f;
        var shape=buttonMotes.shape; shape.shapeType=ParticleSystemShapeType.Box;
        shape.scale=new Vector3(CellSize*2.35f,CellSize*.48f,.01f);
        var velocity=buttonMotes.velocityOverLifetime;
        velocity.enabled=true; velocity.space=ParticleSystemSimulationSpace.World;
        velocity.x=new ParticleSystem.MinMaxCurve(-.1f*CellSize,.1f*CellSize);
        velocity.y=new ParticleSystem.MinMaxCurve(.1f*CellSize,.34f*CellSize);
        var color=buttonMotes.colorOverLifetime; color.enabled=true;
        var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(Color.white,0f),
            new GradientColorKey(new Color(1f,.42f,1f),1f)},
            new[]{new GradientAlphaKey(0f,0f),new GradientAlphaKey(.9f,.25f),
                new GradientAlphaKey(.7f,.7f),new GradientAlphaKey(0f,1f)});
        color.color=gradient;
        var renderer=buttonMotes.GetComponent<ParticleSystemRenderer>();
        renderer.sortingLayerName="UI"; renderer.sortingOrder=201;
        renderer.sharedMaterial=MakeMaterial(Resources.Load<Shader>("UltroniumAltar/Chamber"),Texture2D.whiteTexture,4);
        buttonMotes.Play();
    }
    void UpdateButtonEffects()
    {
        if(!depositButtonRect) return;
        float shimmer=1f+.08f*Mathf.Sin(Time.unscaledTime*3.1f);
        if(depositActiveImage) depositActiveImage.color=new Color(1f,1f,1f,buttonGlow);
        if(depositGlowMaterial) depositGlowMaterial.SetFloat("_EmissionStrength",buttonGlow*2.2f*shimmer);
        if(buttonText) buttonText.color=Color.Lerp(new Color(1f,.85f,.57f),new Color(1f,.96f,.78f),buttonGlow);
        if(depositOutline) depositOutline.effectColor=Color.Lerp(depositOutline.effectColor,
            new Color(1f,.3f,1f,.96f),buttonGlow);
        Vector3 position=depositButtonRect.TransformPoint(Vector3.zero);
        if(buttonLight)
        {
            buttonLight.transform.position=position;
            buttonLight.intensity=buttonGlow*1.15f*shimmer;
        }
        if(buttonMotes)
        {
            buttonMotes.transform.position=position;
            var emission=buttonMotes.emission;
            emission.rateOverTime=buttonGlow*6f;
        }
    }
    void UpdateDepositButtonAnimation()
    {
        if(!depositButtonRect || !progressTextRect || !depositOutline) return;
        if(!map)
        {
            depositButtonRect.localScale=Vector3.one;
            depositButtonRect.anchoredPosition=depositButtonBasePosition;
            progressTextRect.anchoredPosition=progressTextBasePosition;
            depositOutline.effectColor=new Color(.68f,.28f,1f,.68f);
            return;
        }
        float pulseWave=depositButton && depositButton.interactable && map.altarButtonPulseFrequency>0f
            ? Mathf.Sin(Time.unscaledTime*Mathf.PI*2f*map.altarButtonPulseFrequency) : 0f;
        float pulseAmplitude=Mathf.Clamp(map.altarButtonPulseAmplitude,0f,.15f);
        depositButtonRect.localScale=Vector3.one*(1f+pulseWave*pulseAmplitude);
        float floatWave=map.altarButtonFloatFrequency>0f
            ? Mathf.Sin(Time.unscaledTime*Mathf.PI*2f*map.altarButtonFloatFrequency) : 0f;
        Vector2 position=depositButtonBasePosition;
        float floatOffset=floatWave*Mathf.Clamp(map.altarButtonFloatAmplitude,0f,.2f)*110f;
        position.y+=floatOffset;
        depositButtonRect.anchoredPosition=position;
        position=progressTextBasePosition;
        position.y+=floatOffset;
        progressTextRect.anchoredPosition=position;
        float glow=Mathf.Lerp(.62f,.92f,Mathf.InverseLerp(-1f,1f,pulseWave)*pulseAmplitude/.15f);
        depositOutline.effectColor=new Color(.68f,.28f,1f,glow);
    }
    static TextMeshProUGUI Label(Transform parent,string name,Vector2 size,Vector2 position,float fontSize)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); go.transform.SetParent(parent,false);
        var text=go.GetComponent<TextMeshProUGUI>(); text.rectTransform.sizeDelta=size; text.rectTransform.anchoredPosition=position;
        text.font=TMP_Settings.defaultFontAsset; text.fontSize=fontSize; text.alignment=TextAlignmentOptions.Center;
        text.color=new Color(1f,.85f,.57f); text.raycastTarget=false; return text;
    }
    void ReleaseVisuals()
    {
        if(root) { root.SetActive(false); Dispose(root); }
        root=null; materials.Clear();
        foreach(var obj in owned) if(obj) Dispose(obj);
        owned.Clear();
        buttonGroup=null; depositButton=null; depositImage=null; depositActiveImage=null;
        depositGlowMaterial=null; depositOutline=null; depositButtonRect=null; progressTextRect=null;
        buttonMotes=null; buttonLight=null; buttonText=null; progressText=null; buttonGlow=0f;
        ChamberLightMask=null; ChamberLightRect=Vector4.zero;
    }
    void BuildLightMask()
    {
        var b=Layout.Bounds; b.xMin-=2; b.xMax+=2; b.yMin-=2; b.yMax+=2;
        var texture=new Texture2D(b.size.x,b.size.y,TextureFormat.RGBA32,false,true){name="Altar visibility and shell",hideFlags=HideFlags.DontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
        var pixels=new Color[b.size.x*b.size.y];
        foreach(var cell in b.allPositionsWithin)
        {
            bool shell=Layout.IsShell(cell);
            pixels[(cell.y-b.yMin)*b.size.x+cell.x-b.xMin]=new Color(Layout.IsOpen(cell)||shell?1f:0f,shell?1f:0f,0f,1f);
        }
        texture.SetPixels(pixels);texture.Apply();owned.Add(texture);ChamberLightMask=texture;
        var min=map.Terrain.CellToWorld(b.min); var max=map.Terrain.CellToWorld(b.max);
        ChamberLightRect=new Vector4(min.x,min.y,1f/(max.x-min.x),1f/(max.y-min.y));
    }
    static void Dispose(UnityEngine.Object obj) { if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
}
