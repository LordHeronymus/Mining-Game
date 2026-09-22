using System;
using UnityEngine;

public static class PlayerStepUpChecks
{
    public static object Main()
    {
        var root=new GameObject("Step up physics checks");root.transform.position=new Vector3(500,500,0);
        try
        {
            GameObject Box(string name,Vector2 center,Vector2 size)
            {
                var go=new GameObject(name);go.layer=31;go.transform.SetParent(root.transform,false);go.transform.localPosition=center;
                go.AddComponent<BoxCollider2D>().size=size;return go;
            }
            var actor=Box("Actor",new Vector2(0,.5f),new Vector2(.5f,1));
            var body=actor.AddComponent<Rigidbody2D>();body.gravityScale=0;body.constraints=RigidbodyConstraints2D.FreezeRotation;
            var shape=actor.GetComponent<Collider2D>();
            Box("Floor",new Vector2(0,-.5f),new Vector2(10,1));
            var step=Box("Step",new Vector2(.751f,.06f),new Vector2(1,.12f));
            var roof=Box("Ceiling",new Vector2(0,1.55f),new Vector2(2,1));roof.SetActive(false);
            int passed=0;
            void Check(float sign,float height,bool ceiling,bool trigger,bool expected)
            {
                actor.transform.localPosition=new Vector3(0,.5f,0);body.position=(Vector2)root.transform.position+new Vector2(0,.5f);body.linearVelocity=Vector2.zero;
                step.transform.localPosition=new Vector3(sign*.751f,height*.5f,0);
                step.GetComponent<BoxCollider2D>().size=new Vector2(1,height);step.GetComponent<BoxCollider2D>().isTrigger=trigger;
                roof.SetActive(ceiling);Physics2D.SyncTransforms();
                float before=body.position.y;
                bool result=PlayerStepUp.TryStep(body,shape,sign,2,.30f,1<<31);
                if(result!=expected)throw new Exception($"Step {sign}/{height}, ceiling={ceiling}, trigger={trigger}: {result}");
                float rise=body.position.y-before;
                if(expected&&Mathf.Abs(rise-height-.008f)>.003f)throw new Exception("Incorrect step height: "+rise+" expected "+(height+.008f));
                if(!expected&&rise!=0)throw new Exception("Blocked move changed position");
                if(expected&&Mathf.Abs(body.linearVelocity.x-sign*2)>.001f)throw new Exception("Step lost horizontal running speed");
                if(!expected&&body.linearVelocity.x!=0)throw new Exception("Rejected step restored velocity against a wall");
                passed++;
            }
            Check(1,.12f,false,false,true);Check(-1,.12f,false,false,true);
            Check(1,.25f,false,false,true);Check(-1,.25f,false,false,true);
            Check(1,.45f,false,false,false);Check(-1,.45f,false,false,false);
            Check(1,.12f,true,false,false);Check(1,.12f,false,true,false);
            return new{passed};
        }
        finally{UnityEngine.Object.DestroyImmediate(root);Physics2D.SyncTransforms();}
    }
}

