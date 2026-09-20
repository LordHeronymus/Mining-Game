using UnityEngine;
using UnityEngine.Tilemaps;

public enum BlockType
{
    Stone = 0, 
    IronOre = 1,
    CopperOre = 2,
    SilverOre = 4,
    GoldOre = 3,
    Empty = 5,
    PlatinumOre = 6,
    Coal = 7,
    DiamondOre = 8,
    StoneLayer2 = 9,
    StoneLayer3 = 10,
    Dirt = 11,
    StoneLayer4 = 12
}

[CreateAssetMenu(fileName = "New BlockType", menuName = "Blocks/BlockType", order = 0)]
public class Block : ScriptableObject
{
    [Header("Basic Info")]
    public BlockType id;                
    public string displayName;          
    public ItemSO itemDrop = null;
    public SoundType digSound;
    public SoundType breakSound;

    [Header("Stats")]
    public float hardness = 1f;
    [HideInInspector] public int points = 0;
    public bool isSolid = true;         

    public bool IsStone => id == BlockType.Stone || id == BlockType.StoneLayer2 ||
        id == BlockType.StoneLayer3 || id == BlockType.StoneLayer4;

    [Header("Variants")]
    [Tooltip("Alle möglichen Tiles, aus denen zufällig gewählt wird.")]
    public TileBase[] variants;         

    [Header("Ore overlays")]
    public OreTile[] smallOre = new OreTile[0];
    public OreTile[] mediumOre = new OreTile[0];
    public OreTile[] richOre = new OreTile[0];

    public bool HasOreOverlays => smallOre != null && smallOre.Length > 0 &&
        mediumOre != null && mediumOre.Length > 0 && richOre != null && richOre.Length > 0;

    public OreTile[] GetOreVariants(OreRichness richness) => richness == OreRichness.Small
        ? smallOre : richness == OreRichness.Medium ? mediumOre : richOre;

    [Header("Vorkommen")]
    [InspectorName("Vorkommen aktiv")] public bool spawnWithNoise = false;
    [Range(0f, 100f), InspectorName("Erzgewicht")] public float oreFrequencyPercent = 3f;
    [Range(1, 100), InspectorName("Adergröße (Index)")] public int veinSizeIndex = 7;

    public float NoiseScale => 1f / Mathf.Clamp(veinSizeIndex, 1, 100);
    // Retain the serialized frequency field as the relative ore weight.
    public float OreWeight => OreFrequency * 100f;

    public float OreFrequency => float.IsNaN(oreFrequencyPercent) ? 0f : Mathf.Clamp01(oreFrequencyPercent / 100f);

    // Retained only to migrate existing assets without losing their former settings.
    [HideInInspector] public AnimationCurve rarityCurve;
    [HideInInspector] public float noiseScale = 0.05f;
    [HideInInspector] public int noiseSeedOffset = 0;
    [SerializeField, HideInInspector] int generationSettingsVersion;

    public bool MigrateGenerationSettings()
    {
        if (generationSettingsVersion >= 1) return false;
        if (rarityCurve != null && rarityCurve.length > 0)
        {
            float sum = 0;
            const int samples = 1000;
            for (int i = 0; i < samples; i++) sum += Mathf.Clamp01(rarityCurve.Evaluate((i + .5f) / samples));
            oreFrequencyPercent = Mathf.Round(sum / samples * 10000f) / 100f;
            veinSizeIndex = Mathf.Clamp(Mathf.RoundToInt(1f / Mathf.Max(.001f, noiseScale)), 1, 100);
        }
        generationSettingsVersion = 1;
        return true;
    }
}
