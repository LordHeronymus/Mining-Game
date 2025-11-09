using TMPro;
using UnityEngine;
using System.Collections;

public class HUDPoints : MonoBehaviour
{
    public static HUDPoints Instance;

    [Header("Refs")]
    public TextMeshProUGUI pointsText;

    [Header("Settings")]
    public float lerpTime = 0.1f;

    float currentPoints = 0;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        pointsText.text = "0";
    }

    public void UpdatePoints(int points) => StartCoroutine(LerpPoints(points));

    IEnumerator LerpPoints(int points)
    {
        float delta = points - currentPoints;
        float step = delta * (1 / lerpTime) * Time.deltaTime;

        while (currentPoints < points)
        {
            currentPoints = Mathf.Min(currentPoints + step, points);

            pointsText.text = Mathf.Floor(currentPoints).ToString();
            yield return null;
        }
    }
}
