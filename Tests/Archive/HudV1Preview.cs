using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class HudV1Preview
{
    public static string Main()
    {
        if (!Application.isPlaying || !MainMenuController.IsVisible || RunNavigation.IsTransitioning)
            throw new Exception("Start at idle MainMenu in Play mode");
        if (GameSaveSystem.TestDirectory != null || MetaProgression.TestDirectory != null)
            throw new Exception("Another test owns save scope");
        var root = new GameObject("HUD V1 isolated preview"); Object.DontDestroyOnLoad(root);
        root.AddComponent<HudV1PreviewProbe>(); return "Starting isolated HUD run";
    }
    public static string Cleanup()
    {
        var probe=Object.FindFirstObjectByType<HudV1PreviewProbe>();
        if(probe) probe.Finish();
        return "Returning to MainMenu and restoring test scope";
    }
}
public sealed class HudV1PreviewProbe : MonoBehaviour
{
    public const string Report = "Temp/HudV1Preview.txt";
    string save, meta;
    bool background, restored;
    readonly int[] prefs=new int[9];
    readonly bool[] hadPrefs=new bool[9];
    string Key(int i)=>i==0 ? "CompactHud.SlotLayoutVersion" : "CompactHud.Slot."+(i-1);
    IEnumerator Start()
    {
        save=GameSaveSystem.TestDirectory;meta=MetaProgression.TestDirectory;
        background=Application.runInBackground;Application.runInBackground=true;
        for(int i=0;i<9;i++){hadPrefs[i]=PlayerPrefs.HasKey(Key(i));prefs[i]=PlayerPrefs.GetInt(Key(i));}
        string directory=Path.GetFullPath("Temp/HudV1Run-"+DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory);GameSaveSystem.TestDirectory=directory;
        MetaProgression.TestDirectory=Path.Combine(directory,"Profile");
        File.WriteAllText(Report,"Loading isolated fresh run: "+directory);
        RunNavigation.NewGame("HUD-V1");
        while(RunNavigation.IsTransitioning || LoadingProgress.Active)yield return null;
        yield return null;
        var hud=Object.FindFirstObjectByType<CompactHud>();
        if(!hud){File.AppendAllText(Report,"\nFAIL missing HUD");yield break;}
        var catalog=Resources.Load<ItemCatalog>("ItemCatalog");
        var torch=StartingResourcesSettings.Resolve((int)Item.Torche);
        var ladder=StartingResourcesSettings.Resolve((int)Item.Ladder);
        var inventory=InventoryManager.Instance;
        SetCount(inventory,torch,5);SetCount(inventory,ladder,10);
        hud.RestoreRunSlots(new[]{(int)Item.Torche,-1,(int)Item.Ladder,-1,-1,-1,-1,-1},0);
        StatsManager.Instance.AddMoney(100-StatsManager.Instance.Money);
        typeof(StatsManager).GetProperty("Points").SetValue(StatsManager.Instance,0);
        hud.energy.energy=hud.energy.stats.MaxEnergy*.875f;
        hud.energy.consumptionMultiplier=0;
        var view=EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));view.Show();view.Focus();
        Canvas.ForceUpdateCanvases();yield return new WaitForSecondsRealtime(.7f);
        File.AppendAllText(Report,"\nREADY: actual HUD, fresh isolated run, values matched to V1 reference");
    }
    static void SetCount(InventoryManager inventory,ItemSO item,int target)
    {
        int amount=inventory.GetCount(item);
        if(amount<target)inventory.Add(item,target-amount);
        else if(amount>target)inventory.TryRemove(item,amount-target);
    }
    public void Finish()=>StartCoroutine(Return());
    IEnumerator Return()
    {
        Time.timeScale=1;RunNavigation.MainMenu();
        while(RunNavigation.IsTransitioning)yield return null;
        Restore();Object.Destroy(gameObject);
    }
    void Restore()
    {
        if(restored)return;restored=true;
        for(int i=0;i<9;i++){if(hadPrefs[i])PlayerPrefs.SetInt(Key(i),prefs[i]);else PlayerPrefs.DeleteKey(Key(i));}
        PlayerPrefs.Save();GameSaveSystem.TestDirectory=save;MetaProgression.TestDirectory=meta;
        Application.runInBackground=background;
    }
    void OnDestroy()=>Restore();
}
