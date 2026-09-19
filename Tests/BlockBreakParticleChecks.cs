using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class BlockBreakParticleChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static object Main()
    {
        var source = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var registry = source.registry;
        var previous = SceneManager.GetActiveScene();
        var oldCamera = Camera.main;
        bool cameraEnabled = oldCamera && oldCamera.enabled;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var target = new RenderTexture(640,640,24);
        var oldTarget = RenderTexture.active;
        Texture2D capture = null;
        try
        {
            if(oldCamera) oldCamera.enabled=false;
            var root=new GameObject("Test grid",typeof(Grid));
            root.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var terrain=new GameObject("Test map",typeof(Tilemap),typeof(MapGenerator));
            terrain.transform.SetParent(root.transform,false); terrain.layer=31;
            terrain.GetComponent<MapGenerator>().enabled=false;
            terrain.GetComponent<MapGenerator>().registry=registry;
            var tiles=terrain.GetComponent<Tilemap>();
            var camera=new GameObject("Test camera",typeof(Camera)).GetComponent<Camera>();
            camera.tag="MainCamera"; camera.transform.position=new Vector3(.25f,.25f,-10);
            camera.orthographic=true;camera.orthographicSize=1.3f;camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);
            var effect=terrain.AddComponent<BlockBreakParticles>();
            typeof(BlockBreakParticles).GetMethod("OnDisable",Private).Invoke(effect,null);
            effect.debrisMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/StoneDebris.mat");
            effect.dustMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/StoneDust.mat");
            effect.oreMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Map/OreFragment.mat");
            Check(effect.oreMaterial && !ShaderUtil.ShaderHasError(effect.oreMaterial.shader),"Ore material shader missing or invalid");
            typeof(BlockBreakParticles).GetMethod("OnEnable",Private).Invoke(effect,null);
            var debris=(ParticleSystem)typeof(BlockBreakParticles).GetField("debris",Private).GetValue(effect);
            var dust=(ParticleSystem)typeof(BlockBreakParticles).GetField("dust",Private).GetValue(effect);
            var emit=typeof(BlockBreakParticles).GetMethod("OnBlockMined",Private);
            var args=new object[]{(Vector2)tiles.GetCellCenterWorld(Vector3Int.zero),0};
            tiles.SetTile(Vector3Int.zero,registry.GetById(BlockType.Stone).variants[0]);
            emit.Invoke(effect,args);
            Check(debris.particleCount==9 && dust.particleCount==5,"Stone burst counts incorrect");
            var stoneDebris=debris;
            debris.Clear();
            debris=(ParticleSystem)typeof(BlockBreakParticles).GetField("oreDebris",Private).GetValue(effect);
            var kinds=new[]{BlockType.CopperOre,BlockType.GoldOre,BlockType.SilverOre,BlockType.IronOre};
            var particles=new ParticleSystem.Particle[192];
            foreach(var kind in kinds)
            {
                debris.Clear();dust.Clear();
                tiles.SetTile(Vector3Int.zero,registry.GetById(kind).variants[0]);
                emit.Invoke(effect,args);
                Check(debris.particleCount==9 && dust.particleCount==5,kind+" burst missing");
                int count=debris.GetParticles(particles);
                Color expected=OreSparkles.GetOreColor(kind);
                for(int i=0;i<count;i++)
                {
                    Check(particles[i].startSize>=.04f*effect.oreFragmentSizeMultiplier &&
                        particles[i].startSize<=.09f*effect.oreFragmentSizeMultiplier,"Ore fragment size incorrect");
                    Color actual=particles[i].startColor;
                    for(int channel=0;channel<3;channel++)
                        Check(actual[channel]>=Mathf.Clamp01(expected[channel]*.8f)-.005f &&
                            actual[channel]<=Mathf.Clamp01(expected[channel]*1.15f)+.005f,
                            kind+" fragment tint incorrect");
                }
            }
            tiles.SetTile(Vector3Int.zero,null);emit.Invoke(effect,args);
            Check(debris.particleCount==9,"Empty cell emitted fragments");
            debris.Clear();dust.Clear();
            camera.orthographicSize=3.2f;
            camera.transform.position=new Vector3(-.5f,.25f,-10);
            for(int i=0;i<kinds.Length;i++)
            {
                var cell=new Vector3Int((i-2)*3,0,0);
                tiles.SetTile(cell,registry.GetById(kinds[i]).variants[0]);
                emit.Invoke(effect,new object[]{(Vector2)tiles.GetCellCenterWorld(cell),0});
            }
            debris.Simulate(.18f,true,false);dust.Simulate(.18f,true,false);
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            capture=new Texture2D(640,640,TextureFormat.RGB24,false);
            capture.ReadPixels(new Rect(0,0,640,640),0,0);capture.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"BlockBreakPreview.png"),capture.EncodeToPNG());
            int whitePixels=0, coloredPixels=0;
            foreach(var pixel in capture.GetPixels())
            {
                if(pixel.r>.85f && pixel.g>.85f && pixel.b>.85f)whitePixels++;
                if(pixel.r>.45f && pixel.r>pixel.b*1.8f)coloredPixels++;
            }
            Check(whitePixels>5 && coloredPixels>5,"Render lacks white cores or colored ore edges");
            debris.Simulate(3,true,false);dust.Simulate(3,true,false);
            Check(debris.particleCount==0 && dust.particleCount==0,"Particles failed to expire");
            tiles.SetTile(Vector3Int.zero,registry.GetById(BlockType.Stone).variants[0]);
            for(int i=0;i<100;i++)emit.Invoke(effect,args);
            Check(stoneDebris.particleCount<=192 && debris.particleCount<=192 && dust.particleCount<=96,"Particle budget exceeded");
            return "PASS: stone and all four ores, ore sizes and colors, empty cells, particle lifetime, bounded particle budget; preview rendered.";
        }
        finally
        {
            RenderTexture.active=oldTarget;
            SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);
            if(oldCamera)oldCamera.enabled=cameraEnabled;
            UnityEngine.Object.DestroyImmediate(target);if(capture)UnityEngine.Object.DestroyImmediate(capture);
        }
    }
}
