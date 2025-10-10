using UnityEngine;

public class HudTargetMarker : MonoBehaviour
{
    public SmartNavigationSystem nav;   // drag SmartNavigationSystem
    public Transform cam;               // drag ARCamera (auto-finds if null)

    [Header("Placement")]
    public float distanceAhead = 1.1f;  // meters in front of camera
    public float verticalOffset = 0.20f;
    public float bobHeight = 0.05f;
    public float bobSpeed = 2f;

    Renderer[] _renderers;
    bool _vis;
    float _t;

    void Awake()
    {
        if (!cam) cam = Camera.main ? Camera.main.transform : null;
        if (transform.parent != cam) transform.SetParent(cam, false);
        transform.localPosition = new Vector3(0f, verticalOffset, distanceAhead);
        transform.localRotation = Quaternion.identity;
        _renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(false);
    }

    void Update()
    {
        if (!nav) nav = FindFirstObjectByType<SmartNavigationSystem>();
        if (!cam) cam = Camera.main ? Camera.main.transform : null;

        if (!nav || !cam || !SmartNavigationSystem.IsAnyNavigationActive())
        {
            SetVisible(false);
            return;
        }

        // Stay in front of the camera (HUD)
        transform.localPosition = new Vector3(0f, verticalOffset, distanceAhead);

        // Simple bob
        _t += Time.deltaTime * bobSpeed;
        var p = transform.localPosition;
        p.y = verticalOffset + Mathf.Sin(_t) * bobHeight;
        transform.localPosition = p;

        SetVisible(true);
    }

    void SetVisible(bool v)
    {
        if (_vis == v) return;
        _vis = v;
        if (_renderers != null)
            foreach (var r in _renderers) if (r) r.enabled = v;
    }
}
