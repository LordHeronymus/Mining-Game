using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;
public static class SaveTileChecks {
 public static object Main(){if(!Application.isPlaying || !MainMenuController.IsVisible)throw new Exception("Fresh homescreen required");var root=new GameObject("Save tiles checks");Object.DontDestroyOnLoad(root);root.AddComponent<SaveTileProbe>();return "Started";}
}
public sealed class SaveTileProbe:MonoBehaviour {
 string previous,previousMeta,dir;bool restored;int checks;
 const string Output="Assets/Design/Tiefenhall/SaveTilePrototypes/Implemented";
 static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Static;
 void Check(bool value,string text){if(!value)throw new Exception(text);checks++;File.AppendAllText("Temp/SaveTileChecks.txt",text+"\n");}
 Button Button(Component root,string name)=>root.GetComponentsInChildren<Button>(true).First(b=>b.name==name);
 void Clone(int slot,int rank,string name=null){
 object[] args={"Temp/SelectionEdit-639266992421592508/slot-3.thsave",null,null};typeof(GameSaveSystem).GetMethod("ReadEnvelope",Hidden).Invoke(null,args);
 var summary=(GameSaveSystem.Summary)args[1];summary.slot=slot;summary.name=name??"Spielstand "+slot;summary.lastOpenedUtc=new DateTime(2026,10,4,16,4,0,DateTimeKind.Utc).AddMinutes(-rank).Ticks;summary.createdUtc=summary.lastOpenedUtc-TimeSpan.FromDays(4).Ticks;summary.depth=250+rank*10;summary.money=1250+rank*100;
 typeof(GameSaveSystem).GetMethod("WriteEnvelopeAtomic",Hidden).Invoke(null,new object[]{GameSaveSystem.SlotPath(slot),JsonUtility.ToJson(summary),args[2],true});
 }
 IEnumerator Start(){
 previous=GameSaveSystem.TestDirectory;previousMeta=MetaProgression.TestDirectory;dir=Path.GetFullPath("Temp/SaveTileSaves-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(dir);GameSaveSystem.TestDirectory=dir;MetaProgression.TestDirectory=Path.Combine(dir,"Profile");Directory.CreateDirectory(Output);File.WriteAllText("Temp/SaveTileChecks.txt","");
 var routines=new Stack<IEnumerator>();routines.Push(Run());
 while(routines.Count>0){object current=null;bool more=false;Exception fail=null;try{more=routines.Peek().MoveNext();if(more)current=routines.Peek().Current;}catch(Exception e){fail=e;}
 if(fail!=null){File.AppendAllText("Temp/SaveTileChecks.txt","FAIL: "+fail);Cleanup();yield break;}if(!more){routines.Pop();continue;}if(current is IEnumerator nested)routines.Push(nested);else yield return current;}
 File.AppendAllText("Temp/SaveTileChecks.txt","PASS ("+checks+")\n");Cleanup();
 }
 IEnumerator Shot(string name){yield return new WaitForSecondsRealtime(.45f);yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());Object.Destroy(t);}
 IEnumerator Run(){
 while(LoadingProgress.Active || RunNavigation.IsTransitioning)yield return null;
 foreach(var meta in Object.FindObjectsByType<MetaProgressionPanel>(FindObjectsSortMode.None))meta.Close();yield return null;
 var parent=GameObject.Find("ScreenCanvas").transform;
 var panel=SaveSlotPanel.Show(parent,false);yield return null;
 Check(panel.GetComponentsInChildren<Button>().Count(b=>b.name.StartsWith("Slot "))==0,"empty overview contains no unused tiles");
 Check(!Button(panel,"Edit Save").gameObject.activeSelf && !Button(panel,"Start Save").gameObject.activeSelf,"empty overview only offers back");Button(panel,"Back").onClick.Invoke();yield return null;
 int[] slots={3,7,1,6,8,2};string[] names={"Kristallreise","Tiefenreise","Amethysthöhle","Kupferpfad","Bergwacht","Abendstollen"};for(int i=0;i<6;i++)Clone(slots[i],i,names[i]);
 panel=SaveSlotPanel.Show(parent,false);yield return null;yield return new WaitForSecondsRealtime(.4f);
 var tiles=panel.GetComponentsInChildren<Button>().Where(b=>b.name.StartsWith("Slot ")).OrderByDescending(b=>b.GetComponent<RectTransform>().anchoredPosition.y).ThenBy(b=>b.GetComponent<RectTransform>().anchoredPosition.x).ToArray();
 Check(tiles.Select(b=>int.Parse(b.name.Substring(5))).SequenceEqual(slots),"occupied saves sorted newest first across rows");
 Check(tiles.All(b=>b.GetComponent<RectTransform>().sizeDelta==new Vector2(420,210)),"all tiles have equal V1 geometry");
 Check(tiles.Select(b=>b.GetComponent<RectTransform>().anchoredPosition.x).Distinct().Count()==3 && tiles.Select(b=>b.GetComponent<RectTransform>().anchoredPosition.y).Distinct().Count()==2,"three-column two-row grid");
 Check(tiles.All(b=>b.GetComponent<Image>().sprite==HomeUi.Sprite("SelectionCardNormal") && b.GetComponent<HomeButtonFeedback>().selectionManaged),"tiles share challenge artwork and managed gold selection");
 Check(tiles.All(b=>b.transform.Find("Label").GetComponent<TMP_Text>().fontSize==32 && !b.transform.Find("Label").GetComponent<TMP_Text>().enableAutoSizing),"consistent title font size");
 Check(tiles[0].GetComponent<HomeButtonFeedback>().primary && tiles.Skip(1).All(b=>!b.GetComponent<HomeButtonFeedback>().primary),"newest save selected by default");
 Check(tiles[0].transform.Find("Slot Details 3").GetComponent<TMP_Text>().text.Contains("04.10.2026") && tiles[0].transform.Find("Stats/Balance").GetComponent<TMP_Text>().text=="1.250 $","latest opening date and formatted balance rendered");
 Check(tiles.All(b=>{var text=b.transform.Find("Stats/Balance").GetComponent<TMP_Text>();var coin=b.transform.Find("Stats/Coin").GetComponent<RectTransform>();float gap=text.rectTransform.anchoredPosition.x+text.textBounds.min.x-coin.anchoredPosition.x-coin.rect.width*.5f;return gap>=3 && gap<=14;}),"coin sits closely before its balance without overlapping");
 Check(tiles.All(b=>b.GetComponentsInChildren<Graphic>().Where(g=>g!=b.targetGraphic).All(g=>!g.raycastTarget)),"tile decorations do not intercept input");
 var footer=new[]{Button(panel,"Back"),Button(panel,"Edit Save"),Button(panel,"Start Save")};Check(footer.All(b=>b.GetComponent<RectTransform>().sizeDelta==new Vector2(340,70)),"three equal footer buttons");
 yield return Shot("SaveTiles-v1-six");
 var position=tiles[1].GetComponent<RectTransform>().anchoredPosition;ExecuteEvents.Execute(tiles[1].gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);yield return null;
 Check(tiles[1].transform.Find("Highlight").GetComponent<Image>().color.a>0 && tiles[1].transform.Find("Highlight").GetComponent<Image>().color.a<1,"hover fades smoothly");yield return new WaitForSecondsRealtime(.4f);
 Check(tiles[1].GetComponent<RectTransform>().anchoredPosition==position && tiles[1].transform.localScale==Vector3.one,"hover never moves or scales tile");
 tiles[1].onClick.Invoke();yield return null;Check(!RunNavigation.IsTransitioning && MainMenuController.IsVisible && tiles[1].GetComponent<HomeButtonFeedback>().primary && !tiles[0].GetComponent<HomeButtonFeedback>().primary,"click selects without starting game");
 ExecuteEvents.Execute(tiles[1].gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerExitHandler);
 Button(panel,"Edit Save").onClick.Invoke();yield return null;var field=panel.GetComponentInChildren<TMP_InputField>();Check(field.text=="Tiefenreise","edit opens selected tile name");field.text="Tiefenreise umbenannt";Button(panel,"Save Name").onClick.Invoke();yield return null;
 Check(Button(panel,"Slot 7").transform.Find("Label").GetComponent<TMP_Text>().text=="Tiefenreise umbenannt","renamed title refreshes on tile");
 Button(panel,"Slot 6").onClick.Invoke();Button(panel,"Edit Save").onClick.Invoke();yield return null;Button(panel,"Delete Save").onClick.Invoke();yield return null;Button(panel,"Cancel").onClick.Invoke();yield return null;Check(GameSaveSystem.HasSlotData(6),"cancel deletion keeps tile data");
 Button(panel,"Delete Save").onClick.Invoke();yield return null;Button(panel,"Confirm").onClick.Invoke();yield return null;
 Check(!GameSaveSystem.HasSlotData(6) && panel.GetComponentsInChildren<Button>().Count(b=>b.name.StartsWith("Slot "))==5,"confirmed deletion removes tile");
 Check(Button(panel,"Slot 8").GetComponent<RectTransform>().anchoredPosition.x==-446 && Button(panel,"Slot 2").GetComponent<RectTransform>().anchoredPosition.x==0,"later tiles close the grid gap");
 Button(panel,"Back").onClick.Invoke();yield return null;
 foreach(int slot in new[]{4,5,6,9,10})Clone(slot,10+slot);panel=SaveSlotPanel.Show(parent,false);yield return null;
 var scroll=panel.GetComponentInChildren<ScrollRect>();Check(scroll.content.rect.height>scroll.viewport.rect.height && scroll.verticalScrollbar.gameObject.activeSelf,"ten saves enable scrolling");
 Check(panel.GetComponentsInChildren<Button>().Count(b=>b.name.StartsWith("Slot "))==10,"ten-save cap shown as ten occupied tiles");
 EventSystem.current.SetSelectedGameObject(Button(panel,"Slot 10").gameObject);yield return null;Canvas.ForceUpdateCanvases();yield return null;
 var last=Button(panel,"Slot 10").GetComponent<RectTransform>();var corners=new Vector3[4];last.GetWorldCorners(corners);var top=scroll.viewport.InverseTransformPoint(corners[1]).y;var bottom=scroll.viewport.InverseTransformPoint(corners[0]).y;
 Check(top<=scroll.viewport.rect.yMax+.1f && bottom>=scroll.viewport.rect.yMin-.1f,"keyboard focus scrolls entire last tile into view");yield return Shot("SaveTiles-v1-ten-bottom");
 var root=panel.GetComponent<RectTransform>();var min=root.anchorMin;var max=root.anchorMax;var size=root.sizeDelta;root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);
 foreach(var dimensions in new[]{new Vector2(1280,720),new Vector2(1024,768),new Vector2(2560,1080)}){root.sizeDelta=dimensions;Canvas.ForceUpdateCanvases();yield return null;var layout=panel.transform.Find("Slot Layout").GetComponent<RectTransform>();Check(layout.sizeDelta.x*layout.localScale.x<=dimensions.x && layout.sizeDelta.y*layout.localScale.y<=dimensions.y,"panel fits "+dimensions);}
 root.anchorMin=min;root.anchorMax=max;root.sizeDelta=size;Canvas.ForceUpdateCanvases();yield return null;Button(panel,"Back").onClick.Invoke();yield return null;
 GameSaveSystem.DeleteSlot(10,out string ignored);File.WriteAllText(GameSaveSystem.SlotPath(10),"invalid save");panel=SaveSlotPanel.Show(parent,false);yield return null;Button(panel,"Slot 10").onClick.Invoke();yield return null;
 Check(Button(panel,"Slot 10").gameObject.activeSelf && Button(panel,"Slot 10").transform.Find("Slot Details 10").GetComponent<TMP_Text>().text=="Nicht lesbar" && !Button(panel,"Start Save").interactable && Button(panel,"Edit Save").interactable,"damaged saves remain visible and editable but cannot start");
 Button(panel,"Back").onClick.Invoke();yield return null;
 panel=SaveSlotPanel.Show(parent,false);Button(panel,"Start Save").onClick.Invoke();while(RunNavigation.IsTransitioning || LoadingProgress.Active)yield return null;
 var player=Object.FindFirstObjectByType<PlayerMovement>();var camera=Camera.main;Check(GameSaveSystem.ActiveSlot==3 && player && camera && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target==player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled && GameplayTestSettings.CameraFollowEnabled,"start button loads selected save with camera follow and clamp");
 GameSaveSystem.DeleteSlot(10,out ignored);parent=GameObject.Find("ScreenCanvas").transform;panel=SaveSlotPanel.Show(parent,true);yield return null;
 Check(Button(panel,"Slot 10").gameObject.activeSelf && Button(panel,"Slot 10").transform.Find("Slot Details 10").GetComponent<TMP_Text>().text=="Leer","manual saving retains free target tile");Button(panel,"Slot 10").onClick.Invoke();while(GameSaveSystem.IsBusy)yield return null;yield return null;Check(GameSaveSystem.GetSummary(10)!=null,"free target tile still creates a manual save");
 Button(panel,"Back").onClick.Invoke();yield return null;RunNavigation.MainMenu();while(RunNavigation.IsTransitioning)yield return null;
 }
 void Cleanup(){GameSaveSystem.TestDirectory=previous;MetaProgression.TestDirectory=previousMeta;restored=true;Object.Destroy(gameObject);}
 void OnDestroy(){if(!restored){GameSaveSystem.TestDirectory=previous;MetaProgression.TestDirectory=previousMeta;}}
}
