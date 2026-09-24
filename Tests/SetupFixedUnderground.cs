using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupFixedUnderground
{
    public static object Main()
    {
        if(Application.isPlaying)throw new System.Exception("Run setup in Edit Mode");
        const string folder="Assets/GameObjects/Background/Underground/";
        foreach(var name in new[]{"UpperClay","LowerSandstone"})
        {
            string path=folder+name+".png";AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Default;importer.wrapMode=TextureWrapMode.Mirror;
            importer.filterMode=FilterMode.Trilinear;importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=2048;
            importer.SaveAndReimport();
        }
        var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"FixedUnderground.mat");
        if(!mat){mat=new Material(Shader.Find("Mining Game/Fixed Underground"));AssetDatabase.CreateAsset(mat,folder+"FixedUnderground.mat");}
        mat.SetTexture("_UpperTex",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"UpperClay.png"));
        mat.SetTexture("_LowerTex",AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"LowerSandstone.png"));EditorUtility.SetDirty(mat);
        var background=Object.FindFirstObjectByType<FixedUndergroundBackground>();
        if(!background)background=new GameObject("Fixed Underground Background").AddComponent<FixedUndergroundBackground>();
        background.map=Object.FindFirstObjectByType<MapGenerator>();background.material=mat;
        foreach(var layer in Object.FindObjectsByType<ParallaxLayer>(FindObjectsSortMode.None))if(layer.name=="NearHills")background.nearHills=layer;
        background.Refresh();EditorUtility.SetDirty(background);
        EditorSceneManager.MarkSceneDirty(background.gameObject.scene);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(background.gameObject.scene);
        return new{background.topY,fade=background.FadeDepths,sorting=background.nearHills.sortingOrder+1};
    }
}
