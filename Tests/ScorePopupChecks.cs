using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using TMPro;

public static class ScorePopupChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Main()
    {
        Check(Application.isPlaying, "Use Play Mode");
        var before = UnityEngine.Object.FindObjectsByType<Number>(FindObjectsSortMode.None);
        var owner = new GameObject("Score popup checks");
        try
        {
            var spawner = owner.AddComponent<NumnberSpawner>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObjects/UI/Numbers/Number.prefab");
            typeof(NumnberSpawner).GetField("numberPrefab", Private).SetValue(spawner, prefab);
            var iron = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Ores/Iron.asset");
            var gold = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/GameObjects/Items/Ores/Gold.asset");
            var emit = typeof(NumnberSpawner).GetMethod("HandleMiningPoints", Private);
            Action<Vector2,int,ItemSO> send = (p,n,item) => emit.Invoke(spawner,new object[]{p,n,item});
            Func<Number[]> spawned = () => UnityEngine.Object.FindObjectsByType<Number>(FindObjectsSortMode.None).Except(before).ToArray();
            send(Vector2.zero, 50, iron);
            send(Vector2.right * .5f, 100, iron);
            Check(spawned().Length == 1 && spawned()[0].Total == 150, "Nearby gains must accumulate");
            var popup = spawned()[0];
            var text = popup.GetComponentInChildren<TMP_Text>();
            Check(text.font.name.Contains("Bree") && text.text == "+150", "Font or sum incorrect");
            Check(text.fontMaterial.IsKeywordEnabled("OUTLINE_ON") && text.fontMaterial.IsKeywordEnabled("UNDERLAY_ON"), "Outline and shadow must render");
            send(Vector2.zero, 200, gold);
            send(Vector2.right * 10, 50, iron);
            Check(spawned().Length == 3, "Different items and distant pickups must stay separate");
            var advance = typeof(Number).GetMethod("Advance", Private);
            advance.Invoke(popup, new object[]{.3f});
            float risen = popup.transform.position.y;
            Check(risen > .55f && risen < .65f, "Rise phase incorrect");
            advance.Invoke(popup, new object[]{.7f});
            Check(Mathf.Abs(popup.transform.position.y-risen) < .025f && text.alpha > .99f, "Popup must hover visibly");
            advance.Invoke(popup, new object[]{.8f});
            Check(text.alpha > 0 && text.alpha < .8f, "Popup must fade gradually");
            popup.Accumulate(50);
            Check(popup.Total == 200 && text.alpha == 1 && popup.CanAccumulate, "Accumulation must restore visibility and extend life");
            advance.Invoke(popup, new object[]{2.01f});
            Check(!popup.CanAccumulate && text.alpha == 0, "Popup must finish after extended lifetime");
            return "Passed: font, sum, item/distance isolation, rise, hover, fade, refresh and expiry.";
        }
        finally
        {
            foreach(var popup in UnityEngine.Object.FindObjectsByType<Number>(FindObjectsSortMode.None).Except(before))
                UnityEngine.Object.DestroyImmediate(popup.gameObject);
            UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
