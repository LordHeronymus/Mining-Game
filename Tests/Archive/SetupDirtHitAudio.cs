using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class SetupDirtHitAudio
{
    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit Mode required");
        const string folder = "Assets/AC Audio/Mining/DirtHits";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/AC Audio/Mining", "DirtHits");
        var clips = new AudioClip[6];
        for (int i = 0; i < clips.Length; i++)
        {
            string src = "Assets/ZZZ New Assets/dirt hit" + (i + 1) + ".wav";
            string dst = folder + "/DirtHit_0" + (i + 1) + ".wav";
            if (!AssetDatabase.LoadAssetAtPath<AudioClip>(dst))
            {
                string guid = AssetDatabase.AssetPathToGUID(src);
                string error = AssetDatabase.MoveAsset(src, dst);
                if (error.Length > 0) throw new Exception(error);
                if (guid != AssetDatabase.AssetPathToGUID(dst)) throw new Exception("GUID changed");
            }
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(dst);
            if (!clips[i]) throw new Exception("Missing dirt hit clip");
            var importer = (AudioImporter)AssetImporter.GetAtPath(dst);
            var settings = importer.defaultSampleSettings;
            settings.preloadAudioData = true; importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
        var manager = UnityEngine.Object.FindFirstObjectByType<AudioManager>();
        var data = new SerializedObject(manager);
        var sounds = data.FindProperty("sounds");
        int index = -1;
        for (int i = 0; i < sounds.arraySize; i++)
            if (sounds.GetArrayElementAtIndex(i).FindPropertyRelative("type").intValue == (int)SoundType.DirtHit) index = i;
        if (index < 0) index = sounds.arraySize++;
        var sound = sounds.GetArrayElementAtIndex(index);
        sound.FindPropertyRelative("type").intValue = (int)SoundType.DirtHit;
        sound.FindPropertyRelative("clip").objectReferenceValue = clips[0];
        sound.FindPropertyRelative("volume").floatValue = 1f;
        sound.FindPropertyRelative("pitch").floatValue = 1f;
        var variants = sound.FindPropertyRelative("variants"); variants.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++) variants.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        data.ApplyModifiedProperties();
        int blocks = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Block"))
        {
            var block = AssetDatabase.LoadAssetAtPath<Block>(AssetDatabase.GUIDToAssetPath(guid));
            if (!block || (block.id != BlockType.Dirt && block.id != BlockType.Stone)) continue;
            Undo.RecordObject(block, "Set dirt hit variants");
            block.digSound = SoundType.DirtHit;
            EditorUtility.SetDirty(block); blocks++;
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);
        return new { clips = clips.Length, blocks };
    }
}
