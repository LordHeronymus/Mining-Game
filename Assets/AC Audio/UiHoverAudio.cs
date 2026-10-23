using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Soft click whenever the pointer starts hovering an interactable button. One global watcher covers every
// button style (GoldButtonFeedback, HomeUi, plain uGUI), including buttons created at runtime.
public sealed class UiHoverAudio : MonoBehaviour
{
    static UiHoverAudio instance;
    static AudioClip clip;
    static readonly List<RaycastResult> hits = new();
    AudioSource source;
    PointerEventData pointer;
    EventSystem pointerOwner;
    Selectable current;

    public static Selectable Current => instance ? instance.current : null;
    public static int PlayCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { instance = null; clip = null; PlayCount = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance) return;
        var root = new GameObject("UiHoverAudio");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<UiHoverAudio>();
    }

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void Update() => Hover(PointerTarget());

    static Selectable PointerTarget()
    {
        if (!Application.isFocused || !Input.mousePresent || Cursor.lockState == CursorLockMode.Locked) return null;
        return TargetAt(Input.mousePosition);
    }

    // Same target as Unity's pointer-enter bubbling: the topmost UI hit, resolved to its owning button.
    public static Selectable TargetAt(Vector2 position)
    {
        var events = EventSystem.current;
        if (!events || !instance) return null;
        if (instance.pointerOwner != events) { instance.pointer = new PointerEventData(events); instance.pointerOwner = events; }
        var pointer = instance.pointer;
        pointer.Reset();
        pointer.position = position;
        hits.Clear();
        events.RaycastAll(pointer, hits);
        if (hits.Count == 0 || !hits[0].gameObject) return null;
        var target = hits[0].gameObject.GetComponentInParent<Selectable>();
        return IsButton(target) ? target : null;
    }

    static bool IsButton(Selectable target) =>
        target && target.IsInteractable() && (target is Button || target is Toggle || target is TMP_Dropdown || target is Dropdown);

    // Plays only when the hover starts on a new button; staying on it or moving inside it stays silent.
    public static void Hover(Selectable target)
    {
        if (!instance) return;
        if (target && !IsButton(target)) target = null;
        if (target == instance.current) return;
        instance.current = target;
        if (target) instance.Play();
    }

    void Play()
    {
        if (!Application.isPlaying || GameAudioLifecycle.IsStopping) return;
        if (!clip) clip = Resources.Load<AudioClip>("Audio/UiHoverClick");
        if (!clip) return;
        PlayCount++;
        source.pitch = AudioManager.TunedPitch(clip, 1f, source);
        source.PlayOneShot(clip, AudioManager.TunedVolume(clip, 1f, source));
    }
}
