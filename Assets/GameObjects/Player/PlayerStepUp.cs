using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerStepUp
{
    const float Skin=.008f;
    static readonly RaycastHit2D[] hits=new RaycastHit2D[16];
    static readonly ContactPoint2D[] contacts=new ContactPoint2D[16];
    public static bool HasSupport(Rigidbody2D body,Collider2D shape,LayerMask ground)
    {
        var bounds=shape.bounds;
        var filter=new ContactFilter2D();filter.SetLayerMask(ground);filter.useTriggers=false;
        var origin=new Vector2(bounds.center.x,bounds.min.y+.025f);
        int count=body.gameObject.scene.GetPhysicsScene2D().BoxCast(origin,new Vector2(bounds.size.x-Skin*2,.016f),0,
            Vector2.down,.065f,filter,hits);
        for(int i=0;i<count;i++)
            if(hits[i].collider&&hits[i].rigidbody!=body&&hits[i].normal.y>.3f)return true;
        return false;
    }
    static bool Obstructed(Rigidbody2D body,Collider2D shape,Vector2 origin,Vector2 size,Vector2 direction,float distance)
    {
        var filter=new ContactFilter2D();filter.SetLayerMask(Physics2D.GetLayerCollisionMask(shape.gameObject.layer));filter.useTriggers=false;
        int count=body.gameObject.scene.GetPhysicsScene2D().BoxCast(origin,size,0,direction,distance,filter,hits);
        for(int i=0;i<count;i++)if(hits[i].collider&&hits[i].rigidbody!=body)return true;
        return count==hits.Length;
    }
    public static bool TryStep(Rigidbody2D body,Collider2D shape,float direction,float speed,float maximumHeight,LayerMask ground)
    {
        if(maximumHeight<=0||Mathf.Abs(direction)<.01f)return false;
        float sign=Mathf.Sign(direction),advance=Mathf.Max(Mathf.Abs(speed)*Time.fixedDeltaTime+.025f,.035f);
        var bounds=shape.bounds;
        float foot=bounds.min.y;
        maximumHeight=Mathf.Min(maximumHeight,bounds.size.y*.45f);
        var filter=new ContactFilter2D();filter.SetLayerMask(ground);filter.useTriggers=false;
        // Sweep the entire current and upcoming footprint. A ray at its far end
        // misses narrow crests and can report the valley behind the obstacle.
        var probe=new Vector2(bounds.center.x+sign*advance*.5f,foot+maximumHeight+Skin*2);
        var footprint=new Vector2(bounds.size.x+advance-Skin*2,Skin*2);
        int count=body.gameObject.scene.GetPhysicsScene2D().BoxCast(probe,footprint,0,Vector2.down,maximumHeight+.06f,filter,hits);
        if(count==hits.Length)return false;
        float lift=0;
        bool nearFloor=false;
        for(int i=0;i<count;i++)
        {
            var hit=hits[i];if(!hit.collider||hit.rigidbody==body)continue;
            if(hit.distance<=0)return false; // Footprint starts inside an obstacle taller than the step limit.
            float height=hit.point.y-foot;
            if(height>maximumHeight+.001f)return false;
            if(height>-.04f)nearFloor=true;
            if(height>.005f&&hit.normal.y>.45f)lift=Mathf.Max(lift,height+Skin);
        }
        if(lift<=0&&nearFloor)
        {
            int contactCount=shape.GetContacts(contacts);
            for(int i=0;i<contactCount;i++)
                if(contacts[i].collider&&!contacts[i].collider.isTrigger&&
                    contacts[i].otherCollider&&!contacts[i].otherCollider.isTrigger&&
                    contacts[i].normal.x*sign<-.1f&&contacts[i].normal.y>.35f&&
                    contacts[i].point.y>=foot-.04f&&contacts[i].point.y<foot+maximumHeight&&
                    (ground.value&(1<<contacts[i].collider.gameObject.layer))!=0)
                    lift=Mathf.Min(.025f,maximumHeight);
        }
        if(lift<=0)return false;
        // The lower envelope can already touch the step. Sweep the upper body
        // upwards for ceilings, then the full raised body across for walls.
        Vector2 size=(Vector2)bounds.size-Vector2.one*(Skin*2);
        Vector2 center=bounds.center;
        var upperSize=new Vector2(size.x,size.y-maximumHeight);
        if(Obstructed(body,shape,center+Vector2.up*(maximumHeight*.5f),upperSize,Vector2.up,lift))return false;
        if(Obstructed(body,shape,center+Vector2.up*lift,size,Vector2.right*sign,advance))return false;
        body.position+=Vector2.up*lift;
        body.linearVelocity=new Vector2(sign*Mathf.Abs(speed),Mathf.Max(0,body.linearVelocity.y));
        return true;
    }
}
