<#
.SYNOPSIS
  Fokus-Waechter fuer die Unity Editoren der Testbaenke ("Mining Game 2", "Mining Game 3"): Holt Unity ein Testbank-Fenster von selbst nach
  vorne (Stand laden, Play-Mode, Neu laden), aktiviert er sofort wieder das Fenster, in dem der Benutzer war. Liegt die
  Testbank auf einem anderen virtuellen Desktop, wechselt Windows damit auch auf den Desktop des Benutzers zurueck.
  Wechselt der Benutzer selbst zur Testbank (Klick auf das Fenster, Taskleiste, Task-Ansicht, Win+Strg+Pfeil, Alt+Tab),
  bleibt alles, wie es ist. Laeuft im Dienst der Editor-Warteschlange (Tools/editor-queue-server.ps1).
  Doku: Docs/Editor-Warteschlange.md

.EXAMPLE
  Tools/editor-focus.ps1 status          # laeuft der Waechter, ein/aus, letzte Eingriffe aus dem Protokoll
  Tools/editor-focus.ps1 off             # abschalten (Datei <Git Repos>/EditorQueue/<Testbank>.focus-guard-off; -Project fuer eine)
  Tools/editor-focus.ps1 on              # wieder einschalten
  Tools/editor-focus.ps1 watch -Trace    # Waechter im Vordergrund (nur wenn der Dienst ihn nicht schon betreibt), zeigt jeden Fensterwechsel
  Tools/editor-focus.ps1 watch -DryRun   # nur beobachten und protokollieren, nichts zurueckholen
  Tools/editor-focus.ps1 start           # im laufenden Prozess starten und sofort zurueckkehren (nutzt der Dienst)

  Exit-Codes: 0 ok, 1 Fehler, 4 Waechter laeuft schon in einem anderen Prozess (watch/start).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'watch', 'status', 'on', 'off', 'help')]
    [string]$Command = 'help',
    # Testbaenke (Ordnernamen); ohne Angabe alle.
    [string[]]$Project,
    [string]$ProjectRoot,
    [string]$QueueDir,
    # So lange vor dem Fensterwechsel zaehlt ein Klick des Benutzers auf die Testbank bzw. auf Taskleiste/Task-Ansicht als Absicht.
    [int]$ClickMs = 1500,
    # So lange vor dem Fensterwechsel zaehlt eine Wechsel-Taste (Win+..., Alt+Tab, Alt+Esc) als Absicht.
    [int]$KeyMs = 2500,
    [switch]$DryRun,
    [switch]$Trace
)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
if ($Command -eq 'help') { Get-Help $PSCommandPath -Detailed | Out-String | Write-Output; exit 0 }

# --- Ordner (wie Tools/editor-dialog.ps1) -------------------------------------

