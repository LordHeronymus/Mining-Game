using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GameplayDebugWindow
{
    sealed class AudioBrowserGroup
    {
        public string title;
        public bool expanded;
        public RectTransform header;
        public TextMeshProUGUI titleText, countText;
        public RectTransform columns;
        public readonly List<AudioBrowserGroup> children = new();
        public readonly List<AudioBrowserRow> rows = new();
    }

    sealed class AudioBrowserRow
    {
        public string title;
        public AudioClip clip;
        public int layerIndex = -1, clipIndex, detailIndex = -1;
        public SoundType soundType;
        public bool breaking;
        public FirstLayerAmbience detailOwner;
        public AudioVolumeSetting? volumeSetting;
        public AudioTimeOffsetSetting? offsetSetting;
        public RectTransform rect;
        public TMP_InputField[] inputs = new TMP_InputField[3];
    }

    readonly List<AudioBrowserGroup> audioBrowserGroups = new();
    readonly List<AudioBrowserRow> audioBrowserRows = new();
    readonly List<string> legacyAudioItems = new();
    readonly HashSet<AudioClip> audioCataloguedClips = new();
    RectTransform audioBrowserRoot;
    TMP_InputField audioSearch;
    bool audioBrowserBuilt;
    int nextAudioBrowserId;

    static readonly Color AudioPanelColor = new(.055f, .082f, .115f, .96f);
    static readonly Color AudioRowColor = new(.075f, .115f, .16f, .94f);
    static readonly Color AudioGold = new(1f, .72f, .27f);

    void CreateAudioBrowserShell()
    {
        audioBrowserRoot = MakeRect("AudioBrowserRoot", content);
        items.Add(audioBrowserRoot.name, audioBrowserRoot);
        var search = CloneItem("DiggingSpeed", "AudioBrowserSearch", audioBrowserRoot)
            .GetComponent<TMP_InputField>();
        audioSearch = search;
        search.onValueChanged = new TMP_InputField.OnChangeEvent();
        search.onEndEdit = new TMP_InputField.SubmitEvent();
        search.SetTextWithoutNotify(string.Empty);
        search.contentType = TMP_InputField.ContentType.Standard;
        search.characterLimit = 80;
        search.textComponent.fontSize = 23f;
        search.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        search.textComponent.margin = new Vector4(18f, 0f, 6f, 0f);
        if (search.targetGraphic is Image background) background.color = AudioPanelColor;
        var placeholder = CreateAudioText(search.transform, "Clip suchen…", 23f,
            new Color(.53f, .6f, .68f), TextAlignmentOptions.MidlineLeft);
        placeholder.rectTransform.offsetMin = new Vector2(18f, 0f);
        placeholder.rectTransform.offsetMax = new Vector2(-8f, 0f);
        search.placeholder = placeholder;
        search.onValueChanged.AddListener(_ => LayoutAudioBrowser());
    }

    void BuildAudioBrowser()
    {
        if (!audioBrowserRoot && items.TryGetValue("AudioBrowserRoot", out var root))
            audioBrowserRoot = root;
        if (!audioBrowserRoot || !AudioManager.Instance) return;
        if (!audioSearch)
            audioSearch = audioBrowserRoot.Find("AudioBrowserSearch")?.GetComponent<TMP_InputField>();
        if (!audioSearch) return;
        if (audioBrowserBuilt && audioBrowserGroups.Count > 0) return;
        audioSearch.onValueChanged.RemoveAllListeners();
        audioSearch.onValueChanged.AddListener(_ => LayoutAudioBrowser());
        for (int i = audioBrowserRoot.childCount - 1; i >= 0; i--)
        {
            var child = audioBrowserRoot.GetChild(i);
            if (child.name == "AudioBrowserSearch") continue;
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
        audioBrowserGroups.Clear();
        audioBrowserRows.Clear();
        audioCataloguedClips.Clear();
        nextAudioBrowserId = 0;
        audioBrowserBuilt = true;
        var mix = AddAudioGroup("Gesamtpegel", false);
        AddAudioVolumeRow(mix, "Gesamt-Ambience", AudioVolumeSetting.Ambience);
        AddAudioVolumeRow(mix, "Oberfläche", AudioVolumeSetting.Surface);
        AddAudioVolumeRow(mix, "Regen", AudioVolumeSetting.Rain);
        AddAudioVolumeRow(mix, "Gewitter", AudioVolumeSetting.Thunderstorm);
        AddAudioVolumeRow(mix, "Untergrund", AudioVolumeSetting.Underground);
        AddAudioVolumeRow(mix, "Höhle", AudioVolumeSetting.Cave);
        AddAudioVolumeRow(mix, "Abbau", AudioVolumeSetting.DigSounds);
        AddAudioVolumeRow(mix, "Ding 4", AudioVolumeSetting.DingLight);
        AddAudioOffsetRow(mix, "Ding 4 Versatz (s)", AudioTimeOffsetSetting.DingLight);

        var ambience = AddAudioGroup("Ambience", false);
        var details = AddAudioGroup("Ambience-Details", false);
        var mining = AddAudioGroup("Abbau", true);
        var player = AddAudioGroup("Spieler & UI", true);
        var animals = AddAudioGroup("Tiere & Umgebung", false);
        var other = AddAudioGroup("Weitere Sounds", false);

        var torchClips = Resources.Load<TorchAudioClipsAsset>("Audio/TorchAudioClips");
        if (torchClips)
        {
            AddAudioClipRow(animals, torchClips.placeTorch);
            AddAudioClipRow(animals, torchClips.removeTorch);
        }

        var audio = AudioManager.Instance;
        var map = FindFirstObjectByType<MapGenerator>();
        if (map && map.layers != null)
        {
            for (int layerIndex = 0; layerIndex < map.layers.Length; layerIndex++)
            {
                var layer = map.layers[layerIndex];
                if (layer == null || !layer.stone) continue;
                var layerGroup = AddAudioGroup("Layer " + (layerIndex + 1), layerIndex == 0, mining);
                SoundType hit = layerIndex >= 2 ? SoundType.DigDeepStone : layer.stone.digSound;
                SoundType breaking = layerIndex >= 2 ? SoundType.StoneBreak : SoundType.ClayBreak;
                AddAudioLayerClips(layerGroup, layerIndex, hit, false);
                if (layerIndex >= 2)
                    AddAudioLayerClips(layerGroup, layerIndex, SoundType.DigDeepOreHit, false, "Erz · ");
                AddAudioLayerClips(layerGroup, layerIndex, breaking, true);
            }
        }
        var otherMining = AddAudioGroup("Weitere Abbausounds", false, mining);

        foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (!behaviour) continue;
            foreach (var pair in GetAudioFields(behaviour))
            {
                if (behaviour is SurfaceAmbience) AddAudioClipRow(ambience, pair.clip);
                else if (behaviour is FirstLayerAmbience first)
                {
                    if (pair.field == "detailClips")
                        AddAudioClipRow(details, pair.clip, first, pair.index);
                    else if (pair.field == "clip") AddAudioClipRow(ambience, pair.clip);
                }
                else if (behaviour is SecondLayerAmbience)
                    AddAudioClipRow(pair.field == "ghostWhisperClips" ? details : ambience, pair.clip);
                else if (behaviour is SurfaceBirds || behaviour is SurfaceCritters)
                    AddAudioClipRow(animals, pair.clip);
                else if (behaviour is AudioManager)
                    AddAudioClipRow(ClassifyAudioClip(pair.clip, ambience, details, otherMining,
                        player, animals, other), pair.clip);
                else
                    AddAudioClipRow(ClassifyAudioClip(pair.clip, ambience, details, otherMining,
                        player, animals, other), pair.clip);
            }
        }

        foreach (SoundType type in Enum.GetValues(typeof(SoundType)))
        {
            var target = IsMiningSound(type) ? otherMining : IsEnvironmentSound(type) ? animals : player;
            int count = audio.GetSoundClipCount(type);
            for (int i = 0; i < count; i++) AddAudioClipRow(target, audio.GetSoundClip(type, i));
        }

        foreach (var clip in Resources.LoadAll<AudioClip>(""))
            AddAudioClipRow(ClassifyAudioClip(clip, ambience, details, otherMining,
                player, animals, other), clip);
#if UNITY_EDITOR
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:AudioClip",
                     new[] { "Assets" }))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Archive/") || path.Contains("/_SceneBackups/")) continue;
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            AddAudioClipRow(ClassifyAudioClip(clip, ambience, details, otherMining,
                player, animals, other), clip);
        }
