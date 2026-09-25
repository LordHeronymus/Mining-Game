using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// This window edits the existing sources of truth; it does not copy scene settings
// into a second configuration asset that could drift out of sync.
public class GameplaySettingsWindow : EditorWindow
{
    static readonly string[] Tabs = { "Spieler", "Energie", "Map", "Erzverteilung", "Partikel", "Blöcke & Beute", "Licht", "Werkbank", "Audio", "Pflanzen", "Tiere" };
    [SerializeField] int tab;
    [SerializeField] int selectedBlock;
    [SerializeField] StatsManager stats;
    [SerializeField] PlayerMovement movement;
    [SerializeField] TileMiner miner;
    [SerializeField] EnergyManager energy;
    [SerializeField] EnergyMonolyth station;
    [SerializeField] MapGenerator map;
    [SerializeField] MapLighting lighting;
    [SerializeField] OreSparkles oreSparkles;
    [SerializeField] BlockBreakParticles blockBreakParticles;
    [SerializeField] ItemFeed itemFeed;
    [SerializeField] SurfaceBirds birds;
    [SerializeField] SurfaceTrees trees;
    [SerializeField] SurfaceTallGrass tallGrass;
    [SerializeField] SurfaceRabbit rabbit;
    [SerializeField] SurfaceRabbitSpawner rabbitSpawner;
    [SerializeField] SurfaceCritters frogs;
    [SerializeField] SurfaceCritters snails;
    [SerializeField] SurfaceFireflies fireflies;
    [SerializeField] CameraFollow follow;
    Vector2 scroll;
    Component[] sceneComponents = Array.Empty<Component>();
    ItemSO[] items = Array.Empty<ItemSO>();
    CraftingRecipe[] recipes = Array.Empty<CraftingRecipe>();
    [SerializeField] CraftingRecipe selectedRecipe;
    bool showOtherItems;
    string notification;
    GameplaySettingsData savedOverride;
    string overrideWarning;
    bool overrideExists;
    readonly HashSet<string> expandedMapSections = new HashSet<string>();
    int draggingDensityKey = -1;
    int densityCurveControl;
    int panningCurveControl;
    float draggingCurveMaximum;
    string selectedCurvePath;
    int selectedCurveKey = -1;
    readonly Dictionary<string, Vector2> curveViews = new Dictionary<string, Vector2>();

    [MenuItem("Mining Game/Gameplay Settings")]
    public static void Open()
    {
        var window = GetWindow<GameplaySettingsWindow>("Gameplay Settings");
        window.minSize = new Vector2(760, 640);
        window.Show();
    }

    void OnEnable()
    {
        minSize = new Vector2(760, 640);
        EditorApplication.hierarchyChanged += Refresh;
        EditorApplication.projectChanged += Refresh;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorSceneManager.activeSceneChangedInEditMode += SceneChanged;
        Undo.undoRedoPerformed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        EditorApplication.hierarchyChanged -= Refresh;
        EditorApplication.projectChanged -= Refresh;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorSceneManager.activeSceneChangedInEditMode -= SceneChanged;
        Undo.undoRedoPerformed -= Refresh;
    }

    void OnFocus() => Refresh();
    void PlayModeChanged(PlayModeStateChange state) => Refresh();
    void SceneChanged(Scene previous, Scene next) => Refresh();

