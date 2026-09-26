using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class GoldCursorChecks
{
    public static string Main()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(TileMiner);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var target = new RenderTexture(600, 600, 24);
        var oldTarget = RenderTexture.active;
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        var pixels = new Texture2D(600, 600, TextureFormat.RGBA32, false);
        try
        {
            SceneManager.SetActiveScene(scene);
            var grid = new GameObject("Cursor test grid", typeof(Grid));
            grid.GetComponent<Grid>().cellSize = new Vector3(1.1f, 1.1f, 0);
            Tilemap MakeMap(string name)
            {
                var go = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
                go.layer = 31; go.transform.SetParent(grid.transform);
                var tm = go.GetComponent<Tilemap>();
                tm.orientation = Tilemap.Orientation.Custom;
                tm.orientationMatrix = Matrix4x4.Scale(new Vector3(2.2f, 2.2f, 1));
                go.GetComponent<TilemapRenderer>().sharedMaterial = material;
                return tm;
            }
            var terrain = MakeMap("Test terrain");
            var highlight = MakeMap("Test highlight");
            highlight.GetComponent<TilemapRenderer>().sortingOrder = 100;
            var dirt = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameObjects/Map/StoneTest/Dirt/LightDirt.png");
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = Sprite.Create(dirt, new Rect(0, 0, dirt.width, dirt.height), Vector2.one * .5f, dirt.width * 2);
            for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) terrain.SetTile(new Vector3Int(x,y,0), tile);
            var goMiner = new GameObject("Inactive test miner");
            goMiner.SetActive(false);
            goMiner.AddComponent<BoxCollider2D>();
            var miner = goMiner.AddComponent<TileMiner>();
            type.GetField("tilemap", flags).SetValue(miner, terrain);
            type.GetField("highlightMap", flags).SetValue(miner, highlight);
            miner.cursorPulseAlphaRange = Vector2.one;
            miner.cursorPulseSize = 1;
            type.GetMethod("CreateHighlightTiles", flags).Invoke(miner, null);
            var camera = new GameObject("Cursor check camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(.55f,.55f,-10);
            camera.orthographic = true; camera.orthographicSize = 1.8f;
            camera.cullingMask = 1 << 31; camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            Directory.CreateDirectory("Design/Cursor");
            for (int mode = 0; mode < 2; mode++)
            {
                bool smart = mode == 1;
                type.GetField("smartCursor", flags).SetValue(miner, smart);
                type.GetMethod("ShowHighlight", flags).Invoke(miner, new object[] { Vector3Int.zero, Vector3Int.zero });
                var actual = highlight.GetTile<Tile>(Vector3Int.zero);
                var expected = (Tile)type.GetField(smart ? "smartHighlightTile" : "normalHighlightTile", flags).GetValue(miner);
                if (actual != expected) throw new Exception("Incorrect cursor mode mapping");
                float width = actual.sprite.bounds.size.x * 2.2f * .85f;
                if (Mathf.Abs(width - 1.1f) > .005f) throw new Exception("Outline does not match the cell: " + width);
                var raw = new Texture2D(2,2);
                raw.LoadImage(File.ReadAllBytes("Assets/Resources/Cursor/" + (smart ? "Smart" : "Normal") + "CursorFrame.png"));
                if (raw.GetPixel(raw.width/2, raw.height/2).a > .01f) throw new Exception("Opaque cursor center");
                if (smart && raw.GetPixel(raw.width/2, Mathf.RoundToInt(raw.height*.07f)).a > .01f) throw new Exception("Smart cursor has connecting edge");
                UnityEngine.Object.DestroyImmediate(raw);
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0,0,600,600),0,0); pixels.Apply();
                File.WriteAllBytes("Design/Cursor/" + (smart ? "Smart" : "Normal") + "-implemented.png", pixels.EncodeToPNG());
            }
            type.GetMethod("ClearHighlight", flags).Invoke(miner,null);
            if (highlight.HasTile(Vector3Int.zero)) throw new Exception("Highlight did not clear");
            UnityEngine.Object.DestroyImmediate(tile.sprite);
            UnityEngine.Object.DestroyImmediate(tile);
            return "PASS: normal/full frame, smart/open corners; both 1.1 cell width, transparent centers, mode switch and clear. Two Unity renders saved in Design/Cursor.";
        }
        finally
        {
            RenderTexture.active = oldTarget;
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
