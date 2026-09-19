using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class InstallRabbitPopulation
{
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Install outside Play mode.");
        var settings=UnityEngine.Object.FindFirstObjectByType<SurfaceRabbit>();
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if(!settings||!player)throw new Exception("Rabbit settings or player missing.");
        Undo.RecordObject(settings,"Use rabbit as spawn settings");settings.enabled=false;
        var spawner=settings.GetComponent<SurfaceRabbitSpawner>();
        if(!spawner)spawner=Undo.AddComponent<SurfaceRabbitSpawner>(settings.gameObject);
        Undo.RecordObject(spawner,"Configure rabbit population");
        spawner.settings=settings;spawner.player=player.transform;
        EditorUtility.SetDirty(settings);EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(settings.gameObject.scene);
        return "Rabbit population installed; existing appearance and behaviour settings retained.";
    }
}
