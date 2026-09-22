using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public static class UnevenFloorMovementChecks
{
    public static object Main()
    {
        var scene=SceneManager.CreateScene("Isolated uneven floor checks",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var physics=scene.GetPhysicsScene2D();
        var root=new GameObject("Uneven floor test");SceneManager.MoveGameObjectToScene(root,scene);
        var stats=UnityEngine.Object.FindFirstObjectByType<StatsManager>();
        float oldSpeed=stats.MoveSpeed;bool oldFly=GameplayTestSettings.FlyMode;
        var results=new List<string>();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        try
        {
            stats.MoveSpeed=2;GameplayTestSettings.SetMode(GameplayTestMode.Fly,false,out _);
            GameObject Child(string name){var go=new GameObject(name);go.layer=31;go.transform.SetParent(root.transform);return go;}
            var actor=Child("Player");var shape=actor.AddComponent<BoxCollider2D>();shape.size=new Vector2(.5f,1);
            var body=actor.AddComponent<Rigidbody2D>();body.constraints=RigidbodyConstraints2D.FreezeRotation;
            var movement=actor.AddComponent<PlayerMovement>();movement.enabled=false;
            void Set(string name,object value)=>typeof(PlayerMovement).GetField(name,flags).SetValue(movement,value);
            Set("rb",body);Set("col",shape);Set("stats",stats);Set("groundLayer",(LayerMask)(1<<31));
            var tick=typeof(PlayerMovement).GetMethod("FixedUpdate",flags);
            var floor=Child("Floor").AddComponent<BoxCollider2D>();floor.size=new Vector2(20,1);floor.transform.position=new Vector3(0,-.5f,0);
            var obstacle=Child("Uneven surface").AddComponent<PolygonCollider2D>();
            void Reset(float x,float y,float sign)
            {
                body.position=new Vector2(x,y);body.linearVelocity=new Vector2(sign*2,0);
                Set("inputX",sign);Set("previousRunVelocity",sign*2);Set("jumpRequested",false);
                Physics2D.SyncTransforms();
            }
            // A narrow crest lies before the old forward ray, with a valley behind it.
            foreach(float sign in new[]{1f,-1f})
            {
                obstacle.points=new[]{new Vector2(sign*.25f,0),new Vector2(sign*.25f,.02f),
                    new Vector2(sign*.27f,.18f),new Vector2(sign*.30f,.02f),new Vector2(sign*.30f,0)};
                Reset(0,.5f,sign);
                if(!PlayerStepUp.TryStep(body,shape,sign,2,.3f,1<<31)||body.position.y<.67f)
                    throw new Exception("Missed narrow crest: "+sign);
                results.Add("Narrow crest "+sign);
            }
            obstacle.enabled=false;
            Reset(0,.53f,1);
            if(movement.IsOnGround||!PlayerStepUp.HasSupport(body,shape,1<<31))throw new Exception("Near-ground support failed");
            Reset(0,.7f,1);
            if(PlayerStepUp.HasSupport(body,shape,1<<31))throw new Exception("Support reaches into mid-air");
            floor.isTrigger=true;Reset(0,.53f,1);
            if(PlayerStepUp.HasSupport(body,shape,1<<31))throw new Exception("Trigger counted as support");
            floor.isTrigger=false;results.Add("Support gap / airborne / trigger");
            obstacle.enabled=true;
            var outline=new List<Vector2>{new Vector2(-2,-.1f),new Vector2(-2,0)};
            for(int i=0;i<=160;i++)
            {
                float x=-1.6f+i*.02f;
                float y=.07f+.055f*Mathf.Sin(x*17)+.025f*Mathf.Sin(x*39);
                outline.Add(new Vector2(x,y));
            }
            outline.Add(new Vector2(2,0));outline.Add(new Vector2(2,-.1f));obstacle.points=outline.ToArray();
            foreach(float sign in new[]{1f,-1f})
            {
                Reset(-sign*2.4f,.53f,sign);
                float minimumProgress=float.MaxValue;float windowStart=body.position.x;float minimumWindow=float.MaxValue;int stalled=0,maxStalled=0;
                for(int frame=0;frame<130;frame++)
                {
                    float before=body.position.x;
                    tick.Invoke(movement,null);
                    physics.Simulate(Time.fixedDeltaTime);
                    if(frame>8)minimumProgress=Mathf.Min(minimumProgress,(body.position.x-before)*sign);
                    stalled=(body.position.x-before)*sign<.005f?stalled+1:0;maxStalled=Mathf.Max(maxStalled,stalled);
                    if(frame%10==9){minimumWindow=Mathf.Min(minimumWindow,(body.position.x-windowStart)*sign);windowStart=body.position.x;}
                }
                if(body.position.x*sign<2.4f||minimumWindow<.25f||maxStalled>2)
                    throw new Exception("Repeated uneven floor stalls: sign="+sign+" x="+body.position.x+" min progress="+minimumProgress+" min 10-frame progress="+minimumWindow+" consecutive stalls="+maxStalled);
                results.Add("130 physics steps across uneven floor "+sign+", min 10-frame advance="+minimumWindow+", max stalled ticks="+maxStalled);
            }
            // Start with a contact-induced velocity loss and a small gap above the floor.
            obstacle.points=new[]{new Vector2(.25f,0),new Vector2(.25f,.20f),new Vector2(.29f,.20f),new Vector2(.29f,0)};
            Reset(0,.53f,1);body.linearVelocity=new Vector2(.1f,0);
            tick.Invoke(movement,null);
            if(body.position.y<.69f||body.linearVelocity.x<1.99f)throw new Exception("Controller did not recover crest speed without contact");
            results.Add("Controller restores speed with near-ground support");
            return new{passed=true,results};
        }
        finally
        {
            stats.MoveSpeed=oldSpeed;GameplayTestSettings.SetMode(GameplayTestMode.Fly,oldFly,out _);
            UnityEngine.Object.DestroyImmediate(root);
            if(Application.isPlaying)SceneManager.UnloadSceneAsync(scene);else EditorSceneManager.CloseScene(scene,true);
        }
    }
}
