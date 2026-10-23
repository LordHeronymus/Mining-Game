using UnityEngine;

// The player keeps running in the background (runInBackground), so in a build a lost window focus (Alt-Tab, second
// screen) opens the pause menu: energy, fuses and autosave stop instead of ending the run unattended. The loss stays
// pending until the menu can open or the focus returns. The editor and the test bench play unfocused on purpose and
// keep running.
public static class RunFocusPause
{
    static bool focusLost;
#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Register() { focusLost = false; Application.focusChanged -= FocusChanged; Application.focusChanged += FocusChanged; }
#endif
    static void FocusChanged(bool focused) => focusLost = !focused;
    // Polled every frame by RunPauseMenu.Update, so a loss during loading, a blocking save, a time-stopping panel or
    // before the menu existed still pauses as soon as the run would go on unattended.
    internal static void Poll(RunPauseMenu menu)
    {
        if (focusLost && PausesNow(menu)) { focusLost = false; menu.Open(); }
    }
    internal static bool PausesNow(bool hasMenu) => PausesOnFocusLoss(focusLost, hasMenu, RunPauseMenu.IsOpen,
        Time.timeScale <= 0 || GamePause.IsPaused || MainMenuController.IsVisible,
        LoadingProgress.Active || RunNavigation.IsTransitioning,
        GameOverPanel.IsOpen || GameVictoryPanel.IsOpen || ExpeditionResultPanel.IsOpen || UltroniumAltarChamber.VictorySequenceActive,
        GameSaveSystem.IsBusy);
    // Only a running run pauses: no menu or panel that already stops the time, no loading or scene change, no end screen
    // or altar victory sequence, no blocking save. An input block alone does not count, because inventory, shop, storage
    // and workbench keep the time (and the energy drain) running. A background autosave keeps writing under the pause.
    internal static bool PausesOnFocusLoss(bool focusLost, bool hasMenu, bool menuOpen, bool timeStopped, bool loading, bool runEnded,
        bool saving) => focusLost && hasMenu && !menuOpen && !timeStopped && !loading && !runEnded && !saving;
}
