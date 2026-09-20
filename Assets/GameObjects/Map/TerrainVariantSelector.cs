using UnityEngine;
using UnityEngine.Tilemaps;

public static class TerrainVariantSelector
{
    public static int[] VisualGroups(TileBase[] variants)
    {
        var groups=new int[variants.Length];
        var sprites=new Sprite[variants.Length];
        for(int i=0;i<variants.Length;i++)
        {
            var data=new TileData();
            variants[i].GetTileData(Vector3Int.zero,null,ref data);
            int group=i;
            for(int j=0;j<i;j++)if(data.sprite && data.sprite==sprites[j]){group=groups[j];break;}
            groups[i]=group;sprites[i]=data.sprite;
        }
        return groups;
    }

    public static int Choose(int seed,int x,int depth,int[] groups,int left=-1,int above=-1)
    {
        if(groups.Length<=1)return 0;
        int leftGroup=left>=0?groups[left]:-1,aboveGroup=above>=0?groups[above]:-1;
        int available=0;
        for(int i=0;i<groups.Length;i++)
        {
            bool first=true;
            for(int j=0;j<i;j++)if(groups[j]==groups[i]){first=false;break;}
            if(first && groups[i]!=leftGroup && groups[i]!=aboveGroup)available++;
        }
        if(available==0){aboveGroup=-1;available=0;
            for(int i=0;i<groups.Length;i++)if(groups[i]!=leftGroup){
                bool first=true;for(int j=0;j<i;j++)if(groups[j]==groups[i]){first=false;break;}
                if(first)available++;
            }
        }
        if(available==0)return 0;
        int choice=(int)(OreVeins.Hash(seed,x,depth,0x1234u)%(uint)available);
        int chosenGroup=-1;
        for(int i=0;i<groups.Length;i++)
        {
            if(groups[i]==leftGroup || groups[i]==aboveGroup)continue;
            bool first=true;for(int j=0;j<i;j++)if(groups[j]==groups[i]){first=false;break;}
            if(first && choice--==0){chosenGroup=groups[i];break;}
        }
        int matching=0;
        for(int i=0;i<groups.Length;i++)if(groups[i]==chosenGroup)matching++;
        int variant=(int)(OreVeins.Hash(seed,x,depth,0x12A5u)%(uint)matching);
        for(int i=0;i<groups.Length;i++)if(groups[i]==chosenGroup && variant--==0)return i;
        return 0;
    }

}
