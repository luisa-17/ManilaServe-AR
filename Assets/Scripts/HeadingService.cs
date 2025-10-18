using UnityEngine;

[DisallowMultipleComponent]
public class HeadingService : MonoBehaviour
{
    public static HeadingService Instance { get; private set; }

    [Header("Lifecycle")]
    [Tooltip("Start collecting heading automatically when this GameObject is enabled.")]
    public bool autoStart = false;

    [Header("Source")]
    [Tooltip("Use the device magnetometer (Input.compass) on device builds. Fallback to camera yaw if not available.")]
    public bool useCompassIfAvailable = true;

    [Tooltip("Start Location service (Android/iOS) so trueHeading is available. Prompts for Location permission on first run.")]
    public bool autoStartLocation = true;

    [Tooltip("In the Unity Editor there is no magnetometer. If true, heading is driven by camera yaw in Play Mode.")]
    public bool forceCameraYawInEditor = true;

    [Header("Smoothing")]
    [Tooltip("Seconds to smooth heading. 0 = no smoothing.")]
    public float smoothingTau = 0.25f;

    [Header("Debug")]
    [Tooltip("Raw heading before offset/smoothing (0..360).")]
    public float debugRawHeading;
    [Tooltip("Heading after offset+smoothing (0..360).")]
    public float debugDisplayHeading;
    [Tooltip("Compass heading accuracy reported by the device (if available).")]
    public float headingAccuracy;

    float _northOffset;     // degrees added so nav & UI agree
    float _smoothed;        // smoothed heading 0..360
    bool _running;

    public bool IsRunning => _running;

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        if (autoStart) StartHeading();
    }

    void OnDisable()
    {
        StopHeading();
    }

    public void StartHeading()
    {
        _running = true;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (useCompassIfAvailable)
        {
            // Enable magnetometer; many Android devices require Location service for trueHeading
            Input.compass.enabled = true;
            if (autoStartLocation) Input.location.Start();
        }
#endif
        // Initialize smoothed value immediately to avoid a big first jump
        _smoothed = GetRawHeading();
        debugDisplayHeading = _smoothed;
        // Debug.Log("[Heading] Started");
    }

    public void StopHeading()
    {
        _running = false;

#if ENABLE_LEGACY_INPUT_MANAGER
        if (useCompassIfAvailable)
        {
            Input.compass.enabled = false;
            if (autoStartLocation && Input.location.status != LocationServiceStatus.Stopped)
                Input.location.Stop();
        }
#endif
        // Debug.Log("[Heading] Stopped");
    }

    void Update()
    {
        if (!_running) return;

        float raw = GetRawHeading();                  // 0..360 from source
        debugRawHeading = raw;

        float target = Normalize360(raw + _northOffset);   // apply offset
        float k = smoothingTau <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / smoothingTau);
        _smoothed = LerpAngle360(_smoothed, target, k);

        debugDisplayHeading = _smoothed;
    }

    // 0..360 after offset + smoothing
    public float GetHeading() => _smoothed;

    public void SetNorthOffset(float degrees) { _northOffset = degrees; }
    public float GetNorthOffset() => _northOffset;

    // Source selection: Editor uses camera yaw; device prefers magnetometer if enabled and valid
    float GetRawHeading()
    {
#if UNITY_EDITOR
        if (forceCameraYawInEditor)
        {
            var cam = Camera.main ? Camera.main.transform : null;
            return cam ? Normalize360(cam.eulerAngles.y) : 0f;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (useCompassIfAvailable && Input.compass.enabled)
        {
            headingAccuracy = Input.compass.headingAccuracy;
            var h = Input.compass.trueHeading; // 0..360 (true north)
            // Some devices report 0 with 0 accuracy until the sensor is ready; treat that as "not valid"
            bool looksValid = !float.IsNaN(h) && !(Mathf.Approximately(h, 0f) && Mathf.Approximately(headingAccuracy, 0f));
            if (looksValid) return Normalize360(h);
        }
#endif
        // Fallback: camera yaw (stable indoors)
        var c = Camera.main ? Camera.main.transform : null;
        return c ? Normalize360(c.eulerAngles.y) : 0f;
    }

    static float Normalize360(float deg)
    {
        deg %= 360f;
        if (deg < 0f) deg += 360f;
        return deg;
    }

    static float LerpAngle360(float a, float b, float t)
    {
        float d = Mathf.DeltaAngle(a, b);                  // shortest signed delta
        float v = a + d * Mathf.Clamp01(t);
        return Normalize360(v);
    }
}