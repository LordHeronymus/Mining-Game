using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BlockBreakParticlesSetup
{
    public static void Install()
    {
        var debris = CreateMaterial("StoneDebris", false);
        var dust = CreateMaterial("StoneDust", true);
        const string orePath = "Assets/GameObjects/Map/OreFragment.mat";
        var ore = AssetDatabase.LoadAssetAtPath<Material>(orePath);
        if (!ore)
        {
            ore = new Material(Shader.Find("Mining/Ore Fragment"));
            AssetDatabase.CreateAsset(ore, orePath);
        }
        foreach(var map in Object.FindObjectsByType<MapGenerator>(FindObjectsSortMode.None))
        {
            var effect = map.GetComponent<BlockBreakParticles>();
            if(!effect)effect=Undo.AddComponent<BlockBreakParticles>(map.gameObject);
            Undo.RecordObject(effect,"Configure block break particles");
            effect.debrisMaterial=debris;effect.dustMaterial=dust;
            effect.oreMaterial=ore;
            EditorUtility.SetDirty(effect);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
    }

    static Material CreateMaterial(string name,bool soft)
    {
        string folder="Assets/GameObjects/Map/";
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+name+".asset");
        if(!texture)
        {
            texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
            texture.name=name;texture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new Color[1024];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                float px=(x+.5f)/16-1,py=(y+.5f)/16-1;
                float alpha=soft ? Mathf.Pow(Mathf.Clamp01(1-px*px-py*py),2) :
                    Mathf.Clamp01((.85f-Mathf.Abs(px)*.8f-Mathf.Abs(py)*.65f)*20);
                pixels[y*32+x]=new Color(1,1,1,alpha);
            }
            texture.SetPixels(pixels);texture.Apply();AssetDatabase.CreateAsset(texture,folder+name+".asset");
        }
        var material=AssetDatabase.LoadAssetAtPath<Material>(folder+name+".mat");
        if(!material)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            material.name=name;material.SetTexture("_MainTex",texture);
            AssetDatabase.CreateAsset(material,folder+name+".mat");
        }
        return material;
    }
}
