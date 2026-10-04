using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed partial class SurfaceCritters : MonoBehaviour
{
    void Awake() => GpsSettings.ApplyComponent(this);

    public enum Species { Frog, Snail }
    [InspectorName("Tierart")] public Species species;
    public MapGenerator map;
    public SkyController sky;
    public Transform player;
    public Material material;
    [Range(0, 12), InspectorName("Maximale Anzahl")] public int maxCount = 3;
    [Min(.1f), InspectorName("Grösse")] public float size = .85f;
    [InspectorName("Spawnintervall (s)")] public Vector2 spawnInterval = new Vector2(12, 22);
    [InspectorName("Ruhephasen (s)")] public Vector2 restDuration = new Vector2(4, 9);
    [Min(.5f), InspectorName("Bewegungsradius")] public float roamRadius = 6;
    [Min(.01f), InspectorName("Kriechgeschwindigkeit")] public float crawlSpeed = .14f;
    [Min(.1f), InspectorName("Sprunghöhe")] public float hopHeight = .42f;
    [Min(.1f), InspectorName("Sprungdauer (s)")] public float hopDuration = .38f;
    [InspectorName("Sprungweite")] public Vector2 hopDistance = new Vector2(.65f, 1.25f);
    [InspectorName("Körperfarbe")] public Color bodyColor = new Color(.10f, .52f, .025f);
    [InspectorName("Schneckenhaus")] public Color shellColor = new Color(.58f, .20f, .045f);
    [Min(2), InspectorName("Despawn-Abstand")] public float despawnDistance = 30;
    [Min(.1f), InspectorName("Despawn-Verzögerung (s)")] public float despawnDelay = 12;

    sealed class Critter
    {
        public float x, y, home, from, to, phase, age, wait, travel, scale, shade, distant;
        public Rigidbody2D body;
        public int direction, hops;
        public bool jumping, falling, hasFallen, returning, awaitingEntrance, landingSoundTriggered;
    }
    readonly List<Critter> animals = new List<Critter>();
    static readonly HashSet<SurfaceCritters> populations = new HashSet<SurfaceCritters>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => populations.Clear();
    static readonly Plane[] viewPlanes = new Plane[6];
    const int VisibleSpawnLimit = 2;
    readonly System.Random random = new System.Random();
    const string GeneratedName = "Critters (generated)";
    CritterMesh geometry;
    MapGenerator subscribedMap;
    float nextSpawn, surfaceY, referenceRetry;
    bool initialPopulation;
    public int ActiveCount => animals.Count;

    void OnEnable()
    {
        populations.Add(this);
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name != species + " physics" || !child.GetComponent<Rigidbody2D>()) continue;
            child.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
        CritterMesh.RemoveGenerated(transform, GeneratedName);
        if (material) geometry = new CritterMesh(transform, GeneratedName, material, 21);
        if (map) { subscribedMap = map; map.Generated += ResetPopulation; }
        ResetPopulation();
    }
    void ResetPopulation() { ClearAnimals(); nextSpawn = 0; initialPopulation = true; StopCroaking(); }
    void Update() => Tick(Time.deltaTime);

    void Tick(float dt)
    {
        if (geometry == null) return;
        geometry.Clear();
        if (!map || !map.IsGenerated) { ClearAnimals(); geometry.Upload(); StopCroaking(); return; }
        referenceRetry -= dt;
        if (referenceRetry <= 0)
        {
            if (!player) { var found = FindFirstObjectByType<PlayerMovement>(); if (found) player = found.transform; }
            if (!sky) sky = FindFirstObjectByType<SkyController>();
            referenceRetry = 1;
        }
        surfaceY = map.Terrain.CellToWorld(new Vector3Int(0, 1, 0)).y;
        int limit = Mathf.Clamp(maxCount, 0, 12);
        while (SurfacePopulationCount() > limit)
        {
            int lastSurfaceAnimal = animals.FindLastIndex(animal => !animal.hasFallen);
            if (lastSurfaceAnimal < 0) break;
            RemoveAnimal(lastSurfaceAnimal);
        }
        for (int i = animals.Count - 1; i >= 0; i--)
        {
            Critter animal = animals[i];
            if (!animal.body) { animals.RemoveAt(i); continue; }
            animal.x = animal.body.position.x;
            if (!animal.jumping) animal.y = animal.body.position.y;
            if (animal.falling && IsVisibleOnScreen(animal)) animal.distant = 0f;
            else
                animal.distant = !player || Vector2.Distance(new Vector2(animal.x, animal.y), player.position) > Mathf.Max(2, despawnDistance)
                    ? animal.distant + dt : 0f;
            if (animal.distant >= Mathf.Max(.1f, despawnDelay))
            { RemoveAnimal(i); continue; }
            if (!animal.jumping && !animal.falling && !HasSupport(animal))
            { animal.falling = true; animal.hasFallen = true; animal.wait = 0f; animal.body.gravityScale = 1f; }
            Move(animal, dt); Draw(animal);
        }
        nextSpawn = Mathf.Max(0, nextSpawn - dt);
        if (SurfacePopulationCount() < limit && nextSpawn <= 0 && SurfaceInView(out float left, out float right))
        {
            // One initial animal per species gives both populations a chance to use the shared budget.
            if (Spawn(left, right, initialPopulation))
            { initialPopulation = false; nextSpawn = Range(spawnInterval); }
            else nextSpawn = 1;
        }
        geometry.Upload();
        UpdateCroaking(dt);
    }

    int SurfacePopulationCount()
    {
        int count = 0;
        foreach (var animal in animals)
            if (!animal.hasFallen) count++;
        return count;
    }

    bool IsVisibleOnScreen(Critter animal)
    {
        var camera = Camera.main;
        if (!camera || !camera.isActiveAndEnabled || geometry == null || !geometry.renderer ||
            !geometry.renderer.enabled || (camera.cullingMask & (1 << geometry.renderer.gameObject.layer)) == 0) return false;
        GeometryUtility.CalculateFrustumPlanes(camera, viewPlanes);
        float progress = animal.jumping ? Mathf.Clamp01(animal.travel / Mathf.Max(.1f, hopDuration)) : 0f;
        float lift = Mathf.Sin(progress * Mathf.PI) * Mathf.Max(.1f, hopHeight);
        var bounds = new Bounds(new Vector3(animal.x, animal.y + lift + .25f * animal.scale, 0f),
            new Vector3(1.05f * animal.scale, .55f * animal.scale, .02f));
        return GeometryUtility.TestPlanesAABB(viewPlanes, bounds);
    }

    bool SurfaceInView(out float left, out float right)
    {
        left = right = 0;
        var camera = Camera.main;
        if (!camera || !player || Mathf.Abs(player.position.y - surfaceY) > Mathf.Max(2, despawnDistance)) return false;
        var view = camera.WorldToViewportPoint(new Vector3(player.position.x, surfaceY, 0));
        if (view.z <= 0 || view.y < 0 || view.y > 1 || view.x < 0 || view.x > 1) return false;
        left = camera.ViewportToWorldPoint(new Vector3(0, .5f, view.z)).x;
        right = camera.ViewportToWorldPoint(new Vector3(1, .5f, view.z)).x;
        return right > left;
    }

    bool Spawn(float left, float right, bool inside)
    {
        if (CountOccupiedViewSlots(Camera.main) >= VisibleSpawnLimit) return false;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int side = random.Next(2) == 0 ? -1 : 1;
            float x = inside ? Random(left + .6f, right - .6f) : (side < 0 ? left - .65f : right + .65f);
            float scale = Mathf.Max(.1f, size) * Random(.85f, 1.15f);
            if (!HasGround(x, x, scale, surfaceY) || Vector2.Distance(new Vector2(x, surfaceY), player.position) > Mathf.Max(2, despawnDistance)) continue;
            bool crowded = false;
            foreach (var other in animals)
                if (!other.hasFallen && Mathf.Abs(other.x - x) < 1.2f) { crowded = true; break; }
            if (crowded) continue;
            var animal = new Critter { x = x, y = surfaceY, home = x, scale = scale,
                direction = inside ? side : -side, wait = inside ? Random(.4f, 3) : .2f,
                travel = Random(6, 12), phase = Random(0, 6.28f), shade = Random(.87f, 1.1f), awaitingEntrance = !inside };
            if (map.TryGetComponent(out TerrainColliderChunks terrainColliders))
                terrainColliders.EnsureAnimalCollisionAt(new Vector2(x, surfaceY));
            animal.body = CreateBody(animal);
            animals.Add(animal);
            return true;
        }
        return false;
    }

    Rigidbody2D CreateBody(Critter animal)
    {
        var proxy = new GameObject(species + " physics") { hideFlags = HideFlags.DontSave };
        proxy.layer = LayerMask.NameToLayer("Player");
        proxy.transform.SetParent(transform, true);
        proxy.transform.position = new Vector3(animal.x, animal.y, transform.position.z);
        var body = proxy.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        var hitbox = proxy.AddComponent<BoxCollider2D>();
        hitbox.size = new Vector2(.65f * animal.scale, .4f * animal.scale);
        hitbox.offset = new Vector2(0f, .2f * animal.scale);
        return body;
    }

    void RemoveAnimal(int index)
    {
        var body = animals[index].body;
        animals.RemoveAt(index);
        if (body)
        {
            body.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(body.gameObject);
            else DestroyImmediate(body.gameObject);
        }
    }

    void ClearAnimals()
    {
        for (int i = animals.Count - 1; i >= 0; i--) RemoveAnimal(i);
    }

    int CountOccupiedViewSlots(Camera camera)
    {
        if (!camera) return VisibleSpawnLimit;
        GeometryUtility.CalculateFrustumPlanes(camera, viewPlanes);
        int count = 0;
        foreach (var group in populations)
        {
            if (!group || !group.isActiveAndEnabled || group.map != map ||
                (camera.cullingMask & (1 << group.gameObject.layer)) == 0) continue;
            foreach (var animal in group.animals)
            {
                float progress = animal.jumping ? Mathf.Clamp01(animal.travel / Mathf.Max(.1f, group.hopDuration)) : 0;
                float lift = Mathf.Sin(progress * Mathf.PI) * Mathf.Max(.1f, group.hopHeight);
                var bounds = new Bounds(new Vector3(animal.x, animal.y + lift + .25f * animal.scale, 0),
                    new Vector3(1.05f * animal.scale, .55f * animal.scale, .02f));
                bool visible = GeometryUtility.TestPlanesAABB(viewPlanes, bounds);
                if (visible) animal.awaitingEntrance = false;
                // Reserve slots for replacements approaching the view, so both spawners cannot queue extras.
                if (!visible && animal.awaitingEntrance)
                {
                    bounds.Expand(new Vector3(2, 0, 0));
                    visible = GeometryUtility.TestPlanesAABB(viewPlanes, bounds);
                }
                if (visible) count++;
            }
        }
        return count;
    }

    void Move(Critter a, float dt)
    {
        a.age += dt;
        if (a.falling)
        {
            if (HasSupport(a) && a.body.linearVelocity.y <= 0f)
            {
                a.falling = false;
                a.home = a.x; a.wait = .3f; a.distant = 0f;
            }
            return;
        }
        if (a.jumping)
        {
            if (!a.returning && !HasGround(a.from, a.to, a.scale, a.y)) { a.returning = true; a.direction *= -1; }
            float previousY = a.y + Mathf.Sin(Mathf.Clamp01(a.travel / Mathf.Max(.1f, hopDuration)) * Mathf.PI) * hopHeight;
            a.travel = a.returning ? Mathf.Max(0, a.travel - dt) : a.travel + dt;
            float progress = Mathf.Clamp01(a.travel / Mathf.Max(.1f, hopDuration));
            float nextX = Mathf.Lerp(a.from, a.to, progress);
            float nextY = a.y + Mathf.Sin(progress * Mathf.PI) * hopHeight;
            if (!SurfaceAnimalCollision.CanMove(map, a.x, nextX, previousY,
                    .65f * a.scale, .4f * a.scale) ||
                !SurfaceAnimalCollision.CanRise(map, nextX, previousY, nextY,
                    .65f * a.scale, .4f * a.scale))
            { a.jumping = false; a.falling = true; a.hasFallen = true; a.body.gravityScale = 1f; a.wait = .5f; return; }
            if (nextY < previousY && SurfaceAnimalCollision.TryGround(map, nextX, previousY,
                    previousY - nextY, .65f * a.scale, .4f * a.scale, out float landingY))
            {
                a.x = nextX; a.y = landingY; a.jumping = false; a.home = nextX; a.wait = .3f;
                a.body.position = new Vector2(nextX, landingY); a.body.gravityScale = 1f;
                return;
            }
            a.x = nextX;
            a.body.position = new Vector2(a.x, nextY);
            float landingX = a.returning ? a.from : a.to;
            if (species == Species.Frog && HasGround(landingX, landingX, a.scale, a.y) && AudioManager.Instance)
                AudioManager.Instance.UpdateGrassLanding(ref a.landingSoundTriggered,
                    a.returning ? a.travel : Mathf.Max(0f, Mathf.Max(.1f, hopDuration) - a.travel),
                    new Vector3(landingX, a.y, 0));
            if ((a.returning && progress <= 0) || progress >= 1)
            {
                a.jumping = false; a.wait = --a.hops > 0 && !a.returning ? .16f : Range(restDuration);
                a.body.gravityScale = 1f;
                if (a.returning) a.home = a.x;
                if (!HasSupport(a)) { a.falling = true; a.hasFallen = true; }

            }
            return;
        }
        a.wait -= dt;
        if (a.wait > 0) return;
        if (species == Species.Snail)
        {
            a.travel -= dt;
            if (a.travel <= 0)
            {
                a.wait = Range(restDuration); a.travel = Random(8, 16);
                if (random.NextDouble() < .3) a.direction *= -1;
                return;
            }
            float next = a.x + a.direction * Mathf.Max(.01f, crawlSpeed) * dt;
            if (Mathf.Abs(next - a.home) > Mathf.Max(.5f, roamRadius) || !HasGround(a.x, next, a.scale, a.y) ||
                !SurfaceAnimalCollision.CanMove(map, a.x, next, a.y, .65f * a.scale, .4f * a.scale))
            { a.direction *= -1; a.wait = .8f; return; }
            a.x = next;
            a.body.position = new Vector2(a.x, a.y);
        }
        else
        {
            if (a.hops <= 0) { a.hops = random.Next(1, 4); if (random.NextDouble() < .35) a.direction *= -1; }
            float target = a.x + a.direction * Range(hopDistance);
            if (Mathf.Abs(target - a.home) > Mathf.Max(.5f, roamRadius) || !HasGround(a.x, target, a.scale, a.y))
            { a.direction *= -1; a.wait = .5f; a.hops = 0; return; }
            a.from = a.x; a.to = target; a.travel = 0; a.jumping = true; a.returning = false; a.landingSoundTriggered = false;
            a.body.gravityScale = 0f; a.body.linearVelocity = Vector2.zero;
        }
    }

    bool HasSupport(Critter animal) => SurfaceAnimalCollision.TryGround(map, animal.x,
        animal.y, .06f, .65f * animal.scale, .4f * animal.scale, out _) ||
        (animal.body && Mathf.Abs(animal.body.linearVelocity.y) < .05f &&
         animal.body.GetComponent<Collider2D>().IsTouchingLayers(1 << map.gameObject.layer));

    bool HasGround(float from, float to, float scale, float groundY)
    {
        float margin = .38f * scale + .02f;
        var tiles = map.Terrain;
        var left = tiles.WorldToCell(new Vector3(Mathf.Min(from, to) - margin, groundY - SurfaceAnimalCollision.GroundTileProbeDepth, tiles.transform.position.z));
        var right = tiles.WorldToCell(new Vector3(Mathf.Max(from, to) + margin, groundY - SurfaceAnimalCollision.GroundTileProbeDepth, tiles.transform.position.z));
        if (left.y != right.y) return false;
        for (int x = left.x; x <= right.x; x++) if (!tiles.HasTile(new Vector3Int(x, left.y, 0))) return false;
        return true;
    }

    void Draw(Critter a)
    {
        float darkness = sky && sky.isActiveAndEnabled ? Mathf.Lerp(1, .48f, sky.NightBlend) : 1;
        Color tint = new Color(darkness, darkness, darkness, Mathf.Clamp01(a.age / .8f));
        float progress = a.jumping ? Mathf.Clamp01(a.travel / Mathf.Max(.1f, hopDuration)) : 0;
        float lift = Mathf.Sin(progress * Mathf.PI) * Mathf.Max(.1f, hopHeight);
        geometry.Begin(new Vector2(a.x, a.y), a.scale, a.direction, tint);
        geometry.Ellipse(0, .015f, .34f, .022f, new Color(.04f, .07f, .04f, .23f));
        geometry.Begin(new Vector2(a.x, a.y + lift), a.scale, a.direction, tint);
        if (species == Species.Frog) DrawFrog(a, progress); else DrawSnail(a);
    }

    void DrawFrog(Critter a, float progress)
    {
        Color skin = bodyColor * a.shade; skin.a = 1;
        Color edge = Color.Lerp(skin, new Color(.08f, .17f, .07f), .57f);
        Color light = Color.Lerp(skin, new Color(.64f, .85f, .20f), .28f);
        float stretch = Mathf.Sin(progress * Mathf.PI), breath = Mathf.Sin(a.age * 2 + a.phase) * .006f;
        geometry.Ellipse(-.22f, .13f, .15f, .10f, edge, -12);
        geometry.Stroke(-.20f, .12f, -.34f - stretch * .18f, .03f - stretch * .07f, .038f, skin);
        geometry.Ellipse(-.03f, .19f + breath, .30f + stretch * .04f, .177f - stretch * .025f, edge);
        geometry.Ellipse(-.025f, .205f + breath, .275f + stretch * .04f, .151f - stretch * .025f, skin);
        geometry.Ellipse(.11f, .13f, .19f, .074f, light);
        geometry.Ellipse(-.18f, .117f, .142f, .094f, edge, -25);
        geometry.Ellipse(-.176f, .13f, .122f, .073f, skin, -25);
        geometry.Stroke(-.18f, .07f, -.31f - stretch * .13f, .025f - stretch * .1f, .024f, light);
        geometry.Ellipse(.13f, .277f, .096f, .112f, edge);
        geometry.Ellipse(.14f, .288f, .075f, .094f, light);
        geometry.Ellipse(.26f, .20f, .12f, .087f, skin);
        geometry.Stroke(.16f, .15f, .22f - stretch * .09f, .039f, .027f, edge);
        geometry.Stroke(.17f, .15f, .23f - stretch * .09f, .05f, .017f, light);
        for (int i = 0; i < 3; i++) geometry.Stroke(.21f - stretch * .09f, .039f, .285f - stretch * .09f, .017f + i * .015f, .008f, skin);
        bool blink = Mathf.Repeat(a.age + a.phase, 5.1f) > 4.96f;
        geometry.Ellipse(.168f, .31f, .056f, blink ? .006f : .061f, new Color(.86f, .79f, .34f));
        geometry.Ellipse(.185f, .311f, .036f, blink ? .003f : .043f, new Color(.027f, .037f, .02f));
        if (!blink) geometry.Ellipse(.195f, .33f, .013f, .015f, Color.white);
        geometry.Stroke(.25f, .16f, .352f, .165f, .006f, edge);
        geometry.Ellipse(.341f, .218f, .007f, .009f, edge);
        geometry.Ellipse(-.17f, .255f, .034f, .019f, edge, 15);
        geometry.Ellipse(-.068f, .307f, .025f, .014f, edge, -10);
        geometry.Ellipse(-.24f, .194f, .024f, .014f, light);
    }

    void DrawSnail(Critter a)
    {
        Color skin = bodyColor * a.shade; skin.a = 1;
        Color edge = Color.Lerp(skin, new Color(.18f, .13f, .09f), .55f);
        Color shell = shellColor * a.shade; shell.a = 1;
        Color rim = Color.Lerp(shell, new Color(.15f, .07f, .035f), .65f);
        Color gold = Color.Lerp(shell, new Color(1, .79f, .42f), .55f);
        float wave = Mathf.Sin(a.age * 2.1f + a.phase), sway = Mathf.Sin(a.age * 1.2f + a.phase) * .035f;
        geometry.Ellipse(0, .069f, .42f, .064f, edge);
        geometry.Ellipse(.013f, .081f, .40f + wave * .008f, .049f, skin);
        geometry.Ellipse(.26f, .132f, .105f, .115f, skin, -17);
        geometry.Ellipse(.30f, .168f, .078f, .078f, Color.Lerp(skin, Color.white, .15f));
        geometry.Stroke(.269f, .205f, .25f + sway, .35f, .013f, edge);
        geometry.Stroke(.317f, .202f, .371f + sway, .325f, .015f, skin);
        geometry.Ellipse(.25f + sway, .353f, .025f, .028f, skin);
        geometry.Ellipse(.371f + sway, .33f, .028f, .03f, skin);
        geometry.Ellipse(.258f + sway, .357f, .009f, .012f, new Color(.045f, .033f, .02f));
        geometry.Ellipse(.38f + sway, .335f, .012f, .014f, new Color(.045f, .033f, .02f));
        geometry.Ellipse(.385f + sway, .34f, .004f, .005f, Color.white);
        geometry.Stroke(.325f, .14f, .39f, .161f, .007f, skin);
        geometry.Ellipse(-.077f, .259f, .249f, .232f, rim);
        geometry.Ellipse(-.079f, .271f, .225f, .207f, shell);
        geometry.Ellipse(-.13f, .328f, .162f, .135f, gold, -20);
        geometry.Ellipse(-.082f, .29f, .176f, .166f, shell);
        // A continuous spiral makes the shell read as a snail's house even at game scale.
        float oldX = -.068f, oldY = .283f;
        for (int i = 1; i <= 38; i++)
        {
            float t = i / 38f, angle = t * Mathf.PI * 3.7f, radius = t * .19f;
            float x = -.068f + Mathf.Cos(angle) * radius, y = .283f + Mathf.Sin(angle) * radius;
            geometry.Stroke(oldX, oldY, x, y, .009f, rim); oldX = x; oldY = y;
        }
        geometry.Ellipse(-.19f, .377f, .044f, .014f, new Color(1, .87f, .6f, .6f), 34);
    }

    float Random(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    float Range(Vector2 range) => Random(Mathf.Max(.1f, Mathf.Min(range.x, range.y)), Mathf.Max(.1f, Mathf.Max(range.x, range.y)));
    void OnDisable()
    {
        StopCroaking();
        populations.Remove(this);
        if (subscribedMap) subscribedMap.Generated -= ResetPopulation;
        subscribedMap = null; ClearAnimals(); geometry?.Dispose(); geometry = null;
    }
}