    void Refresh()
    {
        var scene = SceneManager.GetActiveScene();
        sceneComponents = scene.IsValid() && scene.isLoaded
            ? scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Component>(true)).Where(c => c).ToArray()
            : Array.Empty<Component>();
        stats = Resolve(stats);
        movement = Resolve(movement);
        miner = Resolve(miner);
        energy = Resolve(energy);
        station = Resolve(station);
        map = Resolve(map);
        lighting = Resolve(lighting);
        oreSparkles = Resolve(oreSparkles);
        blockBreakParticles = Resolve(blockBreakParticles);
        itemFeed = Resolve(itemFeed);
        birds = Resolve(birds);
        trees = Resolve(trees);
        tallGrass = Resolve(tallGrass);
        rabbit = Resolve(rabbit);
        rabbitSpawner = Resolve(rabbitSpawner);
        frogs = sceneComponents.OfType<SurfaceCritters>().FirstOrDefault(group => group.species == SurfaceCritters.Species.Frog);
        snails = sceneComponents.OfType<SurfaceCritters>().FirstOrDefault(group => group.species == SurfaceCritters.Species.Snail);
        fireflies = Resolve(fireflies);
        follow = Resolve(follow);
        items = AssetDatabase.FindAssets("t:ItemSO").Select(guid => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item).OrderBy(item => item.displayName).ToArray();
        recipes = SortCraftableRecipes(AssetDatabase.FindAssets("t:CraftingRecipe")
            .Select(guid => AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(guid))));
        if (!selectedRecipe || !recipes.Contains(selectedRecipe)) selectedRecipe = recipes.FirstOrDefault();
        ReadOverride();
        Repaint();
    }

    T Resolve<T>(T current) where T : Component => current && sceneComponents.Contains(current)
        ? current : sceneComponents.OfType<T>().FirstOrDefault();

    public static CraftingRecipe[] SortCraftableRecipes(IEnumerable<CraftingRecipe> candidates)
    {
        return candidates
            .Where(recipe => recipe && recipe.TryGetCosts(out _))
            .OrderBy(recipe => RecipeCategoryOrder(recipe.Category))
            .ThenBy(recipe => recipe.output.displayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(recipe => recipe.name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string[] GetRecipeLabels(IEnumerable<CraftingRecipe> source)
    {
        return source.Select(recipe => RecipeCategoryLabel(recipe.Category) + " · " + recipe.output.displayName).ToArray();
    }

    static int RecipeCategoryOrder(CraftingRecipe.RecipeCategory category) => category switch
    {
        CraftingRecipe.RecipeCategory.Building => 0,
        CraftingRecipe.RecipeCategory.Materials => 1,
        CraftingRecipe.RecipeCategory.Tools => 2,
        _ => 3
    };

    static string RecipeCategoryLabel(CraftingRecipe.RecipeCategory category) => category switch
    {
        CraftingRecipe.RecipeCategory.Building => "Bauen",
        CraftingRecipe.RecipeCategory.Materials => "Materialien",
        CraftingRecipe.RecipeCategory.Tools => "Werkzeuge",
        _ => "Sonstiges"
    };

    PlayerBaseStats BaseStats => stats ? new SerializedObject(stats).FindProperty("baseStats").objectReferenceValue as PlayerBaseStats : null;
    BlockRegistry Registry => map ? map.registry : null;

    void ReadOverride()
    {
        overrideExists = File.Exists(GameplaySettings.FilePath);
        savedOverride = GameplaySettingsStore.Load(GameplaySettings.FilePath, BaseStats ? BaseStats.miningSpeed : 1.5f, out overrideWarning);
    }

    void OnGUI()
    {
        bool saveRequested = false;
        EditorGUIUtility.labelWidth = 285;
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Gameplay Settings", EditorStyles.largeLabel);
        var scene = SceneManager.GetActiveScene();
        EditorGUILayout.LabelField("Aktive Szene: " + (scene.IsValid() ? scene.name : "keine") + (scene.isDirty ? "  • ungespeichert" : ""), EditorStyles.miniLabel);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Aktualisieren", EditorStyles.toolbarButton, GUILayout.Width(105))) Refresh();
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Einstellungen speichern", EditorStyles.toolbarButton, GUILayout.Width(170)))
                {
                    // Finish field focus, then save after this GUI pass applies
                    // any remaining property changes.
                    GUI.FocusControl(null);
                    saveRequested = true;
                }
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox("Standardwerte sind im Play-Modus schreibgeschützt. Zum Ausprobieren F1 verwenden; für dauerhafte Änderungen den Play-Modus beenden.", MessageType.Info);
        else if (overrideExists)
            EditorGUILayout.HelpBox(overrideWarning ?? $"F1-Test-Override aktiv: Abbaugeschwindigkeit {savedOverride.baseDiggingSpeed:g}{(savedOverride.hasLightingOverride ? " und Lichtwerte" : "")}. Im Editor und in Development Builds haben diese Werte Vorrang.", overrideWarning == null ? MessageType.Warning : MessageType.Error);

        int nextTab = DrawTabRows();
        if (nextTab != tab) { tab = nextTab; scroll = Vector2.zero; }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            switch (tab)
            {
                case 0: DrawPlayer(); break;
                case 1: DrawEnergy(); break;
                case 2: DrawMap(); break;
                case 3: DrawOreDistribution(); break;
                case 4: DrawParticles(); break;
                case 5: DrawBlocks(); break;
                case 6: DrawLighting(); break;
                case 7: DrawWorkbench(); break;
                case 8: DrawAudio(); break;
                case 9: DrawPlants(); break;
                case 10: DrawAnimals(); break;
            }
        }
        EditorGUILayout.Space(12);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.LabelField("Änderungen direkt an Asset/Szene · Strg+Z: Undo · Speichern sichert Assets und aktive Szene", EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(notification)) EditorGUILayout.HelpBox(notification, MessageType.Info);
        if (saveRequested) SaveSettings();
    }

    int DrawTabRows()
    {
        const int tabsPerRow = 6;
        int selected = tab;
        for (int first = 0; first < Tabs.Length; first += tabsPerRow)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int last = Mathf.Min(first + tabsPerRow, Tabs.Length);
                for (int index = first; index < last; index++)
                    if (GUILayout.Toggle(tab == index, Tabs[index], EditorStyles.toolbarButton,
                        GUILayout.Height(28), GUILayout.ExpandWidth(true)) && index != tab) selected = index;
            }
        }
        return selected;
    }

    void DrawPlants()
    {
        Section("Bäume", trees, data =>
        {
            Integer(data, "maximumTrees", "Maximale Anzahl", "", 0, 20);
            var spacing = data.FindProperty("minimumTreeSpacing");
            float cellWidth = map && map.Terrain ? map.Terrain.layoutGrid.cellSize.x : .5f;
            EditorGUI.BeginChangeCheck();
            int tiles = EditorGUILayout.IntField("Mindestabstand (Kacheln)",
                Mathf.RoundToInt(spacing.floatValue / cellWidth));
            if (EditorGUI.EndChangeCheck()) spacing.floatValue = Mathf.Max(4, tiles) * cellWidth;
            Integer(data, "hitsToFell", "Treffer zum Fällen", "", 1, 100);
            Float(data, "fallDurationSeconds", "Fälldauer (s)", "", .1f, 10f);
            EditorGUILayout.CurveField(data.FindProperty("fallRotationCurve"), new Color(.42f, .72f, .28f),
                new Rect(0f, 0f, 1f, 1f), new GUIContent("Fallverlauf"));
            Float(data, "axeHitMultiplier", "Axt-Multiplikator", "", 1f, 20f);
            Integer(data, "woodYieldMin", "Holz mindestens", "", 1, 9999);
            Integer(data, "woodYieldMax", "Holz höchstens", "", 1, 9999);
            var minimum = data.FindProperty("woodYieldMin");
            var maximum = data.FindProperty("woodYieldMax");
            if (maximum.intValue < minimum.intValue) maximum.intValue = minimum.intValue;
            Integer(data, "maximumBonusWood", "Max. Bonus-Holz", "", 0, 9999 - maximum.intValue);
            Float(data, "growthSpeedPercentPerMinute", "Wachstum (% pro Minute)", "", 0f, 300f);
            Float(data, "maximumSizeBonusPercent", "Max. Größenbonus (%)", "", 0f, 200f);
            var interval = data.FindProperty("regrowthSeconds");
            Vector2 seconds = interval.vector2Value;
            EditorGUI.BeginChangeCheck();
            float earliest = EditorGUILayout.FloatField("Nachwuchs frühestens (s)", seconds.x);
            float latest = EditorGUILayout.FloatField("Nachwuchs spätestens (s)", seconds.y);
            if (EditorGUI.EndChangeCheck() && Finite(earliest) && Finite(latest))
            {
                earliest = Mathf.Clamp(earliest, 1f, 3600f);
                interval.vector2Value = new Vector2(earliest, Mathf.Clamp(latest, earliest, 3600f));
            }
        }, false);

        Section("Fasergras", tallGrass, data =>
        {
            Integer(data, "maximumPatches", "Menge", "", 0, 500);
            Integer(data, "minimumSpacing", "Mindestabstand (Kacheln)", "", 1, 100);
            var randomness = data.FindProperty("randomness");
            EditorGUI.BeginChangeCheck();
            float randomPercent = EditorGUILayout.FloatField("Zufälligkeit (%)", randomness.floatValue * 100f);
            if (EditorGUI.EndChangeCheck() && Finite(randomPercent))
                randomness.floatValue = Mathf.Clamp01(randomPercent / 100f);
            Float(data, "spawnQuietRadius", "Spawnradius (m)", "", 0f, 1000f);
            var nearDensity = data.FindProperty("nearSpawnDensity");
            EditorGUI.BeginChangeCheck();
            float nearPercent = EditorGUILayout.FloatField("Menge am Spawn (%)", nearDensity.floatValue * 100f);
            if (EditorGUI.EndChangeCheck() && Finite(nearPercent))
                nearDensity.floatValue = Mathf.Clamp01(nearPercent / 100f);
            FloatRange(data, "respawnSeconds", "Respawnzeit (s)", .1f, 3600f);
            var fiberYield = data.FindProperty("fiberYield");
            Vector2Int yield = fiberYield.vector2IntValue;
            EditorGUI.BeginChangeCheck();
            int minimumFiber = EditorGUILayout.IntField("Fasern mindestens", yield.x);
            int maximumFiber = EditorGUILayout.IntField("Fasern höchstens", yield.y);
            if (EditorGUI.EndChangeCheck())
            {
                minimumFiber = Mathf.Clamp(minimumFiber, 1, 9999);
                fiberYield.vector2IntValue = new Vector2Int(minimumFiber,
                    Mathf.Clamp(maximumFiber, minimumFiber, 9999));
            }
        }, false);
    }

    void DrawAnimals()
    {
        Section("Vögel", birds, data =>
        {
            Float(data, "size", "Größe", "", .1f, 10f);
            FloatRange(data, "flockInterval", "Schwarmintervall (s)", .1f, 3600f);
            Integer(data, "birdsPerFlock", "Vögel pro Schwarm", "", 1, 8);
            Float(data, "flightSpeed", "Fluggeschwindigkeit", "", .1f, 100f);
            FloatRange(data, "altitude", "Flughöhe", 0f, 100f);
            Float(data, "wingbeatsPerSecond", "Flügelschläge pro Sekunde", "", .1f, 30f);
            Float(data, "nightOffscreenDespawnDelay", "Nacht-Despawn (s)", "", .1f, 3600f);
        }, false);

        Section("Häschen", rabbitSpawner, data =>
        {
            FloatRange(data, "spawnInterval", "Spawnintervall (s)", .1f, 3600f);
            Integer(data, "maxRabbits", "Maximale Anzahl", "", 1, 20);
            Float(data, "despawnDistance", "Despawn-Abstand", "", 1f, 10000f);
            Float(data, "despawnDelay", "Despawn-Verzögerung (s)", "", .1f, 3600f);
        }, false);
        Section("Häschen-Bewegung", rabbit, data =>
        {
            Float(data, "roamRadius", "Bewegungsradius", "", 1f, 1000f);
            Float(data, "hopHeight", "Hüpfhöhe", "", .1f, 100f);
            Float(data, "hopDuration", "Hüpfdauer (s)", "", .2f, 60f);
            Float(data, "entryDelay", "Wartezeit vor Auftauchen (s)", "", 0f, 3600f);
            FloatRange(data, "entryRestDuration", "Pausen beim Reinhoppeln (s)", .1f, 3600f);
            FloatRange(data, "restDuration", "Ruhephase (s)", .1f, 3600f);
            FloatRange(data, "restInterval", "Abstand zwischen Ruhephasen (s)", .1f, 3600f);
        }, false);

        DrawCritterBehavior("Frösche", frogs, true);
        DrawCritterBehavior("Schnecken", snails, false);

        Section("Glühwürmchen", fireflies, data =>
        {
            Integer(data, "count", "Anzahl", "", 0, 80);
            Float(data, "cameraMargin", "Abstand ausserhalb der Kamera", "", 0f, 1000f);
            FloatRange(data, "altitude", "Flughöhe", 0f, 100f);
            Float(data, "flightSpeed", "Fluggeschwindigkeit", "", .01f, 100f);
            Float(data, "pulseDuration", "Pulsdauer (s)", "", .1f, 3600f);
            Float(data, "fadeDuration", "Ein-/Ausblenden (s)", "", .1f, 3600f);
        }, false);
    }

    void DrawCritterBehavior(string title, SurfaceCritters critter, bool frog)
    {
        Section(title, critter, data =>
        {
            Integer(data, "maxCount", "Maximale Anzahl", "", 0, 12);
            FloatRange(data, "spawnInterval", "Spawnintervall (s)", .1f, 3600f);
            FloatRange(data, "restDuration", "Ruhephasen (s)", .1f, 3600f);
            Float(data, "roamRadius", "Bewegungsradius", "", .5f, 1000f);
            Float(data, "despawnDistance", "Despawn-Abstand", "", 2f, 10000f);
            Float(data, "despawnDelay", "Despawn-Verzögerung (s)", "", .1f, 3600f);
            if (frog)
            {
                Float(data, "hopHeight", "Sprunghöhe", "", .1f, 100f);
                Float(data, "hopDuration", "Sprungdauer (s)", "", .1f, 60f);
                FloatRange(data, "hopDistance", "Sprungweite", .1f, 100f);
            }
            else Float(data, "crawlSpeed", "Kriechgeschwindigkeit", "", .01f, 100f);
        }, false);
    }

    void DrawAudio()
    {
        Section("Ambience", sceneComponents.OfType<AudioManager>().FirstOrDefault(), data =>
        {
            VolumeSlider(data, "ambienceVolume", "Gesamtlautstärke (%)");
            VolumeSlider(data, "surfaceVolume", "Oberfläche (%)");
            VolumeSlider(data, "rainVolume", "Regen (%)");
            VolumeSlider(data, "thunderstormVolume", "Gewitter (%)");
            VolumeSlider(data, "undergroundVolume", "Untergrund (%)");
            VolumeSlider(data, "caveVolume", "Höhle (%)");
        }, false);
        Section("Tier-Landungen", sceneComponents.OfType<AudioManager>().FirstOrDefault(), data =>
        {
            Float(data, "grassLandingOffset", "Zeitversatz (s)", "", -1f, 1f);
        }, false);
        Section("Abbausounds", sceneComponents.OfType<AudioManager>().FirstOrDefault(), data =>
        {
            VolumeSlider(data, "digSoundVolume", "Lautstärke (%)");
        }, false);
        Section("Untergrund-Details", sceneComponents.OfType<FirstLayerAmbience>().FirstOrDefault(), data =>
        {
            Float(data, "detailsPerMinute", "Ereignisse pro Minute", "", 0f, 60f);
        }, false);
        Section("Höhlen-Tribal-Song", sceneComponents.OfType<SecondLayerAmbience>().FirstOrDefault(), data =>
        {
            int offset = map && map.layers != null && map.layers.Length > 0 && map.layers[0] != null &&
                map.layers[0].stone && map.layers[0].stone.id == BlockType.Dirt ? 1 : 0;
            Float(data, "tribalSongLayer2MeanMinutes", "Ø Minuten in Layer " + (2 + offset), "", .01f, 120f);
            Float(data, "tribalSongLayer3MeanMinutes", "Ø Minuten in Layer " + (3 + offset), "", .01f, 240f);
        }, false);
        Section("Geisterflüstern", sceneComponents.OfType<SecondLayerAmbience>().FirstOrDefault(), data =>
        {
            Float(data, "ghostWhisperMeanMinutes", "Ø Minuten", "", .01f, 120f);
        }, false);
        Section("Vogelgezwitscher", birds, data =>
        {
            VolumeSlider(data, "chirpVolume", "Lautstärke (%)");
            Float(data, "chirpsPerMinute", "Rufe pro Minute", "", 0f, 120f);
            EditorGUILayout.Slider(data.FindProperty("chirpIrregularity"), 0f, 1f,
                new GUIContent("Unregelmässigkeit"));
        }, false);
        Section("Froschquaken", sceneComponents.OfType<SurfaceCritters>()
            .FirstOrDefault(group => group.species == SurfaceCritters.Species.Frog), data =>
        {
            VolumeSlider(data, "croakVolume", "Lautstärke (%)");
            Float(data, "croaksPerMinute", "Quaks pro Minute", "", 0f, 120f);
            EditorGUILayout.Slider(data.FindProperty("croakIrregularity"), 0f, 1f,
                new GUIContent("Unregelmäßigkeit"));
        }, false);
    }

    static void VolumeSlider(SerializedObject data, string field, string label)
    {
        var volume = data.FindProperty(field);
        EditorGUI.BeginChangeCheck();
        float percent = EditorGUILayout.Slider(label, volume.floatValue * 100f, 0f, 100f);
        if (EditorGUI.EndChangeCheck()) volume.floatValue = percent / 100f;
    }

    void DrawWorkbench()
    {
        if (recipes.Length == 0)
        {
            EditorGUILayout.LabelField("Keine Rezepte");
            return;
        }
        int index = Mathf.Max(0, Array.IndexOf(recipes, selectedRecipe));
        index = EditorGUILayout.Popup("Rezept", index, GetRecipeLabels(recipes));
        selectedRecipe = recipes[index];
        Section("Rezept", selectedRecipe, data =>
        {
            var category = data.FindProperty("category");
            category.enumValueIndex = EditorGUILayout.Popup("Kategorie", category.enumValueIndex,
                new[] { "Automatisch", "Werkzeuge", "Bauen", "Materialien" });
            Integer(data, "outputAmount", "Hergestellte Stückzahl", "", 1, int.MaxValue);
            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Zutaten", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Anzahl", EditorStyles.boldLabel, GUILayout.Width(100));
                GUILayout.Space(30);
            }
            var ingredients = data.FindProperty("ingredients");
            var choices = items.Where(item => item != selectedRecipe.output).ToArray();
            var names = new[] { "Auswählen" }.Concat(choices.Select(item => item.displayName)).ToArray();
            int remove = -1;
            for (int i = 0; i < ingredients.arraySize; i++)
            {
                var ingredient = ingredients.GetArrayElementAtIndex(i);
                var item = ingredient.FindPropertyRelative("item");
                var amount = ingredient.FindPropertyRelative("amount");
                using (new EditorGUILayout.HorizontalScope())
                {
                    int current = Array.IndexOf(choices, item.objectReferenceValue as ItemSO) + 1;
                    EditorGUI.BeginChangeCheck();
                    int next = EditorGUILayout.Popup(current, names);
                    if (EditorGUI.EndChangeCheck()) item.objectReferenceValue = next > 0 ? choices[next - 1] : null;
                    EditorGUI.BeginChangeCheck();
                    int value = EditorGUILayout.IntField(amount.intValue, GUILayout.Width(100));
                    if (EditorGUI.EndChangeCheck()) amount.intValue = Mathf.Max(1, value);
                    using (new EditorGUI.DisabledScope(ingredients.arraySize <= 1))
                        if (GUILayout.Button("−", GUILayout.Width(26))) remove = i;
                }
            }
            if (remove >= 0) ingredients.DeleteArrayElementAtIndex(remove);
            using (new EditorGUI.DisabledScope(choices.Length == 0))
                if (GUILayout.Button("Zutat hinzufügen"))
                {
                    int added = ingredients.arraySize++;
                    var ingredient = ingredients.GetArrayElementAtIndex(added);
                    ingredient.FindPropertyRelative("item").objectReferenceValue = choices.FirstOrDefault();
                    ingredient.FindPropertyRelative("amount").intValue = 1;
                }
        }, false);
    }

    void DrawLighting()
    {
        var sky = sceneComponents.OfType<SkyController>().FirstOrDefault();
        Section("Himmel", sky, data =>
        {
            Float(data, "SkyFadeDuration", "SkyFadeDuration (s)", "", 0.01f);
            EditorGUILayout.PropertyField(data.FindProperty("automaticCycle"), new GUIContent("Automatischer Wechsel"));
            Float(data, "DayDuration", "Tagdauer (s)", "", 0.01f);
            Float(data, "NightDuration", "Nachtdauer (s)", "", 0.01f);
            EditorGUILayout.PropertyField(data.FindProperty("isNight"), new GUIContent("Nacht"));
        });
        lighting = Picker("Map-Beleuchtung", lighting);
        Section("Tageslicht und Dunkelheit", lighting, data =>
        {
            EditorGUILayout.PropertyField(data.FindProperty("lightingEnabled"), new GUIContent("Beleuchtung aktiv"));
            Float(data, "daylightStrength", "Tageslichtstärke", "Ausgangswert des Lichts über der obersten Blockreihe. 1 = volle Helligkeit.", 0, 1);
            Float(data, "ambientBrightness", "Grundhelligkeit im Untergrund", "Untergrenze der Helligkeit. 0 = vollständige Dunkelheit. Dieser Wert erzeugt kein weiterwanderndes Licht.", 0, 1);
        });
        Section("Exponentieller Lichtabfall", lighting, data =>
        {
            Float(data, "exponentialStrength", "Exponentielle Stärke", "1 = normal. Höhere Werte lassen Licht schneller abklingen; kleinere Werte verlängern die weiche Auslaufzone.", 0.1f, 5f);
            Float(data, "downwardLoss", "Nach unten durch Luft", "Anteil des verbleibenden Lichts pro Schritt bei Stärke 1. 0,01 = 1 %. Kleine Werte halten Schächte lange hell.", 0.0001f, 1);
            Float(data, "sidewaysLoss", "Seitlich / nach oben durch Luft", "Anteil des verbleibenden Lichts bei seitlichen oder aufwärts gerichteten Schritten. 0,08 = 8 % bei Stärke 1.", 0.0001f, 1);
            Float(data, "blockLoss", "Zusätzlicher Verlust durch Blöcke", "Zusätzlicher Anteil des verbleibenden Lichts beim Eintritt in Stein oder Erz. 0,28 = 28 % bei Stärke 1.", 0, 1);
        });
        if (lighting)
        {
            EditorGUILayout.HelpBox("Pro Zelle bleibt ein Anteil des Lichts erhalten: Restlicht × (1 − Verlust)^Stärke. Der Abfall ist zunächst stark und wird dann flacher. Sehr schwaches Restlicht wird sanft auf Schwarz ausgeblendet. Grundhelligkeit 0 erlaubt vollständige Dunkelheit.", MessageType.Info);
            if (lighting.sidewaysLoss < lighting.downwardLoss)
                EditorGUILayout.HelpBox("Aktuell reicht Licht seitlich weiter als nach unten. Für helle Schächte den Abwärtsverlust kleiner einstellen.", MessageType.Warning);
        }
        EditorGUILayout.HelpBox("Abbau öffnet Lichtwege automatisch. Die Beleuchtung betrifft die Spielwelt; HUD und Map Overview bleiben lesbar. Standardwerte mit Einstellungen speichern sichern.", MessageType.None);
    }

    void DrawPlayer()
    {
        stats = Picker("Spieler-Basiswerte", stats);
        if (BaseStats)
        {
            Section("Bewegung und Abbauen", BaseStats, data =>
            {
                Float(data, "moveSpeed", "Laufgeschwindigkeit", "Welteinheiten pro Sekunde.", 0.01f);
                Float(data, "jumpHeightBlocks", "Sprunghöhe (Blöcke)", "", 0, 100);
                Float(data, "miningSpeed", "Basis-Abbaugeschwindigkeit", "Mehr = schneller. Zeit pro Block = Härte / Geschwindigkeit.", GameplaySettingsStore.MinDiggingSpeed, GameplaySettingsStore.MaxDiggingSpeed);
                Float(data, "reach", "Abbau-Reichweite", "Maximale Entfernung vom Spieler zum Blockzentrum in Welteinheiten.", 0.01f);
            });
        }
        else Missing("Kein PlayerBaseStats-Asset am StatsManager zugewiesen.");

        movement = Picker("Bewegungssteuerung", movement);
        Section("Bewegungsgefühl", movement, data =>
        {
            Float(data, "accelTime", "Beschleunigungszeit (s)", "Zeit bis zum Maximaltempo in der Luft. Am Boden wirkt zusätzlich der Bodenfaktor.", 0.01f);
            Float(data, "decelTime", "Bremszeit (s)", "Zeit von Maximaltempo bis Stillstand in der Luft.", 0.01f);
            Float(data, "directionChangeDecelTime", "Richtungswechsel-Bremszeit (s)", "Zeit zum Abbremsen bis Stillstand bei entgegengesetzter Eingabe.", 0.01f);
            Float(data, "groundedCoeff", "Beschleunigungsfaktor am Boden", "Multipliziert Beschleunigen und Bremsen am Boden. Kleiner = träger. In der Luft gilt Faktor 1.", 0.01f);
        });
        var body = movement ? movement.GetComponent<Rigidbody2D>() : null;
        Section("Sprungphysik", body, data =>
        {
            Float(data, "m_GravityScale", "Gravitationsfaktor", "Skaliert die globale 2D-Gravitation für den Spieler.", 0.01f);
            Float(data, "m_Mass", "Spielermasse", "", 0.01f);
        });

        follow = Picker("Spielkamera", follow);
        Section("Kamera", follow, data =>
        {
            Float(data, "smoothSpeed", "Nachführgeschwindigkeit", "Höher = Kamera folgt schneller.", 0.01f);
            var property = data.FindProperty("offset");
            Vector3 offset = property.vector3Value;
            EditorGUI.BeginChangeCheck();
            Vector2 xy = EditorGUILayout.Vector2Field(new GUIContent("Bildausschnitt-Versatz (X/Y)", "Verschiebt den Bildausschnitt relativ zum Spieler. Kamera-Z bleibt unverändert."), new Vector2(offset.x, offset.y));
            if (EditorGUI.EndChangeCheck() && Finite(xy.x) && Finite(xy.y)) property.vector3Value = new Vector3(xy.x, xy.y, offset.z);
        });
        var camera = follow ? follow.GetComponent<Camera>() : null;
        if (camera && camera.orthographic)
            Section("Sichtweite", camera, data => Float(data, "orthographic size", "Halbe sichtbare Höhe", "Orthographic Size: größere Werte zeigen mehr von der Welt.", 0.1f));
    }

    void DrawEnergy()
    {
        stats = Picker("Spieler-Basiswerte", stats);
        Section("Kapazität", BaseStats, data => Float(data, "maxEnergy", "Maximale Energie", "Kapazität und Startenergie des Spielers.", 1));
        energy = Picker("Energieverbrauch", energy);
        Section("Verbrauch pro Sekunde", energy, data =>
        {
            Float(data, "idleConsumtion", "Grundverbrauch", "Fällt immer an, solange Energie vorhanden ist.", 0);
            Float(data, "consumptionMultiplier", "Verbrauchsmultiplikator", "", 0);
            Float(data, "moveConsumption", "Zusatz beim Bewegen", "Wird zum Grundverbrauch addiert, wenn Bewegungseingaben anliegen.", 0);
            Float(data, "diggingConsumption", "Zusatz beim Abbauen", "Wird zum Grundverbrauch addiert, solange der Miner abbaut.", 0);
        });
        if (energy && BaseStats)
        {
            float total = (energy.idleConsumtion + energy.moveConsumption + energy.diggingConsumption) * energy.consumptionMultiplier;
            EditorGUILayout.HelpBox($"Bewegen + Abbauen: {total:g} Energie/s. Theoretische Laufzeit mit vollem Vorrat: {(total > 0 ? (BaseStats.maxEnergy / total).ToString("0.0") + " s" : "unbegrenzt")}.", MessageType.Info);
        }
        station = Picker("Aufladestation", station);
        Section("Aufladen", station, data => Float(data, "rechargeCost", "Preis pro Energieeinheit", "Geldkosten pro fehlender Energieeinheit. Die aktuelle Aufladelogik rundet auf ganze Münzen ab.", 0.01f));
        EditorGUILayout.HelpBox("Aktueller Spielstand: Leere Energie stoppt Bewegung/Abbau noch nicht. Teilaufladung bei zu wenig Geld enthält einen bekannten Berechnungsfehler; Details in der Gameplay-Analyse.", MessageType.Warning);
    }

    void DrawMap()
    {
        map = Picker("Map-Generator", map);
        using (new EditorGUI.DisabledScope(!map || !Registry))
            if (GUILayout.Button("Map im Editor generieren", GUILayout.Height(28)))
            {
                GUI.FocusControl(null);
                try { notification = $"Map generiert (Seed {MapEditorGeneration.Generate(map)})."; }
                catch (Exception ex) { notification = "Generierung fehlgeschlagen: " + ex.Message; Debug.LogException(ex); }
                GUIUtility.ExitGUI();
            }
        CollapsibleSection("map-size", "Kartengröße und Seed", map, data =>
        {
            Integer(data, "mapWidth", "Breite (Zellen)", "Die Karte wird horizontal um X = 0 zentriert.", 1, 10000);
            Integer(data, "mapHeight", "Tiefe (Zellen)", "Die Karte wächst von Y = 0 nach unten.", 1, 10000);
            var randomSeed = data.FindProperty("randomizeSeed");
            EditorGUILayout.PropertyField(randomSeed, new GUIContent("Seed zufällig generieren"));
            using (new EditorGUI.DisabledScope(randomSeed.boolValue))
                Integer(data, "seed", "Seed", "", -10000000, 10000000);
        });
        CollapsibleSection("map-borders", "Weltgrenzen", map ? map.GetComponent<MapWorldBorders>() : null, data =>
        {
            Integer(data, "sidePaddingCells", "Seitenabstand (Kacheln)", "Abstand zwischen Kartenrand und unsichtbarer Seitenwand.", 0, 10000);
            Float(data, "topBorderY", "Obere Grenze (Welt-Y)", "Unsichtbare obere Wand und Kameragrenze.", -10000f);
        }, false);
        CollapsibleSection("map-surface", "Oberfläche", map, data =>
        {
            EditorGUILayout.Slider(data.FindProperty("grassYOffset"), -.5f, .5f,
                new GUIContent("Gras Y-Versatz (Welteinheiten)"));
        }, false);
        CollapsibleSection("map-layers", "Layer", map, data =>
        {
            var layers = data.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                string label = layer.FindPropertyRelative("name").stringValue;
                if (!Foldout("map-layer-" + i, string.IsNullOrWhiteSpace(label) ? "Layer " + (i + 1) : label)) continue;
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("name"), new GUIContent("Name"));
                using (new EditorGUI.DisabledScope(i == 0))
                    EditorGUILayout.PropertyField(layer.FindPropertyRelative("startDepth"), new GUIContent("Starttiefe (Blöcke)"));
                if (i > 0)
                    EditorGUILayout.PropertyField(layer.FindPropertyRelative("transitionWidth"), new GUIContent("Übergang (Blöcke)"));
                EditorGUILayout.PropertyField(layer.FindPropertyRelative("backgroundSprite"), new GUIContent("Hintergrundsprite"));
                var stoneProperty = layer.FindPropertyRelative("stone");
                EditorGUILayout.PropertyField(stoneProperty, new GUIContent("Gesteinsart"));
                var stone = stoneProperty.objectReferenceValue as Block;
                var hardness = layer.FindPropertyRelative("stoneHardness");
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.FloatField("Gesteinshärte",
                    hardness.floatValue > 0f ? hardness.floatValue : stone ? stone.hardness : 1f);
                if (EditorGUI.EndChangeCheck() && Finite(value)) hardness.floatValue = Mathf.Max(.01f, value);
            }
        }, false);
        CollapsibleSection("map-background", "Untergrund-Hintergrund", UnityEngine.Object.FindFirstObjectByType<FixedUndergroundBackground>(), data =>
        {
            EditorGUILayout.PropertyField(data.FindProperty("yOffset"),new GUIContent("Y-Versatz (Welteinheiten)"));
        }, false);
        CollapsibleSection("map-edges", "Blockränder", map ? map.GetComponent<UniformStoneAppearance>() : null, data =>
        {
            EditorGUILayout.Slider(data.FindProperty("edgeDepth"),0f,2f,new GUIContent("Ausfransungstiefe (×)"));
            EditorGUILayout.Slider(data.FindProperty("edgeIrregularity"),0f,2f,new GUIContent("Unregelmäßigkeit (×)"));
            EditorGUILayout.Slider(data.FindProperty("edgeRounding"),0f,2f,new GUIContent("Eckenrundung (×)"));
        }, false);
        CollapsibleSection("map-rubble", "Wandkrümel", map ? map.GetComponent<UniformStoneAppearance>() : null, data =>
        {
            EditorGUILayout.Slider(data.FindProperty("rubbleAmount"),0f,8f,new GUIContent("Menge pro Blockkante"));
            EditorGUILayout.Slider(data.FindProperty("rubbleMinSize"),.02f,.6f,new GUIContent("Min. Größe (Kacheln)"));
            EditorGUILayout.Slider(data.FindProperty("rubbleMaxSize"),.02f,.6f,new GUIContent("Max. Größe (Kacheln)"));
            var min=data.FindProperty("rubbleMinSize");var max=data.FindProperty("rubbleMaxSize");
            if(max.floatValue<min.floatValue)max.floatValue=min.floatValue;
            EditorGUILayout.Slider(data.FindProperty("rubbleProtrusionPercent"),0f,100f,new GUIContent("Überstand (%)"));
        }, false);
        if (!map) return;
        long cells = (long)map.mapWidth * map.mapHeight;
        EditorGUILayout.LabelField($"{cells:N0} Zellen", EditorStyles.miniLabel);
        if (Registry && Foldout("map-catalog", "Blockkatalog")) DrawBlockCatalog();
    }

    void DrawBlockCatalog()
    {
        Source(Registry);
        var registryData = new SerializedObject(Registry);
        registryData.Update();
        var blocks = registryData.FindProperty("blocks");
        for (int i = 0; i < blocks.arraySize; i++)
        {
            var block = blocks.GetArrayElementAtIndex(i).objectReferenceValue as Block;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{i + 1}. {(block ? block.displayName : "FEHLENDER BLOCK")}" + (block && !block.spawnWithNoise ? "  (inaktiv)" : ""));
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("↑", GUILayout.Width(30))) { blocks.MoveArrayElement(i, i - 1); Apply(registryData); GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(i == blocks.arraySize - 1))
                    if (GUILayout.Button("↓", GUILayout.Width(30))) { blocks.MoveArrayElement(i, i + 1); Apply(registryData); GUIUtility.ExitGUI(); }
            }
        }
        ValidateRegistry();
    }

    void DrawOreDistribution()
    {
        map = Picker("Map-Generator", map);
        CollapsibleSection("ore-density", "Globale Verteilung", map, data =>
        {
            var curve = data.FindProperty("oreDensityCurve");
            EditorGUILayout.PropertyField(curve, new GUIContent("Tiefenkurve (×)"));
            DrawDensityCurvePreview(curve, 0, map.mapHeight);
            EditorGUILayout.Slider(data.FindProperty("oreDensityMultiplierPercent"), 0f, 100f,
                new GUIContent("Basis-Erzdichte (%)"));
        }, false);
        CollapsibleSection("ore-veins", "Adern", map, data =>
        {
            EditorGUILayout.Slider(data.FindProperty("oreScale"), .5f, 3f, new GUIContent("Erzgröße (×)"));
            Integer(data, "minimumOreVeinSize", "Minimale Adergröße (Blöcke)", "", 1, 10000);
        }, false);
        if (!map) return;
        if (!Registry) { Missing("Dem Map-Generator fehlt ein BlockRegistry-Asset."); return; }
        CollapsibleSection("ore-configurations", "Erze", map, DrawOreSettings, false);
    }

    internal static float DensityLayerX(int startDepth, int mapHeight) =>
        CurveLayerX(startDepth, 0, mapHeight);

    internal static float CurveLayerX(int depth, int startDepth, int endDepth) =>
        Mathf.Clamp01((float)(depth - startDepth) / Mathf.Max(1, endDepth - startDepth - 1));

    internal static int CurveDepthAtX(float x, int startDepth, int endDepth) =>
        startDepth + Mathf.RoundToInt(Mathf.Clamp01(x) * Mathf.Max(0, endDepth - startDepth - 1));

    internal static float CurveDepthValueAtX(float x, int startDepth, int endDepth) =>
        startDepth + Mathf.Clamp01(x) * Mathf.Max(0, endDepth - startDepth - 1);

    internal static int DepthTickStep(float visibleDepth, float plotWidth)
    {
        float target = visibleDepth / Mathf.Max(1f, plotWidth / 80f);
        if (target <= 1f) return 1;
        float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(target)));
        float scaled = target / power;
        float nice = scaled < 1.5f ? 1f : scaled < 3.5f ? 2f : scaled < 7.5f ? 5f : 10f;
        return Mathf.Max(1, Mathf.RoundToInt(nice * power));
    }

    internal static int OreAbsenceAtX(float x, int rangeStart, int rangeEnd, MapLayer[] layers,
        HashSet<int> selectedLayers, AnimationCurve weightCurve, float baseWeight)
    {
        if (layers == null || selectedLayers == null) return 0;
        float depth = CurveDepthValueAtX(x, rangeStart, rangeEnd);
        int layerIndex = -1;
        int layerStart = int.MinValue;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != null && layers[i].startDepth <= depth && layers[i].startDepth > layerStart)
            {
                layerIndex = i;
                layerStart = layers[i].startDepth;
            }
        if (layerIndex < 0 || !selectedLayers.Contains(layerIndex)) return 1;
        float factor = weightCurve == null || weightCurve.length == 0 ? 1f : weightCurve.Evaluate(x);
        float weight = baseWeight * factor;
        return !Finite(weight) || weight <= 0f ? 2 : 0;
    }

    internal static Vector2 ZoomCurveView(Vector2 view, float anchor, float factor, float minimumSpan)
    {
        float span = view.y - view.x;
        float nextSpan = Mathf.Clamp(span * factor, minimumSpan, 1f);
        float left = Mathf.Clamp(view.x + Mathf.Clamp01(anchor) * (span - nextSpan), 0f, 1f - nextSpan);
        return new Vector2(left, left + nextSpan);
    }

    internal static Vector2 PanCurveView(Vector2 view, float movement)
    {
        float span = view.y - view.x;
        float left = Mathf.Clamp(view.x + movement, 0f, 1f - span);
        return new Vector2(left, left + span);
    }

    void DrawDensityCurvePreview(SerializedProperty property, int rangeStart, int rangeEnd,
        HashSet<int> selectedLayers = null, AnimationCurve presenceCurve = null, float baseWeight = 1f)
    {
        var curve = property.animationCurveValue;
        if (curve == null || curve.length == 0) curve = AnimationCurve.Constant(0f, 1f, 1f);
        Rect outer = GUILayoutUtility.GetRect(10f, 288f, GUILayout.ExpandWidth(true));
        Rect plot = new Rect(outer.x + 46f, outer.y + 35f, outer.width - 58f, 192f);
        Rect strip = new Rect(plot.x, plot.yMax + 24f, plot.width, 24f);
        string viewKey = property.serializedObject.targetObject.GetInstanceID() + ":" + property.propertyPath;
        if (!curveViews.TryGetValue(viewKey, out Vector2 view)) view = new Vector2(0f, 1f);
        float minimumSpan = Mathf.Min(1f, 4f / Mathf.Max(1, rangeEnd - rangeStart));
        float maximum = 2f;
        foreach (var key in curve.keys) if (Finite(key.value)) maximum = Mathf.Max(maximum, key.value * 1.2f);
        for (int i = 0; i <= 32; i++)
        {
            float value = curve.Evaluate(i / 32f);
            if (Finite(value)) maximum = Mathf.Max(maximum, value * 1.2f);
        }
        maximum = Mathf.Ceil(maximum * 2f) / 2f;
        int control = GUIUtility.GetControlID(FocusType.Passive, plot);
        if (GUIUtility.hotControl == control && densityCurveControl == control && draggingDensityKey >= 0)
            maximum = draggingCurveMaximum;
        Event evt = Event.current;

        if (GUI.Button(new Rect(outer.xMax - 64f, outer.y, 64f, 20f), "Gesamt", EditorStyles.miniButton))
        {
            view = new Vector2(0f, 1f);
            curveViews[viewKey] = view;
            Repaint();
        }
        if (evt.type == EventType.ScrollWheel && (plot.Contains(evt.mousePosition) || strip.Contains(evt.mousePosition)))
        {
            float anchor = Mathf.Clamp01((evt.mousePosition.x - plot.x) / plot.width);
            view = ZoomCurveView(view, anchor, Mathf.Pow(1.15f, evt.delta.y), minimumSpan);
            curveViews[viewKey] = view;
            evt.Use();
            Repaint();
        }
        else if (evt.type == EventType.MouseDown && evt.button == 2 && plot.Contains(evt.mousePosition))
        {
            panningCurveControl = control;
            GUIUtility.hotControl = control;
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == control &&
            panningCurveControl == control)
        {
            view = PanCurveView(view, -evt.delta.x / plot.width * (view.y - view.x));
            curveViews[viewKey] = view;
            evt.Use();
            Repaint();
        }

        if (evt.type == EventType.MouseDown && evt.button == 0 && plot.Contains(evt.mousePosition))
        {
            int nearest = -1;
            float distance = 11f * 11f;
            for (int i = 0; i < curve.length; i++)
            {
                if (curve.keys[i].time < view.x || curve.keys[i].time > view.y) continue;
                Vector2 point = CurvePoint(plot, maximum, view, curve.keys[i].time, curve.keys[i].value);
                float candidate = (point - evt.mousePosition).sqrMagnitude;
                if (candidate >= distance) continue;
                nearest = i;
                distance = candidate;
            }
            if (nearest < 0 && evt.clickCount >= 2)
            {
                float x = Mathf.Lerp(view.x, view.y, Mathf.Clamp01((evt.mousePosition.x - plot.x) / plot.width));
                float y = Mathf.Max(0f, (plot.yMax - evt.mousePosition.y) / plot.height * maximum);
                nearest = curve.AddKey(new Keyframe(x, y));
                if (nearest >= 0)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, nearest, AnimationUtility.TangentMode.Auto);
                    AnimationUtility.SetKeyRightTangentMode(curve, nearest, AnimationUtility.TangentMode.Auto);
                    property.animationCurveValue = curve;
                }
            }
            if (nearest >= 0)
            {
                draggingDensityKey = nearest;
                densityCurveControl = control;
                draggingCurveMaximum = maximum;
                selectedCurvePath = viewKey;
                selectedCurveKey = nearest;
                GUIUtility.hotControl = control;
                evt.Use();
                Repaint();
            }
            else if (selectedCurvePath == viewKey)
            {
                selectedCurveKey = -1;
                Repaint();
            }
        }
        else if (evt.type == EventType.MouseDown && evt.button == 1 && plot.Contains(evt.mousePosition) && curve.length > 2)
        {
            for (int i = 1; i < curve.length - 1; i++)
            {
                if (curve.keys[i].time < view.x || curve.keys[i].time > view.y ||
                    (CurvePoint(plot, maximum, view, curve.keys[i].time, curve.keys[i].value) - evt.mousePosition).sqrMagnitude > 100f)
                    continue;
                curve.RemoveKey(i);
                property.animationCurveValue = curve;
                if (selectedCurvePath == viewKey) selectedCurveKey = -1;
                evt.Use();
                break;
            }
        }
        else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == control &&
            densityCurveControl == control && draggingDensityKey >= 0 && draggingDensityKey < curve.length)
        {
            var keys = curve.keys;
            int index = draggingDensityKey;
            float x = Mathf.Lerp(view.x, view.y, Mathf.Clamp01((evt.mousePosition.x - plot.x) / plot.width));
            if (index == 0) x = 0f;
            else if (index == keys.Length - 1) x = 1f;
            else x = Mathf.Clamp(x, keys[index - 1].time + .001f, keys[index + 1].time - .001f);
            float y = Mathf.Max(0f, (plot.yMax - evt.mousePosition.y) / plot.height * maximum);
            var key = keys[index];
            key.time = x;
            key.value = y;
            draggingDensityKey = curve.MoveKey(index, key);
            selectedCurveKey = draggingDensityKey;
            property.animationCurveValue = curve;
            evt.Use();
            Repaint();
        }
        else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == control)
        {
            GUIUtility.hotControl = 0;
            draggingDensityKey = -1;
            panningCurveControl = 0;
            evt.Use();
        }

        if (evt.type != EventType.Repaint) return;
        EditorGUI.DrawRect(plot, new Color(.11f, .14f, .17f, 1f));
        if (selectedLayers != null)
            DrawOreAbsenceBands(plot, view, rangeStart, rangeEnd, selectedLayers, presenceCurve, baseWeight);
        for (int i = 0; i <= 4; i++)
        {
            float y = Mathf.Lerp(plot.yMax, plot.y, i / 4f);
            EditorGUI.DrawRect(new Rect(plot.x, y, plot.width, 1f), new Color(.35f, .4f, .45f, .35f));
            GUI.Label(new Rect(outer.x, y - 8f, 40f, 16f), (maximum * i / 4f).ToString("0.##"), EditorStyles.miniLabel);
        }
        float depthSpan = Mathf.Max(0, rangeEnd - rangeStart - 1);
        float firstDepth = rangeStart + view.x * depthSpan;
        float lastDepth = rangeStart + view.y * depthSpan;
        int tickStep = DepthTickStep(lastDepth - firstDepth, plot.width);
        int firstTick = Mathf.CeilToInt(firstDepth / tickStep) * tickStep;
        for (int depth = firstTick; depth <= lastDepth + .0001f; depth += tickStep)
        {
            float normalized = CurveLayerX(depth, rangeStart, rangeEnd);
            float x = plot.x + plot.width * (normalized - view.x) / (view.y - view.x);
            EditorGUI.DrawRect(new Rect(x, plot.y, 1f, plot.height), new Color(.35f, .4f, .45f, .25f));
            GUI.Label(new Rect(Mathf.Clamp(x - 24f, plot.x, plot.xMax - 48f),
                plot.yMax + 3f, 48f, 16f), depth.ToString(), EditorStyles.miniLabel);
        }
        if (map && map.layers != null)
        {
            var ordered = map.layers.Select((layer, index) => (layer, index))
                .Where(item => item.layer != null && item.layer.startDepth < rangeEnd)
                .OrderBy(item => item.layer.startDepth).ToArray();
            Color[] colors = { new Color(.55f, .32f, .27f), new Color(.54f, .49f, .31f),
                new Color(.31f, .43f, .54f), new Color(.45f, .33f, .53f) };
            for (int i = 0; i < ordered.Length; i++)
            {
                if (ordered[i].layer.startDepth < rangeStart) continue;
                float start = CurveLayerX(ordered[i].layer.startDepth, rangeStart, rangeEnd);
                float end = i + 1 < ordered.Length ?
                    CurveLayerX(ordered[i + 1].layer.startDepth, rangeStart, rangeEnd) : 1f;
                if (end <= view.x || start >= view.y) continue;
                float left = strip.x + strip.width * Mathf.Clamp01((start - view.x) / (view.y - view.x));
                float right = strip.x + strip.width * Mathf.Clamp01((end - view.x) / (view.y - view.x));
                Color color = selectedLayers == null || selectedLayers.Contains(ordered[i].index) ?
                    colors[ordered[i].index % colors.Length] : new Color(.23f, .25f, .28f);
                EditorGUI.DrawRect(new Rect(left, strip.y, Mathf.Max(0f, right - left), strip.height), color);
                string name = string.IsNullOrWhiteSpace(ordered[i].layer.name) ?
                    "Layer " + (ordered[i].index + 1) : ordered[i].layer.name;
                if (right - left > 26f) GUI.Label(new Rect(left + 4f, strip.y + 3f, right - left - 7f, 18f),
                    right - left > 86f ? name : "L" + (ordered[i].index + 1), EditorStyles.whiteMiniLabel);
                if (start < view.x || start > view.y) continue;
                float marker = left;
                EditorGUI.DrawRect(new Rect(marker, plot.y, 1.5f, plot.height), new Color(1f, .72f, .24f, .75f));
                float labelX = Mathf.Clamp(marker + 3f, plot.x, plot.xMax - 105f);
                float previous = i > 0 ? CurveLayerX(ordered[i - 1].layer.startDepth, rangeStart, rangeEnd) : -1f;
                float labelY = outer.y + (i > 0 && start - previous < .11f * (view.y - view.x) ? 15f : 0f);
                GUI.Label(new Rect(labelX, labelY, 105f, 16f), name + " · " + ordered[i].layer.startDepth, EditorStyles.miniLabel);
            }
        }
        var points = new Vector3[129];
        for (int i = 0; i < points.Length; i++)
        {
            float x = Mathf.Lerp(view.x, view.y, i / 128f);
            points[i] = CurvePoint(plot, maximum, view, x, curve.Evaluate(x));
        }
        Handles.BeginGUI();
        for (int i = 0; i < points.Length - 1; i++)
        {
            int absence = selectedLayers == null ? 0 : OreAbsenceAtX(
                Mathf.Lerp(view.x, view.y, (i + .5f) / (points.Length - 1)), rangeStart, rangeEnd,
                map.layers, selectedLayers, presenceCurve, baseWeight);
            Handles.color = absence == 1 ? new Color(.55f, .59f, .64f) :
                absence == 2 ? new Color(.92f, .49f, .34f) : new Color(.18f, .84f, .91f);
            Handles.DrawAAPolyLine(2.5f, points[i], points[i + 1]);
        }
        for (int i = 0; i < curve.length; i++)
        {
            var key = curve.keys[i];
            if (key.time < view.x || key.time > view.y) continue;
            int absence = selectedLayers == null ? 0 : OreAbsenceAtX(key.time, rangeStart,
                rangeEnd, map.layers, selectedLayers, presenceCurve, baseWeight);
            Handles.color = selectedCurvePath == viewKey && i == selectedCurveKey ? Color.white :
                absence == 1 ? new Color(.7f, .74f, .78f) :
                absence == 2 ? new Color(1f, .58f, .4f) : new Color(.2f, .93f, 1f);
            Handles.DrawSolidDisc(CurvePoint(plot, maximum, view, key.time, key.value), Vector3.forward, 4.5f);
        }
        Handles.EndGUI();
        if (selectedCurvePath == viewKey && selectedCurveKey >= 0 && selectedCurveKey < curve.length)
        {
            var key = curve.keys[selectedCurveKey];
            if (key.time >= view.x && key.time <= view.y)
            {
                Vector2 point = CurvePoint(plot, maximum, view, key.time, key.value);
                const float width = 170f;
                float left = point.x + 10f;
                if (left + width > plot.xMax) left = point.x - width - 10f;
                Rect badge = new Rect(Mathf.Clamp(left, plot.x, plot.xMax - width),
                    Mathf.Clamp(point.y - 28f, plot.y, plot.yMax - 23f), width, 22f);
                EditorGUI.DrawRect(badge, new Color(.08f, .11f, .14f, .96f));
                GUI.Label(new Rect(badge.x + 7f, badge.y + 2f, width - 12f, 18f),
                    "X/Tiefe " + CurveDepthValueAtX(key.time, rangeStart, rangeEnd).ToString("0.##") +
                    "   Y " + key.value.ToString("0.###"), EditorStyles.whiteMiniLabel);
            }
        }
    }

    static Vector2 CurvePoint(Rect plot, float maximum, Vector2 view, float x, float y) => new Vector2(
        plot.x + plot.width * Mathf.Clamp01(Finite(x) ? (x - view.x) / (view.y - view.x) : 0f),
        plot.yMax - plot.height * Mathf.Clamp01(Finite(y) ? y / maximum : 0f));

    void DrawOreAbsenceBands(Rect plot, Vector2 view, int rangeStart, int rangeEnd,
        HashSet<int> selectedLayers, AnimationCurve weightCurve, float baseWeight)
    {
        int columns = Mathf.Max(1, Mathf.CeilToInt(plot.width / 2f));
        int previous = 0, runStart = 0;
        for (int column = 0; column <= columns; column++)
        {
            int kind = column == columns ? 0 : OreAbsenceAtX(
                Mathf.Lerp(view.x, view.y, (column + .5f) / columns), rangeStart, rangeEnd,
                map.layers, selectedLayers, weightCurve, baseWeight);
            if (kind == previous) continue;
            if (previous != 0)
            {
                float left = plot.x + plot.width * runStart / columns;
                float width = plot.width * (column - runStart) / columns;
                EditorGUI.DrawRect(new Rect(left, plot.y, width, plot.height), previous == 1 ?
                    new Color(.32f, .34f, .38f, .58f) : new Color(.48f, .23f, .16f, .52f));
                if (width > 90f)
                    GUI.Label(new Rect(left + 6f, plot.y + 5f, width - 12f, 18f),
                        previous == 1 ? "Nicht in Layer" : "Gewicht 0", EditorStyles.whiteMiniLabel);
            }
            previous = kind;
            runStart = column;
        }
    }

    void DrawOreSettings(SerializedObject data)
    {
        var settings = data.FindProperty("oreSettings");
        var ores = Registry.blocks == null ? Array.Empty<Block>() : Registry.blocks
            .Where(block => block && block.HasOreOverlays)
            .OrderBy(block => BlockPickerOrder(block)).ThenBy(block => block.displayName).ToArray();
        foreach (var ore in ores)
        {
            int index = -1;
            for (int i = 0; i < settings.arraySize; i++)
                if (settings.GetArrayElementAtIndex(i).FindPropertyRelative("ore").intValue == (int)ore.id)
                { index = i; break; }
            if (!Foldout("ore-" + (int)ore.id, ore.displayName)) continue;
            if (index < 0)
            {
                if (!GUILayout.Button("Konfigurieren")) continue;
                index = settings.arraySize;
                settings.InsertArrayElementAtIndex(index);
                var created = settings.GetArrayElementAtIndex(index);
                created.FindPropertyRelative("ore").intValue = (int)ore.id;
                created.FindPropertyRelative("layerIndices").arraySize = 0;
                created.FindPropertyRelative("baseWeight").floatValue = ore.OreWeight;
                created.FindPropertyRelative("weightCurve").animationCurveValue = AnimationCurve.Constant(0f, 1f, 1f);
                created.FindPropertyRelative("baseVeinSize").floatValue = ore.veinSizeIndex;
                created.FindPropertyRelative("veinSizeCurve").animationCurveValue = AnimationCurve.Constant(0f, 1f, 1f);
                data.FindProperty("useOreSettings").boolValue = true;
            }
            var entry = settings.GetArrayElementAtIndex(index);
            var selectedLayers = entry.FindPropertyRelative("layerIndices");
            var enabled = new List<int>();
            for (int i = 0; i < selectedLayers.arraySize; i++) enabled.Add(selectedLayers.GetArrayElementAtIndex(i).intValue);
            for (int i = 0; i < map.layers.Length; i++)
            {
                string name = map.layers[i] == null || string.IsNullOrWhiteSpace(map.layers[i].name) ?
                    "Layer " + (i + 1) : map.layers[i].name;
                bool selected = EditorGUILayout.ToggleLeft(name, enabled.Contains(i));
                if (selected && !enabled.Contains(i)) enabled.Add(i);
                else if (!selected) enabled.Remove(i);
            }
            enabled.Sort();
            selectedLayers.arraySize = enabled.Count;
            for (int i = 0; i < enabled.Count; i++) selectedLayers.GetArrayElementAtIndex(i).intValue = enabled[i];
            if (enabled.Count == 0) EditorGUILayout.LabelField("In keinem Layer", EditorStyles.miniLabel);
            var weight = entry.FindPropertyRelative("baseWeight");
            EditorGUI.BeginChangeCheck();
            float nextWeight = EditorGUILayout.FloatField("Basisgewicht", weight.floatValue);
            if (EditorGUI.EndChangeCheck() && Finite(nextWeight)) weight.floatValue = Mathf.Max(0f, nextWeight);
            var weightCurve = entry.FindPropertyRelative("weightCurve");
            EditorGUILayout.PropertyField(weightCurve, new GUIContent("Gewichtungskurve"));
            if (enabled.Count > 0)
            {
                int first = enabled.Min(layer => map.layers[layer].startDepth);
                int last = enabled.Max(layer => map.layers[layer].startDepth);
                int lastIndex = Array.FindIndex(map.layers, layer => layer != null && layer.startDepth == last);
                int end = lastIndex + 1 < map.layers.Length ?
                    Mathf.Min(map.layers[lastIndex + 1].startDepth, map.mapHeight) : map.mapHeight;
                DrawDensityCurvePreview(weightCurve, first, end, new HashSet<int>(enabled),
                    weightCurve.animationCurveValue, weight.floatValue);
            }
            var size = entry.FindPropertyRelative("baseVeinSize");
            EditorGUI.BeginChangeCheck();
            float nextSize = EditorGUILayout.FloatField("Basisadergröße (Index)", size.floatValue);
            if (EditorGUI.EndChangeCheck() && Finite(nextSize)) size.floatValue = Mathf.Max(1f, nextSize);
            var sizeCurve = entry.FindPropertyRelative("veinSizeCurve");
            EditorGUILayout.PropertyField(sizeCurve, new GUIContent("Adergrößenkurve"));
            if (enabled.Count > 0)
            {
                int first = enabled.Min(layer => map.layers[layer].startDepth);
                int last = enabled.Max(layer => map.layers[layer].startDepth);
                int lastIndex = Array.FindIndex(map.layers, layer => layer != null && layer.startDepth == last);
                int end = lastIndex + 1 < map.layers.Length ?
                    Mathf.Min(map.layers[lastIndex + 1].startDepth, map.mapHeight) : map.mapHeight;
                DrawDensityCurvePreview(sizeCurve, first, end, new HashSet<int>(enabled),
                    weightCurve.animationCurveValue, weight.floatValue);
            }
        }
    }

    Block BlockPicker()
    {
        if (!Registry || Registry.blocks == null || Registry.blocks.Length == 0) { Missing("Keine Blöcke im aktiven Map-Katalog."); return null; }
        selectedBlock = Mathf.Clamp(selectedBlock, 0, Registry.blocks.Length - 1);
        var indices = Enumerable.Range(0, Registry.blocks.Length)
            .OrderBy(index => BlockPickerOrder(Registry.blocks[index]))
            .ThenBy(index => Registry.blocks[index] ? Registry.blocks[index].displayName : "")
            .ToArray();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("Block / Erz");
            var rect = GUILayoutUtility.GetRect(new GUIContent(BlockPickerName(Registry.blocks[selectedBlock])), EditorStyles.popup);
            if (EditorGUI.DropdownButton(rect, new GUIContent(BlockPickerName(Registry.blocks[selectedBlock])), FocusType.Keyboard))
            {
                var menu = new GenericMenu();
                foreach (int index in indices)
                {
                    int choice = index;
                    menu.AddItem(new GUIContent(BlockPickerName(Registry.blocks[choice])), choice == selectedBlock,
                        () => { selectedBlock = choice; Repaint(); });
                }
                menu.DropDown(rect);
            }
        }
        return Registry.blocks[selectedBlock];
    }

    void DrawParticles()
    {
        itemFeed = Picker("Item-Feed", itemFeed);
        Section("Item-Feed", itemFeed, data =>
        {
            EditorGUILayout.IntSlider(data.FindProperty("sparkCount"), 0, 24, new GUIContent("Funkenanzahl"));
            EditorGUILayout.Slider(data.FindProperty("sparkIntensity"), 0f, 3f, new GUIContent("Funkenintensität"));
            EditorGUILayout.Slider(data.FindProperty("sparkRiseHeight"), 0f, 40f, new GUIContent("Funkenhöhe (px)"));
            EditorGUILayout.Slider(data.FindProperty("sparkBrightness"), 0f, 5f, new GUIContent("Funkenhelligkeit"));
            EditorGUILayout.Slider(data.FindProperty("lineBrightness"), 0f, 5f, new GUIContent("Strichhelligkeit"));
            EditorGUILayout.Slider(data.FindProperty("backdropOpacity"), 0f, 1f, new GUIContent("Hintergrundabdunklung"));
        }, false);
        blockBreakParticles = Picker("Blockabbau-Partikel", blockBreakParticles);
        Section("Blockabbau-Partikel", blockBreakParticles, data =>
        {
            EditorGUILayout.IntSlider(data.FindProperty("fragmentsPerBlock"), 0, 24, new GUIContent("Splitter bei Zerstörung"));
            EditorGUILayout.IntSlider(data.FindProperty("dustPerBlock"), 0, 16, new GUIContent("Staub bei Zerstörung"));
            EditorGUILayout.IntSlider(data.FindProperty("fragmentsPerHit"), 0, 12, new GUIContent("Splitter pro Treffer"));
            EditorGUILayout.IntSlider(data.FindProperty("dustPerHit"), 0, 8, new GUIContent("Staub pro Treffer"));
        }, false);
        oreSparkles = Picker("Erzfunkeln", oreSparkles);
        Section("Erzfunkeln", oreSparkles, data =>
        {
            EditorGUILayout.PropertyField(data.FindProperty("sparkleMaterial"), new GUIContent("Material"));
            Float(data, "intervalPerBlock", "Intervall pro Erzblock (s)", "", 0.1f);
            Float(data, "darkIntervalMultiplier", "Intervall bei 0 % Licht (×)", "", 1f);
            Float(data, "lifetime", "Lebensdauer (s)", "", 0.1f);
            Float(data, "size", "Größe", "", 0.02f, 1f);
            Float(data, "opacity", "Deckkraft", "", 0f, 1f);
            Float(data, "oreColorStrength", "Erzfarb-Anteil", "", 0f, 1f);
            Float(data, "brightness", "Helligkeit", "", 1f, 8f);
        }, false);
    }

    static int BlockPickerOrder(Block block)
    {
        if (!block) return 99;
        switch (block.id)
        {
            case BlockType.Dirt: return 0;
            case BlockType.Stone: return 1;
            case BlockType.StoneLayer2: return 2;
            case BlockType.StoneLayer3: return 3;
            case BlockType.StoneLayer4: return 4;
            case BlockType.Coal: return 10;
            case BlockType.CopperOre: return 11;
            case BlockType.IronOre: return 12;
            case BlockType.SilverOre: return 13;
            case BlockType.GoldOre: return 14;
            case BlockType.PlatinumOre: return 15;
            case BlockType.DiamondOre: return 16;
            default: return 90;
        }
    }

    static string BlockPickerName(Block block)
    {
        if (!block) return "Sonstige/Fehlender Block";
        switch (block.id)
        {
            case BlockType.Dirt: return "Gestein/Erde";
            case BlockType.Stone: return "Gestein/Übergangsgestein";
            case BlockType.StoneLayer2: return "Gestein/Stein";
            case BlockType.StoneLayer3: return "Gestein/Tiefstein 1";
            case BlockType.StoneLayer4: return "Gestein/Tiefstein 2";
            default: return (block.HasOreOverlays ? "Erze/" : "Sonstige/") + block.displayName;
        }
    }

    void DrawBlocks()
    {
        map = Picker("Map-Katalog aus", map);
        var block = BlockPicker();
        Section("Abbau und Belohnung", block, data =>
        {
            Float(data, "hardness", "Blockhärte", "Mehr = längere Abbauzeit. Zeit = Härte / Abbaugeschwindigkeit.", 0.01f);
            EditorGUILayout.PropertyField(data.FindProperty("itemDrop"), new GUIContent("Beute-Gegenstand"));
        });
        if (block && BaseStats && BaseStats.miningSpeed > 0)
            EditorGUILayout.LabelField($"Abbauzeit mit Basiswert: {Mathf.Max(0.01f, block.hardness <= 0 ? 1 : block.hardness) / BaseStats.miningSpeed:0.###} s  (ohne JSON-Override/Upgrades)", EditorStyles.miniLabel);
        if (block && block.itemDrop) DrawItem(block.itemDrop);
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Verkaufswerte im Überblick", EditorStyles.boldLabel);
        showOtherItems = EditorGUILayout.ToggleLeft("Auch Gegenstände ohne Beute-Zuordnung anzeigen", showOtherItems);
        var usedItems = Registry && Registry.blocks != null
            ? new HashSet<ItemSO>(Registry.blocks.Where(b => b && b.itemDrop).Select(b => b.itemDrop)) : new HashSet<ItemSO>();
        foreach (var item in items.Where(item => showOtherItems || usedItems.Contains(item)))
        {
            var data = new SerializedObject(item);
            data.Update();
            Integer(data, "worth", item.displayName + (usedItems.Contains(item) ? "" : " (nicht als Beute genutzt)"), AssetDatabase.GetAssetPath(item), 0, 1000000);
            Apply(data);
        }
        EditorGUILayout.HelpBox("Verkaufspreis gilt pro Stück; 0 = nicht verkaufbar. Der Shop zeigt nur Kategorie Ore. Kauf, Werkzeugwirkungen und Inventarlimits sind noch nicht implementiert.", MessageType.Info);
        ValidateRegistry();
    }

    void DrawItem(ItemSO item)
    {
        Section("Zugehöriger Gegenstand", item, data =>
        {
            Integer(data, "worth", "Verkaufspreis pro Stück", "0 = nicht verkaufbar.", 0, 1000000);
            EditorGUILayout.PropertyField(data.FindProperty("category"), new GUIContent("Kategorie", "Im aktuellen Verkaufsfenster erscheinen nur Gegenstände der Kategorie Ore."));
        });
    }

    void ValidateRegistry()
    {
        if (!Registry || Registry.blocks == null) return;
        var blocks = Registry.blocks.Where(b => b).ToArray();
        if (blocks.Length != Registry.blocks.Length) Missing("Der Blockkatalog enthält leere Einträge; die Map-Generierung überspringt sie.");
        if (!blocks.Any(b => b.id == BlockType.Stone)) Missing("Stone als Füllblock fehlt. Die Welt kann dadurch Lücken erhalten.");
        if (blocks.GroupBy(b => b.id).Any(g => g.Count() > 1)) Missing("Doppelte Block-IDs: Registry-Zuordnung und Generierungsreihenfolge können voneinander abweichen.");
        if (blocks.Any(b => b.HasOreOverlays
            ? b.smallOre.Concat(b.mediumOre).Concat(b.richOre).Any(t => !t)
            : b.variants == null || b.variants.Length == 0 || b.variants.Any(t => !t))) Missing("Ein Block hat fehlende Tile-Varianten; beim Generieren können Löcher entstehen.");
        if (miner && new SerializedObject(miner).FindProperty("blockRegistry").objectReferenceValue != Registry)
            Missing("Map und Spieler verwenden unterschiedliche Blockkataloge. Beute/Härte könnten deshalb nicht zu den generierten Tiles passen.");
    }

    T Picker<T>(string label, T current) where T : Component
    {
        var candidates = sceneComponents.OfType<T>().ToArray();
        if (candidates.Length == 0) { Missing(label + ": keine passende Komponente in der aktiven Szene."); return null; }
        if (!current || !candidates.Contains(current)) current = candidates[0];
        if (candidates.Length > 1)
        {
            int index = Array.IndexOf(candidates, current);
            index = EditorGUILayout.Popup(label, index, candidates.Select(c => HierarchyPath(c.transform)).ToArray());
            current = candidates[index];
        }
        return current;
    }

    static string HierarchyPath(Transform transform) => transform.parent ? HierarchyPath(transform.parent) + "/" + transform.name : transform.name;

    bool Foldout(string key, string title)
    {
        bool expanded = expandedMapSections.Contains(key);
        bool next = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
        if (next) expandedMapSections.Add(key);
        else expandedMapSections.Remove(key);
        return next;
    }

    void CollapsibleSection(string key, string title, Object target, Action<SerializedObject> draw, bool showSource = true)
    {
        if (!target) return;
        EditorGUILayout.Space(8);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (!Foldout(key, title)) return;
            if (showSource) Source(target);
            var data = new SerializedObject(target);
            data.Update();
            draw(data);
            Apply(data);
        }
    }

    static void Section(string title, Object target, Action<SerializedObject> draw, bool showSource = true)
    {
        if (!target) return;
        EditorGUILayout.Space(8);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (showSource) Source(target);
            var data = new SerializedObject(target);
            data.Update();
            draw(data);
            Apply(data);
        }
    }

    static void Source(Object target)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            string path = target is Component c ? HierarchyPath(c.transform) + " · " + c.GetType().Name : AssetDatabase.GetAssetPath(target);
            EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            if (GUILayout.Button("Anzeigen", EditorStyles.miniButton, GUILayout.Width(65))) EditorGUIUtility.PingObject(target);
        }
    }

    // Apply valid input immediately, as in Unity's Inspector. Delayed fields keep
    // a private text buffer that may commit after a toolbar Save click has finished.
    // Merely opening the window never changes existing game balance.
    static void Float(SerializedObject data, string name, string label, string tooltip, float min, float max = float.MaxValue)
    {
        var property = data.FindProperty(name);
        if (property == null) { Missing("Feld nicht gefunden: " + name); return; }
        EditorGUI.BeginChangeCheck();
        float value = EditorGUILayout.FloatField(new GUIContent(label, tooltip), property.floatValue);
        if (EditorGUI.EndChangeCheck() && Finite(value)) property.floatValue = Mathf.Clamp(value, min, max);
    }

    static void Integer(SerializedObject data, string name, string label, string tooltip, int min, int max)
    {
        var property = data.FindProperty(name);
        EditorGUI.BeginChangeCheck();
        int value = EditorGUILayout.IntField(new GUIContent(label, tooltip), property.intValue);
        if (EditorGUI.EndChangeCheck()) property.intValue = Mathf.Clamp(value, min, max);
    }

    static void FloatRange(SerializedObject data, string name, string label, float min, float max)
    {
        var property = data.FindProperty(name);
        if (property == null) { Missing("Feld nicht gefunden: " + name); return; }
        EditorGUI.BeginChangeCheck();
        Vector2 value = EditorGUILayout.Vector2Field(label, property.vector2Value);
        if (EditorGUI.EndChangeCheck() && Finite(value.x) && Finite(value.y))
        {
            float lower = Mathf.Clamp(Mathf.Min(value.x, value.y), min, max);
            property.vector2Value = new Vector2(lower, Mathf.Clamp(Mathf.Max(value.x, value.y), lower, max));
        }
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    static void Apply(SerializedObject data)
    {
        if (!data.ApplyModifiedProperties()) return;
        if (data.targetObject is Component component)
        {
            if (component is SurfaceTallGrass grass && !Application.isPlaying) grass.Rebuild();
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }

    static void Missing(string message) => EditorGUILayout.HelpBox(message, MessageType.Warning);

    void SaveSettings()
    {
        if (!this || EditorApplication.isPlayingOrWillChangePlaymode) return;
        AssetDatabase.SaveAssets();
        var scene = SceneManager.GetActiveScene();
        bool saved = !scene.IsValid() || !scene.isDirty || EditorSceneManager.SaveScene(scene);
        notification = saved ? "Einstellungen gespeichert." : "Szene wurde nicht gespeichert.";
        ReadOverride();
        Repaint();
    }

}
