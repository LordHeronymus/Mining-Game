using UnityEditor;
using UnityEngine;
using System.Reflection;
using UnityEngine.Networking;
using System.IO;

public static class HomeAudioEditorPreview
{
    static AudioClip rendered;
    static UnityWebRequest decoding;
    static AudioClip pending;
    static HomeAudioEditorPreview() { AssemblyReloadEvents.beforeAssemblyReload += Stop; EditorApplication.quitting += Stop; }
    static System.Type AudioUtil => typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    public static AudioClip Playing { get; private set; }
    public static void Play(HomeAudioClipTuning tuning) => Play(tuning?.clip);
    public static void Play(AudioClip clip)
    {
        if (!clip) return;
        if (Application.isPlaying) { GpsAudioPreview.Play(clip); return; }
        Stop();
        if(clip.loadType!=AudioClipLoadType.DecompressOnLoad)
        {
            string path=AssetDatabase.GetAssetPath(clip);
            AudioType type=Path.GetExtension(path).ToLowerInvariant() switch { ".wav"=>AudioType.WAV,".mp3"=>AudioType.MPEG,".ogg"=>AudioType.OGGVORBIS,".aiff"=>AudioType.AIFF,_=>AudioType.UNKNOWN };
            decoding=UnityWebRequestMultimedia.GetAudioClip(new System.Uri(Path.GetFullPath(path)).AbsoluteUri,type);
            ((DownloadHandlerAudioClip)decoding.downloadHandler).streamAudio=false;
            pending=clip;Playing=clip;decoding.SendWebRequest();EditorApplication.update+=Decode;return;
        }
        Render(clip,clip);
    }
    static void Decode()
    {
        if(decoding==null)return;
        if(!decoding.isDone){EditorApplication.QueuePlayerLoopUpdate();return;}
        EditorApplication.update-=Decode;
        var request=decoding;decoding=null;
        try
        {
            if(request.result!=UnityWebRequest.Result.Success){Debug.LogWarning("GPS Audiovorschau: "+request.error);Playing=null;return;}
            var decoded=DownloadHandlerAudioClip.GetContent(request);
            try{Render(decoded,pending);}finally{Object.DestroyImmediate(decoded);}
        }
        finally{request.Dispose();pending=null;}
    }
    static void Render(AudioClip clip,AudioClip original)
    {
        clip.LoadAudioData();
        // Read at most the first ten seconds: long ambience files stay inexpensive to audition.
        int inputFrames=Mathf.Min(clip.samples,clip.frequency*10);
        var input = new float[inputFrames * clip.channels];
        if (!clip.GetData(input, 0)) return;
        float pitch = GpsAudio.Pitch(original,1f);
        float volume = Mathf.Clamp01(GpsAudio.Volume(original,1f)) * PlayerSettings.Master;
        int frames = Mathf.Clamp(Mathf.FloorToInt(inputFrames / pitch),1,clip.frequency*10);
        var output = new float[frames * clip.channels];
        for (int frame = 0; frame < frames; frame++)
        {
            float position = frame * pitch;
            int first = Mathf.Min((int)position, inputFrames - 1);
            int second = Mathf.Min(first + 1, inputFrames - 1);
            for (int channel = 0; channel < clip.channels; channel++)
                output[frame * clip.channels + channel] = Mathf.Lerp(input[first * clip.channels + channel],
                    input[second * clip.channels + channel], position - first) * volume;
        }
        rendered = AudioClip.Create("GPS Audio Preview", frames, clip.channels, clip.frequency, false);
        rendered.hideFlags = HideFlags.HideAndDontSave;
        rendered.SetData(output, 0);
        Playing=original;
        AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Public | BindingFlags.Static, null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null)?.Invoke(null, new object[] { rendered, 0, false });
        EditorApplication.update -= Cleanup;
        EditorApplication.update += Cleanup;
    }

    static void Cleanup()
    {
        if(!rendered || decoding!=null)return;
        var playing = AudioUtil?.GetMethod("IsPreviewClipPlaying", BindingFlags.Public | BindingFlags.Static);
        if (playing != null && !(bool)playing.Invoke(null, null)) Stop();
    }

    public static void Stop()
    {
        EditorApplication.update -= Cleanup;
        EditorApplication.update -= Decode;
        if(decoding!=null){decoding.Abort();decoding.Dispose();decoding=null;}pending=null;
        GpsAudioPreview.Stop();Playing=null;
        if (!rendered) return;
        AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        Object.DestroyImmediate(rendered);
        rendered = null;
    }
}
