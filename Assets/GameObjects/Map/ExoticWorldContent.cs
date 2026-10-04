using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public struct SavedBlueprintCache { public Vector3Int cell; public bool claimed; }
[Serializable] public struct SavedBoulder { public Vector3Int cell; public float health; }
[Serializable] public struct SavedDynamite { public Vector3 position; public float fuse; }
[Serializable] public sealed class ExoticWorldState
{
    public int version = 1;
    public SavedBlueprintCache[] caches;
    public SavedBoulder[] boulders;
    public SavedDynamite[] dynamite;
    public int pendingCache = -1;
    public string[] pendingChoices;
}

[DisallowMultipleComponent]
public sealed class ExoticWorldContent : MonoBehaviour
{
    MapGenerator map;
    ExoticWorldState state;
    readonly List<SpriteRenderer> cacheVisuals = new();
    readonly List<MineBoulder> boulders = new();
    Transform player;
    float nextCheck;
    bool generating;
    public MapGenerator Map => map;
    public bool Initialized => state != null;
    public CraftingRecipe[] PendingChoices
    {
        get
        {
            var choices = new List<CraftingRecipe>();
            if (state?.pendingChoices != null)
                foreach (string id in state.pendingChoices)
                { var recipe = ExoticCatalog.Find(id); if (recipe && !RecipeUnlocks.IsUnlocked(recipe)) choices.Add(recipe); }
            return choices.ToArray();
        }
    }
    public static ExoticWorldContent Ensure(MapGenerator map)
    {
        if (!map) return null;
        var world = map.GetComponent<ExoticWorldContent>();
        if (!world) world = map.gameObject.AddComponent<ExoticWorldContent>();
        world.map = map;
        return world;
    }
    public void Generate()
    {
        var steps = GenerateSteps();
        while (steps.MoveNext()) { }
    }
    public IEnumerator GenerateSteps()
    {
        if (state != null || generating || !map || !map.IsGenerated) yield break;
        generating = true;
        try
        {
            var caches = new List<SavedBlueprintCache>();
            var rocks = new List<SavedBoulder>();
            var random = new System.Random(map.ActiveSeed ^ 0x347A52);
            for (int depth = 35; depth < map.GeneratedHeight - 8; depth += 45)
            {
                var search = FindFloorSteps(depth, 1, random, cache => caches.Add(new SavedBlueprintCache { cell = cache }));
                while (search.MoveNext()) yield return search.Current;
                if (depth >= 80)
                {
                    search = FindFloorSteps(depth + 8, 2, random, rock => rocks.Add(new SavedBoulder { cell = rock, health = MineBoulder.MaximumHealth }));
                    while (search.MoveNext()) yield return search.Current;
                }
                yield return null;
            }
            state = new ExoticWorldState { caches = caches.ToArray(), boulders = rocks.ToArray() };
            BuildVisuals();
        }
        finally { generating = false; }
    }
    IEnumerator FindFloorSteps(int depth, int size, System.Random random, Action<Vector3Int> found)
    {
        var budget = new LoadingWorkBudget();
        int width = map.GeneratedWidth, start = random.Next(Math.Max(1, width - size));
        for (int dy = 0; dy < 36; dy++)
        {
            int y = -depth - dy;
            if (y < 3 - map.GeneratedHeight) break;
            for (int n = 0; n < width - size - 2; n++)
            {
                if ((n & 127) == 0 && budget.Expired) { yield return null; budget.Restart(); }
                int x = -width / 2 + 1 + (start + n) % Math.Max(1, width - size - 2);
                var cell = new Vector3Int(x, y, 0);
                if (!map.IsGeneratedCaveCell(cell) || !map.Terrain.HasTile(cell + Vector3Int.down)) continue;
                bool valid = true;
                for (int a = 0; a < size && valid; a++)
                    for (int b = 0; b < size; b++)
                    {
                        var target = cell + new Vector3Int(a, b, 0);
                        if (map.Terrain.HasTile(target) || map.IsCellProtected(target)) { valid = false; break; }
                    }
                if (!valid) continue;
                found(cell); yield break;
            }
        }
    }
    void Update()
    {
        if (state == null || LoadingProgress.Active || RunNavigation.IsTransitioning) return;
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .2f;
        if (!player) player = FindFirstObjectByType<PlayerMovement>()?.transform;
        if (!player || GameplayInputBlocker.IsBlocked || Time.timeScale <= 0) return;
        if (state.pendingCache >= 0 && PendingChoices.Length > 0) { ExoticBlueprintPanel.Show(this); return; }
        bool canFind = false;
        foreach (var recipe in ExoticCatalog.Recipes)
            if (ExoticCatalog.IsAvailable(recipe) && !RecipeUnlocks.IsUnlocked(recipe)) { canFind = true; break; }
        for (int i = 0; i < state.caches.Length; i++)
        {
            var cache = state.caches[i];
            Vector3 position = map.Terrain.GetCellCenterWorld(cache.cell);
            float distance = Vector2.Distance(player.position, position);
            bool nearby = distance < 12f && HasClearSight(player.position, position);
            if (i < cacheVisuals.Count && cacheVisuals[i]) cacheVisuals[i].enabled = !cache.claimed && canFind && nearby;
            if (!cache.claimed && canFind && nearby && distance <= 1.2f && TryDiscover(i)) break;
        }
    }
    bool HasClearSight(Vector2 origin, Vector2 target)
    {
        var terrain = map.Terrain;
        float cellSize = Vector2.Distance(terrain.GetCellCenterWorld(Vector3Int.zero), terrain.GetCellCenterWorld(Vector3Int.right));
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(origin, target) / Mathf.Max(.02f, cellSize * .3f)));
        Vector3Int previous = terrain.WorldToCell(origin);
        for (int i = 1; i <= steps; i++)
        {
            var cell = terrain.WorldToCell(Vector2.Lerp(origin, target, i / (float)steps));
            if (terrain.HasTile(cell)) return false;
            if (cell.x != previous.x && cell.y != previous.y &&
                (terrain.HasTile(new Vector3Int(previous.x, cell.y, 0)) || terrain.HasTile(new Vector3Int(cell.x, previous.y, 0)))) return false;
            previous = cell;
        }
        return true;
    }
    public bool TryDiscover(int index)
    {
        if (GameplayInputBlocker.IsBlocked || GameOverPanel.IsOpen || GameVictoryPanel.IsOpen ||
            state == null || index < 0 || index >= state.caches.Length || state.caches[index].claimed || state.pendingCache >= 0) return false;
        var cell = state.caches[index].cell;
        var id = "exotic-cache:" + cell.x + ":" + cell.y;
        var offer = ExoticCatalog.Offer(id);
        if (offer.Length == 0) return false;
        state.pendingCache = index;
        state.pendingChoices = Array.ConvertAll(offer, recipe => recipe.exoticId);
        if (MetaProgressionRuntime.RewardsAllowed) MetaProgression.RecordDiscovery(id);
        ExoticBlueprintPanel.Show(this);
        return true;
    }
    public bool LearnPending(CraftingRecipe recipe)
    {
        if (state == null || state.pendingCache < 0 || !recipe || state.pendingChoices == null ||
            Array.IndexOf(state.pendingChoices, recipe.exoticId) < 0 || !RecipeUnlocks.LearnExotic(recipe)) return false;
        var cache = state.caches[state.pendingCache]; cache.claimed = true;
        state.caches[state.pendingCache] = cache;
        if (state.pendingCache < cacheVisuals.Count && cacheVisuals[state.pendingCache]) cacheVisuals[state.pendingCache].enabled = false;
        state.pendingCache = -1; state.pendingChoices = null;
        return true;
    }
    void BuildVisuals()
    {
        foreach (var visual in cacheVisuals) if (visual) Destroy(visual.gameObject);
        foreach (var boulder in boulders) if (boulder) { boulder.gameObject.SetActive(false); Destroy(boulder.gameObject); }
        cacheVisuals.Clear(); boulders.Clear();
        if (state.caches == null) state.caches = Array.Empty<SavedBlueprintCache>();
        if (state.boulders == null) state.boulders = Array.Empty<SavedBoulder>();
        for (int i = 0; i < state.caches.Length; i++)
        {
            var cache = state.caches[i];
            var go = new GameObject("Exotic Blueprint Cache"); go.transform.SetParent(transform, false);
            go.transform.position = map.Terrain.GetCellCenterWorld(cache.cell);
            var visual = go.AddComponent<SpriteRenderer>();
            visual.sprite = Resources.Load<Sprite>("Exotics/BlueprintCache"); visual.sortingOrder = 1;
            go.transform.localScale = Vector3.one * .24f;
            visual.enabled = false; cacheVisuals.Add(visual);
        }
        foreach (var saved in state.boulders) boulders.Add(MineBoulder.Create(this, saved));
    }
    public ExoticWorldState CaptureState()
    {
        if (state == null) return null;
        var saved = new List<SavedBoulder>();
        foreach (var boulder in boulders) if (boulder) saved.Add(boulder.Capture());
        state.boulders = saved.ToArray();
        state.dynamite = PlacedDynamite.Capture(map);
        return JsonUtility.FromJson<ExoticWorldState>(JsonUtility.ToJson(state));
    }
    public void RestoreState(ExoticWorldState saved)
    {
        if (saved == null) { Generate(); return; }
        ValidateState(saved, map.GeneratedWidth, map.GeneratedHeight);
        PlacedDynamite.Clear(map);
        state = JsonUtility.FromJson<ExoticWorldState>(JsonUtility.ToJson(saved));
        if (state.pendingCache >= (state.caches?.Length ?? 0)) { state.pendingCache = -1; state.pendingChoices = null; }
        BuildVisuals();
        if (state.dynamite != null) foreach (var charge in state.dynamite) PlacedDynamite.Restore(map, charge);
    }
    public static void ValidateState(ExoticWorldState state, int width, int height)
    {
        if (state == null) return;
        if (state.version != 1 || (state.caches?.Length ?? 0) > 10000 || (state.boulders?.Length ?? 0) > 10000 ||
            (state.dynamite?.Length ?? 0) > 1000 || (state.pendingChoices?.Length ?? 0) > 3 || state.pendingCache < -1 ||
            state.pendingCache >= (state.caches?.Length ?? 0)) throw new System.IO.InvalidDataException("Ungültige exotische Weltobjekte.");
        var cells = new HashSet<Vector3Int>();
        if (state.caches != null) foreach (var cache in state.caches)
            if (!ValidCell(cache.cell, width, height) || !cells.Add(cache.cell)) throw new System.IO.InvalidDataException("Ungültiger Bauplanfund.");
        cells.Clear();
        if (state.boulders != null) foreach (var rock in state.boulders)
            if (!ValidCell(rock.cell, width - 2, height) || !cells.Add(rock.cell) || float.IsNaN(rock.health) ||
                float.IsInfinity(rock.health) || rock.health < 0 || rock.health > MineBoulder.MaximumHealth)
                throw new System.IO.InvalidDataException("Ungültiger Felsbrocken.");
        if (state.dynamite != null) foreach (var charge in state.dynamite)
            if (!Finite(charge.position.x) || !Finite(charge.position.y) || !Finite(charge.position.z) ||
                !Finite(charge.fuse) || charge.fuse < 0 || charge.fuse > 2.5f) throw new System.IO.InvalidDataException("Ungültige Dynamitladung.");
        if (state.pendingCache >= 0)
        {
            if (state.caches[state.pendingCache].claimed || state.pendingChoices == null || state.pendingChoices.Length == 0)
                throw new System.IO.InvalidDataException("Ungültige Bauplanauswahl.");
            var ids = new HashSet<string>();
            foreach (string id in state.pendingChoices) if (string.IsNullOrEmpty(id) || !ids.Add(id) || !ExoticCatalog.Find(id))
                throw new System.IO.InvalidDataException("Unbekannter exotischer Bauplan.");
        }
    }
    static bool ValidCell(Vector3Int cell, int width, int height) => cell.z == 0 && cell.x >= -width / 2 &&
        cell.x < -width / 2 + width && cell.y <= 0 && cell.y >= 1 - height;
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
