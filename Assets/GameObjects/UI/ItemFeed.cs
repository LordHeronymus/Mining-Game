using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ItemFeed : MonoBehaviour
{
    struct Pickup
    {
        public ItemSO item;
        public int amount;
        public bool money;
        public bool artifactPoints;
        public Sprite icon;
        public string customLabel;
    }

    sealed class Entry
    {
        public ItemSO item;
        public string customLabel;
        public bool money;
        public bool artifactPoints;
        public int amount;
        public RectTransform rect;
        public RectTransform iconRect;
        public ItemFeedBackdrop backdrop;
        public CanvasGroup group;
        public TextMeshProUGUI label;
        public RectTransform labelRect;
        public ItemFeedAccent accent;
        public float born;
        public float effectBorn;
        public float hopBorn;
        public float hopStartLift;
    }

    const int MaximumEntries = 4;
    const float PickupInterval = .4f;
    const float Lifetime = 3.5f;
    const float IconHopDuration = .36f;
    const float IconHopHeight = 11f;
    [SerializeField, Range(0, 24)] int sparkCount = 7;
    [SerializeField, Range(0f, 3f)] float sparkIntensity = 1f;
    [SerializeField, Range(0f, 40f)] float sparkRiseHeight = 14f;
    [SerializeField, Range(0f, 5f)] float sparkBrightness = 1f;
    [SerializeField, Range(0f, 5f)] float lineBrightness = 1f;
    [SerializeField, Range(0f, 1f)] float backdropOpacity = .7f;
    [SerializeField] AudioClip collectBling;
    [SerializeField] Sprite moneyIcon;
    [SerializeField] Sprite artifactPointsIcon;
    public static ItemFeed Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => Instance = null;
    readonly List<Entry> entries = new();
    readonly Queue<Pickup> pending = new();
    float nextPickupTime;
    InventoryManager inventory;
    Coroutine binding;
    Coroutine startupSoundDelay;
    bool collectionSoundReady;
    Canvas feedCanvas;

    void OnEnable()
    {
        if (!moneyIcon) moneyIcon = LoadSprite("GameOverCoin", "Gold Coin_0");
        var sunIcon = LoadSprite("ArtifactPointMedallions", "ArtifactPoint_Sun");
        if (sunIcon) artifactPointsIcon = sunIcon;
        Instance = this;
        collectBling = Resources.Load<AudioClip>("Audio/Ding4");
        collectionSoundReady = false;
        pending.Clear();
        nextPickupTime = 0f;
        startupSoundDelay = StartCoroutine(EnableCollectionSoundAfterStartup());
        var canvas = GetComponentInParent<Canvas>();
        if (canvas && transform.parent != canvas.rootCanvas.transform)
        {
            transform.SetParent(canvas.rootCanvas.transform, false);
            transform.SetAsLastSibling();
        }
        feedCanvas = gameObject.GetComponent<Canvas>();
        if (!feedCanvas) feedCanvas = gameObject.AddComponent<Canvas>();
        feedCanvas.overrideSorting = false;
        feedCanvas.sortingOrder = 30001;
        binding = StartCoroutine(BindInventory());
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        if (binding != null) StopCoroutine(binding);
        if (startupSoundDelay != null) StopCoroutine(startupSoundDelay);
        binding = null;
        startupSoundDelay = null;
        collectionSoundReady = false;
        pending.Clear();
        if (inventory) inventory.OnItemGained -= Show;
        inventory = null;
        if (feedCanvas) feedCanvas.overrideSorting = false;
    }

    public void SetAboveArtifactDiscovery(bool visible)
    {
        if (!feedCanvas) return;
        feedCanvas.overrideSorting = visible;
        if (visible) feedCanvas.sortingOrder = 30001;
    }

    IEnumerator EnableCollectionSoundAfterStartup()
    {
        yield return null;
        collectionSoundReady = true;
        startupSoundDelay = null;
    }

    IEnumerator BindInventory()
    {
        while (!InventoryManager.Instance) yield return null;
        inventory = InventoryManager.Instance;
        inventory.OnItemGained += Show;
        binding = null;
    }

    void Show(ItemSO item, int amount)
    {
        if (!item || amount <= 0) return;
        pending.Enqueue(new Pickup { item = item, amount = amount });
        ShowNextPickup();
    }

    public void ShowRecipe(CraftingRecipe recipe)
    {
        if (!recipe || !recipe.output) return;
        string spriteName = recipe.output.name + "Recipe";
        var blueprint = Resources.Load<Sprite>("Shop/" + spriteName);
        string title = string.IsNullOrWhiteSpace(recipe.output.displayName)
            ? recipe.output.name : recipe.output.displayName;
        pending.Enqueue(new Pickup
        {
            item = recipe.output,
            amount = 1,
            icon = blueprint ? blueprint : recipe.output.icon,
            customLabel = "Bauplan: " + title
        });
        ShowNextPickup();
    }

    public void ShowMoney(int amount)
    {
        if (amount <= 0) return;
        pending.Enqueue(new Pickup { amount = amount, money = true });
        ShowNextPickup();
    }

    public void ShowArtifactPoints(int amount)
    {
        if (amount <= 0) return;
        pending.Enqueue(new Pickup { amount = amount, artifactPoints = true });
        ShowNextPickup();
    }

    void ShowNextPickup()
    {
        float now = Time.unscaledTime;
        if (pending.Count == 0 || now < nextPickupTime) return;
        var pickup = pending.Dequeue();
        nextPickupTime = now + PickupInterval;
        if (pickup.money) DisplayCounter(pickup.amount, true);
        else if (pickup.artifactPoints) DisplayCounter(pickup.amount, false);
        else if (!string.IsNullOrEmpty(pickup.customLabel)) DisplayCustomItem(pickup);
        else DisplayItem(pickup.item, pickup.amount);
    }

    void DisplayCustomItem(Pickup pickup)
    {
        float now = Time.unscaledTime;
        PlayCollectionBling();
        var entry = CreateEntry(pickup.item, pickup.amount, iconOverride: pickup.icon,
            customLabel: pickup.customLabel);
        entry.born = now;
        entry.hopBorn = now;
        entries.Insert(0, entry);
        if (entries.Count > MaximumEntries)
        {
            var oldest = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            Destroy(oldest.rect.gameObject);
        }
    }

    void DisplayItem(ItemSO item, int amount)
    {
        float now = Time.unscaledTime;
        PlayCollectionBling();
        var entry = entries.Find(row => row.item == item);
        if (entry != null)
        {
            entry.amount += amount;
            entry.born = now;
            entry.hopStartLift = Mathf.Max(0f, entry.iconRect.anchoredPosition.y - IconBaseY(entry));
            entry.hopBorn = now;
            SetContent(entry);
            return;
        }

        entry = CreateEntry(item, amount);
        entry.born = now;
        entry.hopBorn = now;
        entries.Insert(0, entry);
        if (entries.Count > MaximumEntries)
        {
            var oldest = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            Destroy(oldest.rect.gameObject);
        }
    }

    void PlayCollectionBling()
    {
        if (collectionSoundReady && collectBling && AudioManager.Instance)
            AudioManager.Instance.PlayClipWithOffset(collectBling,
                .6f * AudioManager.Instance.GetVolume(AudioVolumeSetting.DingLight), 0f,
                AudioManager.Instance.GetTimeOffset(AudioTimeOffsetSetting.DingLight),
                Random.Range(.93f, 1.07f));
    }

    void DisplayCounter(int amount, bool money)
    {
        float now = Time.unscaledTime;
        if (!money) PlayCollectionBling();
        var entry = entries.Find(row => row.money == money && row.artifactPoints == !money);
        if (entry != null)
        {
            entry.amount += amount;
            entry.born = now;
            entry.hopStartLift = Mathf.Max(0f, entry.iconRect.anchoredPosition.y - IconBaseY(entry));
            entry.hopBorn = now;
            SetContent(entry);
            return;
        }

        entry = CreateEntry(null, amount, money, !money);
        entry.born = now;
        entry.hopBorn = now;
        entries.Insert(0, entry);
        if (entries.Count > MaximumEntries)
        {
            var oldest = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            Destroy(oldest.rect.gameObject);
        }
    }

    Entry CreateEntry(ItemSO item, int amount, bool money = false, bool artifactPoints = false,
        Sprite iconOverride = null, string customLabel = null)
    {
        var title = money ? "Money" : artifactPoints ? "Artifact Points" : item.name;
        var rect = MakeRect("Pickup " + title, transform, 0f, 0f, 300f, 48f);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        rect.anchoredPosition = new Vector2(-20f, 84f);

        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var backdropRect = MakeRect("Backdrop", rect, -18f, -12f, 336f, 72f);
        var backdrop = backdropRect.gameObject.AddComponent<ItemFeedBackdrop>();
        backdrop.color = new Color(.015f, .009f, .004f, 1f);
        backdrop.SetOpacity(backdropOpacity);
        backdrop.raycastTarget = false;

        var iconRect = MakeRect("Icon", rect, 0f, 0f, 40f, 40f);
        var icon = iconRect.gameObject.AddComponent<Image>();
        icon.sprite = money ? moneyIcon : artifactPoints ? artifactPointsIcon : iconOverride ? iconOverride : item.icon;
        icon.enabled = icon.sprite != null;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var iconOutline = iconRect.gameObject.AddComponent<Outline>();
        iconOutline.effectColor = new Color(.025f, .012f, .006f, .86f);
        iconOutline.effectDistance = new Vector2(1.3f, -1.3f);
        var iconShadow = iconRect.gameObject.AddComponent<Shadow>();
        iconShadow.effectColor = new Color(.04f, .02f, .01f, .72f);
        iconShadow.effectDistance = new Vector2(1f, -2f);

        var textRect = MakeRect("Amount and Item", rect, 52f, 1f, 248f, 38f);
        var label = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(1f, .95f, .83f);
        label.outlineColor = new Color(.025f, .012f, .006f, .85f);
        label.outlineWidth = .18f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        var textShadow = textRect.gameObject.AddComponent<Shadow>();
        textShadow.effectColor = new Color(.035f, .015f, .005f, .95f);
        textShadow.effectDistance = new Vector2(2f, -2f);

        var accentRect = MakeRect("Item Accent", rect, 0f, 37f, 300f, 16f);
        var accent = accentRect.gameObject.AddComponent<ItemFeedAccent>();
        accent.color = money || artifactPoints ? new Color(.96f, .68f, .18f) : item.themeColor;
        accent.Seed = item ? item.GetInstanceID() : GetInstanceID();
        accent.raycastTarget = false;
        accent.ConfigureSparks(sparkCount, sparkIntensity, sparkRiseHeight, sparkBrightness, lineBrightness);

        var entry = new Entry { item = item, customLabel = customLabel, money = money, artifactPoints = artifactPoints, amount = amount, rect = rect, iconRect = iconRect, backdrop = backdrop, group = group,
            label = label, labelRect = textRect, accent = accent, effectBorn = Time.unscaledTime };
        SetContent(entry);
        return entry;
    }

    static void SetContent(Entry entry)
    {
        entry.label.text = !string.IsNullOrEmpty(entry.customLabel) ? entry.customLabel : entry.money ? $"+${entry.amount}" : entry.artifactPoints
            ? $"+{entry.amount}  {(entry.amount == 1 ? "Artefaktpunkt" : "Artefaktpunkte")}"
            : $"+{entry.amount}  {Name(entry.item)}";
        float textWidth = Mathf.Min(286f, Mathf.Ceil(entry.label.preferredWidth) + 4f);
        entry.labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        entry.accent.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 52f + textWidth);
        entry.rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 52f + textWidth);
        entry.backdrop.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 88f + textWidth);
    }

    void Update()
    {
        ShowNextPickup();
        float now = Time.unscaledTime;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            float age = now - entry.born;
            if (age >= Lifetime)
            {
                entries.RemoveAt(i);
                Destroy(entry.rect.gameObject);
                continue;
            }
            float fadeIn = Mathf.Clamp01((now - entry.effectBorn) / .24f);
            float fadeOut = Mathf.Clamp01((Lifetime - age) / .65f);
            entry.group.alpha = Mathf.Min(fadeIn, fadeOut);
            entry.backdrop.SetOpacity(backdropOpacity);
            float hopProgress = Mathf.Clamp01((now - entry.hopBorn) / IconHopDuration);
            float lift = Mathf.Lerp(entry.hopStartLift, 0f, hopProgress)
                + Mathf.Sin(hopProgress * Mathf.PI) * IconHopHeight;
            entry.iconRect.anchoredPosition = new Vector2(0f, IconBaseY(entry) + lift);
            entry.accent.ConfigureSparks(sparkCount, sparkIntensity, sparkRiseHeight, sparkBrightness, lineBrightness);
            entry.accent.SetAge(now - entry.effectBorn);
            Vector2 target = new Vector2(0f, 84f - i * 56f);
            entry.rect.anchoredPosition = Vector2.Lerp(entry.rect.anchoredPosition,
                target, 1f - Mathf.Exp(-13f * Time.unscaledDeltaTime));
        }
    }

    static string Name(ItemSO item) => !item ? string.Empty : string.IsNullOrWhiteSpace(item.displayName)
        ? item.name : item.displayName;

    static float IconBaseY(Entry entry) => 0f;

    static Sprite LoadSprite(string path, string spriteName)
    {
        foreach (var sprite in Resources.LoadAll<Sprite>(path))
            if (sprite && sprite.name == spriteName) return sprite;
        return null;
    }

    static RectTransform MakeRect(string name, Transform parent, float x, float y, float width, float height)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
}
