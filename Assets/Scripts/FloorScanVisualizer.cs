using UnityEngine;

public class FloorScanVisualizer : MonoBehaviour
{
    public enum Mode { Hidden, Scanning, Ready }
    [Header("Look")]
    public float baseRadius = 0.25f;
    public float ringWidth = 0.02f;
    public float rippleMaxRadius = 1.2f;
    public float ripplePeriod = 1.6f;
    public Color scanningColor = new Color(0f, 0.7f, 1f, 0.8f);
    public Color readyColor = new Color(0.1f, 1f, 0.5f, 0.95f);
    public int segments = 64;

    LineRenderer ring, ripple;
    Mode mode = Mode.Hidden;
    float t;
    bool initialized;

    void Awake() { InitIfNeeded(); }
    void OnEnable() { InitIfNeeded(); ApplyModeVisuals(); }

    void InitIfNeeded()
    {
        if (initialized) return;

        ring = CreateLR("Ring");
        ripple = CreateLR("Ripple");
        initialized = true;
        ApplyModeVisuals();
    }

    LineRenderer CreateLR(string name)
    {
        // Reuse if already present
        var child = transform.Find(name);
        LineRenderer lr = child ? child.GetComponent<LineRenderer>() : null;
        if (lr != null) return lr;

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        lr = go.AddComponent<LineRenderer>();

        lr.useWorldSpace = false;
        lr.loop = true;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.widthMultiplier = ringWidth;

        // Simple unlit material (URP or Built?in)
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) shader = Shader.Find("Unlit/Color");
        lr.material = new Material(shader);

        BuildCircle(lr, baseRadius);
        return lr;
    }

    void SafeSetColor(LineRenderer lr, Color c)
    {
        if (!lr) return;
        var m = lr.material;
        if (!m)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) shader = Shader.Find("Unlit/Color");
            lr.material = new Material(shader);
            m = lr.material;
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else m.color = c;
    }

    void BuildCircle(LineRenderer lr, float radius)
    {
        if (!lr) return;
        int count = Mathf.Max(3, segments);
        lr.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float ang = i * Mathf.PI * 2f / (count - 1);
            lr.SetPosition(i, new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
        }
    }

    void ApplyModeVisuals()
    {
        bool on = mode != Mode.Hidden;
        if (ring) ring.enabled = on;
        if (ripple) ripple.enabled = on;

        if (!on) return;

        if (mode == Mode.Ready)
        {
            SafeSetColor(ring, readyColor);
            SafeSetColor(ripple, new Color(readyColor.r, readyColor.g, readyColor.b, 0.35f));
        }
        else // Scanning
        {
            SafeSetColor(ring, scanningColor);
            SafeSetColor(ripple, new Color(scanningColor.r, scanningColor.g, scanningColor.b, 0.5f));
        }
    }

    public void SetMode(Mode m)
    {
        mode = m;
        InitIfNeeded();
        ApplyModeVisuals();
    }

    void Update()
    {
        if (!initialized || mode == Mode.Hidden) return;
        t += Time.deltaTime;

        if (mode == Mode.Scanning)
        {
            float pulse = 1f + Mathf.Sin(t * 3f) * 0.06f;
            BuildCircle(ring, baseRadius * pulse);

            float phase = (t % ripplePeriod) / ripplePeriod;
            float r = Mathf.Lerp(baseRadius * 0.9f, rippleMaxRadius, phase);
            BuildCircle(ripple, r);

            var soft = new Color(scanningColor.r, scanningColor.g, scanningColor.b, Mathf.Lerp(0.6f, 0f, phase));
            SafeSetColor(ripple, soft);
        }
        else if (mode == Mode.Ready)
        {
            float pulse = 1f + Mathf.Sin(t * 2f) * 0.02f;
            BuildCircle(ring, baseRadius * pulse);
            BuildCircle(ripple, baseRadius * 1.2f);
            var soft = new Color(readyColor.r, readyColor.g, readyColor.b, 0.25f + 0.15f * Mathf.Abs(Mathf.Sin(t * 2f)));
            SafeSetColor(ripple, soft);
        }
    }
}