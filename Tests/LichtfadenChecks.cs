using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
public static class LichtfadenChecks {
 public static object Main(){if(!Application.isPlaying)throw new Exception("Play required");var root=new GameObject("Lichtfaden checks");Object.DontDestroyOnLoad(root);root.AddComponent<LichtfadenProbe>();return "Started";}
}
public sealed class LichtfadenProbe:MonoBehaviour {
 readonly List<string> passed=new List<string>();string previous;bool restored;
 void Check(bool value,string message){if(!value)throw new Exception(message);passed.Add(message);File.WriteAllLines("Temp/LichtfadenChecks.txt",passed);}
 IEnumerator Start(){
 previous=GameSaveSystem.TestDirectory;
 var dir=Path.GetFullPath("Temp/LichtfadenSaves-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(dir);
 File.Copy("Temp/SelectionEdit-639266992421592508/slot-3.thsave",Path.Combine(dir,"slot-3.thsave"));GameSaveSystem.TestDirectory=dir;
 var routines=new Stack<IEnumerator>();routines.Push(Run());
 while(routines.Count>0){object current=null;bool more=false;Exception fail=null;try{more=routines.Peek().MoveNext();if(more)current=routines.Peek().Current;}catch(Exception e){fail=e;}
 if(fail!=null){File.AppendAllText("Temp/LichtfadenChecks.txt","\nFAIL: "+fail);Cleanup();yield break;}
 if(!more){routines.Pop();continue;}if(current is IEnumerator nested)routines.Push(nested);else yield return current;
 }
 File.AppendAllText("Temp/LichtfadenChecks.txt","\nPASS");Cleanup();
 }
 IEnumerator Run(){
 while(LoadingProgress.Active || RunNavigation.IsTransitioning)yield return null;
 var setup=Object.FindFirstObjectByType<NewGamePanel>();if(!setup){NewGamePanel.Show(GameObject.Find("ScreenCanvas").transform);setup=Object.FindFirstObjectByType<NewGamePanel>();}
 yield return Field(setup.GetComponentInChildren<TMP_InputField>(),"new name");
 setup.GetComponentsInChildren<Button>().First(b=>b.name=="Cancel New Game").onClick.Invoke();yield return null;
 var panel=SaveSlotPanel.Show(GameObject.Find("ScreenCanvas").transform,false);panel.GetComponentsInChildren<Button>().First(b=>b.name=="Edit Save").onClick.Invoke();yield return null;
 yield return Field(panel.GetComponentInChildren<TMP_InputField>(),"edit name");
 panel.GetComponentsInChildren<Button>().First(b=>b.name=="Cancel Edit").onClick.Invoke();yield return null;
 panel.GetComponentsInChildren<Button>().First(b=>b.name=="Back").onClick.Invoke();yield return null;
 Check(RunNavigation.LoadGame(3,out string error),"load isolated world");
 while(RunNavigation.IsTransitioning || LoadingProgress.Active)yield return null;
 var camera=Camera.main;var player=Object.FindFirstObjectByType<PlayerMovement>();
 Check(camera && player && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target==player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,"camera follows player after fresh loading");
 var shop=Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);shop.ShowPanel(true);shop.ShowBuyPage();yield return new WaitForSecondsRealtime(.4f);
 var buy=Object.FindFirstObjectByType<BuyPage>(FindObjectsInactive.Include);
 var buyField=buy.GetComponentInChildren<TMP_InputField>();yield return Field(buyField,"shop buy search");
 buyField.text="Eisenspitzhacke";yield return null;
 Check(buy.transform.Find("Blueprint Shop/Blueprints/Content").Cast<Transform>().Count(t=>t.gameObject.activeSelf)==1,"buy search listener filters real cards");buyField.text="";
 shop.ShowSellPage();yield return null;var sell=Object.FindFirstObjectByType<SellPage>(FindObjectsInactive.Include);
 yield return Field(sell.GetComponentInChildren<TMP_InputField>(),"shop sell search");sell.SetSearch("");shop.ShowPanel(false);yield return new WaitForSecondsRealtime(.4f);
 var workbench=Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);workbench.ShowPanel(true);yield return null;
 yield return Field(workbench.GetComponentInChildren<TMP_InputField>(),"workbench search");workbench.SetSearch("");workbench.ShowPanel(false);
 yield return null;GpsRuntimePanel.Open();yield return null;
 var gps=Object.FindFirstObjectByType<GpsRuntimePanel>(); gps.SelectTab("Spieler"); yield return null;
 var header=gps.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.GetComponentInChildren<TMP_Text>()?.text=="+ Energie"); if(header)header.onClick.Invoke(); yield return null;
 var fields=gps.GetComponentsInChildren<TMP_InputField>();
 Check(fields.Length>0 && fields.All(f=>f.GetComponent<LichtfadenCaret>()),"all runtime gameplay settings text fields themed");GpsRuntimePanel.Close();
 RunNavigation.MainMenu();while(RunNavigation.IsTransitioning)yield return null;
 }
 IEnumerator Field(TMP_InputField field,string name){
 Check(field && field.GetComponent<LichtfadenCaret>() && field.caretColor.a==0,name+" uses Lichtfaden");
 string old=field.text;bool selectAll=field.onFocusSelectAll;field.onFocusSelectAll=false;
 field.text="Tiefenreise";field.Select();field.ActivateInputField();yield return null;field.caretPosition=field.text.Length;yield return null;yield return null;
 var visual=field.GetComponentInChildren<LichtfadenGraphic>();
 Check(field.isFocused && visual && visual.Opacity>0 && visual.canvasRenderer.GetMesh().vertexCount==4 && !visual.raycastTarget,name+" renders glow mesh while paused");
 var native=field.textComponent.transform.parent.GetComponentInChildren<TMP_SelectionCaret>();
 Check((visual.transform.TransformPoint(visual.Top)-native.transform.TransformPoint(visual.Top)).sqrMagnitude<.0001f,name+" aligns with TMP caret coordinates");
 float minimum=1,maximum=0;for(int i=0;i<35;i++){minimum=Mathf.Min(minimum,visual.Opacity);maximum=Mathf.Max(maximum,visual.Opacity);yield return new WaitForSecondsRealtime(.05f);}
 Check(maximum-minimum>.8f && minimum>=0 && minimum<.01f,name+" smooth pulse uses unscaled time");
 // Type and move the caret near the pulse minimum: neither action may jump back to full brightness.
 float deadline=Time.unscaledTime+2;
 while(visual.Opacity>.1f && Time.unscaledTime<deadline)yield return null;
 field.text+="x";field.caretPosition=field.text.Length;yield return null;yield return null;
 Check(visual.Opacity<.4f,name+" typing preserves the ongoing fade");
 field.caretPosition=Mathf.Max(0,field.caretPosition-1);yield return null;yield return null;
 Check(visual.Opacity<.4f,name+" moving caret preserves the ongoing fade");
 field.selectionAnchorPosition=0;field.selectionFocusPosition=3;yield return null;yield return null;
 Check(visual.Opacity==0,name+" hides caret during text selection");
 field.caretPosition=field.text.Length;yield return null;
 field.text=new string('W',field.characterLimit>0?field.characterLimit:80);field.caretPosition=field.text.Length;yield return null;yield return null;
 Vector3 point=field.textViewport.InverseTransformPoint(visual.transform.TransformPoint((visual.Top+visual.Bottom)*.5f));
 Check(point.x>=field.textViewport.rect.xMin-2 && point.x<=field.textViewport.rect.xMax+2,name+" follows horizontal text scroll");
 field.text="";field.caretPosition=0;yield return null;yield return null;
 Check(visual.Opacity>0 && visual.Top.y>visual.Bottom.y,name+" empty field has caret");
 Vector3 emptyPosition=field.textViewport.InverseTransformPoint(visual.transform.TransformPoint(visual.Bottom));
 Check(emptyPosition.x-field.textViewport.rect.xMin>=10.9f,name+" empty field reserves the full left glow");
 field.text=old;field.onFocusSelectAll=selectAll;
 }
 void Cleanup(){GameSaveSystem.TestDirectory=previous;restored=true;Object.Destroy(gameObject);}
 void OnDestroy(){if(!restored)GameSaveSystem.TestDirectory=previous;}
}