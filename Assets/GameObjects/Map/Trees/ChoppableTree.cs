using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class ChoppableTree : MonoBehaviour
{
    [SerializeField] SpriteRenderer visual;
    [SerializeField] BoxCollider2D trunk;
    [SerializeField] ParticleSystem splinters;
    [SerializeField] ParticleSystem leaves;
    static readonly int SwayBaseY = Shader.PropertyToID("_SwayBaseY");
    static readonly int SwayHeight = Shader.PropertyToID("_SwayHeight");
    static readonly int SwayPhase = Shader.PropertyToID("_SwayPhase");
    static readonly int SwayFrequency = Shader.PropertyToID("_SwayFrequency");
    static readonly int SwayImpact = Shader.PropertyToID("_SwayImpact");
    static readonly int SwayStrength = Shader.PropertyToID("_SwayStrength");
    static readonly int ReachGlow = Shader.PropertyToID("_ReachGlow");
    static readonly List<ChoppableTree> activeTrees = new();
    public static IReadOnlyList<ChoppableTree> ActiveTrees => activeTrees;
    SurfaceTrees owner;
    ItemSO wood;
    Sprite swaySprite;
    Vector2[] pendingSwayVertices;
    ushort[] pendingSwayTriangles;
    MaterialPropertyBlock swayProperties;
    float health;
    int woodMin, woodMax, maximumBonusWood;
    float shake;
    float fullScale;
    float fullHeight;
    float spriteHeight;
    float lastImpact;
    float sizeBonusRatio;
    float maximumSizeBonusRatio;
    float growthRatioPerSecond;
    bool sprouting;
    bool falling;
    bool birchWood;
    int reachGlowLevel;

    public int SurfaceCellX { get; private set; }
    public int Health => Mathf.CeilToInt(health);
    public bool CanChop => health > 0 && !falling && trunk && trunk.enabled;
    public Vector2 HitPoint => (Vector2)transform.position + Vector2.up;
    float Maturity => maximumSizeBonusRatio > 0f
        ? Mathf.Clamp01(sizeBonusRatio / maximumSizeBonusRatio) : 0f;
    public float GrowthFactor => 1f + sizeBonusRatio;
    public int MinimumWoodYield => Mathf.RoundToInt(Mathf.Lerp(woodMin,
        woodMin + maximumBonusWood, Maturity));
    public int MaximumWoodYield => Mathf.RoundToInt(Mathf.Lerp(woodMax,
        woodMax + maximumBonusWood, Maturity));

    void OnEnable()
    {
        if (Application.isPlaying && !activeTrees.Contains(this)) activeTrees.Add(this);
    }

    void OnDisable() => activeTrees.Remove(this);

    public void SetReachGlow(int level)
    {
        level = CanChop ? Mathf.Clamp(level, 0, 2) : 0;
        if (reachGlowLevel == level || swayProperties == null) return;
        reachGlowLevel = level;
        swayProperties.SetFloat(ReachGlow, level);
        visual.SetPropertyBlock(swayProperties);
    }

    public void Initialize(SurfaceTrees forest, int surfaceCellX, Sprite sprite,
        ItemSO woodItem, Vector3 position, float height, int hitPoints,
        int minWood, int maxWood, int bonusWood, float bonusSizePercent,
        float growthPercentPerMinute, bool grown)
    {
        owner = forest;
        SurfaceCellX = surfaceCellX;
        wood = woodItem;
        health = Mathf.Max(1, hitPoints);
        woodMin = Mathf.Clamp(minWood, 1, 9999);
        woodMax = Mathf.Clamp(maxWood, woodMin, 9999);
        maximumBonusWood = Mathf.Clamp(bonusWood, 0, 9999 - woodMax);
        maximumSizeBonusRatio = Mathf.Max(0f, bonusSizePercent) / 100f;
        growthRatioPerSecond = Mathf.Max(0f, growthPercentPerMinute) / 6000f;
        sizeBonusRatio = 0f;
        birchWood = sprite.name.Contains("_Birke_");
        transform.position = position;
        SetupSwaySprite(sprite);
        fullHeight = height;
        fullScale = height / (sprite.rect.height / sprite.pixelsPerUnit);
        transform.localScale = Vector3.one * fullScale;
        trunk.offset = new Vector2(0, 1.15f / fullScale);
        trunk.size = new Vector2(1.15f / fullScale, 2.3f / fullScale);
        if (!grown) StartCoroutine(Grow());
    }

    void SetupSwaySprite(Sprite source)
    {
        var pivot = new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
        swaySprite = Sprite.Create(source.texture, source.rect, pivot, source.pixelsPerUnit, 0,
            SpriteMeshType.FullRect, source.border);
        swaySprite.name = source.name + " Sway";
        const int rows = 16;
        var vertices = new Vector2[(rows + 1) * 2];
        var triangles = new ushort[rows * 6];
        for (int row = 0; row <= rows; row++)
        {
            float y = source.rect.height * row / rows;
            vertices[row * 2] = new Vector2(0, y);
            vertices[row * 2 + 1] = new Vector2(source.rect.width, y);
            if (row == rows) continue;
            int vertex = row * 2, index = row * 6;
            triangles[index] = (ushort)vertex;
            triangles[index + 1] = (ushort)(vertex + 2);
            triangles[index + 2] = (ushort)(vertex + 1);
            triangles[index + 3] = (ushort)(vertex + 1);
            triangles[index + 4] = (ushort)(vertex + 2);
            triangles[index + 5] = (ushort)(vertex + 3);
        }
        pendingSwayVertices = vertices;
        pendingSwayTriangles = triangles;
        visual.sprite = swaySprite;
        spriteHeight = source.rect.height / source.pixelsPerUnit;
        swayProperties = new MaterialPropertyBlock();
        visual.GetPropertyBlock(swayProperties);
        swayProperties.SetFloat(SwayBaseY, -source.pivot.y / source.pixelsPerUnit);
        swayProperties.SetFloat(SwayHeight, spriteHeight);
        uint swaySeed = (uint)SurfaceCellX * 747796405u + 2891336453u;
        swaySeed = (swaySeed ^ (swaySeed >> 16)) * 2246822519u;
        swaySeed ^= swaySeed >> 13;
        swayProperties.SetFloat(SwayPhase, (swaySeed & 0xffffu) / 65535f * Mathf.PI * 2f);
        swayProperties.SetFloat(SwayFrequency, Mathf.Lerp(.85f, 1.15f,
            ((swaySeed >> 16) & 0xffffu) / 65535f));
        swayProperties.SetFloat(SwayImpact, 0f);
        swayProperties.SetFloat(SwayStrength, 1f);
        swayProperties.SetFloat(ReachGlow, 0f);
        visual.SetPropertyBlock(swayProperties);
    }

    void OnDestroy()
    {
        if (swaySprite) Destroy(swaySprite);
    }

    void Update()
    {
        if (pendingSwayVertices != null)
        {
            swaySprite.OverrideGeometry(pendingSwayVertices, pendingSwayTriangles);
            pendingSwayVertices = null;
            pendingSwayTriangles = null;
        }
        AdvanceGrowth(Time.deltaTime);
    }

    void AdvanceGrowth(float seconds)
    {
        if (sprouting || falling) return;
        if (sizeBonusRatio < maximumSizeBonusRatio)
            sizeBonusRatio = Mathf.Min(maximumSizeBonusRatio,
                sizeBonusRatio + Mathf.Max(0f, seconds) * growthRatioPerSecond);
        float scale = fullScale * GrowthFactor;
        if (!Mathf.Approximately(transform.localScale.x, scale))
            transform.localScale = Vector3.one * scale;
    }

    public void Hit(Vector2 attackerPosition)
    {
        if (!CanChop) return;
        health = Mathf.Max(0f, health - (owner ? owner.HitDamage : 1f));
        EmitSplinters();
        AudioManager.Instance?.Play(SoundType.WoodChop, true);
        shake = .13f;
        if (health <= 0f) StartCoroutine(Fall(attackerPosition.x < transform.position.x ? -1 : 1));
    }

    void EmitSplinters()
    {
        if (!splinters) return;
        Color dark = birchWood ? new Color(.82f, .73f, .57f) : new Color(.55f, .25f, .07f);
        Color light = birchWood ? new Color(1f, .96f, .81f) : new Color(1f, .73f, .33f);
        for (int i = 0; i < 18; i++)
        {
            var shard = new ParticleSystem.EmitParams
            {
                position = HitPoint + new Vector2(Random.Range(-.14f, .14f), Random.Range(-.18f, .18f)),
                velocity = new Vector3(Random.Range(-2.7f, 2.7f), Random.Range(1f, 3.6f), 0),
                startSize = Random.Range(.22f, .38f),
                startLifetime = Random.Range(.65f, 1.05f),
                startColor = Color.Lerp(dark, light, Random.value),
                rotation = Random.Range(0f, 360f)
            };
            splinters.Emit(shard, 1);
        }
    }

    void LateUpdate()
    {
        if (falling || !visual || swayProperties == null) return;
        float impact = 0f;
        if (shake > 0)
        {
            shake = Mathf.Max(0f, shake - Time.deltaTime);
            impact = Mathf.Sin((.13f - shake) * 75f) * (shake / .13f) * spriteHeight * .045f;
        }
        if (Mathf.Approximately(lastImpact, impact)) return;
        lastImpact = impact;
        swayProperties.SetFloat(SwayImpact, impact);
        visual.SetPropertyBlock(swayProperties);
    }

    IEnumerator Grow()
    {
        sprouting = true;
        trunk.enabled = false;
        float duration = 10f, elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float scale = Mathf.SmoothStep(.15f, 1f, Mathf.Clamp01(elapsed / duration));
            transform.localScale = Vector3.one * fullScale * scale;
            yield return null;
        }
        transform.localScale = Vector3.one * fullScale;
        trunk.enabled = true;
        sprouting = false;
    }

    IEnumerator Fall(int direction)
    {
        falling = true;
        AudioManager.Instance?.Play(SoundType.TreeFall);
        trunk.enabled = false;
        SetReachGlow(0);
        visual.transform.localRotation = Quaternion.identity;
        if (swayProperties != null)
        {
            swayProperties.SetFloat(SwayStrength, 0f);
            swayProperties.SetFloat(SwayImpact, 0f);
            visual.SetPropertyBlock(swayProperties);
        }
        if (leaves)
        {
            leaves.transform.SetParent(null, true);
            leaves.transform.localScale = Vector3.one;
            Destroy(leaves.gameObject, 4f);
            EmitLeaves(28);
        }
        float elapsed = 0, duration = owner ? owner.FallDurationSeconds : .9f;
        float nextLeaves = .08f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float rotationProgress = owner ? owner.FallRotationProgress(t) : t * t;
            transform.rotation = Quaternion.Euler(0, 0, direction * -82f * rotationProgress);
            if (elapsed >= nextLeaves)
            {
                EmitLeaves(9);
                nextLeaves += .09f;
            }
            if (t > .7f) visual.color = new Color(1, 1, 1, 1 - (t - .7f) / .3f);
            yield return null;
        }
        InventoryManager.Instance?.Add(wood, Random.Range(MinimumWoodYield, MaximumWoodYield + 1));
        owner?.TreeFelled(this);
        Destroy(gameObject);
    }

    void EmitLeaves(int count)
    {
        if (!leaves) return;
        Vector3 crown = transform.TransformPoint(Vector3.up * (fullHeight * .72f / fullScale));
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            var outward = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
            var leaf = new ParticleSystem.EmitParams
            {
                position = crown + new Vector3(Random.Range(-1.3f, 1.3f), Random.Range(-.8f, .8f), 0),
                velocity = outward * Random.Range(.6f, 2f) + new Vector3(0, Random.Range(.5f, 2.3f), 0),
                startSize = Random.Range(.22f, .38f),
                startLifetime = Random.Range(1.5f, 2.8f),
                startColor = Color.Lerp(new Color(.36f, .55f, .16f),
                    new Color(.85f, .65f, .21f), Random.value),
                rotation = Random.Range(0f, 360f)
            };
            leaves.Emit(leaf, 1);
        }
    }

    public static ChoppableTree At(Vector2 point)
    {
        foreach (var collider in Physics2D.OverlapPointAll(point))
        {
            var tree = collider.GetComponentInParent<ChoppableTree>();
            if (tree && tree.CanChop) return tree;
        }
        return null;
    }
}
