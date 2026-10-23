using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Entry signs follow building resizes (F1 → Map → map object sizes): after the building is scaled and grounded
// like MapGenerator.ApplyGroundedSize does, the sign keeps its world size and its gap above the visible roof.
public static class BughuntEntrySignChecks
{
    const string Report = "Temp/BughuntEntrySignChecks.txt";
    const float Gap = .27f + 92 * .01f / 2, WorldScale = .01f;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var cache = (Dictionary<Sprite, Rect>)typeof(EntrySign).GetField("opaqueRects", hidden).GetValue(null);
        var lateUpdate = typeof(EntrySign).GetMethod("LateUpdate", hidden);
        var texture = new Texture2D(32, 64) { hideFlags = HideFlags.HideAndDontSave };
        var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 64), new Vector2(.5f, .5f), 16);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        var building = new GameObject("BughuntEntrySignBuilding") { hideFlags = HideFlags.HideAndDontSave };
        EntrySign sign = null;
        try
        {
            // Opaque rect seeded so the check does not need a GPU readback in Edit Mode.
            cache[sprite] = new Rect(0, 0, 1, 1);
            building.transform.position = new Vector3(3, 1, 0);
            building.transform.localScale = Vector3.one * .75f;
            var art = new GameObject("Sprite", typeof(SpriteRenderer)) { hideFlags = HideFlags.HideAndDontSave };
            art.transform.SetParent(building.transform, false);
            var renderer = art.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            sign = EntrySign.Create(building.transform, renderer, "Test öffnen", null, building.layer);
            foreach (var child in sign.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;

            void Expect(string state)
            {
                lateUpdate.Invoke(sign, null);
                var roof = EntrySign.VisibleBounds(renderer);
                float scale = sign.transform.lossyScale.x / (.92f + .08f * sign.Group.alpha);
                Check(Mathf.Abs(scale - WorldScale) < 1e-5f, state + ": Schild behält seine Weltgrösse (" + scale.ToString("0.#####") + ")");
                float gap = sign.transform.position.y - roof.max.y;
                Check(Mathf.Abs(gap - Gap) < .002f, state + ": Abstand Dach–Schild bleibt " + Gap.ToString("0.00") + " (" + gap.ToString("0.000") + ")");
                float centre = sign.Axis < 0 ? roof.center.x : Mathf.Lerp(renderer.bounds.min.x, renderer.bounds.max.x, sign.Axis);
                Check(Mathf.Abs(sign.transform.position.x - centre) < .002f, state + ": Schild bleibt über der Dachmitte");
            }

            void Resize(float ratio)
            {
                // Same as MapGenerator.ApplyGroundedSize: scale, then lift so the bottom edge stays on the ground.
                float bottom = renderer.bounds.min.y;
                building.transform.localScale *= ratio;
                building.transform.position += Vector3.up * (bottom - renderer.bounds.min.y);
            }

            Expect("Nach dem Aufbau");
            Resize(2);
            Expect("Gebäude doppelt so gross");
            Resize(.25f);
            Expect("Gebäude halb so gross wie zu Beginn");
            building.transform.position += new Vector3(1.5f, .75f, 0);
            Expect("Gebäude verschoben");

            // A building may name its roof peak (WorkbenchBuilding): the sign follows that axis through resizes too.
            var material = (Material)typeof(EntrySign).GetField("fontMaterial", hidden).GetValue(sign);
            if (material) Object.DestroyImmediate(material);
            Object.DestroyImmediate(sign.gameObject);
            sign = EntrySign.Create(building.transform, renderer, "Test öffnen", null, building.layer, .6f);
            foreach (var child in sign.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            Check(Mathf.Abs(sign.transform.position.x - EntrySign.VisibleBounds(renderer).center.x) > .05f, "Dachspitze: Schild weicht von der Bildmitte ab");
            Expect("Dachspitze nach dem Aufbau");
            Resize(1.6f);
            Expect("Dachspitze, Gebäude vergrössert");

            File.WriteAllText(Report, string.Join("\n", checks));
            return "PASS (" + checks.Count + ")";
        }
        catch (Exception ex) { File.WriteAllText(Report, string.Join("\n", checks) + "\nFAIL " + ex.Message); throw; }
        finally
        {
            cache.Remove(sprite);
            var material = sign ? (Material)typeof(EntrySign).GetField("fontMaterial", hidden).GetValue(sign) : null;
            if (material) Object.DestroyImmediate(material);
            Object.DestroyImmediate(building);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }
}
