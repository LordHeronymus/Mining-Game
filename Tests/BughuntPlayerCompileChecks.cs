using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

// Edit Mode. Compiles the runtime scripts (Assembly-CSharp sources) once more as a player assembly, i.e. without
// UNITY_EDITOR, so editor-only members used by runtime code (e.g. GameSaveSystem.TestDirectory) break this check
// instead of a build. A one-line probe first proves that the build really runs without editor references.
public static class BughuntPlayerCompileChecks
{
    const string Report = "Temp/BughuntPlayerCompileChecks.txt";
    static string directory;

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "");
        directory = Path.GetFullPath("Temp/BughuntPlayerCompile-" + DateTime.UtcNow.Ticks);
        Directory.CreateDirectory(directory);
        string probe = Path.Combine(directory, "EditorOnlyProbe.cs");
        File.WriteAllText(probe, "public static class BughuntEditorOnlyProbe { public static bool Playing => UnityEditor.EditorApplication.isPlaying; }\n");
        string[] scripts = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories).Select(path => path.Replace('\\', '/'))
            .Where(path => !path.Split('/').Contains("Editor")).ToArray();
        var done = new TaskCompletionSource<object>();
        Build("Probe.dll", new[] { probe }, probeErrors =>
        {
            if (probeErrors.Length == 0) { Finish(done, "FAIL Spieler-Kompilierung nutzt Editor-Referenzen (Probe mit UnityEditor kompiliert)"); return; }
            Line("PASS Probe: UnityEditor ist im Spieler-Kontext nicht verfügbar");
            Build("Assembly-CSharp.dll", scripts, errors =>
            {
                foreach (var error in errors.Take(20)) Line("FAIL " + error);
                Finish(done, errors.Length == 0 ? "PASS " + scripts.Length + " Laufzeit-Skripte kompilieren ohne UNITY_EDITOR" : "FAIL " + errors.Length + " Compilerfehler ohne UNITY_EDITOR");
            }, done);
        }, done);
        return done.Task;
    }

    static void Build(string name, string[] scripts, Action<string[]> finished, TaskCompletionSource<object> done)
    {
        var builder = new AssemblyBuilder(Path.Combine(directory, name), scripts)
            { flags = AssemblyBuilderFlags.None, buildTargetGroup = BuildTargetGroup.Standalone, buildTarget = EditorUserBuildSettings.activeBuildTarget };
        // The project's own assemblies would duplicate the compiled types (and the editor ones reference UnityEditor).
        builder.excludeReferences = builder.defaultReferences.Where(path =>
        {
            string file = Path.GetFileName(path);
            return file.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) || file.StartsWith("MiningGame.", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
        if (builder.defaultDefines.Contains("UNITY_EDITOR")) { Finish(done, "FAIL Spieler-Kompilierung setzt UNITY_EDITOR"); return; }
        builder.buildFinished += (path, messages) =>
        {
            try { finished(messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.file + "(" + m.line + "): " + m.message).ToArray()); }
            catch (Exception error) { done.TrySetException(error); }
        };
        if (!builder.Build()) Finish(done, "FAIL AssemblyBuilder konnte " + name + " nicht starten");
    }

    static void Line(string text) => File.AppendAllText(Report, text + "\n");

    static void Finish(TaskCompletionSource<object> done, string result)
    {
        Line(result);
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (Exception) { }
        done.TrySetResult(result);
    }
}
