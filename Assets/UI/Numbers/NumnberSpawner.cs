using UnityEngine;

public class NumnberSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] GameObject numberPrefab;

    [Header("Settings")]
    [SerializeField] Color color = Color.green;
    [SerializeField] float fontSize = 10f;
    [SerializeField] float duration = 1.5f;

    void OnEnable() => TileMiner.OnBlockMined += HandleBlockMined;
    void OnDisable() => TileMiner.OnBlockMined -= HandleBlockMined;


    void SpawnNumber(Vector2 position, Color color, float size, float number)
    {
        var go = Instantiate(numberPrefab, position, Quaternion.identity);
        var n = go.GetComponent<Number>();
        n.Init(color, size, number, duration);
    }

    void HandleBlockMined(Vector2 position, int points)
    {
        SpawnNumber(position, color, fontSize, points);
    }
}
