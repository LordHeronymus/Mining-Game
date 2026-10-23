#if UNITY_EDITOR && UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Die Testbank der Chats ("<Projekt> 2", Docs/Editor-Warteschlange.md) legt ihr Editor-Fenster beim Start auf einen
// eigenen virtuellen Windows-Desktop (Standard: Desktop 2), damit sie den Editor des Benutzers nicht ueberdeckt.
// Windows erlaubt das Verschieben nur fuer eigene Fenster, deshalb erledigt es der Testbank-Prozess selbst.
// Ziel-Desktop: Zahl in <Git Repos>/EditorQueue/<Testbank>.desktop (fehlt die Datei: 2; 0 schaltet es ab).
// Nur einmal pro Editor-Sitzung, damit ein spaeteres Verschieben von Hand nicht rueckgaengig gemacht wird.
[InitializeOnLoad]
public static class TestBenchDesktop
{
    const string PlacedKey = "TestBenchDesktop.Placed";
    const double GiveUpSeconds = 300;
    const string MenuPath = "Tools/Testbank/Auf eigenen Desktop verschieben";

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid desktopId);
    }

    static readonly bool isBench;
    static readonly string projectName;
    static readonly string settingPath;
    static double nextTry, giveUpAt;

    static TestBenchDesktop()
    {
        if (Application.isBatchMode) return;
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/');
        var owner = Regex.Match(projectRoot, @"^(.*?)[\\/]\.claude[\\/]worktrees[\\/]");
        string ownerRoot = owner.Success ? owner.Groups[1].Value : projectRoot;
        projectName = Path.GetFileName(ownerRoot);
        // Gleiche Regel wie Tools/editor-bench.ps1 und TestBenchAudioMute: die Testbank ist der Ordner "<Projekt> 2".
        isBench = !owner.Success && projectName.EndsWith(" 2", StringComparison.Ordinal);
        if (!isBench) return;
        string env = Environment.GetEnvironmentVariable("EDITOR_QUEUE_DIR");
        string queueDir = !string.IsNullOrEmpty(env) ? env : Path.Combine(Path.GetDirectoryName(ownerRoot) ?? ownerRoot, "EditorQueue");
        settingPath = Path.Combine(queueDir, projectName + ".desktop");
        if (SessionState.GetBool(PlacedKey, false)) return;
        giveUpAt = EditorApplication.timeSinceStartup + GiveUpSeconds;
        EditorApplication.update += TryPlaceOnStart;
    }

    static void TryPlaceOnStart()
    {
        double t = EditorApplication.timeSinceStartup;
        if (t < nextTry) return;
        nextTry = t + 2;
        if (t > giveUpAt) { Done(); return; }
        if (MainWindow() == IntPtr.Zero) return; // Hauptfenster existiert noch nicht
        Debug.Log("Testbank-Desktop: " + Place());
        Done();
    }

    static void Done()
    {
        EditorApplication.update -= TryPlaceOnStart;
        SessionState.SetBool(PlacedKey, true);
    }

    [MenuItem(MenuPath)]
    public static string Place()
    {
        int index = TargetDesktop();
        if (index <= 0) return "abgeschaltet (" + settingPath + ")";
        var desktops = DesktopIds();
        if (desktops.Length < index) return "nur " + desktops.Length + " Desktop(s), Desktop " + index + " fehlt; Fenster bleibt.";
        IntPtr hwnd = MainWindow();
        if (hwnd == IntPtr.Zero) return "Hauptfenster nicht gefunden.";
        try
        {
            var manager = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")));
            Guid target = desktops[index - 1];
            if (manager.GetWindowDesktopId(hwnd, out var current) == 0 && current == target) return "liegt bereits auf Desktop " + index + ".";
            int hr = manager.MoveWindowToDesktop(hwnd, ref target);
            return hr == 0 ? "auf Desktop " + index + " verschoben." : "Verschieben fehlgeschlagen (0x" + hr.ToString("X8") + ").";
        }
        catch (Exception ex) { return "Fehler: " + ex.Message; }
    }

    [MenuItem(MenuPath, true)]
    static bool PlaceValid() => isBench;

    static int TargetDesktop()
    {
        try
        {
            if (settingPath != null && File.Exists(settingPath) && int.TryParse(File.ReadAllText(settingPath).Trim(), out int value)) return value;
        }
        catch (IOException) { }
        return 2;
    }

    static Guid[] DesktopIds()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
        if (!(key?.GetValue("VirtualDesktopIDs") is byte[] bytes)) return Array.Empty<Guid>();
        var ids = new Guid[bytes.Length / 16];
        for (int i = 0; i < ids.Length; i++)
        {
            var part = new byte[16];
            Array.Copy(bytes, i * 16, part, 0, 16);
            ids[i] = new Guid(part);
        }
        return ids;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);

    // Nur das echte Editor-Hauptfenster ("<Projekt> - <Szene> - ..."), nicht Lade- oder Splash-Fenster.
    // Mono liefert Process.MainWindowTitle nicht, deshalb den Titel ueber Win32 lesen.
    static IntPtr MainWindow()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        IntPtr hwnd = process.MainWindowHandle;
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        var title = new System.Text.StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        return title.ToString().StartsWith(projectName + " - ", StringComparison.Ordinal) ? hwnd : IntPtr.Zero;
    }
}
#endif
