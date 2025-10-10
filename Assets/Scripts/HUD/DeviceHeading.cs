using UnityEngine;

public class DeviceHeading : MonoBehaviour
{
    public static DeviceHeading Instance { get; private set; }

    [Header("Smoothing")]
    public float yawSmooth = 6f;       // higher = snappier
    public float readinessTime = 1.5f; // seconds to warm up compass

    [Header("Debug")]
    public bool forceEditorSim = false;  // in Editor, simulate yaw by ARCamera Y rotation

    public float YawDeg { get; private set; }   // 0..360 world-heading
    float _t; bool _ready;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (SystemInfo.supportsGyroscope) Input.gyro.enabled = true;
        Input.compass.enabled = true; // Android/iOS only; no permission dialog needed
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (!_ready && _t >= readinessTime) _ready = true;

#if UNITY_EDITOR
        if (forceEditorSim)
        {
            var cam = Camera.main;
            float simYaw = cam ? cam.transform.eulerAngles.y : 0f;
            YawDeg = Mathf.Repeat(Mathf.LerpAngle(YawDeg, simYaw, Time.deltaTime * yawSmooth), 360f);
            return;
        }
#endif
        // Prefer compass heading; fallback to gyro yaw if compass is invalid
        float raw = Input.compass.enabled ? Input.compass.trueHeading : float.NaN;

        if (!float.IsNaN(raw) && raw >= 0f) // 0..360
        {
            YawDeg = Mathf.Repeat(Mathf.LerpAngle(YawDeg, raw, Time.deltaTime * yawSmooth), 360f);
        }
        else
        {
            // Very simple gyro fallback (not perfect). You can improve if needed.
            var cam = Camera.main;
            float simYaw = cam ? cam.transform.eulerAngles.y : YawDeg;
            YawDeg = Mathf.Repeat(Mathf.LerpAngle(YawDeg, simYaw, Time.deltaTime * yawSmooth), 360f);
        }
    }
}