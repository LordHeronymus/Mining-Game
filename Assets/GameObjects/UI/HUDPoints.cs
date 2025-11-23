using TMPro;
using UnityEngine;
using System.Collections;

public enum PointType
{
    Points,
    Money,
}

public class HUDPoints : MonoBehaviour
{
    public static HUDPoints Instance;

    [Header("Refs")]
    public TextMeshProUGUI pointsText;
    public TextMeshProUGUI moneyText;

    [Header("Settings")]
    public float lerpTime = 0.1f;

    float currentPoints = 0;
    float currentMoney = 0;

    Coroutine pointsCo, moneyCo;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        pointsText.text = "0";
        moneyText.text = "0";
    }

    public void UpdatePoints(int amount, PointType type)
    {
        if (type == PointType.Points)
        {
            if (pointsCo != null) StopCoroutine(pointsCo);
            pointsCo = StartCoroutine(LerpCounter(amount, type));
        }
        else
        {
            if (moneyCo != null) StopCoroutine(moneyCo);
            moneyCo = StartCoroutine(LerpCounter(amount, type));
        }
    }

    IEnumerator LerpCounter(int amount, PointType type)
    {
        float current = type == PointType.Points ? currentPoints : currentMoney;

        float delta = amount - current;
        float stepPerSecond = delta / lerpTime;

        while (current != amount)
        {
            current += stepPerSecond * Time.deltaTime;

            if (delta > 0)
                current = Mathf.Min(current, amount);
            else
                current = Mathf.Max(current, amount);

            if (type == PointType.Points)
            {
                pointsText.text = Mathf.Floor(current).ToString();
                currentPoints = current;
            }
            else
            {
                moneyText.text = Mathf.Floor(current).ToString();
                currentMoney = current;
            }

            yield return null;
        }
    }
}
