using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent, RequireComponent(typeof(Tilemap), typeof(MapGenerator))]
public sealed class BlockBreakParticles : MonoBehaviour
{
    public Material debrisMaterial;
    public Material dustMaterial;
    public Material oreMaterial;
    [Range(1, 24)] public int fragmentsPerBlock = 9;
    [Range(0, 16)] public int dustPerBlock = 5;
    [Min(.1f)] public float burstSpeed = 1.4f;
    [Min(.1f)] public float duration = .65f;
    public Color stoneColor = new Color(.65f, .63f, .58f);
    [Range(1f, 3f), InspectorName("Erzsplitter-Grössenfaktor")]
    public float oreFragmentSizeMultiplier = 1.5f;

    Tilemap tiles;
    MapGenerator map;
    ParticleSystem debris, dust, oreDebris;
    readonly System.Random random = new System.Random();

    void OnEnable()
    {
        tiles = GetComponent<Tilemap>();
        map = GetComponent<MapGenerator>();
        if (debrisMaterial) debris = CreateSystem("Block fragments", debrisMaterial, 192, false);
        if (dustMaterial) dust = CreateSystem("Block dust", dustMaterial, 96, true);
        if (oreMaterial) oreDebris = CreateSystem("Ore fragments", oreMaterial, 192, false);
        TileMiner.OnBlockMined += OnBlockMined;
    }

    ParticleSystem CreateSystem(string name, Material material, int capacity, bool smoke)
    {
        var child = new GameObject(name + " (generated)");
        child.hideFlags = HideFlags.DontSave;
        child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        var system = child.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.playOnAwake = false; main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = capacity; main.startSpeed = 0;
        main.gravityModifier = smoke ? .03f : .45f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = system.emission; emission.enabled = false;
        var shape = system.shape; shape.enabled = false;
        var fade = system.colorOverLifetime; fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(0,1)});
        fade.color = gradient;
        var scale = system.sizeOverLifetime; scale.enabled = true;
        scale.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, smoke ? .5f : 1, 1, smoke ? 1.5f : .4f));
        var rotation = system.rotationOverLifetime; rotation.enabled = !smoke;
        rotation.z = new ParticleSystem.MinMaxCurve(-5,5);
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        // Keep debris below the map's darkness overlay, unlike the self-visible ore glints.
        renderer.sortingLayerID = SortingLayer.layers[SortingLayer.layers.Length-1].id;
        renderer.sortingOrder = smoke ? 32757 : 32758;
        system.Play();
        return system;
    }

    void OnBlockMined(Vector2 position, int points)
    {
        // TileMiner raises this before removing the tile.
        if (!map.registry) return;
        var cell = tiles.WorldToCell(position);
        var block = map.GetBlockAt(cell);
        if (!block || (!block.IsStone && block.id != BlockType.Dirt && !OreSparkles.IsOre(block))) return;
        bool isOre = OreSparkles.IsOre(block);
        Color tint = isOre ? OreSparkles.GetOreColor(block.id) : block.id == BlockType.Dirt ? new Color(.38f,.24f,.13f) : stoneColor;
        var camera = Camera.main;
        if (!camera) return;
        Vector3 center = tiles.GetCellCenterWorld(cell);
        var view = camera.WorldToViewportPoint(center);
        if (view.z <= 0 || view.x < 0 || view.x > 1 || view.y < 0 || view.y > 1) return;
        float cellSize = Mathf.Min(
            tiles.transform.TransformVector(Vector3.right * tiles.layoutGrid.cellSize.x).magnitude,
            tiles.transform.TransformVector(Vector3.up * tiles.layoutGrid.cellSize.y).magnitude);
        Burst(isOre && oreDebris ? oreDebris : debris, center, cellSize, Mathf.Clamp(fragmentsPerBlock,1,24), false, tint,
            isOre ? Mathf.Clamp(oreFragmentSizeMultiplier, 1f, 3f) : 1f);
        Burst(dust, center, cellSize, Mathf.Clamp(dustPerBlock,0,16), true,
            isOre ? Color.Lerp(stoneColor, tint, .3f) : tint, 1f);
    }

    float Range(float min, float max) => Mathf.Lerp(min,max,(float)random.NextDouble());

    void Burst(ParticleSystem system, Vector3 center, float cellSize, int count, bool smoke, Color tint, float sizeMultiplier)
    {
        if (!system) return;
        count = Mathf.Min(count, system.main.maxParticles-system.particleCount);
        for(int i=0;i<count;i++)
        {
            float angle = Range(0,Mathf.PI*2);
            var direction = new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0);
            Color color = tint * Range(.8f,1.15f);
            color.a = tint.a * (smoke ? .28f : 1f);
            system.Emit(new ParticleSystem.EmitParams {
                position = center + direction * cellSize * Range(.05f,.2f),
                velocity = (direction + Vector3.up * .35f) * Mathf.Max(.1f,burstSpeed) * Range(.4f,1f) * (smoke ? .25f : 1f),
                startSize = cellSize * sizeMultiplier * (smoke ? Range(.35f,.75f) : Range(.08f,.18f)),
                startLifetime = Mathf.Max(.1f,duration) * Range(.75f,1.25f) * (smoke ? 1.3f : 1f),
                rotation = Range(0,360), startColor = color
            },1);
        }
    }

    void OnDisable()
    {
        TileMiner.OnBlockMined -= OnBlockMined;
        Release(debris); Release(dust); Release(oreDebris); debris = dust = oreDebris = null;
    }

    static void Release(ParticleSystem system)
    {
        if (!system) return;
        system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(Application.isPlaying)Destroy(system.gameObject);else DestroyImmediate(system.gameObject);
    }
}
