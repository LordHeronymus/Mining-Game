using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class GameSaveSystem
{
    const int Magic = 0x54484631;
    const int MaxCells = 2000000;
    const int MaxFileBytes = 64 * 1024 * 1024;
    const int MaxExpandedBytes = 256 * 1024 * 1024;
    public const int ManualSlots = 3;
    public static bool IsBusy { get; private set; }
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
        public long savedUtc;
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
#if UNITY_EDITOR
        TestDirectory = null;
#endif
    }
    public static string SlotPath(int slot)
    {
        if (slot < 0 || slot > ManualSlots) throw new ArgumentOutOfRangeException(nameof(slot));
        return Path.Combine(DirectoryPath, "slot-" + slot + ".thsave");
    }
    public static bool CanSave => !IsBusy && !LoadingProgress.Active && StatsManager.Instance &&
        StatsManager.Instance.Health > 0 && !StatsManager.Instance.HasWon && !UltroniumAltarChamber.VictorySequenceActive &&
        UnityEngine.Object.FindFirstObjectByType<EnergyManager>() is EnergyManager energy && energy.energy > 0 &&
        UnityEngine.Object.FindFirstObjectByType<MapGenerator>() is MapGenerator map && map.IsGenerated && !map.IsGenerationStreaming;

    public static Summary GetSummary(int slot)
    {
        foreach (string path in new[] { SlotPath(slot), SlotPath(slot) + ".bak" })
            try
            {
                if (!File.Exists(path)) continue;
                ReadEnvelope(path, out var summary, out _);
                if (summary.slot != slot) continue;
                summary.recovered = path.EndsWith(".bak", StringComparison.Ordinal);
                return summary;
            }
            catch (Exception) { }
        return null;
    }
    public static int MostRecentSlot()
    {
        int slot = -1; long date = 0;
        for (int i = 0; i <= ManualSlots; i++)
        { var summary = GetSummary(i); if (summary != null && summary.savedUtc > date) { date = summary.savedUtc; slot = i; } }
        return slot;
    }
    public static bool PrepareLoad(int slot, out string error)
    {
        error = "Spielstand konnte nicht geladen werden.";
        if (IsBusy) return false;
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
                var state = JsonUtility.FromJson<RunSaveState>(reader.ReadString());
                Validate(state);
                var layers = new Layer[4];
                for (int i = 0; i < layers.Length; i++) layers[i] = ReadLayer(reader, catalog, state);
                if (expanded.Position != expanded.Length) throw new InvalidDataException();
                pending = new LoadedRun { state = state, layers = layers };
                error = null; return true;
            }
            catch (Exception ex) { Debug.LogWarning("Tiefenhall: Spielstandprüfung fehlgeschlagen: " + ex.Message); }
        return false;
    }
    public static void CancelPendingLoad() => pending = null;

    public static IEnumerator Save(int slot, MonoBehaviour owner, Action<bool, string> complete)
    {
        if (!CanSave) { complete?.Invoke(false, "Speichern ist gerade nicht möglich."); yield break; }
        IsBusy = true;
        float previousScale = Time.timeScale;
        Time.timeScale = 0; GameplayInputBlocker.SetBlocked(owner, true);
        yield return null; // Render the busy state before capturing the world.
        Task task = null; string error = null;
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding.UTF8, true);
        RunSaveState state = null;
        IEnumerator capture = null;
        try
        {
            state = Capture();
            writer.Write(JsonUtility.ToJson(state));
            var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
            capture = WriteLayers(writer, map, Resources.Load<SaveAssetCatalog>("SaveAssetCatalog"));
        }
        catch (Exception ex) { error = ex.Message; }
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
                var summary = new Summary { slot = slot, savedUtc = state.savedUtc, money = state.stats.money,
                    points = state.stats.points, playedSeconds = state.playedSeconds,
                    depth = UnityEngine.Object.FindFirstObjectByType<CompactHud>()?.DepthMeters ?? 0 };
                string header = JsonUtility.ToJson(summary), path = SlotPath(slot);
                task = Task.Run(() => WriteAtomic(path, header, bytes));
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
        if (error == null) SlotsChanged?.Invoke();
        else Debug.LogError("Tiefenhall: Speichern fehlgeschlagen: " + error);
        complete?.Invoke(error == null, error == null ? "Gespeichert" : "Spielstand konnte nicht gespeichert werden.");
    }

    static RunSaveState Capture()
    {
        var map = UnityEngine.Object.FindFirstObjectByType<MapGenerator>();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var hud = UnityEngine.Object.FindFirstObjectByType<CompactHud>();
        var state = new RunSaveState { seed = map.ActiveSeed, width = map.GeneratedWidth, height = map.GeneratedHeight,
            savedUtc = DateTime.UtcNow.Ticks, playedSeconds = PlayedSeconds, playerPosition = player.transform.position,
            playerVelocity = player.GetComponent<Rigidbody2D>().linearVelocity, facingLeft = player.transform.localScale.x < 0,
            inventory = InventoryManager.Instance.CaptureRunState(), stats = StatsManager.Instance.CaptureRunState(),
            energy = UnityEngine.Object.FindFirstObjectByType<EnergyManager>().energy, recipes = RecipeUnlocks.CaptureRunState(),
            altar = map.AltarChamber.CaptureState(), discovery = map.GetComponent<PlayerMapDiscovery>()?.CaptureState(),
            hotbar = hud ? hud.slots.Select(x => x ? (int)x.item : -1).ToArray() : Array.Empty<int>(), selectedSlot = hud ? hud.SelectedSlot : 0,
            mining = player.GetComponent<TileMiner>()?.CaptureRunState(),
            torches = PlacedTorch.Active.Where(x => x && x.OwnerMap == map).Select(x => x.Cell).ToArray(),
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
            var data = tiles ? tiles.GetTilesBlock(bounds) : Array.Empty<TileBase>();
            if (data.Length > MaxCells) throw new InvalidDataException("Welt ist zu groß.");
            var palette = new List<TileBase>(); var ids = new Dictionary<TileBase, int>();
            foreach (var tile in data) if (tile && !ids.ContainsKey(tile)) { ids[tile] = palette.Count; palette.Add(tile); }
            writer.Write(bounds.xMin); writer.Write(bounds.yMin); writer.Write(bounds.size.x); writer.Write(bounds.size.y);
            writer.Write(palette.Count); foreach (var tile in palette) writer.Write(catalog.Key(tile));
            int i = 0;
            foreach (var cell in bounds.allPositionsWithin)
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
                if (i % 8192 == 0) yield return null;
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
        LoadingProgress.Configure(run.state.width, run.state.height); LoadingProgress.SetStage(5);
        map.BeginSavedMapRestore(run.state.seed, run.state.width, run.state.height);
        map.AltarChamber.RestoreState(run.state.altar);
        var ladders = map.GetComponent<LadderMap>();
        var targets = new[] { map.Terrain, map.EnsureOreOverlay(), map.EnsureArtifactOverlay(), ladders ? ladders.EnsureTiles() : null };
        for (int n = 0; n < targets.Length; n++)
        {
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
                    target.SetTileFlags(cell, TileFlags.None);
                    if (layer.custom.TryGetValue(i, out var custom)) { target.SetColor(cell, custom.color); target.SetTransformMatrix(cell, custom.matrix); }
                    target.SetTileFlags(cell, layer.flags[i]);
                }
                LoadingProgress.Report((n + row / (float)Mathf.Max(1, layer.bounds.size.y)) / 4f); yield return null;
            }
            target.CompressBounds();
        }
        // Let scene Start methods initialize defaults before applying the saved run.
        yield return null;
        var bounds = new BoundsInt(-run.state.width / 2, 1 - run.state.height, 0, run.state.width, run.state.height, 1);
        var data = new MapGenerator.MapGenerationSnapshot(run.state.seed, run.state.width, run.state.height, bounds.xMin,
            map.Terrain.GetTilesBlock(bounds), map.OreOverlay.GetTilesBlock(bounds), map.ArtifactOverlay.GetTilesBlock(bounds));
        var appearance = map.GetComponent<UniformStoneAppearance>();
        if (appearance && appearance.isActiveAndEnabled) yield return appearance.PrepareForLoading(data);
        map.FinishSavedMapRestore(data.terrainTiles);
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
        var torchItem = StartingResourcesSettings.Resolve((int)Item.Torche);
        foreach (var cell in run.state.torches ?? Array.Empty<Vector3Int>()) PlacedTorch.CreateAt(map, torchItem, cell);
        PlayedSeconds = run.state.playedSeconds;
        RunNavigation.EnsurePlayerCamera(player.transform);
        Physics2D.SyncTransforms();
        LoadingProgress.SetStage(6);
        var lighting = map.GetComponent<MapLighting>();
        if (lighting)
        {
            yield return lighting.PrepareForLoading(data, LoadingProgress.Report);
        }
        LoadingProgress.SetStage(7); yield return null; yield return new WaitForEndOfFrame(); LoadingProgress.Complete();
    }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static void Validate(RunSaveState state)
    {
        if (state == null || state.version != 1 || state.width < 1 || state.height < 1 || (long)state.width * state.height > MaxCells ||
            state.inventory == null || state.stats == null || !Finite(state.energy) || state.energy <= 0 || !Finite(state.stats.health) || state.stats.health <= 0 ||
            !Finite(state.playerPosition.x) || !Finite(state.playerPosition.y) || !Finite(state.playerVelocity.x) || !Finite(state.playerVelocity.y) ||
            !Finite(state.playedSeconds) || state.playedSeconds < 0 || state.stats.won || state.savedUtc <= 0 || state.savedUtc > DateTime.MaxValue.Ticks)
            throw new InvalidDataException("Ungültiger Spielstand.");
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
    static void WriteAtomic(string path, string header, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        byte[] compressed;
        using (var output = new MemoryStream())
        { using (var zip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true)) zip.Write(bytes, 0, bytes.Length); compressed = output.ToArray(); }
        string temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(file, Encoding.UTF8, true))
        { writer.Write(Magic); writer.Write(header); writer.Write(compressed.Length); writer.Write(Digest(header, compressed)); writer.Write(compressed); writer.Flush(); file.Flush(true); }
        // Replacement is atomic; retain the previous completed write as a recovery copy.
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
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
