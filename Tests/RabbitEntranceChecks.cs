using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class RabbitEntranceChecks
{
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static object Get(SurfaceRabbit r,string field)=>typeof(SurfaceRabbit).GetField(field,Private).GetValue(r);
    static void Set(SurfaceRabbit r,string field,object value)=>typeof(SurfaceRabbit).GetField(field,Private).SetValue(r,value);
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static object Main()
    {
        var source=UnityEngine.Object.FindFirstObjectByType<SurfaceRabbit>();
        var originalCamera=Camera.main;bool cameraEnabled=originalCamera && originalCamera.enabled;
        var root=new GameObject("Rabbit entry test",typeof(Grid));root.SetActive(false);
        try
        {
            root.GetComponent<Grid>().cellSize=new Vector3(.5f,.5f,0);
            var terrain=new GameObject("Test terrain",typeof(Tilemap));terrain.transform.SetParent(root.transform,false);
            var map=terrain.AddComponent<MapGenerator>();map.enabled=false;map.registry=source.map.registry;
            var tile=map.registry.GetById(BlockType.Stone).variants[0];
            for(int x=-30;x<30;x++)for(int y=-1;y<=0;y++)map.Terrain.SetTile(new Vector3Int(x,y,0),tile);
            var cameraObject=new GameObject("Test main camera",typeof(Camera));cameraObject.transform.SetParent(root.transform,false);
            var camera=cameraObject.GetComponent<Camera>();camera.tag="MainCamera";
            camera.orthographic=true;camera.orthographicSize=3;camera.aspect=2;camera.transform.position=new Vector3(0,1,-10);
            var rabbitObject=new GameObject("Test rabbit");rabbitObject.transform.SetParent(root.transform,false);
            var rabbit=rabbitObject.AddComponent<SurfaceRabbit>();rabbit.map=map;rabbit.material=source.material;
            if(originalCamera)originalCamera.enabled=false;
            root.SetActive(true);map.RestorePreviewMetadata(1,60,2);
            if(Get(rabbit,"mesh")==null)typeof(SurfaceRabbit).GetMethod("OnEnable",Private).Invoke(rabbit,null);
            var tick=typeof(SurfaceRabbit).GetMethod("Tick",Private);
            var path=typeof(SurfaceRabbit).GetMethod("HasContinuousGround",Private);
            tick.Invoke(rabbit,new object[]{0f});
            Check((bool)Get(rabbit,"ready"),"Rabbit did not spawn");
            float initial=rabbit.transform.position.x;
            Check(Mathf.Abs(initial)>6+rabbit.size*.42f,"Rabbit starts inside camera");
            for(int i=0;i<25;i++)tick.Invoke(rabbit,new object[]{.1f});
            Check(Mathf.Abs(rabbit.transform.position.x-initial)<.001f,"Rabbit moved before entry delay");
            bool pausedDuringEntry=false;
            for(int i=0;i<600 && (bool)Get(rabbit,"entering");i++)
            {
                bool wasResting=(bool)Get(rabbit,"resting");Vector3 previous=rabbit.transform.position;
                tick.Invoke(rabbit,new object[]{.05f});
                pausedDuringEntry|=(bool)Get(rabbit,"entering") && (bool)Get(rabbit,"resting");
                if(wasResting)Check((rabbit.transform.position-previous).sqrMagnitude<.000001f,"Rabbit moved during entrance pause");
                Check(Mathf.Abs(rabbit.transform.position.x)<=Mathf.Abs(previous.x)+.001f,"Rabbit lost direction to center after looking around");
            }
            Check(pausedDuringEntry,"Rabbit did not chill during entrance");
            Check(!(bool)Get(rabbit,"entering") && Mathf.Abs(rabbit.transform.position.x)<.01f,"Rabbit did not reach camera center");
            Check((bool)Get(rabbit,"resting"),"Rabbit did not pause after arriving");
            Vector3 restPosition=rabbit.transform.position;int firstDirection=(int)Get(rabbit,"direction");
            bool lookedAround=false,tiltedHead=false;
            for(int i=0;i<200 && (bool)Get(rabbit,"resting");i++)
            {
                tick.Invoke(rabbit,new object[]{.05f});
                Check((rabbit.transform.position-restPosition).sqrMagnitude<.000001f && !(bool)Get(rabbit,"hopping"),"Rabbit moved during rest");
                lookedAround|=(int)Get(rabbit,"direction")!=firstDirection;
                tiltedHead|=Mathf.Abs((float)Get(rabbit,"headTilt"))>1;
            }
            Check(lookedAround && tiltedHead && !(bool)Get(rabbit,"resting"),"Rest lacks looking animation or never ends");
            bool restedAgain=false,movedAgain=false;
            for(int i=0;i<600;i++)
            {
                tick.Invoke(rabbit,new object[]{.05f});
                restedAgain|=(bool)Get(rabbit,"resting");movedAgain|=(bool)Get(rabbit,"hopping");
                Check(Mathf.Abs(rabbit.transform.position.x)<=rabbit.roamRadius+.01f,"Rabbit left roaming radius");
                Check(rabbit.transform.position.y>=.499f,"Rabbit fell below surface");
            }
            Check(restedAgain && movedAgain,"Rabbit did not alternate between movement and resting");
            // Both endpoints are solid, but a one-tile dip in between must still block a hop.
            map.Terrain.SetTile(Vector3Int.zero,null);
            Check(!(bool)path.Invoke(rabbit,new object[]{-.75f,.75f}),"Rabbit can jump across a one-tile drop");
            Check(!(bool)path.Invoke(rabbit,new object[]{14.25f,16.25f}),"Rabbit can leave map edge");
            rabbit.transform.position=new Vector3(-.75f,.5f,0);
            Set(rabbit,"ready",true);Set(rabbit,"hopping",false);Set(rabbit,"entering",false);
            Set(rabbit,"resting",false);Set(rabbit,"untilRest",100f);
            Set(rabbit,"direction",1);Set(rabbit,"wait",0f);Set(rabbit,"homeX",-.75f);Set(rabbit,"hopsRemaining",3);
            tick.Invoke(rabbit,new object[]{.01f});
            Check((int)Get(rabbit,"direction")==-1 && (float)Get(rabbit,"toX")<-.75f,"Rabbit did not turn away from gap");
            // A freshly mined landing spot reverses the hop smoothly back to takeoff.
            map.Terrain.SetTile(Vector3Int.zero,tile);map.Terrain.SetTile(new Vector3Int(1,0,0),null);
            Set(rabbit,"fromX",-.75f);Set(rabbit,"toX",.75f);Set(rabbit,"hopTime",rabbit.hopDuration*.25f);
            Set(rabbit,"hopping",true);Set(rabbit,"returning",false);Set(rabbit,"direction",1);
            rabbit.transform.position=new Vector3(-.375f,.5f+Mathf.Sin(Mathf.PI*.25f)*rabbit.hopHeight,0);
            tick.Invoke(rabbit,new object[]{.025f});
            Check((bool)Get(rabbit,"returning") && rabbit.transform.position.x<-.375f,"Rabbit did not reverse over a newly mined gap");
            for(int i=0;i<30 && (bool)Get(rabbit,"hopping");i++)tick.Invoke(rabbit,new object[]{.025f});
            Check(Mathf.Abs(rabbit.transform.position.x+.75f)<.001f,"Rabbit failed to return to safe takeoff");
            return "PASS: entrance, stationary rest with looking and head tilt, recurring pauses, resumed hopping, gap avoidance and mid-hop reversal.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            if(originalCamera)originalCamera.enabled=cameraEnabled;
        }
    }
}
