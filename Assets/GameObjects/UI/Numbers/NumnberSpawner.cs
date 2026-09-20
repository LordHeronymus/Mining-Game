using UnityEngine;
using System.Collections.Generic;

public class NumnberSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] GameObject numberPrefab;

    [Header("Settings")]
    [SerializeField] float fontSize = 9f;
    [SerializeField] float duration = 2f;
    [SerializeField] float accumulationRadius = 2.5f;
    readonly List<(ItemSO item, Number popup)> active = new();

    void OnEnable() => TileMiner.OnMiningPoints += HandleMiningPoints;
    void OnDisable() => TileMiner.OnMiningPoints -= HandleMiningPoints;


    void HandleMiningPoints(Vector2 position, int points, ItemSO item)
    {
        if (points <= 0 || !item) return;
        active.RemoveAll(entry => !entry.popup || !entry.popup.CanAccumulate);
        Number nearest = null;
        float distance = accumulationRadius;
        foreach (var entry in active)
        {
            if (entry.item != item) continue;
            float candidate = Vector2.Distance(entry.popup.Origin, position);
            if (candidate > distance) continue;
            distance = candidate;
            nearest = entry.popup;
        }
        if (nearest) { nearest.Accumulate(points); return; }
        var popup = Instantiate(numberPrefab, position, Quaternion.identity).GetComponent<Number>();
        popup.Init(item.themeColor, fontSize, points, duration);
        active.Add((item, popup));
    }
}
