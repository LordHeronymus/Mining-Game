using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class HomeLandscapeSession {
 public static object Main() {
  if(EditorApplication.isPlaying)throw new System.Exception("Stop before fresh home test");
  if(!SessionState.GetBool("HomeLandscapeCaptured",false)) {
   SessionState.SetString("HomeLandscapePreviousStart",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
   SessionState.SetBool("HomeLandscapeReload",EditorSettings.enterPlayModeOptionsEnabled);
   SessionState.SetInt("HomeLandscapeOptions",(int)EditorSettings.enterPlayModeOptions);
   SessionState.SetBool("HomeLandscapeCaptured",true);
  }
  EditorSettings.enterPlayModeOptionsEnabled=false;
  EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/MainMenu.unity");
  EditorApplication.EnterPlaymode();return "Fresh MainMenu start; original scene preserved";
 }
 public static object Finish() {
  if(EditorApplication.isPlaying)throw new System.Exception("Stop first");
  var path=SessionState.GetString("HomeLandscapePreviousStart","");
  EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
  SessionState.EraseString("HomeLandscapePreviousStart");
  EditorSettings.enterPlayModeOptionsEnabled=SessionState.GetBool("HomeLandscapeReload",true);
  EditorSettings.enterPlayModeOptions=(EnterPlayModeOptions)SessionState.GetInt("HomeLandscapeOptions",3);
  SessionState.EraseBool("HomeLandscapeCaptured");
  return new {scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,reload=EditorSettings.enterPlayModeOptionsEnabled,options=(int)EditorSettings.enterPlayModeOptions,save=GameSaveSystem.TestDirectory,meta=MetaProgression.TestDirectory};
 }
}
