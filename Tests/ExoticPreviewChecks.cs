using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;
public static class ExoticPreviewChecks {
 public static string Main(){if(!Application.isPlaying || GameplayInputBlocker.IsBlocked)throw new Exception("Needs free homescreen");new GameObject("Exotic Preview Checks").AddComponent<ExoticPreviewProbe>();return "Started";}
}
public class ExoticPreviewProbe:MonoBehaviour {
 const string Report="Temp/ExoticPreviewChecks.txt";
 MetaProgressionPanel owner; string previous; int count; bool cleaned;
 void Check(bool ok,string text){if(!ok)throw new Exception(text);File.AppendAllText(Report,"PASS "+(++count)+" "+text+"\n");}
 IEnumerator Start(){previous=MetaProgression.TestDirectory;MetaProgression.TestDirectory=Path.GetFullPath("Temp/ExoticPreview-"+DateTime.UtcNow.Ticks);File.WriteAllText(Report,"");var it=Run();try{while(it.MoveNext())yield return it.Current;}finally{Cleanup();}}
 IEnumerator Run(){
 var entries=ExoticBlueprintCatalog.Entries;
 Check(entries.Count==23 && entries.Count(e=>e.Preview)==20,"23 entries including 20 previews");
 Check(entries.Select(e=>e.Id).Distinct().Count()==23,"Unique IDs");
 Check(ExoticCatalog.Recipes.Count==3,"Playable loot catalog unchanged");
 foreach(var e in entries){Check(e.Icon && !string.IsNullOrWhiteSpace(e.Description),e.Name+" icon and description");}
 owner=MetaProgressionPanel.Show(GameObject.Find("ScreenCanvas").transform);
 var timeline=owner.GetComponentInChildren<ProgressionTimeline>();
 foreach(var e in entries){timeline.Focus(e.Level);Check(timeline.GetComponentsInChildren<ExoticBlueprintTooltip>().Any(t=>t.Entry.Id==e.Id),e.Name+" visible at level "+e.Level);}
 timeline.Focus(95);yield return null;
 var target=timeline.GetComponentsInChildren<ExoticBlueprintTooltip>().First(t=>t.Entry.Id=="UltroniumKompass");
 var pointer=new PointerEventData(EventSystem.current){position=new Vector2(Screen.width-5,5)};
 target.OnPointerEnter(pointer);Canvas.ForceUpdateCanvases();
 var tip=GameObject.Find("Exotic Blueprint Tooltip");Check(tip && !tip.GetComponent<CanvasGroup>().blocksRaycasts,"Tooltip nonblocking");
 var corners=new Vector3[4];tip.GetComponent<RectTransform>().GetWorldCorners(corners);
 Check(corners.All(c=>c.x>=0 && c.x<=Screen.width && c.y>=0 && c.y<=Screen.height),"Tooltip clamped to viewport");
 foreach(var text in tip.GetComponentsInChildren<TMP_Text>()){text.ForceMeshUpdate();Check(!text.isTextTruncated,"Tooltip text fits: "+text.name);}
 yield return new WaitForEndOfFrame();Shot("Timeline-Tooltip");
 target.OnPointerExit(pointer);Check(!GameObject.Find("Exotic Blueprint Tooltip"),"Pointer exit hides tooltip");
 target.OnPointerEnter(pointer);timeline.Browse(1);Check(!GameObject.Find("Exotic Blueprint Tooltip"),"Timeline rebuild removes tooltip");
 owner.GetComponentsInChildren<Button>().First(b=>b.name=="Open Baupläne").onClick.Invoke();yield return null;
 var panel=Object.FindFirstObjectByType<BlueprintCollectionPanel>();var tips=panel.GetComponentsInChildren<ExoticBlueprintTooltip>();
 Check(tips.Length==23,"All collection exotics have tooltips");
 foreach(var e in entries.Where(e=>e.Preview)){var t=tips.First(x=>x.Entry.Id==e.Id);Check(t.transform.Find("State").GetComponent<TMP_Text>().text=="Vorschau",e.Name+" preview status");}
 var first=tips.First(t=>t.Entry.Id=="StimShot");first.OnPointerEnter(new PointerEventData(EventSystem.current){position=new Vector2(Screen.width*.5f,Screen.height*.55f)});
 yield return new WaitForEndOfFrame();Shot("Collection-Tooltip");
 first.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1)});Check(!GameObject.Find("Exotic Blueprint Tooltip"),"Scroll hides tooltip");
 first.OnPointerEnter(pointer);panel.Close();yield return null;Check(!GameObject.Find("Exotic Blueprint Tooltip"),"Closing removes tooltip");
 owner.Close();owner=null;yield return null;Check(!GameplayInputBlocker.IsBlocked,"Input ownership restored");
 File.AppendAllText(Report,"COMPLETE "+count+" checks\n");
 }
 void Shot(string name){var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Assets/Design/Tiefenhall/ExoticBlueprints/"+name+".png",t.EncodeToPNG());Object.Destroy(t);}
 void Cleanup(){if(cleaned)return;cleaned=true;if(owner)owner.Close();MetaProgression.TestDirectory=previous;Destroy(gameObject);}
 void OnDestroy(){Cleanup();}
}
