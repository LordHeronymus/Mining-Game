using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class StartLevelUpCheck {
 public static object Main(){
  if(EditorApplication.isPlaying)throw new System.Exception("Already playing");
  System.IO.File.WriteAllText("Temp/LevelUpEditorState.json",JsonUtility.ToJson(new State{scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,reload=EditorSettings.enterPlayModeOptionsEnabled,options=(int)EditorSettings.enterPlayModeOptions}));
  EditorSettings.enterPlayModeOptionsEnabled=false;
  EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");EditorApplication.isPlaying=true;return "Starting fresh MainMenu";
 }
 [System.Serializable]public class State{public string scene;public bool reload;public int options;}
}
