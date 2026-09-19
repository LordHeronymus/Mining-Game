using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceRabbitSpawner : MonoBehaviour
{
    [InspectorName("Häschen-Einstellungen")] public SurfaceRabbit settings;
    [InspectorName("Spieler")] public Transform player;
    [InspectorName("Spawnintervall (s)")] public Vector2 spawnInterval = new Vector2(12,22);
    [Range(1,20), InspectorName("Maximale Anzahl")] public int maxRabbits = 4;
    [Min(1), InspectorName("Despawn-Abstand")] public float despawnDistance = 30;
    [Min(.1f), InspectorName("Despawn nach Entfernung (s)")] public float despawnDelay = 12;

    sealed class Resident
    {
        public SurfaceRabbit rabbit;
        public float distantTime;
    }
    const string SpawnedName = "Rabbit (spawned)";
    readonly List<Resident> residents = new List<Resident>();
    readonly System.Random random = new System.Random();
    MapGenerator subscribedMap;
    float nextSpawn, playerRetry;
    public int ActiveCount => residents.Count;

    void OnEnable()
    {
        ClearRabbits(); nextSpawn = playerRetry = 0;
        if (settings)
        {
            settings.enabled = false;
            subscribedMap = settings.map;
            if (subscribedMap) subscribedMap.Generated += OnMapGenerated;
        }
    }

    void Update() => Tick(Time.deltaTime);

    void Tick(float deltaTime)
    {
        if (!player)
        {
            playerRetry -= deltaTime;
            if (playerRetry <= 0)
            {
                var found = FindFirstObjectByType<PlayerMovement>();
                if (found) player = found.transform;
                playerRetry = 1;
            }
            if (!player) return;
        }
        float distance = Mathf.Max(1,despawnDistance);
        for (int i = residents.Count-1; i >= 0; i--)
        {
            var resident = residents[i];
            if (!resident.rabbit) { residents.RemoveAt(i); continue; }
            bool distant = ((Vector2)(resident.rabbit.transform.position-player.position)).sqrMagnitude > distance*distance;
            resident.distantTime = distant ? resident.distantTime+deltaTime : 0;
            if (resident.distantTime < Mathf.Max(.1f,despawnDelay)) continue;
            Remove(resident.rabbit.gameObject); residents.RemoveAt(i);
        }
        nextSpawn = Mathf.Max(0,nextSpawn-deltaTime);
        if (nextSpawn > 0 || residents.Count >= Mathf.Clamp(maxRabbits,1,20)) return;
        if (!settings || !settings.map || !settings.map.IsGenerated || !settings.material) return;
        var camera = Camera.main;
        if (!camera) return;
        float groundY = settings.map.Terrain.CellToWorld(new Vector3Int(0,1,0)).y;
        var view = camera.WorldToViewportPoint(new Vector3(player.position.x,groundY,settings.transform.position.z));
        // Spawn only near the player while the surface is in view, never deep underground.
        if (Mathf.Abs(player.position.y-groundY) > distance || view.z <= 0 || view.x < 0 || view.x > 1 || view.y < 0 || view.y > 1) return;
        nextSpawn = 2; // Retry a blocked entrance without accumulating invisible rabbits.
        var child = new GameObject(SpawnedName) { hideFlags = HideFlags.DontSave };
        child.SetActive(false); child.layer = settings.gameObject.layer;
        child.transform.SetParent(transform,false);
        child.transform.position = new Vector3(player.position.x,groundY,settings.transform.position.z);
        var rabbit = child.AddComponent<SurfaceRabbit>();
        rabbit.CopySettingsFrom(settings);
        child.SetActive(true);
        // OnEnable creates the geometry and resets transient state, so initialize its entrance afterwards.
        if (!rabbit.TrySpawn() || ((Vector2)(rabbit.transform.position-player.position)).sqrMagnitude > distance*distance)
        {
            Remove(child); return;
        }
        residents.Add(new Resident { rabbit = rabbit });
        float min = Mathf.Max(1,Mathf.Min(spawnInterval.x,spawnInterval.y));
        float max = Mathf.Max(min,Mathf.Max(spawnInterval.x,spawnInterval.y));
        nextSpawn = Mathf.Lerp(min,max,(float)random.NextDouble());
    }

    void OnMapGenerated() { ClearRabbits(); nextSpawn = 0; }

    void ClearRabbits()
    {
        residents.Clear();
        // Include transient children whose managed references were lost during a script reload.
        for (int i = transform.childCount-1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == SpawnedName && child.GetComponent<SurfaceRabbit>() &&
                (child.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave) Remove(child.gameObject);
        }
    }

    void OnDisable()
    {
        if (subscribedMap) subscribedMap.Generated -= OnMapGenerated;
        subscribedMap = null; ClearRabbits();
    }

    static void Remove(GameObject child)
    {
        child.SetActive(false);
        if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
    }
}
