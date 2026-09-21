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
    static readonly string[] Tabs = { "Spieler", "Energie", "Map", "Partikel", "Blöcke & Beute", "Debug", "Licht", "Werkbank", "Audio", "Bäume", "Tiere" };
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
    [SerializeField] SurfaceBirds birds;
    [SerializeField] SurfaceTrees trees;
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
    bool showSources;
    string notification;
    GameplaySettingsData savedOverride;
    string overrideWarning;
    bool overrideExists;

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
        birds = Resolve(birds);
        trees = Resolve(trees);
        rabbit = Resolve(rabbit);
        rabbitSpawner = Resolve(rabbitSpawner);
        frogs = sceneComponents.OfType<SurfaceCritters>().FirstOrDefault(group => group.species == SurfaceCritters.Species.Frog);
        snails = sceneComponents.OfType<SurfaceCritters>().FirstOrDefault(group => group.species == SurfaceCritters.Species.Snail);
        fireflies = Resolve(fireflies);
        follow = Resolve(follow);
        items = AssetDatabase.FindAssets("t:ItemSO").Select(guid => AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(item => item).OrderBy(item => item.displayName).ToArray();
        recipes = AssetDatabase.FindAssets("t:CraftingRecipe")
            .Select(guid => AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(recipe => recipe).OrderBy(recipe => recipe.output ? recipe.output.displayName : recipe.name).ToArray();
        if (!selectedRecipe || !recipes.Contains(selectedRecipe)) selectedRecipe = recipes.FirstOrDefault();
        ReadOverride();
        Repaint();
    }

    T Resolve<T>(T current) where T : Component => current && sceneComponents.Contains(current)
        ? current : sceneComponents.OfType<T>().FirstOrDefault();

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
            EditorGUILayout.HelpBox(overrideWarning ?? $"JSON-Test-Override vorhanden: Abbaugeschwindigkeit {savedOverride.baseDiggingSpeed:g}{(savedOverride.hasLightingOverride ? " und Lichtwerte" : "")}. Diese Werte haben im Editor/Development Build Vorrang vor den Basiswerten. Verwaltung unter Debug.", overrideWarning == null ? MessageType.Warning : MessageType.Error);

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
                case 3: DrawParticles(); break;
                case 4: DrawBlocks(); break;
                case 5: DrawDebug(); break;
                case 6: DrawLighting(); break;
                case 7: DrawWorkbench(); break;
                case 8: DrawAudio(); break;
                case 9: DrawTrees(); break;
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
                        GUILayout.Height(28), GUILayout.ExpandWidth(true))) selected = index;
            }
        }
        return selected;
    }

    void DrawTrees()
    {
        if (!trees) { Missing("Keine Oberflächenbäume in der aktiven Szene."); return; }
        Section("Oberflächenbäume", trees, data =>
        {
            Integer(data, "maximumTrees", "Maximale Anzahl", "", 0, 20);
            var spacing = data.FindProperty("minimumTreeSpacing");
            float cellWidth = map && map.Terrain ? map.Terrain.layoutGrid.cellSize.x : .5f;
            EditorGUI.BeginChangeCheck();
            int tiles = EditorGUILayout.IntField("Mindestabstand (Kacheln)",
                Mathf.RoundToInt(spacing.floatValue / cellWidth));
            if (EditorGUI.EndChangeCheck()) spacing.floatValue = Mathf.Max(4, tiles) * cellWidth;
            Integer(data, "hitsToFell", "Treffer zum Fällen", "", 1, 100);
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
        Section("Layer 1 Details", sceneComponents.OfType<FirstLayerAmbience>().FirstOrDefault(), data =>
        {
            Float(data, "detailsPerMinute", "Ereignisse pro Minute", "", 0f, 60f);
        }, false);
        Section("Höhlen-Tribal-Song", sceneComponents.OfType<SecondLayerAmbience>().FirstOrDefault(), data =>
        {
            Float(data, "tribalSongLayer2MeanMinutes", "Ø Minuten in Layer 2", "", .01f, 120f);
            Float(data, "tribalSongLayer3MeanMinutes", "Ø Minuten in Layer 3", "", .01f, 240f);
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
        index = EditorGUILayout.Popup("Rezept", index,
            recipes.Select(recipe => recipe.output ? recipe.output.displayName : recipe.name).ToArray());
        selectedRecipe = recipes[index];
        Section("Rezept", selectedRecipe, data =>
        {
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
                Float(data, "jumpForce", "Sprungimpuls", "Impuls auf den Rigidbody. Auch Masse und Gravitation beeinflussen den Sprung.", 0);
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
            Float(data, "groundedCoeff", "Beschleunigungsfaktor am Boden", "Multipliziert Beschleunigen und Bremsen am Boden. Kleiner = träger. In der Luft gilt Faktor 1.", 0.01f);
        });
        var body = movement ? movement.GetComponent<Rigidbody2D>() : null;
        Section("Sprungphysik", body, data =>
        {
            Float(data, "m_GravityScale", "Gravitationsfaktor", "Skaliert die globale 2D-Gravitation für den Spieler.", 0.01f);
            Float(data, "m_Mass", "Spielermasse", "Höhere Masse reduziert die Wirkung des Sprungimpulses.", 0.01f);
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
        Section("Kartengröße und Seed", map, data =>
        {
            Integer(data, "mapWidth", "Breite (Zellen)", "Die Karte wird horizontal um X = 0 zentriert.", 1, 10000);
            Integer(data, "mapHeight", "Tiefe (Zellen)", "Die Karte wächst von Y = 0 nach unten.", 1, 10000);
            var randomSeed = data.FindProperty("randomizeSeed");
            EditorGUILayout.PropertyField(randomSeed, new GUIContent("Seed zufällig generieren"));
            using (new EditorGUI.DisabledScope(randomSeed.boolValue))
                Integer(data, "seed", "Seed", "", -10000000, 10000000);
        });
        Section("Oberfläche", map, data =>
        {
            Integer(data, "transitionThickness", "Übergangsdicke (Kacheln)", "", 1, 100);
            EditorGUILayout.Slider(data.FindProperty("grassYOffset"), -.5f, .5f,
                new GUIContent("Gras Y-Versatz (Welteinheiten)"));
        }, false);
        Section("Erzverteilung", map, data =>
        {
            EditorGUILayout.CurveField(data.FindProperty("oreDensityCurve"), Color.cyan,
                new Rect(0f, 0f, 1f, 1f), new GUIContent("Erzverteilung nach Tiefe"));
            EditorGUILayout.Slider(data.FindProperty("oreDensityMultiplierPercent"), 0f, 100f,
                new GUIContent("Multiplikator (%)"));
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Oberflächenanstieg", EditorStyles.boldLabel);
            Integer(data, "surfaceOreRampDepth", "Höhe (Blöcke)", "", 0, 10000);
            EditorGUILayout.CurveField(data.FindProperty("surfaceOreRampCurve"), new Color(1f, .55f, .2f),
                new Rect(0f, 0f, 1f, 1f), new GUIContent("Verteilung im Anfangsbereich"));
        }, false);
        Section("Adern und Layer-Übergänge", map, data =>
        {
            EditorGUILayout.Slider(data.FindProperty("oreScale"), .5f, 3f, new GUIContent("Erzgröße (×)"));
            Integer(data, "minimumOreVeinSize", "Minimale Adergröße (Blöcke)", "", 1, 10000);
            EditorGUILayout.CurveField(data.FindProperty("oreTransitionCurve"), Color.yellow,
                new Rect(0f, 0f, 1f, 1f), new GUIContent("Erz-Übergangskurve"));
            Integer(data, "oreTransitionDepth", "Erz-Übergang (Blöcke)", "", 1, 10000);
            EditorGUILayout.CurveField(data.FindProperty("oreVeinSizeCurve"), Color.green,
                new Rect(0f, 0f, 1f, 1f), new GUIContent("Adergröße im Erz-Übergang"));
        }, false);
        if (!map) return;
        Section("Layers", map, data =>
            EditorGUILayout.PropertyField(data.FindProperty("layers"), new GUIContent("Layers"), true), false);
        long cells = (long)map.mapWidth * map.mapHeight;
        EditorGUILayout.LabelField($"{cells:N0} Zellen", EditorStyles.miniLabel);
        if (!Registry) { Missing("Dem Map-Generator fehlt ein BlockRegistry-Asset."); return; }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Blockkatalog", EditorStyles.boldLabel);
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
        EditorGUILayout.Space(10);
        DrawBlockGeneration();
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

    void DrawBlockGeneration()
    {
        var block = BlockPicker();
        Section("Erzverteilung", block, data =>
        {
            EditorGUILayout.PropertyField(data.FindProperty("spawnWithNoise"), new GUIContent("Vorkommen aktiv"));
            if (!data.FindProperty("spawnWithNoise").boolValue) return;
            EditorGUILayout.Slider(data.FindProperty("oreFrequencyPercent"), 0f, 100f, new GUIContent("Erzgewicht"));
            EditorGUILayout.IntSlider(data.FindProperty("veinSizeIndex"), 1, 100, new GUIContent("Adergröße (Index)"));
        });
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

    void DrawDebug()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Standardwerte und Test-Overrides", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Dieses Fenster bearbeitet die Standardwerte in Assets/Szene. Das F1-Panel schreibt eine separate JSON-Datei, die im Editor und in Development Builds Vorrang hat. Reguläre Builds ignorieren diese Datei.", MessageType.Info);
        EditorGUILayout.LabelField("Basis-Abbaugeschwindigkeit", BaseStats ? BaseStats.miningSpeed.ToString("g") : "Kein Basiswerte-Asset");
        EditorGUILayout.LabelField("Gespeicherter JSON-Override", !overrideExists ? "Keiner" : overrideWarning == null ? savedOverride.baseDiggingSpeed.ToString("g") : "Ungültig – Standardwerte werden verwendet");
        EditorGUILayout.SelectableLabel(GameplaySettings.FilePath, EditorStyles.textField, GUILayout.Height(40));
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Speicherordner öffnen")) EditorUtility.RevealInFinder(Application.persistentDataPath);
            using (new EditorGUI.DisabledScope(!overrideExists))
                if (GUILayout.Button("Test-Override zurücksetzen")) ResetOverride();
        }
        EditorGUILayout.HelpBox("Zurücksetzen verschiebt die JSON-Datei in eine Sicherung. Ab dem nächsten Spielstart gilt wieder das Basiswerte-Asset. Die Sicherung bleibt im selben Ordner.", MessageType.None);
        if (GUILayout.Button("Gameplay-Analyse öffnen"))
            Application.OpenURL(new Uri(Path.GetFullPath("Docs/Gameplay-Einstellungen.md")).AbsoluteUri);
        showSources = EditorGUILayout.Foldout(showSources, "Verwendete Quellen", true);
        if (showSources)
            foreach (var source in new Object[] { BaseStats, movement, energy, station, map, Registry, follow }) if (source) Source(source);
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

    void ResetOverride()
    {
        try
        {
            ArchiveOverride(GameplaySettings.FilePath);
            notification = "Test-Override zurückgesetzt. Standardwerte gelten ab dem nächsten Spielstart.";
            ReadOverride();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            notification = "Zurücksetzen fehlgeschlagen: " + ex.Message;
        }
    }

    internal static string ArchiveOverride(string path)
    {
        string backup = path + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bak";
        File.Move(path, backup);
        return backup;
    }
}
