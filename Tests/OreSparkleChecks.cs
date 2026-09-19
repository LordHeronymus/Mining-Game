using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class OreSparkleChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static object Main()
    {
        var source = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var registry = source.registry;
        var previous = SceneManager.GetActiveScene();
        var scene = Application.isPlaying ? SceneManager.CreateScene("OreSparkleChecks") :
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var texture = new RenderTexture(640, 640, 24);
        Texture2D capture = null;
        var oldTarget = RenderTexture.active;
        try
        {
            var root = new GameObject("Sparkle check", typeof(Grid));
            root.GetComponent<Grid>().cellSize = source.GetComponent<Tilemap>().layoutGrid.cellSize;
            var terrain = new GameObject("Terrain", typeof(Tilemap), typeof(TilemapRenderer), typeof(MapGenerator));
            terrain.transform.SetParent(root.transform, false);
            var map = terrain.GetComponent<MapGenerator>(); map.enabled = false; map.registry = registry;
            var tiles = terrain.GetComponent<Tilemap>();
            var renderer = terrain.GetComponent<TilemapRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/AB Sprites/Parralax BG/ParallaxUnlit.mat");
            var ore = registry.GetById(BlockType.GoldOre).variants[0];
            var kinds = new[] {BlockType.GoldOre,BlockType.CopperOre,BlockType.IronOre,BlockType.SilverOre};
            for (int x=-4;x<4;x++) for(int y=-4;y<4;y++)
                tiles.SetTile(new Vector3Int(x,y,0),registry.GetById(kinds[(x+4)%4]).variants[0]);
            var camera = new GameObject("Check camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10); camera.orthographic=true;
            camera.orthographicSize=4.2f*root.GetComponent<Grid>().cellSize.y;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.025f,.03f,.04f);
            // Keep the existing scene out of the isolated render.
            terrain.layer=31; camera.cullingMask=1<<31;
            var effect = terrain.AddComponent<OreSparkles>();
            effect.sparkleMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/OreSparkle.mat");
            typeof(OreSparkles).GetMethod("OnEnable",Private).Invoke(effect,null);
            var system=(ParticleSystem)typeof(OreSparkles).GetField("particles",Private).GetValue(effect);
            system.gameObject.layer=31;
            var tick=typeof(OreSparkles).GetMethod("Tick",Private);
            var seen=new HashSet<Vector3Int>();
            var buffer=new ParticleSystem.Particle[32];
            var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int step=0;step<300;step++)
            {
                system.Simulate(.1f,true,false);
                tick.Invoke(effect,new object[]{camera,step*.1f});
                int count=system.GetParticles(buffer);
                Check(count<=32,"Particle budget exceeded");
                for(int i=0;i<count;i++)seen.Add(tiles.WorldToCell(buffer[i].position));
            }
            timer.Stop();
            Check(seen.Count==64,"Not every ore block received its own sparkle: "+seen.Count);
            Check(system.particleCount>0,"Visible ore did not emit");
            system.Simulate(.28f,true,false);
            camera.targetTexture=texture; camera.Render(); RenderTexture.active=texture;
            capture=new Texture2D(640,640,TextureFormat.RGB24,false);
            capture.ReadPixels(new Rect(0,0,640,640),0,0); capture.Apply();
            System.IO.File.WriteAllBytes("C:/Users/jlang/AppData/Local/Temp/OreSparklePreview.png",capture.EncodeToPNG());
            Check(system.particleCount<=32,"Particle budget exceeded");
            tiles.ClearAllTiles();
            typeof(OreSparkles).GetMethod("LateUpdate",Private).Invoke(effect,null);
            system.Simulate(.02f,true,false);
            Check(system.particleCount==0,"Mined ore kept particles");
            tiles.SetTile(Vector3Int.zero,registry.GetById(BlockType.Stone).variants[0]);
            tick.Invoke(effect,new object[]{camera,31f});
            Check(system.particleCount==0,"Stone emitted particles");
            tiles.SetTile(Vector3Int.zero,ore);
            var lighting=terrain.AddComponent<MapLighting>();
            lighting.daylightStrength=0;lighting.ambientBrightness=0;
            typeof(OreSparkles).GetField("lighting",Private).SetValue(effect,lighting);
            tick.Invoke(effect,new object[]{camera,35f});
            Check(system.particleCount==0,"Unlit ore emitted particles");
            lighting.lightingEnabled=false;
            camera.transform.position=new Vector3(100,100,-10);
            tick.Invoke(effect,new object[]{camera,40f});
            Check(system.particleCount==0,"Offscreen ore emitted particles");
            return new {passed=true,all64CellsSparkled=true,miningCleanup=true,darkness=true,offscreen=true,budget=32,scheduler300TicksMs=timer.Elapsed.TotalMilliseconds};
        }
        finally
        {
            RenderTexture.active=oldTarget;
            SceneManager.SetActiveScene(previous);
            if(Application.isPlaying)
            {
                foreach(var root in scene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(scene);
            }
            else EditorSceneManager.CloseScene(scene,true);
            if(capture)UnityEngine.Object.DestroyImmediate(capture);
            texture.Release();UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
