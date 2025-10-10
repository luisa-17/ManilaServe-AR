using UnityEngine;
using TMPro;

public class Hud3DArrowController : MonoBehaviour
{
    [Header("Scene refs")]
    public Transform cam; // ARCamera (auto-finds if null)
    public ManilaServeUI ui; // drag Canvas (ManilaServeUI)
    public SmartNavigationSystem nav; // drag SmartNavigationSystem

    [Header("Placement")]
    public float distanceAhead = 0.6f;          // meters in front of camera
    public float verticalOffset = -0.15f;       // down from center
    public float turnSmooth = 10f;              // rotation smoothing

    [Header("UI (optional)")]
    public TMP_Text distanceLabel;              // shows next point name and meters (optional)

    Renderer[] _renderers;
    bool _visible;

    void Awake()
    {
        if (!cam) cam = Camera.main ? Camera.main.transform : null;
        if (transform.parent != cam) transform.SetParent(cam, false);
        transform.localPosition = new Vector3(0f, verticalOffset, distanceAhead);
        transform.localRotation = Quaternion.identity;

        _renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(false); // start hidden until nav is active
    }

    void Update()
    {
        if (!nav) nav = FindFirstObjectByType<SmartNavigationSystem>();
        if (!cam) cam = Camera.main ? Camera.main.transform : null;

        if (!nav || !cam) { SetVisible(false); return; }

        bool navActive = SmartNavigationSystem.IsAnyNavigationActive();
        if (!navActive) { SetVisible(false); return; }

        // Use the next path point (waypoint-driven)
        Vector3 next;
        if (!nav.TryGetNextPoint(out next, 1f))
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        // Bearing to next point (ignore vertical)
        Vector3 dir = next - cam.position;
        dir.y = 0f;

        // World bearing (0..360) of the next point
        float targetYaw = (dir.sqrMagnitude > 1e-6f)
            ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg
            : 0f;

        // Device heading (compass/gyro)
        float rawHeading = DeviceHeading.Instance ? DeviceHeading.Instance.YawDeg : cam.eulerAngles.y;

        // APPLY calibration offset (set by CalibrateToNext). 
        // After this, when you face "calibrated forward", adjustedHeading == targetYaw.
        float adjustedHeading = rawHeading - (nav ? nav.hudWorldNorthOffset : 0f);

        // Compute turn needed (+right, -left)
        float deltaYaw = Mathf.DeltaAngle(adjustedHeading, targetYaw);

        // Rotate the HUD arrow so it points to the next path point
        Quaternion targetRot = Quaternion.Euler(0f, deltaYaw, 0f);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, Time.deltaTime * turnSmooth);

        // Optional: simple distance label
        if (distanceLabel)
        {
            float d = Vector3.Distance(new Vector3(cam.position.x, 0f, cam.position.z),
                                       new Vector3(next.x, 0f, next.z));
            distanceLabel.text = d > 1f ? $"{d:F1} m" : "Almost there";
        }

    }

    void SetVisible(bool v)
    {
        if (_visible == v) return;
        _visible = v;
        if (_renderers != null)
            foreach (var r in _renderers) if (r) r.enabled = v;
        if (distanceLabel) distanceLabel.gameObject.SetActive(v);
    }

    // One-tap alignment: makes "forward" equal the bearing to the next path point
    public void CalibrateToNext()
    {
        if (!nav || DeviceHeading.Instance == null) return;

        if (!nav.TryGetNextPoint(out var next, 0.5f))
            next = nav.GetFinalPathPoint();

        var tCam = cam ? cam : (Camera.main ? Camera.main.transform : null);
        if (!tCam) return;

        Vector3 dir = next - tCam.position; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) return;

        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; // where we want "forward"
        float heading = DeviceHeading.Instance.YawDeg;             // current device forward

        // Align phone heading to the path bearing
        nav.hudWorldNorthOffset = heading - targetYaw;
        Debug.Log($"[HUD] Calibrated. Offset={nav.hudWorldNorthOffset:F1} (heading={heading:F1}, target={targetYaw:F1})");
    }
}