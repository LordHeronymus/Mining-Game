using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class MiningCrackPreview
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var miner = UnityEngine.Object.FindFirstObjectByType<TileMiner>();
        var terrain = (Tilemap)typeof(TileMiner).GetField("tilemap", flags).GetValue(miner);
        var camera = Camera.main;
        var center = terrain.WorldToCell(miner.transform.position);
        Vector3Int cell = center; bool found = false;
        for (int radius = 0; radius < 8 && !found; radius++)
            for (int x = -radius; x <= radius && !found; x++)
                for (int y = -radius; y <= radius && !found; y++)
                {
                    var c = center + new Vector3Int(x, y, 0);
                    if (terrain.HasTile(c)) { cell = c; found = true; }
                }
        if (!found) throw new Exception("No terrain near player");
        var position = camera.transform.position; float zoom = camera.orthographicSize;
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var rt = new RenderTexture(1000, 700, 24);
        var pixels = new Texture2D(1000,700,TextureFormat.RGBA32,false);
        try
        {
            typeof(TileMiner).GetMethod("ShowMiningCracks",flags).Invoke(miner,new object[]{cell,.88f});
            var p = terrain.GetCellCenterWorld(cell);
            camera.transform.position = new Vector3(p.x,p.y,position.z);
            camera.orthographicSize = 2.2f; camera.targetTexture = rt;
            camera.Render(); RenderTexture.active = rt;
            pixels.ReadPixels(new Rect(0,0,1000,700),0,0); pixels.Apply();
            File.WriteAllBytes("Design/MiningCracks/MiningCracks-in-game.png",pixels.EncodeToPNG());
            return "Captured real scene at " + cell + "; camera and effect restored, no terrain changed.";
        }
        finally
        {
            ((MiningCrackVisual)typeof(TileMiner).GetField("miningCracks",flags).GetValue(miner))?.Hide(cell);
            camera.transform.position=position; camera.orthographicSize=zoom; camera.targetTexture=oldTarget;
            RenderTexture.active=oldActive;
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
