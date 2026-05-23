using UnityEngine;

/// <summary>
/// Simple flag visual. Attach to a GameObject with a pole and flag mesh.
/// Auto-destroys after lifetime if set.
/// </summary>
public class FlagPrefabSetup : MonoBehaviour
{
    [SerializeField] private float lifetime = 0f; // 0 = permanent until cleared

    private void Start()
    {
        if (lifetime > 0)
            Destroy(gameObject, lifetime);
    }
}