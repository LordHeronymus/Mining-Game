using UnityEngine;
using UnityEditor;
public static class HomeLandscapeStatus {
 public static object Main()=>new { play=EditorApplication.isPlaying,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,menu=MainMenuController.IsVisible,blocked=GameplayInputBlocker.IsBlocked,save=GameSaveSystem.TestDirectory,meta=MetaProgression.TestDirectory,landscape=Object.FindFirstObjectByType<HomeCaveVisual>()?.Layer,meadow=HomeLandscape.MeadowClip?.name };
}
