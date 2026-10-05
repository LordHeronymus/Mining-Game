using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class CopperOrganicRuntimeChecks
{
    public static string Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || RunNavigation.IsTransitioning)
            throw new Exception("A free editor at the homescreen in Play Mode is required");
        if (GameSaveSystem.TestDirectory != null || MetaProgression.TestDirectory != null)
            throw new Exception("Another test owns the save/profile scope");
        var root = new GameObject("Ore vein runtime checks");
        Object.DontDestroyOnLoad(root); root.AddComponent<CopperOrganicRuntimeProbe>();
        return "Started; report Temp/CopperOrganicRuntimeChecks.txt";
    }
}

public sealed class CopperOrganicRuntimeProbe : MonoBehaviour
{
    const string Report = "Temp/CopperOrganicRuntimeChecks.txt";
    string previousSave, previousMeta, directory;
    bool restored, previousBackground;
    Vector3Int naturalCell;
    OreTile naturalTile;
    int naturalLinks;
    string naturalPlanJson;
    Vector3Int minedNaturalCell;
    void Check(bool value,string message)
    { if(!value)throw new Exception(message); File.AppendAllText(Report,"PASS "+message+"\n"); }

    IEnumerator Start()
    {
        previousSave=GameSaveSystem.TestDirectory; previousMeta=MetaProgression.TestDirectory;
        previousBackground=Application.runInBackground;Application.runInBackground=true;
        directory=Path.GetFullPath("Temp/CopperOrganicRun-"+DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory);GameSaveSystem.TestDirectory=directory;
        MetaProgression.TestDirectory=Path.Combine(directory,"Profile");
        File.WriteAllText(Report,"Isolated save directory: "+directory+"\n");
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0)
        {
            if(GameSaveSystem.TestDirectory!=directory)
            { File.AppendAllText(Report,"INTERRUPTED: save scope changed\n");Cleanup();yield break; }
            bool more=false;object current=null;Exception error=null;
            try { more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current; }
            catch(Exception e){error=e;}
            if(error!=null)
            {
                File.AppendAllText(Report,"FAIL "+error);
                // Keep the isolated paths until the failed test world is gone.
                RunNavigation.MainMenu();
                float deadline=Time.realtimeSinceStartup+30;
                while(RunNavigation.IsTransitioning && Time.realtimeSinceStartup<deadline)yield return null;
                if(!MainMenuController.IsVisible)
                {
                    File.AppendAllText(Report,"\nMenu cleanup timed out; isolated paths retained until Play stops\n");
                    yield break;
                }
                Cleanup();yield break;
            }
            if(!more){stack.Pop();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        File.AppendAllText(Report,"ALL PASSED\n");Cleanup();
    }

    IEnumerator Ready()
    {
        float deadline=Time.realtimeSinceStartup+150;
        while(RunNavigation.IsTransitioning || LoadingProgress.Active)
        { if(Time.realtimeSinceStartup>deadline)throw new Exception("Loading timed out");yield return null; }
        yield return null;
    }

    void CameraCheck(string phase)
    {
        var player=Object.FindFirstObjectByType<PlayerMovement>();var camera=Camera.main;
        Check(camera && player && camera.GetComponent<CameraFollow>().enabled &&
            camera.GetComponent<CameraFollow>().target==player.transform &&
            camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,
            phase+": player follow and world clamp active");
    }

    IEnumerator Run()
    {
        RunNavigation.NewGame("Erzadern-Test");yield return Ready();
        Check(GameSaveSystem.ActiveSlot>0 && GameSaveSystem.InitialSaveError==null,"Fresh isolated run created and saved");
        CameraCheck("Fresh start");
        var map=Object.FindFirstObjectByType<MapGenerator>();
        var visual=map.GetComponent<OreOverlayAppearance>();var camera=Camera.main;
        Check(visual && visual.isActiveAndEnabled,"Ore appearance is active after fresh generation");
        visual.PrepareVeins(camera);
        var props=new MaterialPropertyBlock();map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        Check(props.GetFloat("_VeinEnabled")==1 && props.GetTexture("_VeinCells"),"Real camera binds connected vein lookup");
        var origin=map.Terrain.WorldToCell(Object.FindFirstObjectByType<PlayerMovement>().transform.position);
        yield return CaptureNatural(map,origin.x,1); yield return CaptureNatural(map,origin.x,2);
        var cell=new Vector3Int(origin.x,-6,0);
        var ore=map.registry.GetById(BlockType.CopperOre).richOre[0];
        map.Terrain.SetTile(cell,map.surfaceDirtTile);
        map.Terrain.SetTile(cell+Vector3Int.right,map.surfaceDirtTile);
        foreach(var isolated in new[]{cell+Vector3Int.left,cell+Vector3Int.up,cell+Vector3Int.down,
            cell+Vector3Int.right*2,cell+Vector3Int.right+Vector3Int.up,cell+Vector3Int.right+Vector3Int.down})
            map.OreOverlay.SetTile(isolated,null);
        map.OreOverlay.SetTile(cell,ore);map.OreOverlay.SetTile(cell+Vector3Int.right,ore);
        yield return null;
        map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        visual.PrepareVeins(camera);
        map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        Check(props.GetFloat("_CopperCarpetEnabled")==1 && props.GetTexture("_CopperCarpet"),"Real run binds continuous copper carpet with live organic boundaries with protected cores");
        var hostBounds=props.GetVector("_TestBounds");var host=(Texture2D)props.GetTexture("_TestOccupancy");
        Check(host.GetPixel(cell.x-(int)hostBounds.x,cell.y-(int)hostBounds.y).g>.9f,"Runtime copper fixture uses the actual dirt material");
        using(var field=new OreVeinField())
        {
            Check(field.Prepare(map,camera) && field.TypeAt(cell)==(int)BlockType.CopperOre,"Live field detects added ore pair");
            Check((field.CopperLinksAt(cell)&1)!=0 && (field.CopperLinksAt(cell+Vector3Int.right)&4)!=0,"Actual dirt copper pair has reciprocal visual branches");
            int before=field.Revision;
            for(int n=0;n<120;n++)field.Prepare(map,camera);
            Check(field.Revision==before,"120 unchanged frames reuse metadata without rebuilding");
        }
        var miner=Object.FindFirstObjectByType<TileMiner>();
        var hit=typeof(TileMiner).GetMethod("ApplyMiningHit",BindingFlags.Instance|BindingFlags.NonPublic);
        int count=0;
        while(map.Terrain.HasTile(cell) && count++<100)hit.Invoke(miner,new object[]{cell,.5f});
        Check(!map.Terrain.HasTile(cell) && !map.OreOverlay.HasTile(cell),"Actual pickaxe hits remove terrain and ore");
        yield return null;visual.PrepareVeins(camera);
        map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        var bounds=props.GetVector("_VeinBounds");var texture=(Texture2D)props.GetTexture("_VeinCells");
        Check(texture.GetPixel(cell.x-(int)bounds.x,cell.y-(int)bounds.y).r==0,"Mined cell clears GPU connection field");
        Check(map.GetOreAt(cell+Vector3Int.right)==ore,"Neighbour ore and richness remain intact");
        var neighbour=(Color32)texture.GetPixel(cell.x+1-(int)bounds.x,cell.y-(int)bounds.y);
        Check((neighbour.b&4)==0,"Real mining removes the neighbour's branch into empty space");
        CameraCheck("After mining");
        yield return Capture(map,origin.x);
        var naturalPlan=visual.CopperPlans.Get(map,naturalCell);
        naturalPlanJson=JsonUtility.ToJson(naturalPlan);
        minedNaturalCell=naturalPlan.cells[0]==naturalCell?naturalPlan.cells[1]:naturalPlan.cells[0];
        Check(map.RemoveBlock(minedNaturalCell),"Mine one actual natural copper cell before saving");
        Check(JsonUtility.ToJson(visual.CopperPlans.Get(map,naturalCell))==naturalPlanJson,"Legacy plan data stays compatible after mining");
        var naturalLookupCamera=new GameObject("Natural copper post-mining lookup",typeof(Camera)).GetComponent<Camera>();
        naturalLookupCamera.enabled=false;naturalLookupCamera.orthographic=true;naturalLookupCamera.orthographicSize=4;
        naturalLookupCamera.transform.position=map.Terrain.GetCellCenterWorld(naturalCell)+Vector3.back*10;
        using(var field=new OreVeinField()){field.Prepare(map,naturalLookupCamera);naturalLinks=field.CopperLinksAt(naturalCell);}
        Object.Destroy(naturalLookupCamera.gameObject);
        int slot=GameSaveSystem.ActiveSlot;
        bool saved=false;string saveError=null;
        yield return GameSaveSystem.Save(slot,this,(ok,error)=>{saved=ok;saveError=error;});
        Check(saved,"Save mined world with compatible legacy visual data: "+saveError);
        RunNavigation.MainMenu();yield return Ready();
        Check(RunNavigation.LoadGame(slot,out string loadError),"Load isolated mined save: "+loadError);yield return Ready();
        map=Object.FindFirstObjectByType<MapGenerator>();visual=map.GetComponent<OreOverlayAppearance>();
        visual.PrepareVeins(Camera.main);map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(props);
        Check(props.GetFloat("_VeinEnabled")==1 && props.GetTexture("_VeinCells"),"Save restore rebinds the live vein lookup");
        Check(props.GetFloat("_CopperCarpetEnabled")==1 && props.GetTexture("_CopperCarpet"),"Copper art is rebound after loading the original save");
        Check(map.OreOverlay.GetTile<OreTile>(naturalCell)==naturalTile,"Naturally generated first-layer copper and richness survive save/load");
        Check(!map.Terrain.HasTile(minedNaturalCell) && !map.OreOverlay.HasTile(minedNaturalCell),"Mined natural copper stays absent after loading");
        Check(JsonUtility.ToJson(visual.CopperPlans.Get(map,naturalCell))==naturalPlanJson,"Save/load preserves legacy data without controlling the live border");
        var restoredView=new GameObject("Restored natural copper lookup",typeof(Camera)).GetComponent<Camera>();restoredView.enabled=false;
        restoredView.orthographic=true;restoredView.orthographicSize=4;
        restoredView.transform.position=map.Terrain.GetCellCenterWorld(naturalCell)+Vector3.back*10;
        try
        {using(var field=new OreVeinField()){field.Prepare(map,restoredView);Check(field.CopperLinksAt(naturalCell)==naturalLinks,"Save/load reconstructs the same natural copper branches");}}
        finally {Object.Destroy(restoredView.gameObject);}
        CameraCheck("After restore");
        RunNavigation.MainMenu();yield return Ready();
        Check(MainMenuController.IsVisible,"Returned to homescreen");
    }

    IEnumerator CaptureNatural(MapGenerator map,int playerX,int layer)
    {
        map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        var properties=new MaterialPropertyBlock();map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(properties);
        var host=(Texture2D)properties.GetTexture("_TestOccupancy");var bounds=properties.GetVector("_TestBounds");
        var scan=new BoundsInt(Mathf.Max((int)bounds.x,playerX-64),Mathf.Max((int)bounds.y,-250),0,
            Mathf.Min((int)bounds.x+(int)bounds.z,playerX+65)-Mathf.Max((int)bounds.x,playerX-64),
            -3-Mathf.Max((int)bounds.y,-250),1);
        var ores=map.OreOverlay.GetTilesBlock(scan);
        bool found=false;
        for(int n=ores.Length-1;n>=0;n--)
        {
            var ore=ores[n] as OreTile;if(!ore || !ore.block || ore.block.id!=BlockType.CopperOre)continue;
            var pos=new Vector3Int(scan.xMin+n%scan.size.x,scan.yMin+n/scan.size.x,0);
            var material=host.GetPixel(pos.x-(int)bounds.x,pos.y-(int)bounds.y);
            if(layer==1 ? material.g<.9f : material.b<.9f || material.g>.1f)continue;
            bool interior=layer==1 || pos.y < -20;
            for(int x=-5;x<=5 && interior && layer==2;x++)for(int y=-5;y<=5;y++)
            {
                int hx=pos.x+x-(int)bounds.x,hy=pos.y+y-(int)bounds.y;
                if(hx<0 || hy<0 || hx>=host.width || hy>=host.height || host.GetPixel(hx,hy).b<.9f)
                {interior=false;break;}
            }
            if(!interior)continue;
            naturalCell=pos;naturalTile=ore;found=true;break;
        }
        if(!found && layer==1){File.AppendAllText(Report,"No natural copper in the thin surface Dirt layer; covered by the rendering fixture.\n");yield break;}
        Check(found,"Found actual generated copper in L"+layer+", without inserting ore or changing terrain");
        var deposit=new List<Vector3Int>{naturalCell};var visited=new HashSet<Vector3Int>{naturalCell};
        var directions=new[]{Vector3Int.right,Vector3Int.up,Vector3Int.left,Vector3Int.down};
        for(int n=0;n<deposit.Count && deposit.Count<4096;n++)foreach(var d in directions)
        {
            var pos=deposit[n]+d;if(!visited.Add(pos))continue;
            var ore=map.OreOverlay.GetTile<OreTile>(pos);
            if(ore && ore.block && ore.block.id==BlockType.CopperOre && map.Terrain.HasTile(pos))deposit.Add(pos);
        }
        int minX=naturalCell.x,maxX=minX,minY=naturalCell.y,maxY=minY;
        foreach(var pos in deposit){minX=Mathf.Min(minX,pos.x);maxX=Mathf.Max(maxX,pos.x);minY=Mathf.Min(minY,pos.y);maxY=Mathf.Max(maxY,pos.y);}
        var view=new GameObject("Natural first-layer copper verification",typeof(Camera)).GetComponent<Camera>();
        view.CopyFrom(Camera.main);view.enabled=false;view.orthographic=true;
        float cellSize=map.Terrain.layoutGrid.cellSize.x;
        view.orthographicSize=Mathf.Max((maxY-minY+1)*cellSize*.5f+1.1f,((maxX-minX+1)*cellSize*.5f+1.1f)/(900f/1100f));
        view.transform.position=(map.Terrain.GetCellCenterWorld(new Vector3Int(minX,minY,0))+
            map.Terrain.GetCellCenterWorld(new Vector3Int(maxX,maxY,0)))*.5f+Vector3.back*10;
        using(var field=new OreVeinField())
        {field.Prepare(map,view);naturalLinks=field.CopperLinksAt(naturalCell);
            Check(field.TypeAt(naturalCell)==(int)BlockType.CopperOre,"Natural deposit uses the copper type in its GPU metadata");}
        var target=new RenderTexture(900,1100,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(900,1100,TextureFormat.RGBA32,false);view.targetTexture=target;
        var previous=RenderTexture.active;
        var darkness=map.transform.Find("Daylight Overlay")?.GetComponent<MeshRenderer>();bool darknessEnabled=darkness && darkness.enabled;
        var globals=new List<Light2D>();
        foreach(var existing in Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            if(existing.enabled && existing.lightType==Light2D.LightType.Global){globals.Add(existing);existing.enabled=false;}
        var light=new GameObject("Natural copper verification lamp",typeof(Light2D)).GetComponent<Light2D>();
        light.lightType=Light2D.LightType.Global;light.intensity=1;
        bool legacy=false;
        Action<ScriptableRenderContext,Camera> useLegacy=(context,camera)=>
        {
            if(camera!=view || !legacy)return;
            var p=new MaterialPropertyBlock();map.OreOverlay.GetComponent<TilemapRenderer>().GetPropertyBlock(p);
            p.SetFloat("_CopperCarpetEnabled",0);map.OreOverlay.GetComponent<TilemapRenderer>().SetPropertyBlock(p);
            if(map.GetComponent<OreOverlayAppearance>().CopperSurface)
            {p.SetFloat("_CopperSurfacePass",1);map.GetComponent<OreOverlayAppearance>().CopperSurface.SetPropertyBlock(p);}
        };
        RenderPipelineManager.beginCameraRendering+=useLegacy;
        try
        {
            if(darkness)darkness.enabled=false;
            void Render(string name)
            {
                map.GetComponent<OreOverlayAppearance>().PrepareVeins(view);view.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,900,1100),0,0);pixels.Apply();
                File.WriteAllBytes("Assets/Design/OreVeins/CopperOrganic/Implemented/"+name+".png",pixels.EncodeToPNG());
            }
            Render("Natural-L"+layer+"-copper");var current=pixels.GetPixels32();
            legacy=true;Render("Natural-L"+layer+"-before");legacy=false;
            var before=pixels.GetPixels32();int changed=0;
            for(int n=0;n<current.Length;n++)if(!current[n].Equals(before[n]))changed++;
            Check(changed>1000,"Naturally generated L"+layer+" copper visibly uses the new art: "+changed+" changed pixels");
            Check(map.OreOverlay.GetTile<OreTile>(naturalCell)==naturalTile,"Natural visual comparison preserves the actual ore tile");
            File.AppendAllText(Report,"Natural copper sample: "+naturalCell+", "+deposit.Count+" cells\n");
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering-=useLegacy;view.targetTexture=null;RenderTexture.active=previous;
            if(darkness)darkness.enabled=darknessEnabled;light.enabled=false;foreach(var existing in globals)if(existing)existing.enabled=true;
            Object.Destroy(view.gameObject);Object.Destroy(light.gameObject);Object.Destroy(target);Object.Destroy(pixels);
        }
        yield return null;
    }

    IEnumerator Capture(MapGenerator map,int playerX)
    {
        var offsets=new[]{new Vector2Int(7,10),new Vector2Int(7,9),new Vector2Int(6,8),new Vector2Int(7,8),
            new Vector2Int(7,7),new Vector2Int(8,7),new Vector2Int(8,8),new Vector2Int(6,7),
            new Vector2Int(6,6),new Vector2Int(6,5),new Vector2Int(5,5),new Vector2Int(4,5),
            new Vector2Int(7,5),new Vector2Int(8,5),new Vector2Int(8,4),new Vector2Int(8,3),
            new Vector2Int(8,2),new Vector2Int(7,3),new Vector2Int(6,3),new Vector2Int(6,2),new Vector2Int(6,1)};
        var copper=map.registry.GetById(BlockType.CopperOre);
        for(int x=playerX-7;x<=playerX+7;x++)for(int y=-20;y<=-7;y++)
        {var pos=new Vector3Int(x,y,0);map.Terrain.SetTile(pos,map.surfaceDirtTile);map.OreOverlay.SetTile(pos,null);}
        for(int n=0;n<offsets.Length;n++)
        {
            var pos=new Vector3Int(playerX-7+offsets[n].x,-18+offsets[n].y,0);
            var variants=copper.GetOreVariants(n%5==0?OreRichness.Rich:n%3==0?OreRichness.Medium:OreRichness.Small);
            map.OreOverlay.SetTile(pos,variants[n%variants.Length]);
        }
        yield return null;map.GetComponent<UniformStoneAppearance>().RefreshAppearance();
        var globals=new List<Light2D>();
        foreach(var existing in Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None))
            if(existing.enabled && existing.lightType==Light2D.LightType.Global){globals.Add(existing);existing.enabled=false;}
        var view=new GameObject("Copper dirt verification camera",typeof(Camera)).GetComponent<Camera>();
        view.CopyFrom(Camera.main);view.enabled=false;view.orthographicSize=6.05f;
        view.transform.position=map.Terrain.GetCellCenterWorld(new Vector3Int(playerX-1,-13,0))+Vector3.back*10;
        var light=new GameObject("Copper dirt verification light",typeof(Light2D)).GetComponent<Light2D>();
        light.lightType=Light2D.LightType.Global;light.intensity=1;
        var target=new RenderTexture(900,1100,24,RenderTextureFormat.ARGB32);
        var pixels=new Texture2D(900,1100,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;
        var darkness=map.transform.Find("Daylight Overlay")?.GetComponent<MeshRenderer>();
        bool darknessEnabled=darkness && darkness.enabled;
        try
        {
            if(darkness)darkness.enabled=false;
            view.targetTexture=target;map.GetComponent<OreOverlayAppearance>().PrepareVeins(view);
            view.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,900,1100),0,0);pixels.Apply();
            const string path="Assets/Design/OreVeins/CopperOrganic/Implemented/Runtime-copper.png";
            File.WriteAllBytes(path,pixels.EncodeToPNG());
            int visible=0;foreach(var pixel in pixels.GetPixels32())if(pixel.r>40 || pixel.g>40 || pixel.b>40)visible++;
            Check(visible>pixels.width*pixels.height/2,"Rendered a visible copper/dirt fixture in the actual gameplay scene with verification lighting");
        }
        finally
        {view.targetTexture=null;RenderTexture.active=previous;if(darkness)darkness.enabled=darknessEnabled;
            light.enabled=false;foreach(var existing in globals)if(existing)existing.enabled=true;
            Object.Destroy(view.gameObject);Object.Destroy(light.gameObject);Object.Destroy(target);Object.Destroy(pixels);}
    }

    void Cleanup()
    {
        if(restored)return;restored=true;
        if(GameSaveSystem.TestDirectory==directory)
        { GameSaveSystem.TestDirectory=previousSave;MetaProgression.TestDirectory=previousMeta; }
        Application.runInBackground=previousBackground;Object.Destroy(gameObject);
    }
    void OnDestroy(){if(!restored)Cleanup();}
}
