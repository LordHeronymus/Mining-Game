using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// Ladder segment uses the Grubenleiter sprite with the same tile format as before (1254 px, 2508 PPU, centre pivot).
public static class LadderSpriteChecks
{
    public static string Main()
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var tile=AssetDatabase.LoadAssetAtPath<Tile>("Assets/GameObjects/Map/Ladders/LadderSegment.asset");
        var sprite=tile.sprite;
        void Check(bool ok,string what){if(!ok)throw new Exception("FAIL "+what);}
        Check(sprite && sprite.texture.name=="LadderSegment_Grubenleiter","tile uses LadderSegment_Grubenleiter, got "+(sprite?sprite.texture.name:"null"));
        Check(sprite.rect.width==1254 && sprite.rect.height==1254,"full 1254 px tile rect, got "+sprite.rect);
        Check(Mathf.Approximately(sprite.pixelsPerUnit,2508),"2508 pixels per unit");
        Check(Vector2.Distance(sprite.pivot,new Vector2(627,627))<.01f,"centre pivot, got "+sprite.pivot);
        Check(Mathf.Abs(sprite.bounds.size.x-.5f)<.001f && Mathf.Abs(sprite.bounds.size.y-.5f)<.001f,"0.5 world units");
        Check(sprite.texture.mipmapCount>1 && sprite.texture.filterMode==FilterMode.Trilinear,"mipmaps and trilinear filtering");
        var old=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameObjects/Map/Ladders/LadderSegment.png");
        Check(old && AssetDatabase.AssetPathToGUID("Assets/GameObjects/Map/Ladders/LadderSegment.png")=="7172fdac6d1bea545a0168138a64f9a5","original sprite kept with its GUID");
        var ladders=UnityEngine.Object.FindFirstObjectByType<LadderMap>();
        Check(ladders && ladders.segment==tile,"LadderMap in the scene keeps the same tile asset");
        return "PASS: ladder tile shows Grubenleiter (1254 px, 2508 PPU, centre pivot, 0.5 WE, mipmaps), original kept, LadderMap tile unchanged.";
    }
}
