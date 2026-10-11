using UnityEngine;

// Keeps the selected level-up artwork and clip available in player builds.
public sealed class LevelUpPresentationAssets : ScriptableObject
{
    public Sprite medallion;
    public AudioClip sound;
    public float impactSeconds = .16f;
}
