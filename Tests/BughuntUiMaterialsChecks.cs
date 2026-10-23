using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Checks that score popups and item feed rows release their own TMP material copies (outline setters create one per
// text), when a row expires or overflows, when a popup is destroyed and when the feed itself is destroyed.
// Everything is built in a temporary additive scene; the open SampleScene is not changed.
public static class BughuntUiMaterialsChecks
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add("PASS " + label); }

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        checks.Clear();
        var temporary = new List<Object>();
        var materials = new List<Material>();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            // Score popup: Init creates the outlined copy, OnDestroy releases it.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/UI/Numbers/Number.prefab");
            Check(prefab && prefab.GetComponent<Number>(), "Number-Prefab vorhanden");
            var popupObject = Object.Instantiate(prefab);
            temporary.Add(popupObject);
            foreach (var child in popupObject.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var popup = popupObject.GetComponent<Number>();
            popup.Init(Color.white, 9f, 10, 2f);
            var popupText = popupObject.GetComponentInChildren<TMP_Text>(true);
            var popupMaterial = popupText.fontSharedMaterial;
            materials.Add(popupMaterial);
            Check(popupMaterial && popupMaterial != popupText.font.material, "Punkte-Popup nutzt eine eigene Materialkopie");
            Check(popupMaterial.IsKeywordEnabled("OUTLINE_ON") && popupMaterial.IsKeywordEnabled("UNDERLAY_ON") &&
                Mathf.Abs(popupMaterial.GetFloat(ShaderUtilities.ID_OutlineWidth) - .12f) < .001f &&
                Mathf.Abs(popupMaterial.GetFloat(ShaderUtilities.ID_FaceDilate) - .18f) < .001f,
                "Punkte-Popup: Kontur, Schatten und Dicke unverändert");
            var popupDestroy = typeof(Number).GetMethod("OnDestroy", Members);
            Check(popupDestroy != null, "Number räumt beim Zerstören auf (OnDestroy vorhanden)");
            popupDestroy.Invoke(popup, null);
            Check(!popupMaterial, "Punkte-Popup gibt seine Materialkopie beim Zerstören frei");

            // Item feed: overflowing and expired rows release their label copy together with the row.
            var root = new GameObject("BughuntUiMaterials Feed", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            temporary.Add(root);
            var feed = root.AddComponent<ItemFeed>();
            var entries = (IList)typeof(ItemFeed).GetField("entries", Members).GetValue(feed);
            var display = typeof(ItemFeed).GetMethod("DisplayItem", Members);
            Material NewestLabelMaterial()
            {
                var entry = entries[0];
                var label = (TextMeshProUGUI)entry.GetType().GetField("label", Members).GetValue(entry);
                return label.fontSharedMaterial;
            }
            var feedMaterials = new List<Material>();
            for (int i = 0; i < 6; i++)
            {
                var item = ScriptableObject.CreateInstance<ItemSO>();
                item.hideFlags = HideFlags.HideAndDontSave;
                item.name = "BughuntUiMaterials Item " + i;
                item.displayName = "Testerz " + i;
                temporary.Add(item);
                display.Invoke(feed, new object[] { item, 1 + i });
                var material = NewestLabelMaterial();
                feedMaterials.Add(material);
                materials.Add(material);
            }
            Check(entries.Count == 4, "Item-Feed behält höchstens vier Zeilen");
            var newest = feedMaterials[5];
            Check(newest && newest != ((TextMeshProUGUI)entries[0].GetType().GetField("label", Members).GetValue(entries[0])).font.material &&
                Mathf.Abs(newest.GetFloat(ShaderUtilities.ID_OutlineWidth) - .18f) < .001f &&
                Mathf.Abs(newest.GetColor(ShaderUtilities.ID_OutlineColor).a - .85f) < .01f, "Item-Feed: eigene Kopie, Konturbreite und -deckkraft unverändert");
            Check(!feedMaterials[0] && !feedMaterials[1], "Item-Feed gibt die Materialkopie verdrängter Zeilen frei");
            Check(feedMaterials[2] && feedMaterials[3] && feedMaterials[4] && feedMaterials[5], "Sichtbare Zeilen behalten ihr Material");
            Check(root.transform.childCount == 4, "Verdrängte Zeilen werden entfernt");

            var born = entries[0].GetType().GetField("born", Members);
            foreach (var entry in entries) born.SetValue(entry, -1000f);
            typeof(ItemFeed).GetMethod("Update", Members).Invoke(feed, null);
            Check(entries.Count == 0 && root.transform.childCount == 0, "Abgelaufene Zeilen werden entfernt");
            Check(!feedMaterials[2] && !feedMaterials[3] && !feedMaterials[4] && !feedMaterials[5], "Item-Feed gibt die Materialkopie abgelaufener Zeilen frei");

            // Rows still visible when the feed is destroyed (scene change) release their copy too.
            var last = ScriptableObject.CreateInstance<ItemSO>();
            last.hideFlags = HideFlags.HideAndDontSave;
            last.name = "BughuntUiMaterials Item last";
            temporary.Add(last);
            display.Invoke(feed, new object[] { last, 3 });
            var remaining = NewestLabelMaterial();
            materials.Add(remaining);
            var feedDestroy = typeof(ItemFeed).GetMethod("OnDestroy", Members);
            Check(feedDestroy != null, "ItemFeed räumt beim Zerstören auf (OnDestroy vorhanden)");
            feedDestroy.Invoke(feed, null);
            Check(!remaining, "Item-Feed gibt beim Zerstören die Materialkopien sichtbarer Zeilen frei");

            string report = "PASS (" + checks.Count + ")\n" + string.Join("\n", checks);
            File.WriteAllText("Temp/BughuntUiMaterialsChecks.txt", report); return report.Split('\n')[0];
        }
        catch (Exception ex)
        {
            if (ex is TargetInvocationException { InnerException: not null } inner) ex = inner.InnerException;
            File.WriteAllText("Temp/BughuntUiMaterialsChecks.txt", "FAIL: " + ex + "\n" + string.Join("\n", checks)); throw ex;
        }
        finally
        {
            for (int i = temporary.Count - 1; i >= 0; i--) if (temporary[i]) Object.DestroyImmediate(temporary[i]);
            foreach (var material in materials) if (material && !EditorUtility.IsPersistent(material)) Object.DestroyImmediate(material);
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
