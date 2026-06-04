using UnityEngine;
using TMPro;
using System.Collections;

public class FloatingDamageText : MonoBehaviour
{
    [SerializeField] private float floatSpeed = 1.5f;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private AnimationCurve floatCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private TextMeshPro tmp;
    private Color startColor;
    private Vector3 startPosition;

    private void Awake()
    {
        tmp = GetComponent<TextMeshPro>();
        if (tmp == null) tmp = GetComponentInChildren<TextMeshPro>();
    }

    private void OnEnable()
    {
        startPosition = transform.position;
        if (tmp != null) startColor = tmp.color;
        StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        float elapsed = 0f;
        Vector3 targetPos = startPosition + Vector3.up * 2f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float curveT = floatCurve.Evaluate(t);

            transform.position = Vector3.Lerp(startPosition, targetPos, curveT);

            if (tmp != null)
            {
                Color c = startColor;
                c.a = Mathf.Lerp(1f, 0f, t);
                tmp.color = c;
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    public void SetDamage(int amount, Color? color = null)
    {
        if (tmp != null)
        {
            tmp.text = amount.ToString();
            tmp.color = color ?? Color.red;
        }
    }

    public void SetText(string text, Color? color = null)
    {
        if (tmp != null)
        {
            tmp.text = text;
            tmp.color = color ?? Color.white;
        }
    }
}