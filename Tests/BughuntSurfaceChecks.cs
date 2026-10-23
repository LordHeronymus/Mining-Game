using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Edit Mode checks for the surface fixes: a save during the fall animation keeps the regrowth timer,
// F1/GPS tree values reach standing trees, and the animal/tree-hover physics queries match the old
// *All queries (mask, distance, trigger rule) without allocating a new array on every call.
public static class BughuntSurfaceChecks
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> lines = new();
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); lines.Add("PASS " + label); }
    static FieldInfo Field(Type type, string name) => type.GetField(name, Any) ?? throw new Exception("Feld fehlt: " + type.Name + "." + name);
    static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
    static T Get<T>(object target, string name) => (T)Field(target.GetType(), name).GetValue(target);

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode erwartet.");
        lines.Clear();
        var created = new List<GameObject>();
        bool hitTriggers = Physics2D.queriesHitTriggers;
        string report = Path.GetFullPath("Temp/BughuntSurfaceChecks.txt");
        try
        {
            ForestChecks(created);
            PhysicsChecks(created);
            File.WriteAllText(report, string.Join("\n", lines) + "\n");
            return "PASS " + lines.Count + " checks";
        }
        catch (Exception exception)
        {
            lines.Add("FAIL " + exception.Message);
            File.WriteAllText(report, string.Join("\n", lines) + "\n");
            throw;
        }
        finally
        {
            Physics2D.queriesHitTriggers = hitTriggers;
            foreach (var item in created) if (item) Object.DestroyImmediate(item);
            Physics2D.SyncTransforms();
        }
    }

    static GameObject Temporary(List<GameObject> created, string name, bool active)
    {
        var item = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        item.SetActive(active);
        created.Add(item);
        return item;
    }

    static void ForestChecks(List<GameObject> created)
    {
        var forest = Temporary(created, "Bughunt forest probe", false).AddComponent<SurfaceTrees>();
        var trees = Get<HashSet<ChoppableTree>>(forest, "trees");
        Set(forest, "variants", Array.Empty<Sprite>());
        Set(forest, "regrowthSeconds", new Vector2(100f, 240f));
        Set(forest, "random", new System.Random(7));
        Set(forest, "nextGrowth", Time.time - 50f);

        var standing = Temporary(created, "Bughunt standing tree", false).AddComponent<ChoppableTree>();
        trees.Add(standing);
        var saved = forest.CaptureRunState();
        Check(saved.trees.Length == 1 && Mathf.Approximately(saved.regrowth, 0f), "Ohne fallenden Baum bleibt ein abgelaufener Nachwuchs-Timer 0");

        // C-surface-0: the last hit already paid out the wood; TreeFelled only runs when the fall ends.
        var falling = Temporary(created, "Bughunt falling tree", false).AddComponent<ChoppableTree>();
        Set(falling, "falling", true);
        trees.Add(falling);
        saved = forest.CaptureRunState();
        Check(saved.trees.Length == 1, "Fallender Baum wird weiterhin nicht gespeichert");
        Check(saved.regrowth >= 100f - .001f, "Speichern während des Fallens hält mindestens die kürzeste Nachwuchszeit (" + saved.regrowth + " s)");
        Set(forest, "nextGrowth", Time.time + 500f);
        saved = forest.CaptureRunState();
        Check(Mathf.Abs(saved.regrowth - 500f) < .1f, "Längerer laufender Timer bleibt beim Fallen erhalten");
        Set(forest, "regrowthSeconds", new Vector2(0f, 0f));
        Set(forest, "nextGrowth", Time.time - 50f);
        saved = forest.CaptureRunState();
        Check(Mathf.Approximately(saved.regrowth, 1f), "Nachwuchs-Minimum 0 wird wie ScheduleGrowth auf 1 s angehoben");
        Check(Get<System.Random>(forest, "random").NextDouble() == new System.Random(7).NextDouble(), "Speichern verbraucht keinen Zufallswert des Waldes");
        var roundTrip = JsonUtility.FromJson<SavedForest>(JsonUtility.ToJson(saved));
        Check(roundTrip.trees.Length == 1 && Mathf.Approximately(roundTrip.regrowth, saved.regrowth), "Spielstandformat SavedForest unverändert");
        trees.Remove(falling);

        // C-surface-2: F1/GPS yield and growth values reach a standing tree; run state stays.
        Set(standing, "woodMin", 10); Set(standing, "woodMax", 20); Set(standing, "maximumBonusWood", 10);
        Set(standing, "maximumSizeBonusRatio", .5f); Set(standing, "sizeBonusRatio", .4f);
        Set(standing, "growthRatioPerSecond", .001f); Set(standing, "health", 7f); Set(standing, "pendingWoodYield", 33);
        Set(forest, "woodYieldMin", 50); Set(forest, "woodYieldMax", 60); Set(forest, "maximumBonusWood", 5);
        Set(forest, "maximumSizeBonusPercent", 20f); Set(forest, "growthSpeedPercentPerMinute", 60f);
        Set(forest, "random", new System.Random(11));
        int capacity = forest.Capacity;
        var apply = typeof(SurfaceTrees).GetMethod("ApplySettings", Any, null, Type.EmptyTypes, null);
        Check(apply != null, "SurfaceTrees.ApplySettings vorhanden (GPS-Hook)");
        apply.Invoke(forest, null);
        Check(standing.MinimumWoodYield == 55 && standing.MaximumWoodYield == 65,
            "Neuer Holzertrag gilt für stehenden Baum (" + standing.MinimumWoodYield + "-" + standing.MaximumWoodYield + ")");
        Check(Mathf.Approximately(Get<float>(standing, "maximumSizeBonusRatio"), .2f) && Mathf.Approximately(standing.GrowthFactor, 1.2f),
            "Gesenkter Grössenbonus begrenzt den Wuchs des stehenden Baums");
        Check(Mathf.Approximately(Get<float>(standing, "growthRatioPerSecond"), .01f), "Neue Wachstumsgeschwindigkeit gilt für stehenden Baum");
        Check(standing.Health == 7 && Get<int>(standing, "pendingWoodYield") == 33, "Treffer und bereits gewürfelter Ertrag bleiben Laufzustand");
        Check(forest.ActiveCount == 1 && forest.Capacity == capacity, "Ohne erzeugte Karte bleibt der Wald unverändert");
        Check(Get<System.Random>(forest, "random").NextDouble() == new System.Random(11).NextDouble(), "GPS-Übernahme verbraucht keinen Zufallswert");

        var map = Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
        if (map && map.IsGenerated && map.Terrain)
        {
            Set(forest, "map", map);
            Set(forest, "maximumTrees", 0);
            apply.Invoke(forest, null);
            Check(forest.Capacity == 0 && forest.ActiveCount == 1, "Gesenkte maximale Anzahl wirkt sofort, ohne Bäume zu entfernen");
            Set(forest, "maximumTrees", 1);
            apply.Invoke(forest, null);
            Check(forest.Capacity <= 1, "Maximale Anzahl begrenzt die Zielgrösse des Waldes");
        }
        else lines.Add("PASS Zielgrösse des Waldes: keine erzeugte Karte in der Szene, Teilprüfung entfällt");
    }

    // The pre-fix queries, kept here as the reference the buffered versions must match.
    static bool ReferenceGround(MapGenerator map, float x, float fromY, float distance, float width, float height, out float groundY)
    {
        groundY = fromY;
        var hits = Physics2D.BoxCastAll(new Vector2(x, fromY + height * .5f + .02f), new Vector2(width, height), 0f,
            Vector2.down, distance + .02f, 1 << map.gameObject.layer);
        float nearest = float.PositiveInfinity;
        foreach (var hit in hits)
            if (hit.collider && hit.normal.y >= .5f && hit.distance < nearest) nearest = hit.distance;
        if (float.IsPositiveInfinity(nearest)) return false;
        groundY = fromY + .02f - nearest;
        return true;
    }

    static bool ReferenceMove(MapGenerator map, float fromX, float toX, float feetY, float width, float height)
    {
        float distance = Mathf.Abs(toX - fromX);
        var direction = toX > fromX ? Vector2.right : Vector2.left;
        foreach (var hit in Physics2D.BoxCastAll(new Vector2(fromX, feetY + height * .5f + .02f), new Vector2(width, height), 0f,
            direction, distance, 1 << map.gameObject.layer))
            if (hit.collider && Vector2.Dot(hit.normal, direction) < -.5f && hit.distance < distance - .02f) return false;
        return true;
    }

    static bool ReferenceRise(MapGenerator map, float x, float fromY, float toY, float width, float height)
    {
        float distance = toY - fromY;
        foreach (var hit in Physics2D.BoxCastAll(new Vector2(x, fromY + height * .5f + .02f), new Vector2(width, height), 0f,
            Vector2.up, distance, 1 << map.gameObject.layer))
            if (hit.collider && hit.normal.y < -.5f && hit.distance < distance - .02f) return false;
        return true;
    }

    static ChoppableTree ReferenceAt(Vector2 point)
    {
        foreach (var collider in Physics2D.OverlapPointAll(point))
        {
            var tree = collider.GetComponentInParent<ChoppableTree>();
            if (tree && tree.CanChop) return tree;
        }
        return null;
    }

    static BoxCollider2D Box(List<GameObject> created, string name, int layer, Vector2 center, Vector2 size, bool trigger = false)
    {
        var item = Temporary(created, name, true);
        item.layer = layer;
        item.transform.position = center;
        var box = item.AddComponent<BoxCollider2D>();
        box.size = size;
        box.isTrigger = trigger;
        return box;
    }

    static void PhysicsChecks(List<GameObject> created)
    {
        var map = Object.FindFirstObjectByType<MapGenerator>(FindObjectsInactive.Include);
        Check(map, "MapGenerator in der geöffneten Szene");
        int layer = map.gameObject.layer;
        const float X = 2000f, Y = 2000f;
        Physics2D.queriesHitTriggers = true;
        Check(Physics2D.OverlapBoxAll(new Vector2(X + 20f, Y), new Vector2(120f, 60f), 0f).Length == 0, "Testbereich ohne fremde Collider");

        Box(created, "Bughunt ground", layer, new Vector2(X, Y - .5f), new Vector2(4f, 1f));
        Box(created, "Bughunt wall", layer, new Vector2(X + 1.5f, Y + .5f), new Vector2(.2f, 1f));
        Box(created, "Bughunt trigger wall", layer, new Vector2(X - 1.5f, Y + .5f), new Vector2(.2f, 1f), true);
        Box(created, "Bughunt ceiling", layer, new Vector2(X - 3f, Y + 1.5f), new Vector2(2f, .2f));
        for (int i = 0; i < 40; i++)
            Box(created, "Bughunt slab " + i, layer, new Vector2(X + 20f, Y - i * .3f), new Vector2(2f, .1f));
        var treeRoot = Temporary(created, "Bughunt hover tree", true);
        treeRoot.transform.position = new Vector3(X + 40f, Y, 0f);
        var trunkObject = new GameObject("Trunk") { hideFlags = HideFlags.HideAndDontSave };
        trunkObject.transform.SetParent(treeRoot.transform, false);
        var trunk = trunkObject.AddComponent<BoxCollider2D>();
        trunk.size = new Vector2(1f, 2f); trunk.isTrigger = true;
        var tree = treeRoot.AddComponent<ChoppableTree>();
        Set(tree, "trunk", trunk); Set(tree, "health", 5f);
        // Clutter around the hover point overflows the 8-slot buffer; the lower trunk stays clear for the allocation probe.
        for (int i = 0; i < 20; i++) Box(created, "Bughunt hover clutter " + i, 0, new Vector2(X + 40f, Y + .9f), new Vector2(.5f, .5f));
        Physics2D.SyncTransforms();

        foreach (bool triggers in new[] { true, false })
        {
            Physics2D.queriesHitTriggers = triggers;
            string mode = triggers ? " (Trigger an)" : " (Trigger aus)";
            bool grounded = SurfaceAnimalCollision.TryGround(map, X, Y + .3f, 1f, .5f, .4f, out float groundY);
            bool reference = ReferenceGround(map, X, Y + .3f, 1f, .5f, .4f, out float referenceY);
            Check(grounded && reference && Mathf.Approximately(groundY, referenceY), "TryGround findet den Boden wie BoxCastAll" + mode);
            Check(!SurfaceAnimalCollision.TryGround(map, X + 10f, Y + .3f, 1f, .5f, .4f, out _) && !ReferenceGround(map, X + 10f, Y + .3f, 1f, .5f, .4f, out _),
                "TryGround ohne Boden bleibt erfolglos" + mode);
            grounded = SurfaceAnimalCollision.TryGround(map, X + 20f, Y + .3f, 20f, .5f, .4f, out groundY);
            reference = ReferenceGround(map, X + 20f, Y + .3f, 20f, .5f, .4f, out referenceY);
            Check(grounded && reference && Mathf.Approximately(groundY, referenceY),
                "TryGround mit 40 Treffern wie BoxCastAll" + mode);
            Check(!SurfaceAnimalCollision.CanMove(map, X, X + 2f, Y, .5f, .4f) && !ReferenceMove(map, X, X + 2f, Y, .5f, .4f), "CanMove wird von der Wand gestoppt" + mode);
            Check(SurfaceAnimalCollision.CanMove(map, X, X + .5f, Y, .5f, .4f) && ReferenceMove(map, X, X + .5f, Y, .5f, .4f), "CanMove vor der Wand frei" + mode);
            bool towardTrigger = SurfaceAnimalCollision.CanMove(map, X, X - 2f, Y, .5f, .4f);
            Check(towardTrigger == ReferenceMove(map, X, X - 2f, Y, .5f, .4f) && towardTrigger == !triggers,
                "CanMove behandelt Trigger wie Physics2D.queriesHitTriggers" + mode);
            Check(!SurfaceAnimalCollision.CanRise(map, X - 3f, Y, Y + 2f, .5f, .4f) && !ReferenceRise(map, X - 3f, Y, Y + 2f, .5f, .4f), "CanRise wird von der Decke gestoppt" + mode);
            Check(SurfaceAnimalCollision.CanRise(map, X, Y, Y + 2f, .5f, .4f) && ReferenceRise(map, X, Y, Y + 2f, .5f, .4f), "CanRise ohne Decke frei" + mode);
            var hover = new Vector2(X + 40f, Y + .9f);
            Check(ChoppableTree.At(hover) == ReferenceAt(hover) && ChoppableTree.At(hover) == (triggers ? tree : null),
                "Baum-Hover findet den Trigger-Stamm wie OverlapPointAll, auch zwischen 20 weiteren Collidern" + mode);
        }
        Physics2D.queriesHitTriggers = true;
        Check(ChoppableTree.At(new Vector2(X + 40f, Y - .9f)) == tree, "Baum-Hover am freien unteren Stamm");
        Check(Field(typeof(ChoppableTree), "pointHits").GetValue(null) is Collider2D[] points && points.Length > 20, "Hover-Puffer wächst statt Collider abzuschneiden");
        Check(Field(typeof(SurfaceAnimalCollision), "hits").GetValue(null) is RaycastHit2D[] buffer && buffer.Length > 40, "Trefferpuffer der Tiere wächst statt Treffer abzuschneiden");
        Set(tree, "falling", true);
        Check(ChoppableTree.At(new Vector2(X + 40f, Y + .9f)) == null, "Fallender Baum ist nicht mehr anwählbar");
        Set(tree, "falling", false);

        void Queries()
        {
            SurfaceAnimalCollision.TryGround(map, X, Y + .3f, 1f, .5f, .4f, out _);
            SurfaceAnimalCollision.CanMove(map, X, X + 2f, Y, .5f, .4f);
            SurfaceAnimalCollision.CanRise(map, X - 3f, Y, Y + 2f, .5f, .4f);
            ChoppableTree.At(new Vector2(X + 40f, Y - .9f));
        }
        void OldQueries()
        {
            Physics2D.BoxCastAll(new Vector2(X, Y + .52f), new Vector2(.5f, .4f), 0f, Vector2.down, 1.02f, 1 << layer);
            Physics2D.BoxCastAll(new Vector2(X, Y + .22f), new Vector2(.5f, .4f), 0f, Vector2.right, 2f, 1 << layer);
            Physics2D.BoxCastAll(new Vector2(X - 3f, Y + .22f), new Vector2(.5f, .4f), 0f, Vector2.up, 2f, 1 << layer);
            Physics2D.OverlapPointAll(new Vector2(X + 40f, Y - .9f));
        }
        Queries(); OldQueries();
        long oldBytes = Allocated(OldQueries, out string method);
        if (oldBytes <= 0) { lines.Add("PASS Allokationsmessung (" + method + ") nicht aussagekräftig, Teilprüfung entfällt"); return; }
        long newBytes = Allocated(Queries, out _);
        Check(newBytes * 20 < oldBytes, "Tier- und Baum-Hover-Abfragen ohne neue Arrays (" + method + ": " + newBytes + " statt " + oldBytes + " Bytes)");
    }

    // Bytes allocated by many repetitions; the precise per-thread counter when Mono provides it, otherwise
    // the used heap size, repeated when a collection interferes.
    static long Allocated(Action body, out string method)
    {
        long start = GC.GetAllocatedBytesForCurrentThread();
        var probe = new byte[4096];
        bool precise = GC.GetAllocatedBytesForCurrentThread() - start >= probe.Length;
        method = precise ? "GC-Zähler" : "Heap-Grösse";
        int repetitions = precise ? 200 : 4000;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int collections = GC.CollectionCount(0);
            long before = precise ? GC.GetAllocatedBytesForCurrentThread() : UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < repetitions; i++) body();
            long after = precise ? GC.GetAllocatedBytesForCurrentThread() : UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            if (GC.CollectionCount(0) == collections) return Math.Max(0, after - before);
        }
        return -1;
    }
}
