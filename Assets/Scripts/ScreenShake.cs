using UnityEngine;

public class ScreenShake : MonoBehaviour
{
    public static ScreenShake Instance { get; private set; }

    [SerializeField] private float maxShakeMagnitude = 0.5f;
    [SerializeField] private float maxShakeDuration  = 0.5f;

    private Vector3 lastShakeOffset = Vector3.zero;
    private float   shakeTimer      = 0f;
    private float   shakeMagnitude  = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void LateUpdate()
    {
        // 1. Remove previous frame's offset so RTSCamera panning/movement is preserved
        if (lastShakeOffset != Vector3.zero)
        {
            transform.localPosition -= lastShakeOffset;
            lastShakeOffset = Vector3.zero;
        }

        // 2. If shaking, calculate and apply the new offset
        if (shakeTimer > 0f)
        {
            lastShakeOffset = Random.insideUnitSphere * shakeMagnitude;
            transform.localPosition += lastShakeOffset;
            shakeTimer -= Time.deltaTime;
            shakeMagnitude = Mathf.Lerp(shakeMagnitude, 0f, Time.deltaTime * 4f);
        }
    }

    private void OnDisable()
    {
        // Clean up any remaining offset if component is disabled or destroyed
        if (lastShakeOffset != Vector3.zero)
        {
            transform.localPosition -= lastShakeOffset;
            lastShakeOffset = Vector3.zero;
        }
    }

    public void Shake(float magnitude = 0.3f, float duration = 0.3f)
    {
        shakeMagnitude = Mathf.Max(shakeMagnitude, Mathf.Clamp(magnitude, 0f, maxShakeMagnitude));
        shakeTimer     = Mathf.Max(shakeTimer, Mathf.Clamp(duration, 0f, maxShakeDuration));
    }

    public void LightShake()  => Shake(0.15f, 0.15f);
    public void MediumShake() => Shake(0.3f, 0.3f);
    public void HeavyShake()  => Shake(0.5f, 0.5f);

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// A destroyed MonoBehaviour is NOT a real CLR null (Unity fake-null only affects
    /// == and !=). Any "Instance?.Foo()" call site elsewhere would therefore still
    /// dispatch into this dead object and throw MissingReferenceException the next
    /// time the scene reloads. Clearing the field makes it a true null.
    /// </summary>
    private void OnDestroy()
    {
        if (lastShakeOffset != Vector3.zero)
        {
            transform.localPosition -= lastShakeOffset;
            lastShakeOffset = Vector3.zero;
        }
        if (Instance == this) Instance = null;
    }
}
