using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

public static class DynamiteRuntimeChecks
{
    public static object Main()
    {
        if (!Application.isPlaying || (string.IsNullOrEmpty(GameSaveSystem.TestDirectory) && string.IsNullOrEmpty(MetaProgression.TestDirectory)))
            throw new InvalidOperationException("Isolated active gameplay required.");
        if (LoadingProgress.Active || GameplayInputBlocker.IsBlocked || Time.timeScale <= 0)
            throw new InvalidOperationException("Ready unpaused gameplay required.");
        new GameObject("Dynamite Runtime Checks").AddComponent<DynamiteRuntimeProbe>();
        return "Started; report Temp/DynamiteRuntimeChecks.txt";
    }
}

public sealed class DynamiteRuntimeProbe : MonoBehaviour
{
    const string Report = "Temp/DynamiteRuntimeChecks.txt";
    MapGenerator map; ExoticWorldContent world; ExoticWorldState oldWorld;
    InventoryManager inventory; SavedInventory oldInventory; StatsManager stats; SavedStats oldStats;
    EnergyManager energy; float oldEnergy, oldConsumption, oldScale;
    PlayerMovement player; Rigidbody2D body; TileMiner miner; SavedMiningCell[] oldMining;
    bool oldPlayerEnabled, oldSimulated, oldMinerEnabled, oldWorldEnabled, cleaned, captured;
    Vector3 oldPosition, oldCameraPosition; Vector2 oldVelocity;
    Camera camera; CameraFollow follow; CameraWorldBorderClamp clamp; bool oldFollow, oldClamp; float oldOrtho;
    string previousMeta; MetaRunState previousRun;
    PlacedTorch testLight;
    int checks;
    readonly List<CellSnapshot> terrainBackup = new();
    struct CellSnapshot { public Tilemap map; public Vector3Int cell; public TileBase tile; public Color color; public Matrix4x4 transform; public TileFlags flags; }
    void Check(bool pass, string text)
    {
        if (!pass) throw new Exception(text);
        checks++; File.AppendAllText(Report, "PASS " + text + "\n");
    }
    IEnumerator Start()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Report, "Dynamite runtime\n");
        var stack = new Stack<IEnumerator>(); stack.Push(Run());
        while (stack.Count > 0)
        {
            bool more = false; object current = null; Exception error = null;
            try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; } catch (Exception ex) { error = ex; }
            if (error != null) { File.AppendAllText(Report, "FAIL " + error + "\n"); Cleanup(); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        File.AppendAllText(Report, "COMPLETE " + checks + " checks\n"); Cleanup();
    }
    IEnumerator Run()
    {
        map = Object.FindFirstObjectByType<MapGenerator>(); player = Object.FindFirstObjectByType<PlayerMovement>();
        inventory = InventoryManager.Instance; stats = StatsManager.Instance; energy = Object.FindFirstObjectByType<EnergyManager>();
        Check(map && map.IsGenerated && player && inventory && stats && energy, "Existing generated gameplay available");
        world = ExoticWorldContent.Ensure(map); oldWorld = world.CaptureState();
        Check(oldWorld != null && oldWorld.boulders.Any(b => b.health > 0), "Natural generated boulder available");
        Check(PlacedDynamite.Capture(map).Length == 0, "No unrelated live charges in fixture");
        body = player.GetComponent<Rigidbody2D>(); miner = player.GetComponent<TileMiner>();
        oldInventory = inventory.CaptureRunState(); oldStats = stats.CaptureRunState(); oldMining = miner.CaptureRunState();
        oldEnergy = energy.energy; oldConsumption = energy.consumptionMultiplier; oldScale = Time.timeScale;
        oldPosition = player.transform.position; oldVelocity = body.linearVelocity;
        oldPlayerEnabled = player.enabled; oldMinerEnabled = miner.enabled; oldSimulated = body.simulated; oldWorldEnabled = world.enabled;
        camera = Camera.main; follow = camera.GetComponent<CameraFollow>(); clamp = camera.GetComponent<CameraWorldBorderClamp>();
        oldFollow = follow && follow.enabled; oldClamp = clamp && clamp.enabled;
        oldCameraPosition = camera.transform.position; oldOrtho = camera.orthographicSize;
        previousMeta = MetaProgression.TestDirectory; previousRun = MetaProgression.CaptureRunState(); captured = true;
        MetaProgression.EndRun(); MetaProgression.TestDirectory = Path.GetFullPath("Temp/DynamiteProfile-" + DateTime.UtcNow.Ticks);
        MetaProgression.BeginRun("dynamite-runtime-checks");
        player.enabled = false; miner.enabled = false; body.simulated = false; world.enabled = false; energy.consumptionMultiplier = 0;
        inventory.RestoreRunState(new SavedInventory { items = Array.Empty<SavedItem>(), owned = Array.Empty<int>(),
            powerups = new[] { (int)Item.CopperPickaxe }, carryingLevel = 1, energyLevel = 1 });
        var dynamite = StartingResourcesSettings.Resolve((int)Item.Dynamite);
        Check(inventory.Add(dynamite, 2), "Two real dynamite items available");
        var natural = Object.FindObjectsByType<MineBoulder>(FindObjectsSortMode.None).First(b => b.Map == map && b.Health > 0);
        Vector2 blastPoint = natural.HitPoint; Vector3Int boulderCell = natural.Capture().cell;
        Vector3Int naturalFloor = boulderCell + Vector3Int.down;
        Check(map.Terrain.HasTile(naturalFloor) && !map.IsCellProtected(naturalFloor), "Natural terrain floor lies inside boulder blast");
        var layout = map.AltarChamber.Layout;
        Check(layout.valid, "Protected altar shell exists");
        Vector3Int protectedCell = layout.origin + Vector3Int.down;
        Vector3Int protectedChargeCell = layout.origin;
        Vector2 protectedBlast = map.Terrain.GetCellCenterWorld(protectedChargeCell);
        var protectedTile = map.Terrain.GetTile(protectedCell);
        Check(protectedTile && map.IsCellProtected(protectedCell) && !map.IsCellProtected(protectedChargeCell), "Protected shell and valid adjacent charge position selected");
        BackUpBlast(blastPoint); BackUpBlast(protectedBlast);
        Time.timeScale = 0;
        int before = inventory.GetCount(dynamite);
        Check(!PlacedDynamite.TryPlace(map, dynamite, map.Terrain.GetCellCenterWorld(protectedCell), map.Terrain.GetCellCenterWorld(protectedCell), stats.Reach)
            && inventory.GetCount(dynamite) == before, "Direct placement on protected terrain rejected without consuming item");
        PlaceAt(dynamite, blastPoint); PlaceAt(dynamite, protectedBlast);
        Check(inventory.GetCount(dynamite) == before - 2 && PlacedDynamite.Capture(map).Length == 2, "Actual placement consumes one item per live charge");
        var saved = JsonUtility.FromJson<ExoticWorldState>(JsonUtility.ToJson(world.CaptureState()));
        Check(saved.dynamite.Length == 2 && saved.dynamite.All(d => Mathf.Abs(d.fuse - 2.5f) < .0001f) &&
            saved.dynamite.Any(d => Vector2.Distance(d.position, blastPoint) < .001f) &&
            saved.dynamite.Any(d => Vector2.Distance(d.position, protectedBlast) < .001f), "Run save capture records both fuse timers and positions");
        world.RestoreState(saved); world.enabled = false; yield return null;
        Check(PlacedDynamite.Capture(map).Length == 2, "Save restoration replaces rather than duplicates live charges");
        yield return new WaitForSecondsRealtime(.35f);
        Check(PlacedDynamite.Capture(map).All(d => Mathf.Abs(d.fuse - 2.5f) < .0001f), "TimeScale zero freezes live fuse over real elapsed time");
        var existingLights = PlacedTorch.Active.ToArray();
        PlacedTorch.CreateAt(map, StartingResourcesSettings.Resolve((int)Item.Torche), boulderCell);
        testLight = PlacedTorch.Active.First(light => !existingLights.Contains(light));
        if (follow) follow.enabled = false; if (clamp) clamp.enabled = false;
        camera.orthographicSize = 3f; camera.transform.position = new Vector3(blastPoint.x, blastPoint.y + .4f, oldCameraPosition.z);
        yield return null; yield return new WaitForEndOfFrame();
        string imagePath = "Assets/Design/Tiefenhall/Metaprogression/Dynamite-BeforeExplosion.png";
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
        var screenshot = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(imagePath, screenshot.EncodeToPNG()); Object.Destroy(screenshot);
        camera.transform.position = oldCameraPosition; camera.orthographicSize = oldOrtho;
        if (follow) follow.enabled = oldFollow; if (clamp) clamp.enabled = oldClamp;
        Check(Vector2.Distance(player.transform.position, blastPoint) > 5 && Vector2.Distance(player.transform.position, protectedBlast) > 5, "Player remains safely outside both blast radii");
        Time.timeScale = 1;
        yield return new WaitForSeconds(.35f);
        var elapsed = PlacedDynamite.Capture(map);
        Check(elapsed.Length == 2 && elapsed.All(d => d.fuse < 2.3f && d.fuse > 1.5f), "Gameplay time advances actual fuse");
        var partial = world.CaptureState(); world.RestoreState(partial); world.enabled = false; yield return null;
        var resumed = PlacedDynamite.Capture(map);
        Check(resumed.Length == 2 && resumed.All(d => d.fuse < 2.3f), "Partial fuse resumes from saved remainder");
        float end = Time.realtimeSinceStartup + 6;
        while (PlacedDynamite.Capture(map).Length > 0)
        { if (Time.realtimeSinceStartup > end) throw new Exception("Dynamite did not explode within deadline"); yield return null; }
        yield return null;
        Check(world.CaptureState().boulders.First(b => b.cell == boulderCell).health == 0, "Elapsed fuse explosion destroys natural boulder");
        Check(!map.Terrain.HasTile(naturalFloor), "Same explosion removes ordinary natural terrain");
        Check(map.Terrain.GetTile(protectedCell) == protectedTile, "Adjacent actual explosion preserves protected altar shell");
        Check(PlacedDynamite.Capture(map).Length == 0, "Exploded charges clear runtime and save state");
        Check(stats.Health + .01f >= oldStats.health, "Safe player receives no explosion damage");
    }
    void PlaceAt(ItemSO item, Vector2 position)
    {
        player.transform.position = position + Vector2.right * Mathf.Min(1f, stats.Reach * .5f);
        body.position = player.transform.position;
        Check(PlacedDynamite.TryPlace(map, item, position, player.transform.position, stats.Reach), "TryPlace accepts charge within real player reach");
        player.transform.position = oldPosition; body.position = oldPosition;
    }
    void BackUpBlast(Vector2 position)
    {
        var center = map.Terrain.WorldToCell(position);
        foreach (var layer in new[] { map.Terrain, map.OreOverlay, map.ArtifactOverlay })
        {
            if (!layer) continue;
            for (int x = -3; x <= 3; x++) for (int y = -3; y <= 3; y++)
            {
                var cell = center + new Vector3Int(x, y, 0);
                terrainBackup.Add(new CellSnapshot { map = layer, cell = cell, tile = layer.GetTile(cell), color = layer.GetColor(cell),
                    transform = layer.GetTransformMatrix(cell), flags = layer.GetTileFlags(cell) });
                if (layer == map.ArtifactOverlay) layer.SetTile(cell, null); // Keep the probe focused on explosions, without artifact modal interruptions.
            }
        }
    }
    void Cleanup()
    {
        if (cleaned) return; cleaned = true;
        if (captured)
        {
            if (testLight) Object.DestroyImmediate(testLight.gameObject);
            PlacedDynamite.Clear(map);
            foreach (var entry in terrainBackup) if (entry.map)
            {
                entry.map.SetTile(entry.cell, entry.tile); entry.map.SetTileFlags(entry.cell, TileFlags.None);
                entry.map.SetColor(entry.cell, entry.color); entry.map.SetTransformMatrix(entry.cell, entry.transform); entry.map.SetTileFlags(entry.cell, entry.flags);
                if (entry.map == map.Terrain) map.GetComponent<MapLighting>()?.NotifyTileChanged(entry.cell);
            }
            MetaProgression.EndRun(); MetaProgression.TestDirectory = previousMeta;
            if (previousRun != null) MetaProgression.BeginRun(previousRun.runId, previousRun);
            inventory.RestoreRunState(oldInventory); stats.RestoreRunState(oldStats, map); miner.RestoreRunState(oldMining);
            energy.energy = oldEnergy; energy.consumptionMultiplier = oldConsumption;
            world.RestoreState(oldWorld); world.enabled = oldWorldEnabled;
            player.transform.position = oldPosition; body.position = oldPosition; body.linearVelocity = oldVelocity;
            body.simulated = oldSimulated; player.enabled = oldPlayerEnabled; miner.enabled = oldMinerEnabled;
            camera.transform.position = oldCameraPosition; camera.orthographicSize = oldOrtho;
            if (follow) follow.enabled = oldFollow; if (clamp) clamp.enabled = oldClamp;
            Time.timeScale = oldScale; Physics2D.SyncTransforms();
        }
        Destroy(gameObject);
    }
    void OnDestroy() { if (!cleaned) Cleanup(); }
}
