using UnityEngine;

// One looped crackle for all placed torches: loud right beside a torch, silent from GPS "Knistern hörbar bis"
// (default 4 tiles). Several torches add up without getting louder than a single torch at full volume.
[DisallowMultipleComponent]
public sealed class TorchCrackleAudio : MonoBehaviour
{
    const float FullVolumeTiles = .5f;
    const float MaxPan = .6f;
    const float FadePerSecond = 6f;
    static TorchCrackleAudio instance;

    AudioSource source;
    PlayerMovement player;
    float gain, volume;
    bool paused;
    readonly System.Random startRandom = new();

    public static TorchCrackleAudio Instance => instance;
    public AudioSource Source => source;
    public float CurrentGain => gain;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => instance = null;

    public static void Ensure()
    {
        if (instance || !Application.isPlaying) return;
        instance = new GameObject("Torch Crackle Audio").AddComponent<TorchCrackleAudio>();
    }

    // 1 within half a tile, falling off quadratically to 0 at rangeTiles.
    public static float GainAt(float distance, float tileSize, float rangeTiles)
    {
        float range = Mathf.Max(.01f, rangeTiles * tileSize);
        float full = Mathf.Min(FullVolumeTiles * tileSize, range * .5f);
        float t = Mathf.Clamp01((range - distance) / Mathf.Max(.01f, range - full));
        return t * t;
    }

    void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.clip = AudioManager.TorchCrackleClip;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (GamePause.IsPaused || !source) return;
        if (!source.clip) source.clip = AudioManager.TorchCrackleClip;
        float target = 0f, pan = 0f;
        if (!player) player = FindFirstObjectByType<PlayerMovement>();
        if (player && source.clip)
        {
            Vector2 listener = player.transform.position;
            float silence = 1f, panSum = 0f, weightSum = 0f;
            foreach (var torch in PlacedTorch.Active)
            {
                if (!torch || torch.IsLavaLamp || !torch.OwnerMap || !torch.OwnerMap.Terrain) continue;
                var map = torch.OwnerMap;
                var terrain = map.Terrain;
                float tile = terrain.transform.TransformVector(Vector3.up * terrain.layoutGrid.cellSize.y).magnitude;
                Vector2 offset = torch.LightPosition - listener;
                float g = GainAt(offset.magnitude, tile, map.torchSoundRangeTiles);
                if (g <= 0f) continue;
                silence *= 1f - g;
                float range = Mathf.Max(.01f, map.torchSoundRangeTiles * tile);
                panSum += g * Mathf.Clamp(offset.x / range * 1.5f, -1f, 1f);
                weightSum += g;
                volume = Mathf.Clamp01(map.torchSoundVolume);
            }
            target = 1f - silence;
            if (weightSum > 0f) pan = panSum / weightSum * MaxPan;
        }
        gain = Mathf.MoveTowards(gain, target, FadePerSecond * Time.unscaledDeltaTime);
        if (gain <= .001f)
        {
            if (source.isPlaying) { source.Pause(); paused = true; }
            return;
        }
        source.panStereo = Mathf.Lerp(source.panStereo, pan, Mathf.Clamp01(Time.unscaledDeltaTime * 8f));
        source.volume = AudioManager.TunedVolume(source.clip, volume * gain, source);
        if (source.isPlaying) return;
        if (paused) { source.UnPause(); paused = false; return; }
        // Own random source: the first start lands somewhere inside the loop without touching Unity's random state.
        source.time = (float)startRandom.NextDouble() * source.clip.length * .9f;
        source.Play();
    }
}
