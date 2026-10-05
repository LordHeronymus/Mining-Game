using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class GameSaveSystem
{
    const int Magic = 0x54484631;
    const int MaxCells = 2000000;
    const int MaxFileBytes = 64 * 1024 * 1024;
    const int MaxExpandedBytes = 256 * 1024 * 1024;
    public const int MaxSlots = 10;
    public static bool IsBusy { get; private set; }
    public static int ActiveSlot { get; private set; } = -1;
    public static string ActiveRunName { get; private set; }
    public static bool HasPendingNewRun { get; private set; }
    public static string InitialSaveError { get; private set; }
    public static float PlayedSeconds { get; set; }
    public static event Action SlotsChanged;
    static LoadedRun pending;
    public static bool HasPendingLoad => pending != null;
#if UNITY_EDITOR
    public static string TestDirectory;
#endif
    public static string DirectoryPath
    {
        get
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(TestDirectory)) return TestDirectory;
#endif
            return Path.Combine(Application.persistentDataPath, "Tiefenhall", "Saves");
        }
    }
    [Serializable] public sealed class Summary
    {
        public int slot, depth, money, points;
        public int homeLayer;
        public string name;
        public long savedUtc;
        public long createdUtc, lastOpenedUtc;
        public float playedSeconds;
        public bool recovered;
    }
    sealed class Layer
    {
        public BoundsInt bounds;
        public TileBase[] tiles;
        public TileFlags[] flags;
        public readonly Dictionary<int, (Color color, Matrix4x4 matrix)> custom = new();
    }
    sealed class LoadedRun { public RunSaveState state; public Layer[] layers; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        IsBusy = false; pending = null; PlayedSeconds = 0; SlotsChanged = null;
        ActiveSlot = -1; ActiveRunName = null; HasPendingNewRun = false; InitialSaveError = null;
#if UNITY_EDITOR
        TestDirectory = null;
#endif
    }
    public static string SlotPath(int slot)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(DirectoryPath, "slot-" + slot + ".thsave");
    }
    static bool CanCaptureRun => StatsManager.Instance &&
        StatsManager.Instance.Health > 0 && !StatsManager.Instance.HasWon && !UltroniumAltarChamber.VictorySequenceActive &&
        UnityEngine.Object.FindFirstObjectByType<EnergyManager>() is EnergyManager energy && energy.energy > 0 &&
        UnityEngine.Object.FindFirstObjectByType<MapGenerator>() is MapGenerator map && map.IsGenerated && !map.IsGenerationStreaming;
    public static bool CanSave => !IsBusy && !LoadingProgress.Active && CanCaptureRun;

    public static bool HasSlotData(int slot) => File.Exists(SlotPath(slot)) ||
        File.Exists(SlotPath(slot) + ".bak") || File.Exists(SlotPath(slot) + ".tmp");

    public static int[] OccupiedSlots()
    {
        var slots = new SortedSet<int>();
        if (!Directory.Exists(DirectoryPath)) return Array.Empty<int>();
        foreach (string path in Directory.EnumerateFiles(DirectoryPath, "slot-*.thsave*"))
        {
            string name = Path.GetFileName(path);
            int suffix = name.IndexOf(".thsave", StringComparison.Ordinal);
            if (suffix > 5 && (name.EndsWith(".thsave", StringComparison.Ordinal) ||
                name.EndsWith(".thsave.bak", StringComparison.Ordinal) || name.EndsWith(".thsave.tmp", StringComparison.Ordinal)) &&
                int.TryParse(name.Substring(5, suffix - 5), out int slot) && slot > 0 && slot <= MaxSlots) slots.Add(slot);
        }
        return slots.ToArray();
    }
    public static int NextFreeSlot()
    {
        int slot = 1;
        foreach (int occupied in OccupiedSlots())
        {
            if (occupied > slot) break;
            if (occupied == slot) slot++;
        }
        return slot <= MaxSlots ? slot : -1;
    }
    public static bool BeginNewRun(string name = null)
    {
        int slot = NextFreeSlot();
        if (slot < 1 || IsBusy) return false;
        CancelPendingLoad(); PlayedSeconds = 0;
        ActiveSlot = slot; HasPendingNewRun = true; InitialSaveError = null;
        ActiveRunName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        MetaProgression.BeginRun(Guid.NewGuid().ToString("N"));
        return true;
    }
    public static void LeaveRun() { MetaProgression.EndRun(); ActiveSlot = -1; ActiveRunName = null; HasPendingNewRun = false; }

    public static IEnumerator SaveNewRunForLoading(MonoBehaviour owner)
    {
        if (!HasPendingNewRun) yield break;
        yield return SaveRun(ActiveSlot, owner, (ok, message) => {
            HasPendingNewRun = false; InitialSaveError = ok ? null : message;
        }, true, true);
    }

    public static void MigrateLegacyAutomaticSave()
    {
        string legacy = SlotPath(0), marker = legacy + ".migrated";
        if (File.Exists(marker) || (!File.Exists(legacy) && !File.Exists(legacy + ".bak"))) return;
        foreach (string source in new[] { legacy, legacy + ".bak" })
        {
            try
            {
                if (!File.Exists(source)) continue;
                ReadEnvelope(source, out var summary, out var compressed);
                if (summary.slot != 0) continue;
                int slot = NextFreeSlot(); if (slot < 1) return;
                summary.slot = slot;
                WriteEnvelopeAtomic(SlotPath(slot), JsonUtility.ToJson(summary), compressed, true);
                File.WriteAllText(marker, slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            catch (Exception error) { Debug.LogWarning("Tiefenhall: Alter Spielstand konnte nicht übernommen werden: " + error.Message); }
        }
    }

    public static bool DeleteSlot(int slot, out string error)
    {
        error = "Spielstand konnte nicht gelöscht werden.";
        if (slot < 1 || slot > MaxSlots || IsBusy || RunNavigation.IsTransitioning) return false;
        try
        {
            string path = SlotPath(slot);
            foreach (string file in new[] { path, path + ".bak", path + ".tmp" })
                if (File.Exists(file)) File.Delete(file);
            error = null; SlotsChanged?.Invoke(); return true;
        }
        catch (Exception exception) { Debug.LogWarning("Tiefenhall: Löschen fehlgeschlagen: " + exception.Message); return false; }
    }

    public static Summary GetSummary(int slot)
    {
        foreach (string path in new[] { SlotPath(slot), SlotPath(slot) + ".bak" })
            try
            {
                if (!File.Exists(path)) continue;
                ReadEnvelope(path, out var summary, out _);
                if (summary.slot != slot) continue;
                summary.recovered = path.EndsWith(".bak", StringComparison.Ordinal);
                NormalizeDates(summary);
                return summary;
            }
            catch (Exception) { }
        return null;
    }
    static void NormalizeDates(Summary summary)
    {
        if (summary.createdUtc <= 0 || summary.createdUtc > DateTime.MaxValue.Ticks) summary.createdUtc = summary.savedUtc;
        if (summary.lastOpenedUtc <= 0 || summary.lastOpenedUtc > DateTime.MaxValue.Ticks) summary.lastOpenedUtc = summary.savedUtc;
    }
    public static bool RenameSlot(int slot, string name, out string error)
    {
        error = "Name konnte nicht gespeichert werden.";
        if (IsBusy || RunNavigation.IsTransitioning || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40) return false;
        if (!UpdateMetadata(slot, s => s.name = name.Trim(), out error)) return false;
        if (slot == ActiveSlot) ActiveRunName = name.Trim();
        return true;
    }
    static bool UpdateMetadata(int slot, Action<Summary> change, out string error)
    {
        error = "Spielstand konnte nicht aktualisiert werden.";
        if (slot < 1 || slot > MaxSlots) return false;
        try
        {
            var summary = GetSummary(slot); if (summary == null) return false;
            ReadEnvelope(SlotPath(slot) + (summary.recovered ? ".bak" : ""), out var header, out var payload);
            NormalizeDates(header); change(header); header.recovered = false;
            WriteEnvelopeAtomic(SlotPath(slot), JsonUtility.ToJson(header), payload, false);
            error = null; SlotsChanged?.Invoke(); return true;
        }
        catch (Exception ex) { Debug.LogWarning("Tiefenhall: Metadatenänderung fehlgeschlagen: " + ex.Message); return false; }
    }
    public static int MostRecentSlot()
    {
        int slot = -1; long date = 0;
        foreach (int i in OccupiedSlots())
        { var summary = GetSummary(i); if (summary != null && summary.lastOpenedUtc > date) { date = summary.lastOpenedUtc; slot = i; } }
        return slot;
    }
    // Read-only legacy preview: never creates a pending load or updates a user's save.
    public static int GetHomeLayer(int slot)
    {
        var summary = GetSummary(slot);
        if (summary == null) return 1;
        if (summary.homeLayer > 0) return summary.homeLayer;
        foreach (string path in new[] { SlotPath(slot), SlotPath(slot) + ".bak" })
            try {
                if (!File.Exists(path)) continue;
                ReadEnvelope(path, out var header, out var compressed);
                if (header.slot != slot) continue;
                using var packed = new MemoryStream(compressed);
                using var zip = new GZipStream(packed, CompressionMode.Decompress);
                using var reader = new BinaryReader(zip, Encoding.UTF8);
                int length = 0, shift = 0;
                byte b;
                do {
                    if (shift >= 35) throw new InvalidDataException();
                    b = reader.ReadByte(); length |= (b & 127) << shift; shift += 7;
                } while ((b & 128) != 0);
                if (length < 0 || length > MaxExpandedBytes) throw new InvalidDataException();
                byte[] json = reader.ReadBytes(length);
                if (json.Length != length) throw new InvalidDataException();
                var state = ParseRunState(Encoding.UTF8.GetString(json));
                return HomeLandscape.LayerAtDepth(state.progression?.maxDepth ?? summary.depth, state.generationSettings);
            } catch (Exception) { }
        return HomeLandscape.LayerAtDepth(summary.depth);
    }
    public static bool PrepareLoad(int slot, out string error)
    {
        using var measurement = new Unity.Profiling.ProfilerMarker("Loading.ValidateSave").Auto();
        error = "Spielstand konnte nicht geladen werden.";
        if (IsBusy || slot < 1 || slot > MaxSlots) return false;
        foreach (string path in new[] { SlotPath(slot), SlotPath(slot) + ".bak" })
            try
            {
                if (!File.Exists(path)) continue;
                ReadEnvelope(path, out var summary, out byte[] compressed);
                if (summary.slot != slot) throw new InvalidDataException();
                var catalog = Resources.Load<SaveAssetCatalog>("SaveAssetCatalog");
                if (!catalog) throw new InvalidDataException("Spielstand-Katalog fehlt.");
                using var packed = new MemoryStream(compressed);
                using var zip = new GZipStream(packed, CompressionMode.Decompress);
                using var expanded = new MemoryStream();
                var buffer = new byte[81920]; int count;
                while ((count = zip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (expanded.Length + count > MaxExpandedBytes) throw new InvalidDataException();
                    expanded.Write(buffer, 0, count);
                }
                expanded.Position = 0;
                using var reader = new BinaryReader(expanded, Encoding.UTF8, true);
                var state = ParseRunState(reader.ReadString());
                Validate(state);
                // A legacy run keeps its identity across slot copies and receives neutral bonuses.
                NormalizeDates(summary);
                if (state.progression == null)
                    state.progression = new MetaRunState { runId = "legacy-" + summary.createdUtc + "-" + state.seed + "-" + state.width + "-" + state.height,
                        loadout = new MetaLoadout() };
                var layers = new Layer[4];
                for (int i = 0; i < layers.Length; i++) layers[i] = ReadLayer(reader, catalog, state);
                if (expanded.Position != expanded.Length) throw new InvalidDataException();
                pending = new LoadedRun { state = state, layers = layers };
                ActiveSlot = slot; HasPendingNewRun = false; InitialSaveError = null;
                ActiveRunName = summary.name;
                error = null; return true;
            }
            catch (Exception ex) { Debug.LogWarning("Tiefenhall: Spielstandprüfung fehlgeschlagen: " + ex.Message); }
        return false;
    }
    public static void CancelPendingLoad() => pending = null;

    // Unity's inline serializer materializes missing serializable class fields. Read actual
    // root-property presence first, so old saves keep optional sections absent while a
    // malformed section that really exists still reaches its strict semantic validator.
    internal static RunSaveState ParseRunState(string json)
    {
        bool progression = false, exotics = false, placedLights = false;
        using (var text = new StringReader(json ?? ""))
        using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 128 })
        {
            if (!reader.Read() || reader.TokenType != JsonToken.StartObject) throw new InvalidDataException("Ungültige Spielstanddaten.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            bool ended = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject && reader.Depth == 0) { ended = true; break; }
                if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1 ||
                    !(reader.Value is string name) || !names.Add(name) || !reader.Read())
                    throw new InvalidDataException("Ungültige Spielstandfelder.");
                if (name == "progression" || name == "exotics" || name == "placedLights")
                {
                    JsonToken expected = name == "placedLights" ? JsonToken.StartArray : JsonToken.StartObject;
                    if (reader.TokenType != expected && reader.TokenType != JsonToken.Null)
                        throw new InvalidDataException("Ungültiger optionaler Spielstandbereich.");
                    bool present = reader.TokenType != JsonToken.Null;
                    if (name == "progression") progression = present;
                    else if (name == "exotics") exotics = present;
                    else placedLights = present;
                }
                reader.Skip();
            }
            if (!ended || reader.Read()) throw new InvalidDataException("Ungültiger Abschluss der Spielstanddaten.");
        }
        var state = JsonUtility.FromJson<RunSaveState>(json);
        if (state == null) throw new InvalidDataException("Ungültige Spielstanddaten.");
        if (!progression) state.progression = null;
        if (!exotics) state.exotics = null;
        if (!placedLights) state.placedLights = null;
        return state;
    }

    public static IEnumerator Save(int slot, MonoBehaviour owner, Action<bool, string> complete)
        => SaveRun(slot, owner, complete, false, false);

    static IEnumerator SaveRun(int slot, MonoBehaviour owner, Action<bool, string> complete, bool createOnly, bool allowLoading)
    {
        if (slot < 1 || slot > MaxSlots || IsBusy || !CanCaptureRun || (!allowLoading && LoadingProgress.Active))
        { complete?.Invoke(false, "Speichern ist gerade nicht möglich."); yield break; }
        if (createOnly && HasSlotData(slot)) { complete?.Invoke(false, "Speicherplatz ist bereits belegt."); yield break; }
        IsBusy = true;
        float previousScale = Time.timeScale;
        Time.timeScale = 0; GameplayInputBlocker.SetBlocked(owner, true);
        yield return null; // Render the busy state before capturing the world.
        Task task = null; string error = null;
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, true);
        RunSaveState state = null;
        IEnumerator capture = null;
        Task<string> serialize = null;
        try
        {
            state = Capture();
            // Capture owns independent managed state, including discovery arrays.
            // Unity's plain-data JSON serializer supports background threads.
            serialize = Task.Run(() => JsonUtility.ToJson(state));
            var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            capture = WriteLayers(writer, map, Resources.Load<SaveAssetCatalog>("SaveAssetCatalog"));
        }
        catch (Exception ex) { error = ex.Message; }
        if (serialize != null)
        {
            while (!serialize.IsCompleted) yield return null;
            try { writer.Write(serialize.GetAwaiter().GetResult()); }
            catch (Exception ex) { error = ex.Message; }
        }
        while (error == null && capture != null)
        {
            bool more = false;
            try { more = capture.MoveNext(); } catch (Exception ex) { error = ex.Message; }
            if (!more || error != null) break;
            yield return capture.Current;
        }
        if (error == null)
        {
            try
            {
                writer.Flush(); byte[] bytes = payload.ToArray();
                var summary = new Summary { slot = slot, name = ActiveRunName, savedUtc = state.savedUtc, money = state.stats.money,
                    points = state.stats.points, playedSeconds = state.playedSeconds,
                    depth = UnityEngine.Object.FindFirstObjectByType<CompactHud>()?.DepthMeters ?? 0,
                    homeLayer = HomeLandscape.LayerAtDepth(state.progression?.maxDepth ?? 0, state.generationSettings) };
                var previous = createOnly ? null : GetSummary(slot);
                summary.createdUtc = previous?.createdUtc ?? state.savedUtc;
                summary.lastOpenedUtc = previous?.lastOpenedUtc ?? state.savedUtc;
                string header = JsonUtility.ToJson(summary), path = SlotPath(slot);
                task = Task.Run(() => WriteAtomic(path, header, bytes, createOnly));
            }
            catch (Exception ex) { error = ex.Message; }
        }
        if (task != null)
        {
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) error = task.Exception.GetBaseException().Message;
        }
        IsBusy = false; Time.timeScale = previousScale;
        // An open pause/slot panel owns its own input gate; do not release it here.
        if (!(owner is RunPauseMenu) && !(owner is SaveSlotPanel)) GameplayInputBlocker.SetBlocked(owner, false);
        if (error == null) { ActiveSlot = slot; SlotsChanged?.Invoke(); }
        else Debug.LogError("Tiefenhall: Speichern fehlgeschlagen: " + error);
        complete?.Invoke(error == null, error == null ? "Gespeichert" : "Spielstand konnte nicht gespeichert werden.");
    }

    static RunSaveState Capture()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        var state = new RunSaveState { seed = map.ActiveSeed, width = map.GeneratedWidth, height = map.GeneratedHeight,
            progression = MetaProgression.CaptureRunState(),
            exotics = ExoticWorldContent.Ensure(map).CaptureState(),
            placedLights = PlacedTorch.Capture(map),
            generationSettings = GpsSettings.CaptureGeneration(map),
            copperVisuals = map.GetComponent<OreOverlayAppearance>().CopperPlans.Capture(),
            savedUtc = DateTime.UtcNow.Ticks, playedSeconds = PlayedSeconds, playerPosition = player.transform.position,
            playerVelocity = player.GetComponent<Rigidbody2D>().linearVelocity, facingLeft = player.transform.localScale.x < 0,
            inventory = InventoryManager.Instance.CaptureRunState(), stats = StatsManager.Instance.CaptureRunState(),
            energy = UnityEngine.Object.FindFirstObjectByType<EnergyManager>().energy, recipes = RecipeUnlocks.CaptureRunState(),
            altar = map.AltarChamber.CaptureState(), discovery = map.GetComponent<PlayerMapDiscovery>()?.CaptureState(),
            hotbar = hud ? hud.slots.Select(x => x ? (int)x.item : -1).ToArray() : Array.Empty<int>(), selectedSlot = hud ? hud.SelectedSlot : 0,
            mining = player.GetComponent<TileMiner>()?.CaptureRunState(),
            torches = PlacedTorch.Active.Where(x => x && x.OwnerMap == map && !x.IsLavaLamp).Select(x => x.Cell).ToArray(),
            storage = UnityEngine.Object.FindObjectsByType<SurfaceStorageBuilding>(FindObjectsSortMode.None)
                .Select(x => new SavedStorage { key = StorageKey(x.transform), items = x.CaptureRunState() }).ToArray(),
            forest = UnityEngine.Object.FindFirstObjectByType<SurfaceTrees>()?.CaptureRunState(),
            grass = map.GetComponent<SurfaceTallGrass>()?.CaptureRunState() };
        Validate(state); return state;
    }
    static string StorageKey(Transform t) => t.name + "@" + t.position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
        "," + t.position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static IEnumerator WriteLayers(BinaryWriter writer, MapGenerator map, SaveAssetCatalog catalog)
    {
        if (!catalog) throw new InvalidDataException("Spielstand-Katalog fehlt.");
        var ladders = map.GetComponent<LadderMap>();
        foreach (var tiles in new[] { map.Terrain, map.EnsureOreOverlay(), map.EnsureArtifactOverlay(), ladders ? ladders.EnsureTiles() : null })
        {
            var bounds = tiles ? tiles.cellBounds : new BoundsInt(0, 0, 0, 0, 0, 1);
            if ((long)bounds.size.x * bounds.size.y > MaxCells) throw new InvalidDataException("Welt ist zu groß.");
            var palette = tiles ? new TileBase[tiles.GetUsedTilesCount()] : Array.Empty<TileBase>();
            if (tiles) tiles.GetUsedTilesNonAlloc(palette);
            var ids = new Dictionary<TileBase, int>();
            for (int index = 0; index < palette.Length; index++) ids.Add(palette[index], index);
            writer.Write(bounds.xMin); writer.Write(bounds.yMin); writer.Write(bounds.size.x); writer.Write(bounds.size.y);
            writer.Write(palette.Length); foreach (var tile in palette) writer.Write(catalog.Key(tile));
            var budget = new LoadingWorkBudget();
            int rowsPerBatch = Mathf.Max(1, 1024 / Mathf.Max(1, bounds.size.x));
            for (int row = 0; row < bounds.size.y; row += rowsPerBatch)
            {
                int rows = Mathf.Min(rowsPerBatch, bounds.size.y - row);
                var batch = new BoundsInt(bounds.xMin, bounds.yMin + row, 0, bounds.size.x, rows, 1);
                var data = tiles.GetTilesBlock(batch);
                int i = 0;
                foreach (var cell in batch.allPositionsWithin)
                {
                    var tile = data[i++]; writer.Write(tile ? ids[tile] : -1);
                    if (tile)
                    {
                        var color = tiles.GetColor(cell); var matrix = tiles.GetTransformMatrix(cell);
                        writer.Write((int)tiles.GetTileFlags(cell));
                        bool custom = color != Color.white || matrix != Matrix4x4.identity; writer.Write(custom);
                        if (custom)
                        { writer.Write(color.r); writer.Write(color.g); writer.Write(color.b); writer.Write(color.a); for (int n = 0; n < 16; n++) writer.Write(matrix[n]); }
                    }
                    if ((i & 127) == 0 && budget.Expired) { yield return null; budget.Restart(); }
                }
                if (budget.Expired) { yield return null; budget.Restart(); }
            }
            yield return null;
        }
    }
    static Layer ReadLayer(BinaryReader reader, SaveAssetCatalog catalog, RunSaveState state)
    {
        int x = reader.ReadInt32(), y = reader.ReadInt32(), w = reader.ReadInt32(), h = reader.ReadInt32();
        if (w < 0 || h < 0 || (long)w * h > MaxCells || w > state.width + 8 || h > state.height + 8 ||
            x < -state.width / 2 - 4 || x > state.width / 2 + 4 || y < -state.height - 4 || y > 4)
            throw new InvalidDataException("Ungültige Weltgrenzen.");
        var layer = new Layer { bounds = new BoundsInt(x, y, 0, w, h, 1), tiles = new TileBase[w * h], flags = new TileFlags[w * h] };
        int paletteCount = reader.ReadInt32(); if (paletteCount < 0 || paletteCount > 65536) throw new InvalidDataException();
        var palette = new TileBase[paletteCount]; for (int i = 0; i < paletteCount; i++) palette[i] = catalog.Resolve(reader.ReadString());
        for (int i = 0; i < layer.tiles.Length; i++)
        {
            int index = reader.ReadInt32(); if (index == -1) continue;
            if (index < 0 || index >= palette.Length) throw new InvalidDataException();
            layer.tiles[i] = palette[index]; layer.flags[i] = (TileFlags)reader.ReadInt32();
            if (!reader.ReadBoolean()) continue;
            var color = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var matrix = new Matrix4x4(); for (int n = 0; n < 16; n++) { matrix[n] = reader.ReadSingle(); if (!Finite(matrix[n])) throw new InvalidDataException(); }
            if (!Finite(color.r) || !Finite(color.g) || !Finite(color.b) || !Finite(color.a)) throw new InvalidDataException();
            layer.custom[i] = (color, matrix);
        }
        return layer;
    }
    public static IEnumerator RestorePending(MapGenerator map)
    {
        var run = pending; pending = null;
        MetaProgression.BeginRun(run.state.progression.runId, run.state.progression);
        LoadingProgress.Configure(run.state.width, run.state.height); LoadingProgress.SetStage(1);
        GpsSettings.RestoreGeneration(map, run.state.generationSettings);
        map.BeginSavedMapRestore(run.state.seed, run.state.width, run.state.height);
        map.AltarChamber.RestoreState(run.state.altar);
        var ladders = map.GetComponent<LadderMap>();
        var targets = new[] { map.Terrain, map.EnsureOreOverlay(), map.EnsureArtifactOverlay(), ladders ? ladders.EnsureTiles() : null };
        var tileBudget = new LoadingWorkBudget();
        for (int n = 0; n < targets.Length; n++)
        {
            LoadingProgress.SetStage(n + 1);
            var target = targets[n]; var layer = run.layers[n]; if (!target) continue;
            target.ClearAllTiles();
            int batchRows = Mathf.Max(1, 1024 / Mathf.Max(1, layer.bounds.size.x));
            for (int row = 0; row < layer.bounds.size.y; row += batchRows)
            {
                int rows = Mathf.Min(batchRows, layer.bounds.size.y - row), w = layer.bounds.size.x;
                var block = new TileBase[w * rows]; Array.Copy(layer.tiles, row * w, block, 0, block.Length);
                target.SetTilesBlock(new BoundsInt(layer.bounds.xMin, layer.bounds.yMin + row, 0, w, rows, 1), block);
                for (int i = row * w; i < (row + rows) * w; i++)
                {
                    if (!layer.tiles[i]) continue;
                    var cell = new Vector3Int(layer.bounds.xMin + i % w, layer.bounds.yMin + i / w);
                    if (layer.custom.TryGetValue(i, out var custom))
                    {
                        target.SetTileFlags(cell, TileFlags.None);
                        target.SetColor(cell, custom.color); target.SetTransformMatrix(cell, custom.matrix);
                    }
                    target.SetTileFlags(cell, layer.flags[i]);
                }
                tileBudget.ChargeTiles(block.Length);
                LoadingProgress.Report((row + rows) / (float)Mathf.Max(1, layer.bounds.size.y));
                if (tileBudget.Expired) { yield return null; tileBudget.Restart(); }
            }
        }
        // Let scene Start methods initialize defaults before applying the saved run.
        LoadingProgress.SetStage(5);
        yield return null;
        var bounds = new BoundsInt(-run.state.width / 2, 1 - run.state.height, 0, run.state.width, run.state.height, 1);
        var restored = new TileBase[3][];
        for (int layer = 0; layer < restored.Length; layer++)
        {
            int index = layer;
            yield return ExpandSavedLayer(run.layers[layer], bounds, value => restored[index] = value);
        }
        var data = new MapGenerator.MapGenerationSnapshot(run.state.seed, run.state.width, run.state.height, bounds.xMin,
            restored[0], restored[1], restored[2]);
        var appearance = map.GetComponent<UniformStoneAppearance>();
        if (appearance && appearance.isActiveAndEnabled) yield return appearance.PrepareForLoading(data);
        yield return map.FinishSavedMapRestoreSteps(data.terrainTiles);
        map.GetComponent<OreOverlayAppearance>().RestoreCopperPlans(run.state.copperVisuals);
        var surface = map.GetComponent<DirtSurfaceAppearance>();
        if (surface && surface.isActiveAndEnabled) yield return surface.PrepareForLoading();
        InventoryManager.Instance.RestoreRunState(run.state.inventory);
        RecipeUnlocks.RestoreRunState(run.state.recipes);
        StatsManager.Instance.RestoreRunState(run.state.stats, map);
        UnityEngine.Object.FindFirstObjectByType<SurfaceTrees>()?.RestoreRunState(run.state.forest);
        map.GetComponent<SurfaceTallGrass>()?.RestoreRunState(run.state.grass);
        foreach (var storage in UnityEngine.Object.FindObjectsByType<SurfaceStorageBuilding>(FindObjectsSortMode.None))
            storage.RestoreRunState(run.state.storage?.FirstOrDefault(x => x.key == StorageKey(storage.transform))?.items);
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        player.transform.position = run.state.playerPosition;
        var scale = player.transform.localScale; scale.x = Mathf.Abs(scale.x) * (run.state.facingLeft ? -1 : 1); player.transform.localScale = scale;
        var body = player.GetComponent<Rigidbody2D>(); body.position = run.state.playerPosition; body.linearVelocity = run.state.playerVelocity;
        player.GetComponent<TileMiner>()?.RestoreRunState(run.state.mining);
        UnityEngine.Object.FindFirstObjectByType<EnergyManager>().energy = Mathf.Clamp(run.state.energy, 0, StatsManager.Instance.MaxEnergy);
        UnityEngine.Object.FindFirstObjectByType<CompactHud>()?.RestoreRunSlots(run.state.hotbar, run.state.selectedSlot);
        if (run.state.discovery != null) map.GetComponent<PlayerMapDiscovery>()?.RestoreState(run.state.discovery);
        if (run.state.exotics == null) yield return ExoticWorldContent.Ensure(map).GenerateSteps();
        else ExoticWorldContent.Ensure(map).RestoreState(run.state.exotics);
        var torchItem = StartingResourcesSettings.Resolve((int)Item.Torche);
        if (run.state.placedLights != null) PlacedTorch.Restore(map, run.state.placedLights);
        else foreach (var cell in run.state.torches ?? Array.Empty<Vector3Int>()) PlacedTorch.CreateAt(map, torchItem, cell);
        PlayedSeconds = run.state.playedSeconds;
        RunNavigation.EnsurePlayerCamera(player.transform);
        Physics2D.SyncTransforms();
        LoadingProgress.SetStage(6);
        var lighting = map.GetComponent<MapLighting>();
        if (lighting)
        {
            yield return lighting.PrepareForLoading(data, LoadingProgress.Report);
        }
        LoadingProgress.SetStage(7); yield return null; yield return new WaitForEndOfFrame();
        UpdateMetadata(ActiveSlot, s => s.lastOpenedUtc = DateTime.UtcNow.Ticks, out _);
        LoadingProgress.Complete();
    }

    static IEnumerator ExpandSavedLayer(Layer layer, BoundsInt bounds, Action<TileBase[]> complete)
    {
        if (layer.bounds == bounds) { complete(layer.tiles); yield break; }
        var result = new TileBase[bounds.size.x * bounds.size.y];
        int sourceX = Mathf.Max(0, bounds.xMin - layer.bounds.xMin);
        int targetX = Mathf.Max(0, layer.bounds.xMin - bounds.xMin);
        int count = Mathf.Min(layer.bounds.size.x - sourceX, bounds.size.x - targetX);
        var budget = new LoadingWorkBudget();
        if (count > 0)
        for (int y = Mathf.Max(bounds.yMin, layer.bounds.yMin); y < Mathf.Min(bounds.yMax, layer.bounds.yMax); y++)
        {
            Array.Copy(layer.tiles, (y - layer.bounds.yMin) * layer.bounds.size.x + sourceX,
                result, (y - bounds.yMin) * bounds.size.x + targetX, count);
            if (budget.Expired) { yield return null; budget.Restart(); }
        }
        complete(result);
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static void Validate(RunSaveState state)
    {
        if (state == null || state.version != 1 || state.width < 1 || state.height < 1 || (long)state.width * state.height > MaxCells ||
            state.inventory == null || state.stats == null || !Finite(state.energy) || state.energy <= 0 || !Finite(state.stats.health) || state.stats.health <= 0 ||
            !Finite(state.playerPosition.x) || !Finite(state.playerPosition.y) || !Finite(state.playerVelocity.x) || !Finite(state.playerVelocity.y) ||
            !Finite(state.playedSeconds) || state.playedSeconds < 0 || state.stats.won || state.savedUtc <= 0 || state.savedUtc > DateTime.MaxValue.Ticks)
            throw new InvalidDataException("Ungültiger Spielstand.");
        if (state.generationSettings != null && !GpsSettings.ValidateGeneration(state.generationSettings, state.width, state.height, out var generationError))
            throw new InvalidDataException(generationError);
        if (!MetaProgression.ValidateRunState(state.progression))
            throw new InvalidDataException("Ungültiger Fortschritt im Spielstand.");
        ExoticWorldContent.ValidateState(state.exotics, state.width, state.height);
        if (state.placedLights != null)
        {
            if (state.placedLights.Length > MaxCells) throw new InvalidDataException("Zu viele Lichtquellen.");
            var occupied = new HashSet<Vector3Int>();
            foreach (var light in state.placedLights)
                if ((light.itemId != (int)Item.Torche && light.itemId != (int)Item.LavaLamp) || !occupied.Add(light.cell) ||
                    light.cell.z != 0 || light.cell.x < -state.width / 2 || light.cell.x >= state.width - state.width / 2 ||
                    light.cell.y < 1 - state.height || light.cell.y > 4)
                    throw new InvalidDataException("Ungültige Lichtquelle im Spielstand.");
        }
        foreach (var item in state.inventory.items ?? Array.Empty<SavedItem>())
            if (item.count < 0 || !StartingResourcesSettings.Resolve(item.id)) throw new InvalidDataException("Unbekanntes Inventar-Item.");
        foreach (int id in (state.inventory.owned ?? Array.Empty<int>()).Concat(state.inventory.powerups ?? Array.Empty<int>()))
            if (!StartingResourcesSettings.Resolve(id)) throw new InvalidDataException("Unbekanntes Upgrade.");
        if (state.discovery != null && (state.discovery.width != state.width || state.discovery.height != state.height || state.discovery.seed != state.seed))
            throw new InvalidDataException("Kartenentdeckung passt nicht zur Welt.");
    }
    static byte[] Digest(string header, byte[] payload)
    {
        using var hash = SHA256.Create(); byte[] prefix = Encoding.UTF8.GetBytes(header);
        hash.TransformBlock(prefix, 0, prefix.Length, null, 0); hash.TransformFinalBlock(payload, 0, payload.Length); return hash.Hash;
    }
    static void WriteAtomic(string path, string header, byte[] bytes, bool createOnly = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        byte[] compressed;
        using (var output = new MemoryStream())
        { using (var zip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true)) zip.Write(bytes, 0, bytes.Length); compressed = output.ToArray(); }
        WriteEnvelopeAtomic(path, header, compressed, createOnly);
    }
    static void WriteEnvelopeAtomic(string path, string header, byte[] compressed, bool createOnly)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (createOnly && (File.Exists(path) || File.Exists(path + ".bak") || File.Exists(path + ".tmp")))
            throw new IOException("Speicherplatz ist bereits belegt.");
        string temporary = path + ".tmp";
        using (var file = new FileStream(temporary, createOnly ? FileMode.CreateNew : FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(file, Encoding.UTF8, true))
        { writer.Write(Magic); writer.Write(header); writer.Write(compressed.Length); writer.Write(Digest(header, compressed)); writer.Write(compressed); writer.Flush(); file.Flush(true); }
        // Replacement is atomic; retain the previous completed write as a recovery copy.
        if (createOnly) File.Move(temporary, path);
        else if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
    static void ReadEnvelope(string path, out Summary summary, out byte[] compressed)
    {
        using var file = File.OpenRead(path);
        if (file.Length > MaxFileBytes || file.Length < 40) throw new InvalidDataException();
        using var reader = new BinaryReader(file, Encoding.UTF8, true);
        if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unbekanntes Spielstandformat.");
        string header = reader.ReadString(); if (header.Length > 4096) throw new InvalidDataException();
        summary = JsonUtility.FromJson<Summary>(header);
        int size = reader.ReadInt32(); if (size < 1 || size > MaxFileBytes || file.Length - file.Position != size + 32L) throw new InvalidDataException();
        byte[] checksum = reader.ReadBytes(32); compressed = reader.ReadBytes(size);
        if (!checksum.SequenceEqual(Digest(header, compressed)) || summary == null || summary.savedUtc <= 0 || summary.savedUtc > DateTime.MaxValue.Ticks)
            throw new InvalidDataException("Spielstand ist beschädigt.");
    }
}
