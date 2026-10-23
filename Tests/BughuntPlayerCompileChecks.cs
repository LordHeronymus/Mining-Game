using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// Edit Mode. Runtime scripts (Assembly-CSharp, compiled into builds without UNITY_EDITOR) must use editor-only members
// only inside #if UNITY_EDITOR: GameSaveSystem.TestDirectory and the UnityEditor namespace. Otherwise the player build
// fails to compile, as it did with VoiceLinePlayer/GpsSettings. Tracks #if/#elif/#else/#endif per file.
public static class BughuntPlayerCompileChecks
{
    const string Report = "Temp/BughuntPlayerCompileChecks.txt";
    static readonly Regex EditorOnly = new(@"GameSaveSystem\.TestDirectory|\bUnityEditor\.|^\s*using\s+UnityEditor\b");

    public static object Main()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "");
        var scripts = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories).Select(path => path.Replace('\\', '/'))
            .Where(path => !path.Split('/').Contains("Editor")).ToArray();
        var violations = new List<string>();
        foreach (string path in scripts)
        {
            var stack = new Stack<(bool editor, bool notEditor)>();
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("#if")) { stack.Push(Condition(line.Substring(3))); continue; }
                if (line.StartsWith("#elif") && stack.Count > 0) { stack.Pop(); stack.Push(Condition(line.Substring(5))); continue; }
                if (line.StartsWith("#else") && stack.Count > 0) { var top = stack.Pop(); stack.Push((top.notEditor, top.editor)); continue; }
                if (line.StartsWith("#endif") && stack.Count > 0) { stack.Pop(); continue; }
                if (!EditorOnly.IsMatch(Code(lines[i]))) continue;
                if (!stack.Any(scope => scope.editor)) violations.Add(path + ":" + (i + 1) + ": " + line);
            }
        }
        // Self-test of the scanner: a guarded use passes, an unguarded one is found.
        bool guarded = Scan("#if UNITY_EDITOR\nx = GameSaveSystem.TestDirectory;\n#endif") == 0;
        bool unguarded = Scan("x = GameSaveSystem.TestDirectory;") == 1 && Scan("#if !UNITY_EDITOR\nusing UnityEditor;\n#endif") == 1 &&
            Scan("x = 1; // GameSaveSystem.TestDirectory") == 0;
        if (!guarded || !unguarded) violations.Insert(0, "Scanner-Selbsttest fehlgeschlagen");
        foreach (string violation in violations) File.AppendAllText(Report, "FAIL " + violation + "\n");
        string result = violations.Count == 0
            ? "PASS " + scripts.Length + " Laufzeit-Skripte nutzen Editor-Only-Member nur unter #if UNITY_EDITOR"
            : "FAIL " + violations.Count + " Editor-Only-Zugriffe ausserhalb von #if UNITY_EDITOR";
        File.AppendAllText(Report, result + "\n");
        return result;
    }

    // (inside an editor-only branch, inside a branch that excludes the editor)
    static (bool editor, bool notEditor) Condition(string expression)
    {
        string text = expression.Replace(" ", "");
        bool negated = text.Contains("!UNITY_EDITOR");
        bool mentions = text.Contains("UNITY_EDITOR");
        bool editorOnly = mentions && !negated && !text.Contains("||");
        return (editorOnly, mentions && negated && !text.Contains("||"));
    }

    // Code before a line comment; mentions in comments are fine.
    static string Code(string line) { int comment = line.IndexOf("//", StringComparison.Ordinal); return comment < 0 ? line : line.Substring(0, comment); }

    static int Scan(string source)
    {
        string file = Path.Combine("Temp", "BughuntPlayerCompileProbe.cs.txt");
        File.WriteAllText(file, source);
        var stack = new Stack<(bool editor, bool notEditor)>(); int found = 0;
        foreach (string raw in File.ReadAllLines(file))
        {
            string line = raw.Trim();
            if (line.StartsWith("#if")) { stack.Push(Condition(line.Substring(3))); continue; }
            if (line.StartsWith("#else") && stack.Count > 0) { var top = stack.Pop(); stack.Push((top.notEditor, top.editor)); continue; }
            if (line.StartsWith("#endif") && stack.Count > 0) { stack.Pop(); continue; }
            if (EditorOnly.IsMatch(Code(raw)) && !stack.Any(scope => scope.editor)) found++;
        }
        File.Delete(file);
        return found;
    }
}
