using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class RabbitPopulationChecks
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static object Main()
    {
        var source=UnityEngine.Object.FindFirstObjectByType<SurfaceRabbit>();
        var oldCamera=Camera.main;bool wasEnabled=oldCamera && oldCamera.enabled;
        var root=new GameObject("Rabbit population test",typeof(Grid));root.SetActive(false);
        try
        {
            root.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var terrain=new GameObject("Test terrain",typeof(Tilemap));terrain.transform.SetParent(root.transform,false);
            var map=terrain.AddComponent<MapGenerator>();map.enabled=false;map.registry=source.map.registry;
            var tile=map.registry.GetById(BlockType.Stone).variants[0];
            for(int x=-30;x<30;x++)map.Terrain.SetTile(new Vector3Int(x,0,0),tile);
            var cameraObject=new GameObject("Test camera",typeof(Camera));cameraObject.transform.SetParent(root.transform,false);
            var camera=cameraObject.GetComponent<Camera>();camera.tag="MainCamera";
            camera.orthographic=true;camera.orthographicSize=3;camera.aspect=2;camera.transform.position=new Vector3(0,1,-10);
            var player=new GameObject("Test player").transform;player.SetParent(root.transform,false);player.position=new Vector3(0,.5f,0);
            var population=new GameObject("Test population");population.transform.SetParent(root.transform,false);
            var settings=population.AddComponent<SurfaceRabbit>();settings.enabled=false;settings.CopySettingsFrom(source);settings.map=map;
            var spawner=population.AddComponent<SurfaceRabbitSpawner>();spawner.settings=settings;spawner.player=player;
            spawner.spawnInterval=new Vector2(2,2);spawner.maxRabbits=3;spawner.despawnDistance=10;spawner.despawnDelay=2;
            if(oldCamera)oldCamera.enabled=false;
            root.SetActive(true);map.RestorePreviewMetadata(1,60,1);
            typeof(SurfaceRabbitSpawner).GetMethod("OnDisable",Private).Invoke(spawner,null);
            typeof(SurfaceRabbitSpawner).GetMethod("OnEnable",Private).Invoke(spawner,null);
            var tick=typeof(SurfaceRabbitSpawner).GetMethod("Tick",Private);
            Action<float> step=dt=>tick.Invoke(spawner,new object[]{dt});
            step(0);Check(spawner.ActiveCount==1,"Initial rabbit missing");
            foreach(var rabbit in population.GetComponentsInChildren<SurfaceRabbit>())
                if(rabbit!=settings)Check(Mathf.Abs(rabbit.transform.position.x)>6+.4f,"Rabbit spawned inside camera");
            step(1);Check(spawner.ActiveCount==1,"Spawn interval ignored");
            step(1);Check(spawner.ActiveCount==2,"Repeated spawn missing");
            step(2);step(10);Check(spawner.ActiveCount==3,"Population limit ignored");
            player.position=new Vector3(50,.5f,0);step(1);
            Check(spawner.ActiveCount==3,"Rabbits despawned too soon");
            player.position=new Vector3(0,.5f,0);step(0);
            player.position=new Vector3(50,.5f,0);step(1.5f);
            Check(spawner.ActiveCount==3,"Returning player did not reset distance timer");
            step(.6f);Check(spawner.ActiveCount==0,"Distant rabbits did not despawn");
            player.position=new Vector3(0,.5f,0);step(0);
            Check(spawner.ActiveCount==1,"Population did not recover after despawn");
            player.position=new Vector3(0,-50,0);camera.transform.position=new Vector3(0,-50,-10);
            step(3);step(20);Check(spawner.ActiveCount==0,"Rabbits keep spawning while player is underground");
            player.position=new Vector3(0,.5f,0);camera.transform.position=new Vector3(0,1,-10);step(0);
            Check(spawner.ActiveCount==1,"Surface return did not resume spawns");
            map.RestorePreviewMetadata(2,60,1);Check(spawner.ActiveCount==0,"Map regeneration retained old population");
            step(0);Check(spawner.ActiveCount==1,"Regenerated map did not spawn rabbits");
            typeof(SurfaceRabbitSpawner).GetMethod("OnDisable",Private).Invoke(spawner,null);
            Check(spawner.ActiveCount==0,"Disabling spawner did not clear population");
            foreach(var rabbit in population.GetComponentsInChildren<SurfaceRabbit>())
                Check(rabbit==settings,"Spawned rabbit remains active after cleanup");
            return "PASS: repeated offscreen spawns, population cap, distance timeout/reset, replacement, underground gating, regeneration and cleanup.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);if(oldCamera)oldCamera.enabled=wasEnabled;
        }
    }
}
