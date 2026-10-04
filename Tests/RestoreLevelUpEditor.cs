using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class RestoreLevelUpEditor {
 [System.Serializable]public class State{public string scene;public bool reload;public int options;}
 public static object Main(){
  if(EditorApplication.isPlaying)throw new System.Exception("Still playing");
  var s=JsonUtility.FromJson<State>(System.IO.File.ReadAllText("Temp/LevelUpEditorState.json"));
  EditorSettings.enterPlayModeOptionsEnabled=s.reload;EditorSettings.enterPlayModeOptions=(EnterPlayModeOptions)s.options;
  if(!string.IsNullOrEmpty(s.scene)&&UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=s.scene)EditorSceneManager.OpenScene(s.scene);
  return new {s.scene,s.reload,s.options,save=GameSaveSystem.TestDirectory,meta=MetaProgression.TestDirectory};
 }
}
