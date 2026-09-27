using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
public static class UltroniumAltarChecks
{
    static void Check(bool ok,string message) {if(!ok)throw new Exception(message);}
    public static async Task<string> Run()
    {
        Check(Application.isPlaying,"Play mode required");
        Application.runInBackground=true;
        var map=UnityEngine.Object.FindFirstObjectByType<MapGenerator>(); map.CompleteStreamingGeneration();
        var altar=map.AltarChamber; var stats=StatsManager.Instance; var inventory=InventoryManager.Instance;
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>(); var body=player.GetComponent<Rigidbody2D>();
        var item=Resources.Load<ItemCatalog>("ItemCatalog").items.First(i=>i&&i.item==Item.Ultronium);
        var saved=altar.CaptureState(); int initialCount=inventory.GetCount(item), required=stats.ultroniumRequiredToWin;
        Vector3 initialPosition=player.transform.position; var initialVelocity=body.linearVelocity;
        float health=stats.Health; int tested=0;
        try
        {
            foreach(int width in new[]{180,300,600}) foreach(int height in new[]{80,300,1000})
                for(int seed=-20;seed<20;seed++)
                {
                    var layers=new[]{new MapLayer{startDepth=0},new MapLayer{startDepth=height/2}};
                    var layout=UltroniumChamberLayout.Choose(seed,width,height,layers,12);
                    Check(layout.valid,"Valid map rejected");
                    Check(layout.origin==UltroniumChamberLayout.Choose(seed,width,height,layers,12).origin,"Seed instability");
                    int lo=layout.Bounds.xMin+width/2,hi=layout.Bounds.xMax+width/2;
                    Check(hi<=width/3||lo>=width-width/3,"Middle third overlap");
                    Check(lo>=14&&hi<=width-14,"World border overlap");
                    Check(-layout.Bounds.yMax+1>=height*3/4&&-layout.Bounds.yMin<height,"Depth placement invalid");
                    foreach(int side in new[]{-1,1}) for(int x=0;x<=11;x++) for(int y=0;y<2;y++)
                        Check(layout.IsOpen(layout.origin+new Vector3Int(x*side,y,0)),"Entrance obstructed");
                    for(int x=-11;x<=11;x++) Check(layout.IsShell(layout.origin+new Vector3Int(x,-1,0)),"Floor gap");
                    Check(!layout.IsShell(layout.origin+new Vector3Int(12,0,0)),"Tunnel capped with indestructible stone");
                    tested++;
                }
            Check(!UltroniumChamberLayout.Choose(1,30,30,null,12).valid,"Impossible map has out of bounds chamber");
            int open=0,shell=0;
            foreach(var cell in altar.Layout.Bounds.allPositionsWithin)
            {
                if(!altar.Layout.IsReserved(cell)) continue;
                Check(!map.GetOreAt(cell)&&!map.GetArtifactAt(cell),"Ore or artifact in reserved chamber");
                if(altar.Layout.IsOpen(cell)) {Check(!map.Terrain.HasTile(cell),"Terrain inside chamber");open++;}
                else {Check(map.Terrain.HasTile(cell),"Shell missing");Check(!map.RemoveBlock(cell),"Shell mined through API");shell++;}
            }
            var miner=UnityEngine.Object.FindFirstObjectByType<TileMiner>();
            Check(!miner.CompleteMining(altar.Layout.origin+Vector3Int.down),"Mining rewards bypass shell protection");
            Check(inventory.GetCount(item)==initialCount,"Shell mining changed inventory");
            var collider=player.GetComponent<Collider2D>();
            Check(collider.bounds.size.y<altar.CellSize*2f,"Player does not fit entrance");
            stats.ultroniumRequiredToWin=50; stats.SetHealthForDebug(stats.MaxHealth);
            altar.ResetChargeForNewRun();
            if(initialCount>0) inventory.TryRemove(item,initialCount);
            inventory.AddStartingItem(item,60);
            player.transform.position=altar.AltarPosition+Vector3.up*altar.CellSize*15;
            await Task.Delay(150);
            Check(!stats.HasWon&&!GameVictoryPanel.IsOpen,"Inventory incorrectly triggered victory");
            Check(!altar.TryDeposit()&&inventory.GetCount(item)==60,"Remote deposit accepted");
            inventory.TryRemove(item,45);
            player.transform.position=altar.AltarPosition+new Vector3(-altar.CellSize*2f,altar.CellSize*.85f,0);
            body.linearVelocity=Vector2.zero;
            await Task.Delay(100);
            Check(altar.CanInteract,"Altar not interactive in range");
            // Cast the player's real body through each two-cell entrance against the live terrain colliders.
            await Task.Delay(300);
            foreach(int side in new[]{-1,1})
            {
                var start=(Vector2)altar.AltarPosition+new Vector2(side*altar.CellSize*10.5f,collider.bounds.size.y*.5f+.04f);
                var hit=Physics2D.BoxCast(start,(Vector2)collider.bounds.size-Vector2.one*.03f,0,Vector2.left*side,altar.CellSize*5f,1<<map.gameObject.layer);
                Check(!hit.collider,"Player body blocked at entrance: "+side+" / "+(hit.collider?hit.collider.name:""));
                var floor=Physics2D.Raycast(start,Vector2.down,altar.CellSize*2f,1<<map.gameObject.layer);
                Check(floor.collider,"Entrance floor has no collider");
            }
            bool reentered=false;
            Action callback=()=>{if(altar.TryDeposit())reentered=true;};
            inventory.OnInventoryChanged+=callback;
            try {Check(altar.TryDeposit(),"First deposit rejected");} finally {inventory.OnInventoryChanged-=callback;}
            Check(!reentered&&altar.DepositedUltronium==15&&inventory.GetCount(item)==0,"Deposit not atomic");
            Check(!altar.TryDeposit()&&altar.DepositedUltronium==15,"Empty deposit changed progress");
            await Task.Delay(1100);
            Check(Mathf.Abs(altar.DisplayedCharge-.3f)<.01f,"Charge did not interpolate");
            var partial=altar.CaptureState();
            string json=JsonUtility.ToJson(partial);
            altar.ResetChargeForNewRun(); altar.RestoreState(JsonUtility.FromJson<UltroniumAltarChamber.SaveState>(json));
            Check(altar.DepositedUltronium==15&&altar.Layout.origin==new Vector3Int(partial.x,partial.y,0),"State roundtrip failed");
            // Exercise the actual versioned map save/load path, not just the state DTO.
            string file="Library/UltroniumAltarChecks.bin";
            var write=typeof(LastPlayedMap).GetMethod("Write",BindingFlags.NonPublic|BindingFlags.Static,null,new[]{typeof(MapGenerator),typeof(string)},null);
            var read=typeof(LastPlayedMap).GetMethod("Read",BindingFlags.NonPublic|BindingFlags.Static);
            write.Invoke(null,new object[]{map,file});
            altar.ResetChargeForNewRun(); read.Invoke(null,new object[]{file});
            Check(altar.DepositedUltronium==15&&altar.CaptureState().seed==partial.seed,"Map persistence lost altar state");
            File.Delete(file);
            inventory.AddStartingItem(item,60);
            Check(altar.TryDeposit(),"Final deposit rejected");
            Check(altar.DepositedUltronium==50&&inventory.GetCount(item)==25,"Final deposit consumed surplus");
            Check(altar.IsCompleting&&!stats.HasWon,"Victory climax missing");
            Check(!altar.TryDeposit()&&inventory.GetCount(item)==25,"Duplicate final deposit");
            Check(!stats.ApplyDamage(stats.MaxHealth),"Damage interrupts victory climax");
            await Task.Delay(2800);
            Check(stats.HasWon&&GameVictoryPanel.IsOpen,"Victory screen missing");
            var panel=UnityEngine.Object.FindFirstObjectByType<GameVictoryPanel>(); UnityEngine.Object.Destroy(panel.gameObject);
            await Task.Delay(100);
            stats.ResetRun();
            Check(!stats.HasWon&&altar.DepositedUltronium==0&&!UltroniumAltarChamber.VictorySequenceActive,"New run retained charge");
            string result="PASS: "+tested+" seed/size placements; "+open+" open and "+shell+" protected cells; physical entrances/floor, ore exclusion, inventory-only no-win, range, reentrancy, partial/final deposits, surplus, interpolation, version-5 map roundtrip, climax/victory and reset.";
            File.WriteAllText("Library/UltroniumAltarChecks-result.txt",result);
            return result;
        }
        finally
        {
            altar.RestoreState(saved); stats.ultroniumRequiredToWin=required; stats.SetHealthForDebug(health);
            int delta=inventory.GetCount(item)-initialCount;
            if(delta>0)inventory.TryRemove(item,delta);else if(delta<0)inventory.AddStartingItem(item,-delta);
            player.transform.position=initialPosition;body.linearVelocity=initialVelocity;
        }
    }
}
