using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupMinerPlayer
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Install outside Play mode.");
        var movement = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if (!movement) throw new Exception("Player missing.");
        var player = movement.gameObject;
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameObjects/Background/BirdSilhouette.mat");
        if (!material) throw new Exception("Figure material missing.");
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Replace ball with miner");
        var circle = player.GetComponent<CircleCollider2D>();
        var capsule = player.GetComponent<CapsuleCollider2D>();
        float bottom = capsule ? capsule.bounds.min.y : circle ? circle.bounds.min.y : player.transform.position.y - .4f;
        if (!capsule)
        {
            capsule = Undo.AddComponent<CapsuleCollider2D>(player);
            if (circle) { capsule.sharedMaterial = circle.sharedMaterial; capsule.density = circle.density; }
        }
        Undo.RecordObject(capsule, "Miner collision shape");
        capsule.direction = CapsuleDirection2D.Vertical;
        capsule.size = new Vector2(.46f / Mathf.Abs(player.transform.lossyScale.x), .94f / Mathf.Abs(player.transform.lossyScale.y));
        capsule.offset = player.transform.InverseTransformPoint(new Vector3(player.transform.position.x, bottom + .47f, 0));
        EditorUtility.SetDirty(capsule);
        if (circle) Undo.DestroyObjectImmediate(circle);
        var body = player.GetComponent<Rigidbody2D>();
        Undo.RecordObject(body, "Keep miner upright"); body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        body.angularVelocity = 0; body.rotation = 0;
        Undo.RecordObject(player.transform, "Keep miner upright"); player.transform.rotation = Quaternion.identity;
        EditorUtility.SetDirty(body);
        var oldSprite = player.GetComponent<SpriteRenderer>();
        if (oldSprite) { Undo.RecordObject(oldSprite, "Hide ball"); oldSprite.enabled = false; EditorUtility.SetDirty(oldSprite); }
        var visual = player.GetComponent<MinerPlayerVisual>();
        if (!visual) visual = Undo.AddComponent<MinerPlayerVisual>(player);
        Undo.RecordObject(visual, "Configure miner");
        visual.material = material; visual.legacySprite = oldSprite; visual.height = 1.06f;
        visual.sky = UnityEngine.Object.FindFirstObjectByType<SkyController>();
        var oldGlow = player.GetComponent<NightGlow>();
        if (oldGlow)
        {
            Undo.RecordObject(oldGlow, "Replace ball glow with helmet lamp"); oldGlow.enabled = false;
            if (oldGlow.glow) { Undo.RecordObject(oldGlow.glow, "Hide ball glow"); oldGlow.glow.enabled = false; EditorUtility.SetDirty(oldGlow.glow); }
            visual.headlamp = oldGlow.localLight;
            if (visual.headlamp)
            {
                Undo.RecordObject(visual.headlamp.gameObject, "Name helmet lamp"); visual.headlamp.gameObject.name = "Helmet Light";
            }
            EditorUtility.SetDirty(oldGlow);
        }
        visual.Refresh(); EditorUtility.SetDirty(visual);
        EditorSceneManager.MarkSceneDirty(player.scene); Undo.CollapseUndoOperations(undo);
        return new { player = player.name, height = visual.height, collider = capsule.bounds.size.ToString(),
            constraints = body.constraints.ToString(), saved = EditorSceneManager.SaveScene(player.scene) };
    }
}
