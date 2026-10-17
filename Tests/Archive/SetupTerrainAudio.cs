using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupTerrainAudio
{
    public static object Main()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Edit mode required");
        const string folder = "Assets/AC Audio/Mining";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/AC Audio", "Mining");
        string[] files = { "Brösmelig.wav", "Brösmelig 2.wav", "Dumpf.wav" };
        string[] names = { "Dirt_Crumble.wav", "TransitionStone_Crumble.wav", "Stone_Thud.wav" };
        SoundType[] types = { SoundType.DigDirt, SoundType.DigTransitionStone, SoundType.DigStone };
        var manager = UnityEngine.Object.FindFirstObjectByType<AudioManager>();
        if (!manager) throw new Exception("Missing AudioManager");
        var data = new SerializedObject(manager);
        var sounds = data.FindProperty("sounds");
        for (int i = 0; i < files.Length; i++)
        {
            string src = "Assets/ZZZ New Assets/" + files[i], dst = folder + "/" + names[i];
            if (!AssetDatabase.LoadAssetAtPath<AudioClip>(dst))
            {
                string guid = AssetDatabase.AssetPathToGUID(src);
                string error = AssetDatabase.MoveAsset(src, dst);
                if (error.Length > 0) throw new Exception(error);
                if (guid != AssetDatabase.AssetPathToGUID(dst)) throw new Exception("GUID changed");
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(dst);
            if (!clip) throw new Exception("Missing " + dst);
            var importer = (AudioImporter)AssetImporter.GetAtPath(dst);
            var settings = importer.defaultSampleSettings;
            settings.preloadAudioData = true; importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            int index = -1;
            for (int j = 0; j < sounds.arraySize; j++)
                if (sounds.GetArrayElementAtIndex(j).FindPropertyRelative("type").intValue == (int)types[i]) index = j;
            if (index < 0) index = sounds.arraySize++;
            var entry = sounds.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("type").intValue = (int)types[i];
            entry.FindPropertyRelative("clip").objectReferenceValue = clip;
            entry.FindPropertyRelative("volume").floatValue = 1f;
            entry.FindPropertyRelative("pitch").floatValue = 1f;
        }
        data.ApplyModifiedProperties();
        var changed = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Block"))
        {
            var block = AssetDatabase.LoadAssetAtPath<Block>(AssetDatabase.GUIDToAssetPath(guid));
            if (!block) continue;
            SoundType type;
            if (block.id == BlockType.Dirt) type = SoundType.DigDirt;
            else if (block.id == BlockType.Stone) type = SoundType.DigTransitionStone;
            else if (block.IsStone) type = SoundType.DigStone;
            else continue;
            Undo.RecordObject(block, "Set terrain mining audio");
            block.digSound = block.breakSound = type;
            EditorUtility.SetDirty(block);
            changed.Add(block.id + " -> " + type);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveScene(manager.gameObject.scene);
        return changed;
    }
}
