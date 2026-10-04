using UnityEngine;

// Keeps the original clip reference (and its GPS tuning/GUID) available in player builds.
public sealed class LevelUpPresentationAssets : ScriptableObject
{
    public Sprite medallion;
    public AudioClip sound;
    public float impactSeconds = .16f;
}
