using UnityEngine;
using UnityEditor;
using Unity.Profiling;

public static class ProfileOreSparkles
{
    static ProfilerRecorder recorder;
    static double deadline;
    static bool previousBackground;
    public static object Main()
    {
        if(!Application.isPlaying)return "Play mode required";
        previousBackground=Application.runInBackground;
        Application.runInBackground=true;
        SessionState.SetString("OreSparkles.Profile", "Running");
        recorder=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Mining.OreSparkles",128);
        deadline=EditorApplication.timeSinceStartup+5;
        EditorApplication.update+=Finish;
        return "Recording 5 seconds";
    }
    static void Finish()
    {
        if(EditorApplication.timeSinceStartup<deadline && Application.isPlaying)return;
        EditorApplication.update-=Finish;
        recorder.Stop();
        double total=0,max=0;
        for(int i=0;i<recorder.Count;i++)
        {
            double ms=recorder.GetSample(i).Value/1000000.0;
            total+=ms;max=System.Math.Max(max,ms);
        }
        SessionState.SetString("OreSparkles.Profile",$"Samples={recorder.Count}; meanMs={total/System.Math.Max(1,recorder.Count):F4}; maxMs={max:F4}");
        recorder.Dispose();
        Application.runInBackground=previousBackground;
    }
}
