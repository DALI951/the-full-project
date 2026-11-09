using UnityEngine;

/// <summary>
/// Simple flag visual. Attach to a GameObject with a pole and flag mesh.
/// Auto-destroys after lifetime if set.
/// </summary>
public class FlagPrefabSetup : MonoBehaviour
{
    [SerializeField] private float lifetime = 0f; // 0 = permanent until cleared

    private void Awake()
    {
        // Move / rally flags are visual markers: ensure colliders never block unit movement or clicks
        var colliders = GetComponentsInChildren<Collider>();
        foreach (var c in colliders)
        {
            Destroy(c);
        }
    }

    private void Start()
    {
        if (lifetime > 0)
            Destroy(gameObject, lifetime);
    }
}
