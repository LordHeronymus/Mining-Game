using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiningGame.CI
{
    // Runs the Tests/*Checks.cs scripts without a connected editor (batchmode, GitHub Actions).
    // Tests/CI/prepare.py copies the selected checks into Assets/Editor/CIChecks and writes the plan.
    // Game types are resolved by reflection, so this assembly does not depend on Assembly-CSharp.
    public static class CiCheckRunner
    {
        const string DefaultScene = "Assets/Scenes/SampleScene.unity";
        const string MenuScene = "Assets/Scenes/MainMenu.unity";

        [Serializable] class Plan { public List<Entry> checks = new(); }

        [Serializable]
        class Entry
        {
            public string name;
            public string mode = "edit";
            public string scene;
            public string entry = "Main";
            public string afterStop;
            public string resultFile;
            public bool waitForResultFile;
            public string resultDonePattern;
            public float timeoutSeconds = 300;
        }

        class Result
        {
            public string name, mode, status = "FAIL";
            public readonly StringBuilder output = new();
            public readonly List<string> logErrors = new();
            public double seconds;
        }

        enum Phase { Next, StopPlay, WaitMenu, Invoke, WaitTask, WaitFile, StopForAfter, AfterStop }

        static Plan plan;
        static string outputDirectory;
        static int index = -1;
        static Phase phase;
        static double checkStart, phaseStart;
        static Entry current;
        static Result result;
        static Task pendingTask;
        static readonly List<Result> results = new();
        static DateTime runStarted;

        public static void Run()
        {
            try
            {
                string planPath = Argument("-ciPlan") ?? "Artifacts/plan.json";
                outputDirectory = Argument("-ciOutput") ?? "Artifacts";
                runStarted = DateTime.UtcNow;
                Directory.CreateDirectory(outputDirectory);
                Directory.CreateDirectory("Temp");
                plan = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath));
                if (plan?.checks == null || plan.checks.Count == 0) throw new Exception("Plan has no checks: " + planPath);
                if (EditorUtility.scriptCompilationFailed) throw new Exception("Scripts have compile errors, see editor.log.");
                Application.logMessageReceived += OnLog;
                Set(Phase.Next);
                EditorApplication.update += Tick;
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                File.WriteAllText(Path.Combine(outputDirectory ?? "Artifacts", "ci-results.md"), "Runner error: " + error.Message + "\n");
                EditorApplication.Exit(3);
            }
        }

        static void Tick()
        {
            try { Step(); }
            catch (Exception error) { Fail(Unwrap(error).ToString()); }
        }

        static void Step()
        {
            double now = EditorApplication.timeSinceStartup;
            if (phase != Phase.Next && now - checkStart > current.timeoutSeconds)
            {
                Fail($"Timeout after {current.timeoutSeconds:0}s in phase {phase}.");
                return;
            }
            switch (phase)
            {
                case Phase.Next:
                    if (++index >= plan.checks.Count) { Finish(); return; }
                    current = plan.checks[index];
                    result = new Result { name = current.name, mode = current.mode };
                    checkStart = now;
                    Debug.Log($"[CI] ({index + 1}/{plan.checks.Count}) {current.name} [{current.mode}]");
                    Set(Phase.StopPlay);
                    return;

                case Phase.StopPlay:
                    if (!StopPlay()) return;
                    if (IsPlayCheck)
                    {
                        // Fresh Play per check, started from MainMenu like a manual test.
                        EditorSceneManager.OpenScene(DefaultScene, OpenSceneMode.Single);
                        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(string.IsNullOrEmpty(current.scene) ? MenuScene : current.scene);
                        EditorApplication.isPlaying = true;
                        Set(Phase.WaitMenu);
                    }
                    else
                    {
                        string scene = string.IsNullOrEmpty(current.scene) ? DefaultScene : current.scene;
                        if (EditorSceneManager.GetActiveScene().path != scene) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                        Set(Phase.Invoke);
                    }
                    return;

                case Phase.WaitMenu:
                    if (!EditorApplication.isPlaying || now - phaseStart < 2) return;
                    bool menu = GameFlag("MainMenuController", "IsVisible") ?? true;
                    bool busy = (GameFlag("LoadingProgress", "Active") ?? false) || (GameFlag("RunNavigation", "IsTransitioning") ?? false) || (GameFlag("GameSaveSystem", "IsBusy") ?? false);
                    if (menu && !busy) Set(Phase.Invoke);
                    return;

                case Phase.Invoke:
                    if (!string.IsNullOrEmpty(current.resultFile) && File.Exists(current.resultFile)) File.Delete(current.resultFile);
                    HandleValue(Call(current.entry), Phase.WaitFile);
                    return;

                case Phase.WaitTask:
                    if (!pendingTask.IsCompleted) return;
                    if (pendingTask.IsFaulted || pendingTask.IsCanceled) { Fail(pendingTask.Exception != null ? Unwrap(pendingTask.Exception).ToString() : "Task canceled."); return; }
                    var value = pendingTask.GetType().IsGenericType ? pendingTask.GetType().GetProperty("Result")?.GetValue(pendingTask) : null;
                    pendingTask = null;
                    HandleValue(value, Phase.WaitFile);
                    return;

                case Phase.WaitFile:
                    // Asynchronous checks run on EditorApplication.update and write their result file when done.
                    if (current.waitForResultFile && !ResultFileDone()) return;
                    if (!string.IsNullOrEmpty(current.afterStop) && !afterStopDone) { Set(Phase.StopForAfter); return; }
                    if (ResultFileOk()) Pass();
                    return;

                case Phase.StopForAfter:
                    if (!StopPlay()) return;
                    Set(Phase.AfterStop);
                    return;

                case Phase.AfterStop:
                    afterStopDone = true;
                    HandleValue(Call(current.afterStop), Phase.WaitFile);
                    return;
            }
        }

        static bool afterStopDone;

        static bool IsPlayCheck => string.Equals(current.mode, "play", StringComparison.OrdinalIgnoreCase);

        static void Set(Phase next)
        {
            if (next == Phase.Next) afterStopDone = false;
            phase = next;
            phaseStart = EditorApplication.timeSinceStartup;
        }

        static bool StopPlay()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            // isPlayingOrWillChangePlaymode drops as soon as the exit is requested; isPlaying only once it is done (run #154).
            return !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        static object Call(string methodName)
        {
            var type = FindType(current.name) ?? throw new Exception($"Type {current.name} not found. Compile error in the check? See editor.log.");
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(m => m.Name == methodName).OrderBy(m => m.GetParameters().Length).FirstOrDefault()
                ?? throw new Exception($"{current.name}.{methodName}() not found.");
            var arguments = method.GetParameters()
                .Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                .ToArray();
            return method.Invoke(null, arguments);
        }

        static void HandleValue(object value, Phase next)
        {
            if (value is Task task) { pendingTask = task; Set(Phase.WaitTask); return; }
            string text = Describe(value);
            result.output.AppendLine(text);
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Requires", StringComparison.OrdinalIgnoreCase))
            {
                Fail(null);
                return;
            }
            Set(next);
        }

        // Some checks write a placeholder first ("läuft …"); resultDonePattern marks the final report.
        static bool ResultFileDone()
        {
            if (!File.Exists(current.resultFile)) return false;
            if (string.IsNullOrEmpty(current.resultDonePattern)) return true;
            try { return System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(current.resultFile), current.resultDonePattern, System.Text.RegularExpressions.RegexOptions.Multiline); }
            catch (IOException) { return false; }
        }

        // Checks often write Temp/<Name>.txt; any line starting with FAIL fails the check.
        static bool ResultFileOk()
        {
            if (string.IsNullOrEmpty(current.resultFile) || !File.Exists(current.resultFile)) return true;
            string content = File.ReadAllText(current.resultFile);
            result.output.AppendLine("--- " + current.resultFile).AppendLine(content.TrimEnd());
            if (content.Split('\n').Any(line => line.TrimStart().StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))) { Fail(null); return false; }
            return true;
        }

        static void Pass() => Record("PASS");

        static void Fail(string message)
        {
            if (current == null) { Debug.LogError("[CI] Runner error: " + message); Finish(3); return; }
            if (!string.IsNullOrEmpty(message)) result.output.AppendLine(message);
            Record("FAIL");
        }

        static void Record(string status)
        {
            pendingTask = null;
            result.status = status;
            result.seconds = EditorApplication.timeSinceStartup - checkStart;
            results.Add(result);
            Debug.Log($"[CI] {status} {current.name} ({result.seconds:0.0}s)\n{result.output}");
            Set(Phase.Next);
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (result == null || phase == Phase.Next || type == LogType.Log || type == LogType.Warning) return;
            if (result.logErrors.Count < 5) result.logErrors.Add(type + ": " + condition);
        }

        static void Finish() => Finish(results.Any(r => r.status != "PASS") ? 2 : 0);

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            try { WriteReports(); }
            catch (Exception error) { Debug.LogException(error); }
            try { CollectPreviews(); }
            catch (Exception error) { Debug.LogException(error); }
            Debug.Log($"[CI] Finished: {results.Count(r => r.status == "PASS")}/{results.Count} passed, exit code {code}.");
            EditorApplication.Exit(code);
        }

        // Images the checks wrote during the run become previews (Docs/Cloud-Tests.md). Collected here because
        // Unity deletes the Temp folder when the editor quits (run #155 lost Temp/VeinRichness/*.png).
        static void CollectPreviews()
        {
            var images = new[] { "Temp", "Design" }.Where(Directory.Exists)
                .SelectMany(root => Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
                .Where(path => File.GetLastWriteTimeUtc(path) >= runStarted.AddSeconds(-2))
                .OrderBy(path => path, StringComparer.Ordinal).Take(60);
            foreach (string image in images)
            {
                string target = Path.Combine(outputDirectory, "previews", image);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(image, target, true);
            }
        }

        static void WriteReports()
        {
            int failed = results.Count(r => r.status != "PASS");
            var md = new StringBuilder();
            md.AppendLine($"### Unity Checks: {results.Count - failed}/{results.Count} bestanden").AppendLine();
            md.AppendLine("| Status | Check | Modus | Zeit | Meldung |").AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var r in results)
                md.AppendLine($"| {(r.status == "PASS" ? "✅" : "❌")} | {r.name} | {r.mode} | {r.seconds:0.0}s | {Cell(r.output.ToString())} |");
            foreach (var r in results)
            {
                md.AppendLine().AppendLine($"<details><summary>{r.status} {r.name}</summary>").AppendLine().AppendLine("```");
                md.AppendLine(Shorten(r.output.ToString().TrimEnd(), 6000));
                if (r.logErrors.Count > 0) md.AppendLine("--- Fehler im Log:").AppendLine(string.Join("\n", r.logErrors.Select(e => Shorten(e, 600))));
                md.AppendLine("```").AppendLine("</details>");
            }
            File.WriteAllText(Path.Combine(outputDirectory, "ci-results.md"), md.ToString());

            var xml = new StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            xml.AppendLine($"<testsuite name=\"UnityChecks\" tests=\"{results.Count}\" failures=\"{failed}\" time=\"{results.Sum(r => r.seconds):0.000}\">");
            foreach (var r in results)
            {
                xml.Append($"  <testcase classname=\"{r.mode}\" name=\"{SecurityElement.Escape(r.name)}\" time=\"{r.seconds:0.000}\">");
                if (r.status != "PASS") xml.Append($"<failure message=\"{SecurityElement.Escape(Shorten(FirstLine(r.output.ToString()), 300))}\"/>");
                xml.AppendLine($"<system-out>{SecurityElement.Escape(r.output.ToString())}</system-out></testcase>");
            }
            xml.AppendLine("</testsuite>");
            File.WriteAllText(Path.Combine(outputDirectory, "ci-results.xml"), xml.ToString());

            // Read by Tests/CI/progress.py for the live status in the cloud dashboard.
            var json = new ResultFile();
            foreach (var r in results)
                json.checks.Add(new ResultRow { name = r.name, mode = r.mode, status = r.status, seconds = r.seconds, output = Shorten(r.output.ToString().TrimEnd(), 4000), logErrors = r.logErrors.ToArray() });
            File.WriteAllText(Path.Combine(outputDirectory, "ci-results.json"), JsonUtility.ToJson(json, true));
        }

        [Serializable] class ResultFile { public List<ResultRow> checks = new(); }

        [Serializable]
        class ResultRow
        {
            public string name, mode, status, output;
            public double seconds;
            public string[] logErrors;
        }

        static string Describe(object value)
        {
            switch (value)
            {
                case null: return "(null)";
                case string text: return text;
                case IEnumerable items: return string.Join("\n", items.Cast<object>().Select(Describe));
            }
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum) return value.ToString();
            string json = JsonUtility.ToJson(value);
            return string.IsNullOrEmpty(json) || json == "{}" ? value.ToString() : json;
        }

        static Type FindType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name, false))
                .FirstOrDefault(type => type != null);

        static bool? GameFlag(string typeName, string member)
        {
            var type = FindType(typeName);
            var property = type?.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property != null) return property.GetValue(null) as bool?;
            return type?.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as bool?;
        }

        static Exception Unwrap(Exception error)
        {
            while (error is TargetInvocationException or AggregateException && error.InnerException != null) error = error.InnerException;
            return error;
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        static string FirstLine(string text) => text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
        static string Shorten(string text, int max) => text.Length <= max ? text : text.Substring(0, max) + " …";
        static string Cell(string text) => Shorten(FirstLine(text), 160).Replace("|", "\\|");
    }
}
