// CameraViewportFit.cs — attach to Main Camera
using UnityEngine;

public class CameraViewportFit : MonoBehaviour
{
    [SerializeField] private RectTransform bottomBar;
    private Camera cam;

    private void Awake() => cam = GetComponent<Camera>();

    private void LateUpdate()
    {
        if (bottomBar == null) return;
        Vector3[] corners = new Vector3[4];
        bottomBar.GetWorldCorners(corners);
        float panelScreenTop = RectTransformUtility.WorldToScreenPoint(null, corners[1]).y;
        float ratio = panelScreenTop / Screen.height;
        cam.rect = new Rect(0f, ratio, 1f, 1f - ratio);
    }
}