#endif
        RefreshAudioBrowserRows();
        LayoutAudioBrowser();
    }

    static IEnumerable<(string field, int index, AudioClip clip)> GetAudioFields(MonoBehaviour behaviour)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in behaviour.GetType().GetFields(flags))
        {
            if (field.FieldType == typeof(AudioClip))
            {
                if (field.GetValue(behaviour) is AudioClip clip && clip)
                    yield return (field.Name, -1, clip);
            }
            else if (field.FieldType == typeof(AudioClip[]))
            {
                if (field.GetValue(behaviour) is not AudioClip[] clips) continue;
                for (int i = 0; i < clips.Length; i++)
                    if (clips[i]) yield return (field.Name, i, clips[i]);
            }
        }
    }

    static AudioBrowserGroup ClassifyAudioClip(AudioClip clip, AudioBrowserGroup ambience,
        AudioBrowserGroup details, AudioBrowserGroup mining, AudioBrowserGroup player,
        AudioBrowserGroup animals, AudioBrowserGroup other)
    {
        if (!clip) return other;
        string name = clip.name.ToLowerInvariant();
        if (name.Contains("detail") || name.Contains("whisper")) return details;
        if (name.Contains("ambience") || name.Contains("ambient") || name.Contains("wind") ||
            name.Contains("tribal") || name.Contains("meadow")) return ambience;
        if (name.Contains("ultronium")) return player;
        if (name.Contains("dirt") || name.Contains("stone") || name.Contains("ore") ||
            name.Contains("pickaxe") || name.Contains("mining") || name.Contains("crumble") ||
            name.Contains("breakrock") || name.Contains("claybreak") ||
            name.Contains("metal") || name.Contains("blunt") || name.Contains("rubble")) return mining;
        if (name.Contains("bird") || name.Contains("chirp") || name.Contains("frog") ||
            name.Contains("croak") || name.Contains("grass") || name.Contains("tree") ||
            name.Contains("wood") || name.Contains("torch")) return animals;
        if (name.Contains("hurt") || name.Contains("heartbeat") || name.Contains("ding") ||
            name.Contains("craft") || name.Contains("cloth") || name.Contains("paper") ||
            name.Contains("gameover") || name.Contains("bip")) return player;
        return other;
    }

    static bool IsMiningSound(SoundType type) => type is SoundType.DigSoft or SoundType.DigMedium or
        SoundType.DigHard or SoundType.DigOre or SoundType.BreakRock or SoundType.BreakOre or
        SoundType.DigDirt or SoundType.DigTransitionStone or SoundType.DigStone or
        SoundType.DirtHit or SoundType.DigDeepStone or SoundType.DigDeepOreHit or
        SoundType.StoneBreak or SoundType.ClayBreak;

    static bool IsEnvironmentSound(SoundType type) => type is SoundType.WoodChop or
        SoundType.LadderPlace or SoundType.LadderRemove or SoundType.DryGrass or SoundType.TreeFall;

    AudioBrowserGroup AddAudioGroup(string title, bool expanded, AudioBrowserGroup parent = null)
    {
        var group = new AudioBrowserGroup { title = title, expanded = expanded };
        if (parent == null) audioBrowserGroups.Add(group);
        else parent.children.Add(group);
        group.header = MakeRect("AudioGroup" + nextAudioBrowserId++, audioBrowserRoot);
        var image = group.header.gameObject.AddComponent<Image>();
        image.color = AudioPanelColor;
        var button = group.header.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            group.expanded = !group.expanded;
            LayoutAudioBrowser();
        });
        group.titleText = CreateAudioText(group.header, "", 24f, AudioGold,
            TextAlignmentOptions.MidlineLeft);
        group.countText = CreateAudioText(group.header, "", 18f,
            new Color(.69f, .74f, .8f), TextAlignmentOptions.MidlineRight);
        group.columns = MakeRect("Columns", audioBrowserRoot);
        CreateAudioColumnText(group.columns, "Lautstärke %", 0);
        CreateAudioColumnText(group.columns, "Pitch %", 1);
        CreateAudioColumnText(group.columns, "Spread ± %", 2);
        return group;
    }

    void AddAudioLayerClips(AudioBrowserGroup group, int layerIndex, SoundType type, bool breaking,
        string labelPrefix = null)
    {
        var audio = AudioManager.Instance;
        int count = audio.GetSoundClipCount(type);
        for (int i = 0; i < count; i++)
        {
            var clip = audio.GetSoundClip(type, i);
            if (!clip) continue;
            audioCataloguedClips.Add(clip);
            var prefix = labelPrefix ?? (breaking ? "Bruch · " : "Hieb · ");
            var row = AddAudioBrowserRow(group, prefix + clip.name, clip);
            row.layerIndex = layerIndex;
            row.soundType = type;
            row.breaking = breaking;
            row.clipIndex = i;
        }
    }

    void AddAudioClipRow(AudioBrowserGroup group, AudioClip clip,
        FirstLayerAmbience detailOwner = null, int detailIndex = -1)
    {
        if (!clip || !audioCataloguedClips.Add(clip)) return;
        var row = AddAudioBrowserRow(group, clip.name, clip);
        row.detailOwner = detailOwner;
        row.detailIndex = detailIndex;
    }

    void AddAudioVolumeRow(AudioBrowserGroup group, string title, AudioVolumeSetting setting)
    {
        var row = AddAudioBrowserRow(group, title, null);
        row.volumeSetting = setting;
        row.inputs[1].transform.parent.gameObject.SetActive(false);
        row.inputs[2].transform.parent.gameObject.SetActive(false);
    }

    void AddAudioOffsetRow(AudioBrowserGroup group, string title, AudioTimeOffsetSetting setting)
    {
        var row = AddAudioBrowserRow(group, title, null);
        row.offsetSetting = setting;
        row.inputs[1].transform.parent.gameObject.SetActive(false);
        row.inputs[2].transform.parent.gameObject.SetActive(false);
    }

    AudioBrowserRow AddAudioBrowserRow(AudioBrowserGroup group, string title, AudioClip clip)
    {
        var row = new AudioBrowserRow { title = title, clip = clip };
        group.rows.Add(row);
        audioBrowserRows.Add(row);
        row.rect = MakeRect("AudioRow" + nextAudioBrowserId++, audioBrowserRoot);
        var image = row.rect.gameObject.AddComponent<Image>();
        image.color = AudioRowColor;
        image.raycastTarget = false;
        CreateAudioText(row.rect, title, 22f, Color.white, TextAlignmentOptions.MidlineLeft);
        var play = MakeRect("Play", row.rect);
        var playImage = play.gameObject.AddComponent<Image>();
        playImage.color = new Color(.12f, .17f, .24f, .98f);
        var playButton = play.gameObject.AddComponent<Button>();
        playButton.targetGraphic = playImage;
        playButton.onClick.AddListener(() => PreviewAudioBrowserRow(row));
        var speaker = MakeRect("SpeakerIcon", play);
        speaker.anchorMin = Vector2.zero;
        speaker.anchorMax = Vector2.one;
        speaker.offsetMin = new Vector2(7f, 7f);
        speaker.offsetMax = new Vector2(-7f, -7f);
        var speakerIcon = speaker.gameObject.AddComponent<AudioSpeakerIcon>();
        speakerIcon.color = AudioGold;
        speakerIcon.raycastTarget = false;
        play.name = "Play";
        if (!clip) play.gameObject.SetActive(false);
        for (int i = 0; i < 3; i++)
        {
            int field = i;
            var holder = MakeRect("Value" + i, row.rect);
            var minus = CreateAudioButton(holder, "−", 20f, () => StepAudioBrowserRow(row, field, -1f));
            minus.name = "Minus";
            var input = CloneItem("DiggingSpeed", "AudioBrowserInput" + nextAudioBrowserId++, holder)
                .GetComponent<TMP_InputField>();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.contentType = TMP_InputField.ContentType.DecimalNumber;
            input.characterLimit = 8;
            input.textComponent.fontSize = 21f;
            input.textComponent.alignment = TextAlignmentOptions.Center;
            if (input.targetGraphic is Image background)
                background.color = new Color(.11f, .16f, .22f, .96f);
            DisableInputChildRaycasts(input);
            input.onEndEdit.AddListener(_ => CommitAudioBrowserRow(row, field));
            row.inputs[i] = input;
            var plus = CreateAudioButton(holder, "+", 20f, () => StepAudioBrowserRow(row, field, 1f));
            plus.name = "Plus";
        }
        return row;
    }

    void PreviewAudioBrowserRow(AudioBrowserRow row)
    {
        var audio = AudioManager.Instance;
        if (!audio || !row.clip) return;
        if (row.layerIndex >= 0)
            audio.PreviewLayerMiningClip(row.layerIndex, row.soundType, row.breaking, row.clipIndex);
        else audio.PreviewClip(row.clip, row.detailOwner && row.detailIndex >= 0
            ? row.detailOwner.GetDetailVolumeMultiplier(row.detailIndex) : 1f);
    }

    void StepAudioBrowserRow(AudioBrowserRow row, int field, float step)
    {
        if (!float.TryParse(row.inputs[field].text.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value)) value = field == 2 ? 0f : 100f;
        row.inputs[field].SetTextWithoutNotify((value + step).ToString("0.##", CultureInfo.InvariantCulture));
        CommitAudioBrowserRow(row, field);
    }

    void CommitAudioBrowserRow(AudioBrowserRow row, int field)
    {
        var audio = AudioManager.Instance;
        if (!audio || !float.TryParse(row.inputs[field].text.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value))
        { RefreshAudioBrowserRow(row); return; }
        float minimum = row.offsetSetting.HasValue ? -10f : field == 1 ? 50f : 0f;
        float maximum = row.offsetSetting.HasValue ? 10f : field == 1 ? 200f : field == 2 ? 50f : 100f;
        if (value < minimum || value > maximum) { RefreshAudioBrowserRow(row); return; }
        if (row.volumeSetting.HasValue)
        {
            audio.SetVolume(row.volumeSetting.Value, value / 100f);
            QueueGpsDefault(audio, AudioVolumeProperty(row.volumeSetting.Value));
        }
        else if (row.offsetSetting.HasValue)
        {
            audio.SetTimeOffset(row.offsetSetting.Value, value);
            QueueGpsDefault(audio, "dingLightOffsetSeconds");
        }
        else if (row.layerIndex >= 0)
        {
            var tuning = audio.GetLayerMiningClipTuning(row.layerIndex, row.breaking,
                row.soundType, row.clipIndex);
            float volume = field == 0 ? value / 100f : tuning.volume;
            float pitch = field == 1 ? value / 100f : tuning.pitch;
            float spread = field == 2 ? value / 100f : tuning.pitchSpread;
            audio.SetLayerMiningClipTuning(row.layerIndex, row.breaking, row.soundType,
                row.clipIndex, volume, pitch, spread);
        }
        else if (row.clip)
        {
            audio.GetClipTuning(row.clip, out float volume, out float pitch, out float spread);
            if (row.detailOwner && row.detailIndex >= 0 && field == 0)
            {
                row.detailOwner.SetDetailVolumeMultiplier(row.detailIndex, value / 100f);
                QueueGpsDefault(row.detailOwner, "detailVolumeMultipliers");
            }
            else
            {
                if (field == 0) volume = value / 100f;
                else if (field == 1) pitch = value / 100f;
                else spread = value / 100f;
                audio.SetClipTuning(row.clip, volume, pitch, spread);
            }
        }
        RefreshAudioBrowserRow(row);
    }

    void RefreshAudioBrowserRows()
    {
        foreach (var row in audioBrowserRows) RefreshAudioBrowserRow(row);
    }

    void RefreshAudioBrowserRow(AudioBrowserRow row)
    {
        var audio = AudioManager.Instance;
        if (!audio || !row.inputs[0]) return;
        float volume, pitch = 1f, spread = 0f;
        if (row.volumeSetting.HasValue) volume = audio.GetVolume(row.volumeSetting.Value);
        else if (row.offsetSetting.HasValue)
        {
            row.inputs[0].SetTextWithoutNotify(audio.GetTimeOffset(row.offsetSetting.Value)
                .ToString("0.##", CultureInfo.InvariantCulture));
            return;
        }
        else if (row.layerIndex >= 0)
        {
            var tuning = audio.GetLayerMiningClipTuning(row.layerIndex, row.breaking,
                row.soundType, row.clipIndex);
            volume = tuning.volume; pitch = tuning.pitch; spread = tuning.pitchSpread;
        }
        else
        {
            audio.GetClipTuning(row.clip, out volume, out pitch, out spread);
            if (row.detailOwner && row.detailIndex >= 0)
                volume = row.detailOwner.GetDetailVolumeMultiplier(row.detailIndex);
        }
        row.inputs[0].SetTextWithoutNotify((volume * 100f).ToString("0.##", CultureInfo.InvariantCulture));
        if (row.volumeSetting.HasValue) return;
        row.inputs[1].SetTextWithoutNotify((pitch * 100f).ToString("0.##", CultureInfo.InvariantCulture));
        row.inputs[2].SetTextWithoutNotify((spread * 100f).ToString("0.##", CultureInfo.InvariantCulture));
    }

    void LayoutAudioBrowser(float x = 18f, float width = 940f)
    {
        if (!audioBrowserRoot) return;
        Place(audioBrowserRoot.name, x, 12f, width, 100f);
        SetAudioRect(audioSearch.GetComponent<RectTransform>(), 0f, 0f, width, 50f);
        string query = audioSearch.text.Trim();
        float y = 64f;
        foreach (var group in audioBrowserGroups)
            LayoutAudioGroup(group, 0, query, width, ref y);
        audioBrowserRoot.sizeDelta = new Vector2(width, y + 12f);
        content.sizeDelta = new Vector2(0f, y + 36f);
    }

    void LayoutAudioGroup(AudioBrowserGroup group, int depth, string query, float width, ref float y)
    {
        if (CountAudioRows(group) == 0 || (!string.IsNullOrEmpty(query) && !GroupMatches(group, query)))
        { HideAudioGroup(group); return; }
        group.header.gameObject.SetActive(true);
        float indent = depth * 24f;
        SetAudioRect(group.header, indent, y, width - indent, 47f);
        group.titleText.text = (group.expanded || query.Length > 0 ? "−  " : "›  ") + group.title;
        SetAudioRect(group.titleText.rectTransform, 18f, 0f, width - indent - 150f, 47f);
        group.countText.text = CountAudioRows(group) + (group.title == "Gesamtpegel" ? " Werte" : " Clips");
        SetAudioRect(group.countText.rectTransform, width - indent - 128f, 0f, 112f, 47f);
        y += 54f;
        if (!group.expanded && query.Length == 0)
        {
            group.columns.gameObject.SetActive(false);
            foreach (var row in group.rows) row.rect.gameObject.SetActive(false);
            foreach (var child in group.children) HideAudioGroup(child);
            return;
        }

        bool groupTitleMatches = query.Length > 0 &&
            group.title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        bool visibleRows = false;
        foreach (var row in group.rows)
            if (query.Length == 0 || groupTitleMatches ||
                row.title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
            { visibleRows = true; break; }
        group.columns.gameObject.SetActive(visibleRows);
        if (visibleRows)
        {
            SetAudioRect(group.columns, indent + 18f, y, width - indent - 18f, 31f);
            LayoutAudioColumns(group.columns, width - indent - 18f);
            y += 32f;
        }
        foreach (var row in group.rows)
        {
            bool visible = query.Length == 0 || groupTitleMatches ||
                row.title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
            row.rect.gameObject.SetActive(visible);
            if (!visible) continue;
            SetAudioRect(row.rect, indent + 18f, y, width - indent - 18f, 44f);
            LayoutAudioRow(row, width - indent - 18f);
            y += 46f;
        }
        foreach (var child in group.children)
            LayoutAudioGroup(child, depth + 1, groupTitleMatches ? string.Empty : query, width, ref y);
        y += depth == 0 ? 9f : 2f;
    }

    static bool GroupMatches(AudioBrowserGroup group, string query)
    {
        if (group.title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
        foreach (var row in group.rows)
            if (row.title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
        foreach (var child in group.children)
            if (GroupMatches(child, query)) return true;
        return false;
    }

    static int CountAudioRows(AudioBrowserGroup group)
    {
        int count = group.rows.Count;
        foreach (var child in group.children) count += CountAudioRows(child);
        return count;
    }

    static void HideAudioGroup(AudioBrowserGroup group)
    {
        group.header.gameObject.SetActive(false);
        group.columns.gameObject.SetActive(false);
        foreach (var row in group.rows) row.rect.gameObject.SetActive(false);
        foreach (var child in group.children) HideAudioGroup(child);
    }

    void LayoutAudioRow(AudioBrowserRow row, float width)
    {
        float valueWidth = Mathf.Clamp(width * .18f, 122f, 240f);
        float nameWidth = Mathf.Max(140f, width - valueWidth * 3f - 14f);
        var label = row.rect.GetComponentInChildren<TextMeshProUGUI>();
        SetAudioRect(label.rectTransform, 16f, 0f, nameWidth - 64f, 44f);
        var play = row.rect.Find("Play") as RectTransform;
        SetAudioRect(play, nameWidth - 52f, 3f, 44f, 38f);
        for (int i = 0; i < 3; i++)
        {
            var holder = row.inputs[i].transform.parent as RectTransform;
            SetAudioRect(holder, nameWidth + i * valueWidth, 3f, valueWidth - 8f, 38f);
            SetAudioRect(holder.Find("Minus") as RectTransform, 0f, 0f, 29f, 38f);
            SetAudioRect(row.inputs[i].GetComponent<RectTransform>(), 30f, 0f,
                valueWidth - 68f, 38f);
            SetAudioRect(holder.Find("Plus") as RectTransform, valueWidth - 37f, 0f, 29f, 38f);
        }
    }

    void LayoutAudioColumns(RectTransform columns, float width)
    {
        float valueWidth = Mathf.Clamp(width * .18f, 122f, 240f);
        float nameWidth = Mathf.Max(140f, width - valueWidth * 3f - 14f);
        for (int i = 0; i < 3; i++)
            SetAudioRect(columns.GetChild(i) as RectTransform,
                nameWidth + i * valueWidth, 0f, valueWidth - 8f, 30f);
    }

    void CreateAudioColumnText(RectTransform parent, string text, int index)
    {
        var label = CreateAudioText(parent, text, 18f, new Color(.78f, .83f, .89f),
            TextAlignmentOptions.Center);
        label.name = "Column" + index;
    }

    TextMeshProUGUI CreateAudioText(Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment)
    {
        var rect = MakeRect("Text", parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = items["Title"].GetComponent<TextMeshProUGUI>().font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    RectTransform CreateAudioButton(Transform parent, string text, float fontSize, Action onClick)
    {
        var rect = MakeRect("Button", parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(.12f, .17f, .24f, .98f);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());
        CreateAudioText(rect, text, fontSize, AudioGold, TextAlignmentOptions.Center);
        return rect;
    }

    static void SetAudioRect(RectTransform rect, float x, float y, float width, float height)
    {
        if (!rect) return;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
