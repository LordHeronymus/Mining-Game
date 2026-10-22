using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using Object=UnityEngine.Object;

// CI: local-only
// Preview of the ladder variants in Design/Ladder/Sources: digs a shaft below the grass in an isolated run,
// fills it with each variant (runtime tiles, no assets touched) and captures Temp/LadderPreview/*.png.
public static class LadderVariantPreview
{
    public static string Main()
    {
        if(!Application.isPlaying || !MainMenuController.IsVisible || RunNavigation.IsTransitioning)
            throw new Exception("Requires a fresh Play start at the homescreen");
        if(GameSaveSystem.TestDirectory!=null || MetaProgression.TestDirectory!=null)
            throw new Exception("Another test owns the save scope");
        var root=new GameObject("Ladder variant preview");Object.DontDestroyOnLoad(root);
        root.AddComponent<LadderVariantPreviewProbe>();return "Started; report Temp/LadderVariantPreview.txt";
    }
}

public sealed class LadderVariantPreviewProbe:MonoBehaviour
{
    const string Report="Temp/LadderVariantPreview.txt";
    const string Output="Temp/LadderPreview";
    static readonly string[] Variants={"A_Grubenleiter","B_Messing","C_Seilleiter","D_Schutzleisten"};
    string previousSave,previousMeta,directory;bool restored,previousBackground;

    void Log(string line)=>File.AppendAllText(Report,line+"\n");
    IEnumerator Start()
    {
        previousSave=GameSaveSystem.TestDirectory;previousMeta=MetaProgression.TestDirectory;
        previousBackground=Application.runInBackground;Application.runInBackground=true;
        directory=Path.GetFullPath("Temp/LadderPreviewRun-"+DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory);GameSaveSystem.TestDirectory=directory;
        MetaProgression.TestDirectory=Path.Combine(directory,"Profile");
        File.WriteAllText(Report,"Isolated save directory: "+directory+"\n");
        var run=Run();
        while(true)
        {
            bool more;
            try{more=run.MoveNext();}
            catch(Exception e){Log("FAIL "+e);more=false;}
            if(!more)break;
            yield return run.Current;
        }
        RunNavigation.MainMenu();
        while(RunNavigation.IsTransitioning)yield return null;
        Log("DONE");Cleanup();
    }

    IEnumerator Run()
    {
        RunNavigation.NewGame("Leiter-Vorschau");
        float deadline=Time.realtimeSinceStartup+150;
        while(RunNavigation.IsTransitioning || LoadingProgress.Active)
        {if(Time.realtimeSinceStartup>deadline)throw new Exception("Loading timeout");yield return null;}
        yield return new WaitForSeconds(1f);
        var map=Object.FindFirstObjectByType<MapGenerator>();
        var ladders=Object.FindFirstObjectByType<LadderMap>();
        var player=Object.FindFirstObjectByType<PlayerMovement>();
        var follow=Camera.main?Camera.main.GetComponent<CameraFollow>():null;
        Log("Camera follow active: "+(follow && follow.enabled && follow.target==player.transform &&
            Camera.main.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled));
        var miner=Object.FindFirstObjectByType<TileMiner>();
        var hit=typeof(TileMiner).GetMethod("ApplyMiningHit",BindingFlags.NonPublic|BindingFlags.Instance);
        int x=map.Terrain.WorldToCell(player.transform.position).x+9;
        int top=40;while(top>-40 && !map.Terrain.HasTile(new Vector3Int(x,top,0)))top--;
        Log("Shaft column "+x+", surface row "+top);
        const int depth=9;
        for(int y=top;y>top-depth;y--)
        {
            var cell=new Vector3Int(x,y,0);int hits=0;
            while(map.Terrain.HasTile(cell) && hits++<200)hit.Invoke(miner,new object[]{cell,.5f});
            if(map.Terrain.HasTile(cell)){map.Terrain.SetTile(cell,null);map.OreOverlay.SetTile(cell,null);Log("Cleared directly "+cell);}
        }
        // Let the mining dust and rubble settle before capturing.
        yield return new WaitForSeconds(5f);
        var cellSize=map.Terrain.layoutGrid.cellSize.y;
        var centre=map.Terrain.GetCellCenterWorld(new Vector3Int(x,top,0))+Vector3.down*cellSize*1.6f;
        Directory.CreateDirectory(Output);
        Fill(ladders,x,top,depth,ladders.segment);yield return null;
        Capture(map,centre,"Heute");
        foreach(var name in Variants)
        {
            var tile=Load(ladders.segment,"Design/Ladder/Sources/LadderSegment_"+name+".png");
            Fill(ladders,x,top,depth,tile);yield return null;
            Capture(map,centre,name);
        }
        Fill(ladders,x,top,depth,null);
    }

    static Tile Load(Tile template,string path)
    {
        var tex=new Texture2D(2,2,TextureFormat.RGBA32,true);
        tex.LoadImage(File.ReadAllBytes(path));
        tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Trilinear;tex.anisoLevel=template.sprite.texture.anisoLevel;
        tex.Apply(true);
        var tile=Object.Instantiate(template);
        // Same pixels per unit and centre pivot as the current LadderSegment sprite.
        tile.sprite=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),template.sprite.pixelsPerUnit,0,SpriteMeshType.FullRect);
        return tile;
    }

    static void Fill(LadderMap ladders,int x,int top,int depth,TileBase tile)
    {
        var tiles=ladders.EnsureTiles();
        for(int y=top;y>top-depth;y--)tiles.SetTile(new Vector3Int(x,y,0),tile);
    }

    void Capture(MapGenerator map,Vector3 centre,string name)
    {
        const int width=760,height=1300;
        var view=new GameObject("Ladder preview camera",typeof(Camera)).GetComponent<Camera>();
        view.CopyFrom(Camera.main);view.enabled=false;view.orthographic=true;
        view.orthographicSize=map.Terrain.layoutGrid.cellSize.y*3.6f;
        view.transform.position=new Vector3(centre.x,centre.y,-10);
        var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(width,height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
        try
        {
            view.targetTexture=target;
            map.GetComponent<OreOverlayAppearance>()?.PrepareVeins(view);
            view.Render();
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();
            File.WriteAllBytes(Output+"/"+name+".png",pixels.EncodeToPNG());
            Log("Captured "+name);
        }
        finally
        {
            view.targetTexture=null;RenderTexture.active=previous;
            Object.Destroy(view.gameObject);Object.Destroy(target);Object.Destroy(pixels);
        }
    }

    void Cleanup()
    {
        if(restored)return;restored=true;
        if(GameSaveSystem.TestDirectory==directory)GameSaveSystem.TestDirectory=previousSave;
        if(directory!=null && MetaProgression.TestDirectory==Path.Combine(directory,"Profile"))MetaProgression.TestDirectory=previousMeta;
        Application.runInBackground=previousBackground;Object.Destroy(gameObject);
    }
    void OnDestroy(){if(!restored)Cleanup();}
}
