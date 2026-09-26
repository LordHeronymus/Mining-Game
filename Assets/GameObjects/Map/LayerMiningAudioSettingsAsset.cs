using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class LayerMiningAudioSettingEntry
{
    public int layerIndex;
    public bool breaking;
    public SoundType soundType;
    public int clipIndex;
    public LayerMiningClipTuning tuning = new LayerMiningClipTuning();
}

[CreateAssetMenu(fileName = "LayerMiningAudioSettings", menuName = "Audio/Layer Mining Settings")]
public sealed class LayerMiningAudioSettingsAsset : ScriptableObject
{
    [SerializeField] List<LayerMiningAudioSettingEntry> entries = new List<LayerMiningAudioSettingEntry>();

    public LayerMiningClipTuning GetOrCreate(int layerIndex, bool breaking, SoundType soundType, int clipIndex)
    {
        if (entries == null) entries = new List<LayerMiningAudioSettingEntry>();
        var entry = entries.Find(item => item != null && item.layerIndex == layerIndex &&
            item.breaking == breaking && item.soundType == soundType && item.clipIndex == clipIndex);
        if (entry == null)
        {
            entry = new LayerMiningAudioSettingEntry
            {
                layerIndex = layerIndex,
                breaking = breaking,
                soundType = soundType,
                clipIndex = clipIndex
            };
            entries.Add(entry);
        }
        if (entry.tuning == null) entry.tuning = new LayerMiningClipTuning();
        return entry.tuning;
    }
}
