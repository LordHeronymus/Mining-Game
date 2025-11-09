using UnityEngine;
using System.Collections;

public class Number : MonoBehaviour
{
    [Header("References")]
    [SerializeField] TMPro.TMP_Text tmp;

    [Header("Settings")]
    [SerializeField] AnimationCurve alphaCurve;

    float speedX = 0.5f;
    Vector2 position;

    public void Init(Color color, float size, float number, float duration)
    {
        StartCoroutine(Decay(duration));

        tmp.fontSize = size;
        tmp.text = $"{+number}";
        tmp.color = color;

        Destroy(gameObject, duration);
    }

    void Awake()
    {
        position = transform.position;
    }

    void Update()
    {
        position += Vector2.up * speedX * Time.deltaTime;
        transform.localPosition = position;
    }

    IEnumerator Decay(float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float x = t / duration;
            float a = alphaCurve.Evaluate(x);
            tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, a);
            yield return null;
        }
    }
}
