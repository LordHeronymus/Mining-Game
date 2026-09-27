using UnityEngine;

// All coordinates are real terrain cells. The open tunnel ends are ordinary mineable rock.
public readonly struct UltroniumChamberLayout
{
    public const int HalfWidth = 11;
    public const int Height = 8;
    public readonly Vector3Int origin;
    public readonly bool valid;
    static readonly int[] RowHalfWidths = { 7, 7, 7, 6, 5, 4, 3, 2 };
    public UltroniumChamberLayout(Vector3Int origin) { this.origin = origin; valid = true; }
    public BoundsInt Bounds => new BoundsInt(origin.x-HalfWidth, origin.y-1, 0, HalfWidth*2+1, Height+2, 1);
    public bool IsOpen(Vector3Int cell)
    {
        int x = Mathf.Abs(cell.x-origin.x), y = cell.y-origin.y;
        return valid && y >= 0 && y < Height && x <= (y < 2 ? HalfWidth : RowHalfWidths[y]);
    }
    public bool IsShell(Vector3Int cell)
    {
        if (!valid || !Bounds.Contains(cell) || IsOpen(cell)) return false;
        for (int y=-1; y<=1; y++)
            for (int x=-1; x<=1; x++)
                if (IsOpen(cell+new Vector3Int(x,y,0))) return true;
        return false;
    }
    public bool IsReserved(Vector3Int cell) => IsOpen(cell) || IsShell(cell);

    public static UltroniumChamberLayout Choose(int seed, int width, int height, MapLayer[] layers, int padding)
    {
        int lastStart=0;
        if (layers != null) foreach(var layer in layers)
            if(layer != null && layer.startDepth < height) lastStart=Mathf.Max(lastStart,layer.startDepth);
        int minDepth=lastStart+(height-lastStart)/2+Height;
        int maxDepth=height-3;
        int third=width/3, offset=-width/2;
        int minX=padding+2+HalfWidth, maxX=third-HalfWidth-1;
        if(minX>maxX || minDepth>maxDepth) return default;
        uint hash=OreVeins.Hash(seed,width,height,0xA17A9u);
        int x=minX+(int)(hash%(uint)(maxX-minX+1));
        if((OreVeins.Hash(seed,width,height,0xA17AAu)&1u)!=0) x=width-1-x;
        int depth=minDepth+(int)(OreVeins.Hash(seed,width,height,0xA17ABu)%(uint)(maxDepth-minDepth+1));
        return new UltroniumChamberLayout(new Vector3Int(x+offset,-depth,0));
    }
}
