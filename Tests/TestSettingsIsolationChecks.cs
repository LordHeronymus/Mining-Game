// Run in Play mode. Leaves the requested 10x test factor saved.
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

public static class TestSettingsIsolationChecks
{
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    public static object Main()
    {
        var p=UnityEngine.Object.FindFirstObjectByType<GameplayDebugPanel>();
        var w=p.GetComponent<GameplayDebugWindow>();
        var stats=UnityEngine.Object.FindFirstObjectByType<StatsManager>();
        if(!GameplayDebugPanel.IsOpen)p.Toggle();
        float basis=GameplaySettings.BaseDiggingSpeed;
        string original=File.Exists(GameplaySettings.FilePath)?File.ReadAllText(GameplaySettings.FilePath):null;
        w.SwitchTab(true);
        var content=p.transform.Find("Card/WindowViewport/WindowContent");
        Check(!content.Find("MakeDefaults").gameObject.activeSelf,"Promotion button visible in test tab.");
        var input=content.Find("TestMultiplier").GetComponent<TMP_InputField>();
        Check(input.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Where(g=>g && g!=input.targetGraphic).All(g=>!g.raycastTarget),"Input child intercepts clicks.");
        input.text="10";
        input.onEndEdit.Invoke(input.text);
        Check(GameplayTestSettings.DiggingMultiplier==10 && !GameplayTestSettings.HasUnsavedChanges,"Test save failed.");
        Check(Mathf.Approximately(stats.MiningSpeed,basis*stats.MiningSpeedMultiplier*10),"Effective speed incorrect.");
        Check(GameplaySettings.BaseDiggingSpeed==basis,"Test factor changed base speed.");
        Check((File.Exists(GameplaySettings.FilePath)?File.ReadAllText(GameplaySettings.FilePath):null)==original,"Tests overwrote gameplay JSON.");
        var saved=JsonUtility.FromJson<GameplayTestSettingsData>(File.ReadAllText(GameplayTestSettings.FilePath));
        Check(saved.diggingMultiplier==10,"Factor not saved separately.");
        input.text="NaN";
        Check(!w.ApplyTestInput() && GameplayTestSettings.DiggingMultiplier==10,"Invalid input accepted.");
        input.text="10";w.ApplyTestInput();w.SwitchTab(false);
        Check(content.Find("MakeDefaults").gameObject.activeSelf,"Gameplay promotion unavailable.");
        string previous=SessionState.GetString("MiningGame.DebugDefaults.Pending","");
        try
        {
            Check(GameplayDebugDefaults.QueueCurrent(out string message),message);
            string pending=SessionState.GetString("MiningGame.DebugDefaults.Pending","");
            var data=JsonUtility.FromJson<PendingSpeed>(pending);
            Check(data.speed==basis,"Promotion included the test multiplier.");
            Check(!pending.Contains("diggingMultiplier"),"Test values leaked into promotion.");
        }
        finally { SessionState.SetString("MiningGame.DebugDefaults.Pending",previous); }
        w.SwitchTab(true);
        return new {passed=true,basis,effective=stats.MiningSpeed,testFactor=GameplayTestSettings.DiggingMultiplier};
    }
    [Serializable] class PendingSpeed { public float speed; }
}
