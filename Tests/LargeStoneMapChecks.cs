using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public static class LargeStoneMapChecks
{
    struct SavedCell
    {
        public Tilemap map; public Vector3Int cell; public TileBase tile; public Matrix4x4 matrix; public Color color; public TileFlags flags;
        public SavedCell(Tilemap map, Vector3Int cell)
        { this.map=map; this.cell=cell; tile=map.GetTile(cell); matrix=map.GetTransformMatrix(cell); color=map.GetColor(cell); flags=map.GetTileFlags(cell); }
        public void Restore()
        { map.SetTile(cell,tile); map.SetTileFlags(cell,TileFlags.None); map.SetTransformMatrix(cell,matrix); map.SetColor(cell,color); map.SetTileFlags(cell,flags); }
    }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static async Task<string> Run()
    {
        Check(Application.isPlaying,"Play Mode required");
        var map=Object.FindFirstObjectByType<MapGenerator>();
        map.CompleteStreamingGeneration();
        Check(map.uniformTestStone && map.uniformTestTile,"Uniform stone configured");
        foreach(int y in new[]{-400})
            Check(map.registry.FromTile(map.Terrain.GetTile(new Vector3Int(50,y)))==map.uniformTestStone,"Uniform material at depth "+y);
        if(map.layerThreeTile)
            Check(map.registry.FromTile(map.Terrain.GetTile(new Vector3Int(50,-850)))==map.layerThreeTile.block,"Layer three restored");
        if(map.layerOneTile)
            Check(map.registry.FromTile(map.Terrain.GetTile(new Vector3Int(50,-40)))==map.layerOneTile.block,"Layer one restored");
        if(map.surfaceDirtTile)
            Check(map.registry.FromTile(map.Terrain.GetTile(new Vector3Int(50,0))).id==BlockType.Dirt,"Surface dirt restored");
        Check(Mathf.Abs(map.Terrain.CellToWorld(Vector3Int.up).y-.5f)<.001f,"Surface height preserved");
        var p=Object.FindFirstObjectByType<PlayerMovement>();
        var body=p.GetComponent<Rigidbody2D>(); var collider=p.GetComponent<Collider2D>();
        var miner=p.GetComponent<TileMiner>(); var ladder=p.GetComponent<PlayerLadder>();
        var camera=Camera.main; var follow=camera.GetComponent<CameraFollow>();
        var position=body.position; var velocity=body.linearVelocity; float gravity=body.gravityScale;
        var cameraPosition=camera.transform.position; float cameraSize=camera.orthographicSize;
        bool movement=p.enabled, mining=miner.enabled, following=follow.enabled;
        var saved=new List<SavedCell>();
        var ladderMap=map.GetComponent<LadderMap>();
        try
        {
            p.enabled=false; miner.enabled=false; follow.enabled=false;
            for(int x=28;x<=38;x++) for(int y=-6;y<=-4;y++)
            {
                // One-cell tunnel on the left, a small chamber on the right.
                if(x<34 && y!=-5) continue;
                var cell=new Vector3Int(x,y);
                foreach(var tiles in new[]{map.Terrain,map.OreOverlay,ladderMap.Tiles})
                { saved.Add(new SavedCell(tiles,cell)); tiles.SetTile(cell,null); }
                if(x==36) ladderMap.Tiles.SetTile(cell,ladderMap.segment);
            }
            float floor=map.Terrain.CellToWorld(new Vector3Int(30,-5)).y;
            await Task.Delay(400);
            body.position=new Vector2(map.Terrain.GetCellCenterWorld(new Vector3Int(30,-5)).x,
                floor+collider.bounds.extents.y-(collider.bounds.center.y-p.transform.position.y)+.025f);
            body.linearVelocity=Vector2.zero;
            body.gravityScale=3;
            Physics2D.SyncTransforms();
            await Task.Delay(350);
            float start=body.position.x;
            for(int step=0;step<100 && body.position.x-start<1.2f;step++)
            { body.linearVelocity=new Vector2(1.2f,body.linearVelocity.y); await Task.Delay(50); }
            Check(body.position.x-start>1.1f,"Player can walk through a one-cell tunnel: travelled="+(body.position.x-start));
            Check(Mathf.Abs(collider.bounds.min.y-floor)<.06f,"Player stands on the correct tile surface: feet="+collider.bounds.min.y+" floor="+floor);
            body.linearVelocity=Vector2.zero;
            Check(map.Terrain.layoutGrid.cellSize.y-collider.bounds.size.y>.08f,"Tunnel has headroom");
            var ladderCell=new Vector3Int(36,-5);
            body.position=(Vector2)ladderMap.Tiles.GetCellCenterWorld(ladderCell)-collider.offset*(Vector2)p.transform.lossyScale;
            Physics2D.SyncTransforms();
            Check(ladderMap.FindContact(collider.bounds,out _),"Ladder contact uses the enlarged grid");
            ladder.Step(0,1,false,false);
            Check(ladder.IsClimbing,"Player can climb enlarged ladder segments");
            ladder.Detach();
            body.position=new Vector2(map.Terrain.GetCellCenterWorld(new Vector3Int(34,-6)).x,
                map.Terrain.CellToWorld(new Vector3Int(34,-6)).y+collider.bounds.extents.y-(collider.bounds.center.y-p.transform.position.y)+.025f);
            body.linearVelocity=Vector2.zero;
            camera.transform.position=map.Terrain.GetCellCenterWorld(new Vector3Int(34,-5))+new Vector3(0,0,-10);
            camera.orthographicSize=4;
            await Task.Delay(600);
            string path=Path.GetFullPath("Design/LargeStoneMap-implemented.png");
            ScreenCapture.CaptureScreenshot(path);
            await Task.Delay(600);
            return "PASS: uniform stone at all layers; continuous 1.1 grid collisions; one-cell tunnel traversal; ladder contact and climbing. Screenshot: "+path;
        }
        finally
        {
            ladder.Detach(); foreach(var cell in saved)cell.Restore();
            body.position=position;body.linearVelocity=velocity;body.gravityScale=gravity;
            camera.transform.position=cameraPosition;camera.orthographicSize=cameraSize;
            follow.enabled=following;p.enabled=movement;miner.enabled=mining;
            Physics2D.SyncTransforms();
        }
    }
}