if (-not $ProjectRoot) { $ProjectRoot = Split-Path $PSScriptRoot -Parent }
$repoRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
$ownerMatch = [regex]::Match($repoRoot, '^(.*?)[\\/]\.claude[\\/]worktrees[\\/]')
$ownerRoot = if ($ownerMatch.Success) { $ownerMatch.Groups[1].Value } else { $repoRoot }
$baseName = (Split-Path $ownerRoot -Leaf) -replace ' \d+$', ''
$reposDir = Split-Path $ownerRoot -Parent
# Ohne -Project alle Testbaenke "<Projekt> 2" und "<Projekt> 3" (gleiche Regel wie Assets/Editor/TestBenchNames.cs).
if (-not $Project) {
    $Project = @(Get-ChildItem -LiteralPath $reposDir -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match ('^' + [regex]::Escape($baseName) + ' [23]$') -and (Test-Path -LiteralPath (Join-Path $_.FullName '.git')) } |
        Sort-Object Name | ForEach-Object { $_.Name })
    if (-not $Project) { $Project = @("$baseName 2") }
}
if ($Project -contains $baseName) {
    Write-Output "'$baseName' ist der Editor des Benutzers. editor-focus arbeitet nur mit Testbaenken."
    exit 1
}
if (-not $QueueDir) { $QueueDir = $env:EDITOR_QUEUE_DIR }
if (-not $QueueDir) { $QueueDir = Join-Path $reposDir 'EditorQueue' }
$projectPaths = @($Project | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $reposDir $_)).TrimEnd('\') })
$logFiles = @($Project | ForEach-Object { Join-Path $QueueDir "$_.log" })
$activityFiles = @($Project | ForEach-Object { Join-Path $QueueDir "$_.activity.json" })
$offFiles = @($Project | ForEach-Object { Join-Path $QueueDir "$_.focus-guard-off" })
# Ein Waechter fuer alle Testbaenke dieses Ordners: er muss alle kennen, sonst holt er beim Zurueckholen eine andere Testbank nach vorne.
$mutexName = 'Local\EditorQueueFocusGuard-' + ($reposDir.ToLowerInvariant() -replace '[^a-z0-9]', '_')

# --- Waechter (eigener Thread mit Nachrichtenschleife) --------------------------

if (-not ('BenchFocusGuard' -as [type])) {
    Add-Type -ReferencedAssemblies System.Management -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

[ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IVirtualDesktopManager {
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
    [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid id);
}

public static class BenchFocusGuard {
    // --- Win32 ---
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    const uint WM_TIMER = 0x113, GA_ROOT = 2;
    const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80;
    const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;
    const byte VK_MENU = 0x12, VK_NOMENU = 0xE8;   // 0xE8: nicht belegt, verhindert, dass Alt allein das Menue oeffnet

    // --- Einstellungen (vor Start setzen) ---
    // Je Testbank (gleicher Index): Ordner, Aktivitaetssignal, Abschalt-Datei, Protokoll.
    public static string[] BenchPaths, ActivityFiles, OffFiles, LogFiles;
    public static int ClickMs = 1500, KeyMs = 2500;
    public static bool DryRun, Trace, Echo;
    public static int MaxRestores = 6, RestoreWindowMs = 20000, PauseMs = 60000;

    // --- Zustand ---
    public static int[] BenchPids;   // Prozess je Testbank, 0 = laeuft nicht (vom Timer gesetzt, im Hook-Thread gelesen)
    public static int Restored, Allowed, Failed;
    public static readonly ConcurrentQueue<string> Messages = new ConcurrentQueue<string>();
    static readonly BlockingCollection<KeyValuePair<string, string>> logQueue = new BlockingCollection<KeyValuePair<string, string>>();
    static Thread thread, logger;
    static Timer pidTimer;
    static Mutex mutex;
    static WinEventProc winProc = OnForeground;
    static HookProc mouseProc = OnMouse, keyProc = OnKey;
    static IntPtr winHook, mouseHook, keyHook;
    static IVirtualDesktopManager vdm;

    static IntPtr lastFg; static uint lastFgPid;
    static readonly List<IntPtr> history = new List<IntPtr>();   // zuletzt aktive Fenster ausserhalb der Testbank, neuestes zuerst
    static long clickTick = long.MinValue / 4; static IntPtr clickWnd; static uint clickPid;
    static long keyTick = long.MinValue / 4; static string keyName; static bool winDown;
    static readonly Queue<long> restoreTicks = new Queue<long>();
    static long pausedUntil; static bool[] offLogged;
    static DateTime[] pidStarts;
    static readonly Dictionary<uint, string> procNames = new Dictionary<uint, string>();

    static long Now() { return (long)GetTickCount64(); }

    // Startet den Waechter-Thread. false = laeuft schon (in diesem oder einem anderen Prozess).
    public static bool Start() {
        if (thread != null) return false;
        BenchPids = new int[BenchPaths.Length];
        pidStarts = new DateTime[BenchPaths.Length];
        offLogged = new bool[BenchPaths.Length];
        var ready = new ManualResetEvent(false);
        bool ok = false;
        thread = new Thread(() => {
            bool created;
            mutex = new Mutex(true, MutexName, out created);
            if (!created) { mutex.Dispose(); mutex = null; ready.Set(); return; }
            ok = true;
            ready.Set();
            Loop();
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.WaitOne();
        if (!ok) { thread = null; return false; }
        logger = new Thread(LogLoop); logger.IsBackground = true; logger.Start();
        pidTimer = new Timer(_ => RefreshPid(), null, 0, 3000);
        return true;
    }
    public static string MutexName;
    public static bool Running { get { return thread != null; } }

    static void Loop() {
        try { vdm = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"))); } catch { vdm = null; }
        lastFg = GetForegroundWindow(); GetWindowThreadProcessId(lastFg, out lastFgPid);
        Remember(lastFg, lastFgPid);
        winHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        InstallInputHooks();
        // Windows entfernt Low-Level-Hooks stillschweigend, wenn sie einmal zu langsam antworten: regelmaessig neu setzen.
        SetTimer(IntPtr.Zero, IntPtr.Zero, 30000, IntPtr.Zero);
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) {
            if (msg.message == WM_TIMER && msg.hwnd == IntPtr.Zero) { InstallInputHooks(); continue; }
            TranslateMessage(ref msg); DispatchMessage(ref msg);
        }
    }

    static void InstallInputHooks() {
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
        IntPtr mod = GetModuleHandle(null);
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, mod, 0);
        keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyProc, mod, 0);
    }

    // --- Eingaben des Benutzers (nur merken, nichts Langsames hier) ---
    static IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam) {
        if (code >= 0) {
            int m = wParam.ToInt32();
            if (m == 0x201 || m == 0x204 || m == 0x207 || m == 0x20B) {   // Maustaste gedrueckt (links, rechts, mitte, X)
                POINT p; p.X = Marshal.ReadInt32(lParam, 0); p.Y = Marshal.ReadInt32(lParam, 4);
                IntPtr w = GetAncestor(WindowFromPoint(p), GA_ROOT);
                uint pid; GetWindowThreadProcessId(w, out pid);
                clickWnd = w; clickPid = pid; clickTick = Now();
            }
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    static IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam) {
        if (code >= 0) {
            int m = wParam.ToInt32();
            int vk = Marshal.ReadInt32(lParam, 0);
            int flags = Marshal.ReadInt32(lParam, 8);
            bool injected = (flags & 0x10) != 0, alt = (flags & 0x20) != 0;
            bool down = m == 0x100 || m == 0x104;
            if (vk == 0x5B || vk == 0x5C) winDown = down;
            if (down && !injected) {
                string k = null;
                if (vk == 0x5B || vk == 0x5C) k = "Win";
                else if (winDown) k = "Win+Taste";
                else if (alt && vk == 0x09) k = "Alt+Tab";
                else if (alt && vk == 0x1B) k = "Alt+Esc";
                if (k != null) { keyName = k; keyTick = Now(); }
            }
        }
        return CallNextHookEx(keyHook, code, wParam, lParam);
    }

    // --- Fensterwechsel ---
    static void OnForeground(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thr, uint time) {
        try { Handle(hwnd); } catch (Exception e) { Log(0, "FOKUS Fehler im Waechter: " + e.Message); }
    }

    // Index der Testbank, zu der der Prozess gehoert, sonst -1.
    static int BenchOf(uint pid) {
        int[] pids = BenchPids;
        for (int i = 0; i < pids.Length; i++) if (pids[i] != 0 && (uint)pids[i] == pid) return i;
        return -1;
    }

    static void Handle(IntPtr hwnd) {
        if (hwnd == IntPtr.Zero) return;
        uint pid; GetWindowThreadProcessId(hwnd, out pid);
        int k = BenchOf(pid);
        IntPtr prev = lastFg; uint prevPid = lastFgPid;
        lastFg = hwnd; lastFgPid = pid;
        if (Trace) Show(string.Format("Vordergrund: {0}{1}", Describe(hwnd, pid), k >= 0 ? "  [Testbank " + Path.GetFileName(BenchPaths[k]) + "]" : ""));
        if (k < 0) { Remember(hwnd, pid); return; }
        // Innerhalb der Testbank (Dialog, Fortschrittsfenster) oder von einer Testbank zur anderen: der Benutzer war schon dort.
        if (BenchOf(prevPid) >= 0) return;
        uint bench = pid;

        if (File.Exists(OffFiles[k])) {
            if (!offLogged[k]) { Log(k, "FOKUS Waechter abgeschaltet (" + Path.GetFileName(OffFiles[k]) + "): Testbank-Fenster bleibt vorne."); offLogged[k] = true; }
            return;
        }
        offLogged[k] = false;

        long now = Now();
        string intent = null;
        if (now - clickTick <= ClickMs && (clickPid == bench || IsShell(clickPid)))
            intent = string.Format("Klick auf {0} vor {1} ms", clickPid == bench ? "die Testbank" : ProcName(clickPid), now - clickTick);
        else if (now - keyTick <= KeyMs)
            intent = string.Format("Taste {0} vor {1} ms", keyName, now - keyTick);
        string from = prev != IntPtr.Zero ? Describe(prev, prevPid) : "(kein Fenster)";
        if (intent != null) {
            Allowed++;
            Log(k, string.Format("FOKUS Testbank bewusst geoeffnet ({0}), bleibt vorne. Vorher: {1}", intent, from));
            return;
        }

        IntPtr target = PickTarget();
        if (target == IntPtr.Zero) { Log(k, "FOKUS Testbank kam von selbst nach vorne, aber kein Fenster zum Zurueckholen. Vorher: " + from); return; }
        uint tpid; GetWindowThreadProcessId(target, out tpid);
        string to = Describe(target, tpid);

        if (now < pausedUntil) return;
        while (restoreTicks.Count > 0 && now - restoreTicks.Peek() > RestoreWindowMs) restoreTicks.Dequeue();
        if (restoreTicks.Count >= MaxRestores) {
            pausedUntil = now + PauseMs;
            Log(k, string.Format("FOKUS Testbank holt sich laufend den Fokus ({0}x in {1} s): Waechter pausiert {2} s.", restoreTicks.Count, RestoreWindowMs / 1000, PauseMs / 1000));
            return;
        }
        if (DryRun) { Log(k, "FOKUS (Probelauf) Testbank kam von selbst nach vorne, wuerde zurueckholen: " + to); return; }

        restoreTicks.Enqueue(now);
        var sw = Stopwatch.StartNew();
        string how = Restore(target);
        sw.Stop();
        if (how != null) {
            Restored++;
            Log(k, string.Format("FOKUS Testbank kam von selbst nach vorne ({0}), zurueck zu {1} ({2}, {3} ms{4}).",
                Describe(hwnd, pid), to, how, sw.ElapsedMilliseconds, DesktopNote(target)));
        }
        else {
            Failed++;
            Log(k, string.Format("FOKUS Zurueckholen von {0} hat nicht geklappt (Windows hat es verweigert); Testbank bleibt vorne.", to));
        }
    }

    // Aktiviert ein Fenster aus diesem Hintergrundprozess. Windows erlaubt das nicht jedem Prozess, deshalb in Stufen.
    // Stufen: direkt; kurzer Alt-Tastendruck (gibt Vordergrundrechte, die unbelegte Taste dazwischen verhindert das Alt-Menue);
    // zuletzt AttachThreadInput an den Unity-Thread (kann warten, solange Unity beschaeftigt ist, z. B. beim Play-Start).
    static string Restore(IntPtr t) {
        var sw = Stopwatch.StartNew();
        if (SetForegroundWindow(t) && GetForegroundWindow() == t) return "direkt";
        long direct = sw.ElapsedMilliseconds;
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_NOMENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_NOMENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(t);
        if (GetForegroundWindow() == t) return string.Format("Alt-Taste, direkt {0} ms abgelehnt", direct);
        long alt = sw.ElapsedMilliseconds;
        uint me = GetCurrentThreadId(), dummy;
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out dummy);
        uint tThread = GetWindowThreadProcessId(t, out dummy);
        bool a1 = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        bool a2 = tThread != 0 && tThread != me && tThread != fgThread && AttachThreadInput(me, tThread, true);
        try { BringWindowToTop(t); SetForegroundWindow(t); }
        finally { if (a1) AttachThreadInput(me, fgThread, false); if (a2) AttachThreadInput(me, tThread, false); }
        if (GetForegroundWindow() == t) return string.Format("AttachThreadInput, direkt {0} ms / Alt {1} ms abgelehnt", direct, alt - direct);
        return null;
    }

    // Zuletzt benutztes Fenster ausserhalb aller Testbaenke.
    static IntPtr PickTarget() {
        foreach (IntPtr h in history) {
            if (!IsWindow(h) || !IsWindowVisible(h) || IsIconic(h)) continue;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (BenchOf(pid) < 0) return h;
        }
        return IntPtr.Zero;
    }

    // Merkt sich echte Anwendungsfenster (keine Taskleiste, kein Desktop, keine Werkzeugfenster) als Ziel zum Zurueckholen.
    static void Remember(IntPtr hwnd, uint pid) {
        if (hwnd == IntPtr.Zero) return;
        IntPtr root = GetAncestor(hwnd, GA_ROOT);
        if (root != IntPtr.Zero) hwnd = root;
        string cls = ClassOf(hwnd);
        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "XamlExplorerHostIslandWindow"
            || cls == "MultitaskingViewFrame" || cls == "ForegroundStaging" || cls == "TaskListThumbnailWnd" || cls == "Windows.UI.Core.CoreWindow") return;
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return;
        history.Remove(hwnd);
        history.Insert(0, hwnd);
        if (history.Count > 8) history.RemoveAt(history.Count - 1);
    }

    static bool IsShell(uint pid) {
        string n = ProcName(pid).ToLowerInvariant();
        return n == "explorer" || n == "shellexperiencehost" || n == "startmenuexperiencehost" || n == "searchhost" || n == "searchapp" || n == "shellhost";
    }

    static string ProcName(uint pid) {
        string n;
        lock (procNames) { if (procNames.TryGetValue(pid, out n)) return n; }
        try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = "?"; }
        lock (procNames) { if (procNames.Count > 200) procNames.Clear(); procNames[pid] = n; }
        return n;
    }

    static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
    static string Caption(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

    static string Describe(IntPtr h, uint pid) {
        string t = Caption(h);
        if (t.Length > 60) t = t.Substring(0, 60) + "...";
        return string.Format("'{0}' ({1} {2})", t, ProcName(pid), pid);
    }

    static string DesktopNote(IntPtr target) {
        if (vdm == null) return "";
        int on;
        if (vdm.IsWindowOnCurrentVirtualDesktop(target, out on) != 0) return "";
        return on != 0 ? ", Desktop des Benutzers aktiv" : ", Desktop noch nicht zurueck";
    }

    // --- Prozesse der Testbaenke (Unity.exe mit -projectpath auf den Testbank-Ordner); nicht im Hook-Thread ---
    static void RefreshPid() {
        for (int k = 0; k < BenchPaths.Length; k++) {
            try { RefreshPid(k); } catch { }
        }
    }

    static void RefreshPid(int k) {
        int pid = BenchPids[k];
        if (pid != 0) {
            try {
                var p = Process.GetProcessById(pid);
                if (!p.HasExited && p.StartTime == pidStarts[k]) {
                    int hint = ActivityPid(k);
                    if (hint == 0 || hint == pid) return;
                }
            } catch { }
        }
        int found = FindBench(k);
        if (found != BenchPids[k]) {
            if (found != 0) { try { pidStarts[k] = Process.GetProcessById(found).StartTime; } catch { } }
            BenchPids[k] = found;
            string name = Path.GetFileName(BenchPaths[k]);
            if (Trace) Show(found != 0 ? "Testbank-Editor " + name + ": Prozess " + found : "Testbank-Editor " + name + " laeuft nicht.");
        }
    }

    static int ActivityPid(int k) {
        try {
            if (ActivityFiles[k] == null || !File.Exists(ActivityFiles[k])) return 0;
            var m = Regex.Match(File.ReadAllText(ActivityFiles[k]), "\"processId\"\\s*:\\s*(\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        } catch { return 0; }
    }

    static int FindBench(int k) {
        int hint = ActivityPid(k), first = 0;
        using (var s = new ManagementObjectSearcher("SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name='Unity.exe'")) {
            foreach (ManagementObject o in s.Get()) {
                string exe = (o["ExecutablePath"] as string) ?? "", cmd = (o["CommandLine"] as string) ?? "";
                if (!Regex.IsMatch(exe, @"\\Editor\\Unity\.exe$", RegexOptions.IgnoreCase)) continue;
                var m = Regex.Match(cmd, "-projectpath\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string path = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                try { path = Path.GetFullPath(path).TrimEnd('\\', '/'); } catch { continue; }
                if (!string.Equals(path, BenchPaths[k], StringComparison.OrdinalIgnoreCase)) continue;
                int id = Convert.ToInt32(o["ProcessId"]);
                if (id == hint) return id;
                if (first == 0) first = id;
            }
        }
        return first;
    }

    // --- Protokoll (eigener Thread: die Protokolldatei kann kurz gesperrt sein) ---
    static void Show(string text) { if (Echo) Messages.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text); }

    // Ins Protokoll der Testbank k (<Testbank>.log).
    static void Log(int k, string text) {
        Show(text);
        logQueue.Add(new KeyValuePair<string, string>(LogFiles[k],
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + text + "\r\n"));
    }

    static void LogLoop() {
        var utf8 = new UTF8Encoding(false);
        foreach (var item in logQueue.GetConsumingEnumerable()) {
            string file = item.Key;
            if (file == null || !Directory.Exists(Path.GetDirectoryName(file))) continue;
            for (int i = 0; i < 20; i++) {
                try { File.AppendAllText(file, item.Value, utf8); break; } catch { Thread.Sleep(50); }
            }
        }
    }
}
'@
}

function Test-GuardElsewhere {
    $m = $null
    if ([Threading.Mutex]::TryOpenExisting($mutexName, [ref]$m)) { $m.Dispose(); return $true }
    return $false
}

function Start-Guard([bool]$echo) {
    if ([BenchFocusGuard]::Running) { return $true }
    [BenchFocusGuard]::BenchPaths = [string[]]$projectPaths
    [BenchFocusGuard]::ActivityFiles = [string[]]$activityFiles
    [BenchFocusGuard]::OffFiles = [string[]]$offFiles
    [BenchFocusGuard]::LogFiles = [string[]]$logFiles
    [BenchFocusGuard]::MutexName = $mutexName
    [BenchFocusGuard]::ClickMs = $ClickMs
    [BenchFocusGuard]::KeyMs = $KeyMs
    [BenchFocusGuard]::DryRun = [bool]$DryRun
    [BenchFocusGuard]::Trace = [bool]$Trace
    [BenchFocusGuard]::Echo = $echo
    return [BenchFocusGuard]::Start()
}

$names = "'" + ($Project -join "', '") + "'"
switch ($Command) {
    'start' {
        if (-not (Start-Guard $false)) { Write-Output "Fokus-Waechter fuer $names laeuft schon in einem anderen Prozess."; exit 4 }
        $states = for ($i = 0; $i -lt $Project.Count; $i++) {
            "$($Project[$i]): " + $(if (Test-Path -LiteralPath $offFiles[$i]) { 'abgeschaltet (Datei ' + (Split-Path $offFiles[$i] -Leaf) + ')' } else { 'ein' })
        }
        Write-Output "Fokus-Waechter: $($states -join ', ')$(if ($DryRun) { ', Probelauf' })."
        exit 0
    }
    'watch' {
        if (-not (Start-Guard $true)) { Write-Output "Fokus-Waechter fuer $names laeuft schon (Dienst der Editor-Warteschlange). Zum Beobachten: Protokolle $($logFiles -join ', ')"; exit 4 }
        Write-Output "Fokus-Waechter fuer $names laeuft im Vordergrund$(if ($DryRun) { ' (Probelauf)' }). Beenden mit Strg+C."
        while ($true) {
            $line = $null
            while ([BenchFocusGuard]::Messages.TryDequeue([ref]$line)) { Write-Output $line }
            Start-Sleep -Milliseconds 200
        }
    }
    'status' {
        $running = Test-GuardElsewhere
        Write-Output ("Fokus-Waechter: {0}." -f $(if ($running) { 'laeuft' } else { 'laeuft nicht (Dienst der Editor-Warteschlange starten)' }))
        for ($i = 0; $i -lt $Project.Count; $i++) {
            Write-Output ("'{0}': {1}." -f $Project[$i], $(if (Test-Path -LiteralPath $offFiles[$i]) { "abgeschaltet ($($offFiles[$i]))" } else { 'eingeschaltet' }))
            if (Test-Path -LiteralPath $logFiles[$i]) {
                $lines = @(Get-Content -LiteralPath $logFiles[$i] -Encoding UTF8 -Tail 400 | Where-Object { $_ -match '  FOKUS ' } | Select-Object -Last 10)
                if ($lines.Count) { Write-Output 'Letzte Eintraege:'; $lines | ForEach-Object { Write-Output "  $_" } }
            }
        }
        exit 0
    }
    'off' {
        New-Item -ItemType Directory -Path $QueueDir -Force | Out-Null
        foreach ($f in $offFiles) { [IO.File]::WriteAllText($f, [DateTime]::UtcNow.ToString('o', $inv)) }
        Write-Output "Fokus-Waechter fuer $names abgeschaltet: Die Testbank darf nach vorne kommen. Wieder ein: Tools/editor-focus.ps1 on"
        exit 0
    }
    'on' {
        foreach ($f in $offFiles) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
        Write-Output "Fokus-Waechter fuer $names eingeschaltet$(if (-not (Test-GuardElsewhere)) { ' (laeuft aber nicht: Dienst der Editor-Warteschlange starten)' })."
        exit 0
    }
}
