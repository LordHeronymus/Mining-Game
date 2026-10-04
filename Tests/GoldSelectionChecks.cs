using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
using UnityEngine.EventSystems;
public static class GoldSelectionPreview {
public static object Main(){if(!Application.isPlaying || !MainMenuController.IsVisible)throw new Exception("Start at homescreen in Play Mode");var root=new GameObject("Gold selection preview");Object.DontDestroyOnLoad(root);root.AddComponent<GoldSelectionRunner>();return "Started";}
}
public sealed class GoldSelectionRunner:MonoBehaviour {
string previous, previousMeta, directory;bool restored;
const string Output="Assets/Design/Tiefenhall/SelectionBevels/Implemented";
void Check(bool value,string message){if(!value)throw new Exception(message);File.AppendAllText("Temp/GoldSelectionChecks.txt",message+"\n");}
IEnumerator Start(){
previous=GameSaveSystem.TestDirectory;previousMeta=MetaProgression.TestDirectory;
var dir=Path.GetFullPath("Temp/GoldSelectionSaves-"+DateTime.UtcNow.Ticks);Directory.CreateDirectory(dir);
File.Copy("Temp/SelectionEdit-639266992421592508/slot-3.thsave",Path.Combine(dir,"slot-3.thsave"));
File.Copy("Temp/NamedRun-639266972388098671/slot-1.thsave",Path.Combine(dir,"slot-1.thsave"));directory=dir;GameSaveSystem.TestDirectory=dir;MetaProgression.TestDirectory=Path.Combine(dir,"Profile");Directory.CreateDirectory(Output);File.WriteAllText("Temp/GoldSelectionChecks.txt","");
var routines=new Stack<IEnumerator>();routines.Push(Run());
while(routines.Count>0){if(GameSaveSystem.TestDirectory!=directory){File.AppendAllText("Temp/GoldSelectionChecks.txt","INTERRUPTED: another task changed test scope\n");Cleanup();yield break;}object current=null;bool more=false;Exception fail=null;try{more=routines.Peek().MoveNext();if(more)current=routines.Peek().Current;}catch(Exception e){fail=e;}
if(fail!=null){File.AppendAllText("Temp/GoldSelectionChecks.txt","FAIL: "+fail);Cleanup();yield break;}
if(!more){routines.Pop();continue;}if(current is IEnumerator nested)routines.Push(nested);else yield return current;
}
File.AppendAllText("Temp/GoldSelectionChecks.txt","PASS\n");Cleanup();
}
IEnumerator Shot(string name){yield return new WaitForSecondsRealtime(.7f);yield return null;yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Output+"/"+name+".png",t.EncodeToPNG());Object.Destroy(t);}
void Styled(Image image,string name){Check(image && image.material && image.material.shader.name=="UI/Tiefenhall/PanelWoodTone",name+" uses the dark wood shader");}
IEnumerator Run(){
while(LoadingProgress.Active || RunNavigation.IsTransitioning)yield return null;
Styled(GameObject.Find("Home Layout/Menu Board").GetComponent<Image>(),"home");yield return Shot("Home");
var settings=Object.FindFirstObjectByType<SettingsPanel>(FindObjectsInactive.Include);settings.Open();yield return null;
Styled(settings.transform.Find("Layout/Background").GetComponent<Image>(),"settings");yield return Shot("Settings");
settings.GetComponentsInChildren<Button>().First(b=>b.name=="Fortsetzen").onClick.Invoke();yield return null;
var parent=GameObject.Find("ScreenCanvas").transform;
NewGamePanel.Show(parent);yield return null;var setup=Object.FindFirstObjectByType<NewGamePanel>();
Styled(setup.transform.Find("New Game Layout/Board").GetComponent<Image>(),"new game");yield return Shot("NewGame");
setup.GetComponentsInChildren<Button>().First(b=>b.name=="Cancel New Game").onClick.Invoke();yield return null;
var saves=SaveSlotPanel.Show(parent,false);yield return null;yield return Shot("SaveSlots");
var slot=saves.GetComponentsInChildren<Button>().First(b=>b.GetComponent<HomeButtonFeedback>().primary);
var feedback=slot.GetComponent<HomeButtonFeedback>();var overlay=slot.transform.Find("Selection").GetComponent<Image>();
Check(feedback.primary && overlay.color.a>.98f,"saved game gets illustrated gold selection");Check(overlay.sprite.name=="SaveSelected" && overlay.sprite.texture.name=="SelectionButtons-v2","selection uses the new sculpted gold bevel artwork");
Check(HomeUi.Sprite("SaveButton").rect.size==HomeUi.Sprite("SaveActive").rect.size && HomeUi.Sprite("SaveActive").rect.size==overlay.sprite.rect.size && HomeUi.Sprite("SaveButton").border==overlay.sprite.border,"three button states share dimensions and slicing borders");
Check(HomeUi.Sprite("SelectionCardNormal").rect.size==HomeUi.Sprite("SelectionCardSelected").rect.size && HomeUi.Sprite("SelectionCardNormal").border==HomeUi.Sprite("SelectionCardHover").border,"square card states share dimensions and slicing borders");
EventSystem.current.SetSelectedGameObject(null);yield return new WaitForSecondsRealtime(.6f);
Check(overlay.color.a>.98f,"chosen save stays gold without input focus");
var backButton=saves.GetComponentsInChildren<Button>().First(b=>b.name=="Back");var backFeedback=backButton.GetComponent<HomeButtonFeedback>();
var hover=backButton.transform.Find("Highlight").GetComponent<Image>();var pointer=new PointerEventData(EventSystem.current);
var original=backButton.GetComponent<RectTransform>().anchoredPosition;
backFeedback.OnPointerEnter(pointer);yield return null;
Check(hover.color.a>0 && hover.color.a<.95f,"hover fades in gradually");yield return new WaitForSecondsRealtime(.6f);
Check(hover.color.a>.98f && hover.sprite.name=="SaveActive" && backButton.transform.Find("Selection").GetComponent<Image>().color.a<.01f,"hover lights only the gold frame");
Check(backButton.GetComponent<RectTransform>().anchoredPosition==original,"hover retains button position");
yield return Shot("SaveHover");backFeedback.OnPointerExit(pointer);yield return new WaitForSecondsRealtime(.6f);
Check(hover.color.a<.01f,"hover fades back to normal wood");
var second=saves.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith("Slot ") && b!=slot);
EventSystem.current.SetSelectedGameObject(second.gameObject);second.onClick.Invoke();yield return new WaitForSecondsRealtime(.7f);
Check(!feedback.primary && overlay.color.a<.01f,"previous save loses gold after selection change");
Check(second.GetComponent<HomeButtonFeedback>().primary && second.transform.Find("Selection").GetComponent<Image>().color.a>.98f,"new save becomes gold");
feedback.OnSelect(new BaseEventData(EventSystem.current));yield return new WaitForSecondsRealtime(.6f);
Check(overlay.color.a<.01f,"old save focus cannot create a second logical selection");feedback.OnDeselect(new BaseEventData(EventSystem.current));
yield return Shot("SaveSelectionChanged");
saves.GetComponentsInChildren<Button>().First(b=>b.name=="Edit Save").onClick.Invoke();yield return null;yield return Shot("EditSave");
saves.GetComponentsInChildren<Button>().First(b=>b.name=="Cancel Edit").onClick.Invoke();yield return null;
saves.GetComponentsInChildren<Button>().First(b=>b.name=="Back").onClick.Invoke();yield return null;
Check(RunNavigation.LoadGame(3,out string error),"load isolated save");
while(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!=RunNavigation.GameScene || !Object.FindFirstObjectByType<PlayerMovement>() || RunNavigation.IsTransitioning || LoadingProgress.Active)yield return null;
yield return new WaitForSecondsRealtime(.8f);
var camera=Camera.main;var player=Object.FindFirstObjectByType<PlayerMovement>();
Check(camera && player && camera.GetComponent<CameraFollow>().enabled && camera.GetComponent<CameraFollow>().target==player.transform && camera.GetComponent<CameraWorldBorderClamp>().enabled,"camera follows player after fresh play start");
Check(Object.FindFirstObjectByType<CompactHud>().GetComponentsInChildren<GoldButtonFeedback>().Count(f=>f.Chosen)==1,"hotbar has one persistent gold selection");
Check(Object.FindFirstObjectByType<CompactHud>().GetComponentsInChildren<GoldButtonFeedback>().First(f=>f.Chosen).transform.Find("Gold Fill").GetComponent<Image>().sprite.texture.name=="SelectionCards-v2","hotbar uses proportional square gold artwork");
var activeHotbar=Object.FindFirstObjectByType<CompactHud>().GetComponentsInChildren<GoldButtonFeedback>().First(f=>f.Chosen);
activeHotbar.OnSelect(new BaseEventData(EventSystem.current));activeHotbar.SetChosen(false);yield return new WaitForSecondsRealtime(.6f);
Check(activeHotbar.transform.Find("Gold Fill").GetComponent<Image>().color.a<.01f,"old keyboard focus cannot retain a gold logical selection");activeHotbar.SetChosen(true);activeHotbar.OnDeselect(new BaseEventData(EventSystem.current));
InventoryManager.Instance.Add(Resources.FindObjectsOfTypeAll<ItemSO>().First(i=>i.displayName=="Eisen"),3);
var shop=Object.FindFirstObjectByType<ShopPanel>(FindObjectsInactive.Include);shop.ShowPanel(true);shop.ShowBuyPage();yield return null;
var theme=Object.FindFirstObjectByType<ShopVisualTheme>(FindObjectsInactive.Include);Styled(theme.GetComponent<Image>(),"shop");yield return Shot("ShopBuy");
Check(Object.FindObjectsByType<GoldButtonFeedback>(FindObjectsSortMode.None).Any(f=>f.Chosen),"shop blueprint and tab have logical gold selection");
Check(shop.GetComponentsInChildren<GoldButtonFeedback>().All(f=>f.GetComponentsInChildren<Image>().Where(i=>i.name=="Gold Fill").All(i=>i.sprite.texture.name.StartsWith("Selection"))),"shop uses shared painted selection artwork");
Check(shop.GetComponentsInChildren<GoldButtonFeedback>().Where(f=>f.name.StartsWith("Blueprint ")).All(f=>f.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Where(t=>t.transform.parent==f.transform).All(t=>{var corners=new Vector3[4];t.rectTransform.GetWorldCorners(corners);var r=f.GetComponent<RectTransform>();return r.InverseTransformPoint(corners[0]).y-r.rect.yMin>=39;})),"shop card names and prices clear the bottom metal rail");
shop.ShowSellPage();yield return null;yield return Shot("ShopSell");
var oreSlot=shop.GetComponentsInChildren<ShopSlot>().First(s=>s.Item && s.Item.displayName=="Eisen");oreSlot.GetComponent<Button>().onClick.Invoke();yield return new WaitForSecondsRealtime(.6f);
Check(oreSlot.GetComponent<GoldButtonFeedback>().Chosen && oreSlot.transform.Find("Gold Fill").GetComponent<Image>().color.a>.98f,"sell card click retains the actual gold selection");
shop.ShowPanel(false);while(GameplayInputBlocker.IsBlocked)yield return null;
var work=Object.FindFirstObjectByType<WorkbenchPanel>(FindObjectsInactive.Include);work.ShowPanel(true);yield return null;
Styled(work.GetComponentsInChildren<Image>().First(i=>i.sprite && i.sprite.texture.name=="WorkshopBackground-v2"),"workbench");yield return Shot("Workbench");
Check(work.GetComponentsInChildren<GoldButtonFeedback>().Count(f=>f.Chosen)>=2,"workbench category and recipe are selected in gold");
Check(work.GetComponentsInChildren<GoldButtonFeedback>().Where(f=>f.GetComponent<RectTransform>().rect.height>100).All(f=>f.GetComponentsInChildren<TMPro.TextMeshProUGUI>().Where(t=>t.transform.parent==f.transform && t.text.Length>0).All(t=>{var corners=new Vector3[4];t.rectTransform.GetWorldCorners(corners);var r=f.GetComponent<RectTransform>();return r.InverseTransformPoint(corners[0]).y-r.rect.yMin>=22;})),"workbench recipe captions clear the bottom metal rail");work.ShowPanel(false);yield return null;
var inventory=Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);File.AppendAllText("Temp/GoldSelectionChecks.txt","Inventory sprites: "+inventory.slotSprite.name+" / "+inventory.slotSprite.texture.name+" / "+inventory.selectionSprite.name+"\n");InventoryManager.Instance.Add(Resources.FindObjectsOfTypeAll<ItemSO>().First(i=>i.displayName=="Eisen"),3);inventory.ShowPanel();yield return null;
Styled(inventory.GetComponentsInChildren<Image>().First(i=>i.sprite && i.sprite.texture.name=="InventoryFrame"),"inventory");yield return Shot("Inventory");Check(inventory.GetComponentsInChildren<GoldButtonFeedback>().Count(f=>f.Chosen)>=2,"inventory filter and item get gold selection");inventory.HidePanel();yield return new WaitForSecondsRealtime(.3f);
RunNavigation.MainMenu();while(RunNavigation.IsTransitioning)yield return null;
}
void Cleanup(){GameSaveSystem.TestDirectory=previous;MetaProgression.TestDirectory=previousMeta;restored=true;Object.Destroy(gameObject);}
void OnDestroy(){if(!restored){GameSaveSystem.TestDirectory=previous;MetaProgression.TestDirectory=previousMeta;}}
}
