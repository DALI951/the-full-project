using UnityEngine;
using System.Collections;

public class ScreenShake : MonoBehaviour
{
    public static ScreenShake Instance { get; private set; }

    [SerializeField] private float maxShakeMagnitude = 0.5f;
    [SerializeField] private float maxShakeDuration = 0.5f;

    private Vector3 originalPosition;
    private float shakeTimer = 0f;
    private float shakeMagnitude = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        originalPosition = transform.localPosition;
    }

    private void Update()
    {
        if (shakeTimer > 0f)
        {
            transform.localPosition = originalPosition + Random.insideUnitSphere * shakeMagnitude;
            shakeTimer -= Time.deltaTime;
            shakeMagnitude = Mathf.Lerp(shakeMagnitude, 0f, Time.deltaTime * 4f);
        }
        else if (transform.localPosition != originalPosition)
        {
            transform.localPosition = originalPosition;
        }
    }

    public void Shake(float magnitude = 0.3f, float duration = 0.3f)
    {
        shakeMagnitude = Mathf.Max(shakeMagnitude, Mathf.Clamp(magnitude, 0f, maxShakeMagnitude));
        shakeTimer = Mathf.Max(shakeTimer, Mathf.Clamp(duration, 0f, maxShakeDuration));
    }

    public void LightShake() => Shake(0.15f, 0.15f);
    public void MediumShake() => Shake(0.3f, 0.3f);
    public void HeavyShake() => Shake(0.5f, 0.5f);
}