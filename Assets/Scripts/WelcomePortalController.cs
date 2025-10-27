using System.Collections;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class WelcomePortalController : MonoBehaviour
{
    [Header("Refs")]
    public Transform ringRoot;
    public LineRenderer ringMain;
    public LineRenderer ringRipple;
    public Transform pillar;               // Cylinder or Quad group
    public Renderer pillarRenderer;        // Material color/alpha control
    public Transform logoBillboard;        // Faces camera (Y-only)
    public Canvas textCanvas;              // World-space
    public TMP_Text welcomeText;           // Optional: “Welcome to ManilaServe”
    public AudioSource audioSource;        // Optional whoosh FX

    [Header("Sizing")]
    public float baseRingRadius = 0.40f;
    public float visualBaseYOffset = 0.0f; // match PlaceOnFloorARF.visualBaseYOffset
    public float pillarHeight = 1.2f;
    public float pillarRadius = 0.22f;

    [Header("Colors")]
    public Color accentColor = new Color(0.0f, 0.92f, 0.78f, 1f); // tweak to your brand
    public Color rippleColor = new Color(0.0f, 0.92f, 0.78f, 0.35f);
    public Color pillarColor = new Color(0.4f, 0.9f, 1f, 0.35f);

    [Header("Timing")]
    public float appearDuration = 0.45f;
    public float ripplePeriod = 1.6f;

    [Header("Idle Motion")]
    public float spinSpeed = 30f;          // deg/sec
    public float bobAmplitude = 0.015f;    // meters
    public float bobSpeed = 1.6f;

    [Header("Billboarding")]
    public bool billboardLogoAndText = true;

    Camera _cam;
    float _t;
    float _rippleT;
    Vector3 _pillarTargetScale;
    MaterialPropertyBlock _mpb;

    public bool scaleTextWithDistance = true;
    public Transform textRoot; // assign TextCanvas.transform
    public float baseDistance = 1.2f; // meters where it looks “just right”
    public float baseScale = 0.002f; // matches your TextCanvas localScale
    public float minScale = 0.0012f, maxScale = 0.0035f;

    public void Initialize(Pose pose, Camera cam)
    {
        _cam = cam != null ? cam : Camera.main;

        // Place root and elevate by offset
        transform.SetPositionAndRotation(pose.position + Vector3.up * visualBaseYOffset, pose.rotation);

        // Setup pillar
        if (pillar != null)
        {
            _pillarTargetScale = new Vector3(pillarRadius * 2f, pillarHeight, pillarRadius * 2f);
            pillar.localScale = new Vector3(_pillarTargetScale.x, 0f, _pillarTargetScale.z); // height 0 at start
        }

        // Set initial ring visuals (start small)
        SetRing(ringMain, baseRingRadius, accentColor);
        SetRing(ringRipple, baseRingRadius * 0.8f, rippleColor);

        // Pillar initial alpha lower (fade in)
        SetRendererColor(pillarRenderer, new Color(pillarColor.r, pillarColor.g, pillarColor.b, 0f));

        // Optional text
        if (welcomeText != null && string.IsNullOrWhiteSpace(welcomeText.text))
            welcomeText.text = "Welcome to ManilaServe";

        // Pop-in animation
        StopAllCoroutines();
        StartCoroutine(AppearRoutine());

        // Sound
        if (audioSource != null) audioSource.Play();
    }

    void Update()
    {
        // Idle loop
        _t += Time.deltaTime;
        _rippleT += Time.deltaTime;

        // Spin + bob
        if (ringRoot != null)
        {
            ringRoot.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
            float bob = Mathf.Sin(_t * bobSpeed) * bobAmplitude;
            ringRoot.localPosition = new Vector3(0f, bob, 0f);
        }

        // Ripple expand/soften over time (reticle-like)
        if (ringRipple != null)
        {
            float phase = (_rippleT % ripplePeriod) / ripplePeriod;
            float r = Mathf.Lerp(baseRingRadius * 0.9f, baseRingRadius * 1.4f, phase);
            Color c = new Color(rippleColor.r, rippleColor.g, rippleColor.b, Mathf.Lerp(0.45f, 0f, phase));
            SetRing(ringRipple, r, c);
        }

        // Billboard to camera (Y-only)
        if (billboardLogoAndText && _cam)
        {
            Vector3 camPos = _cam.transform.position;
            if (logoBillboard != null)
            {
                var look = camPos;
                look.y = logoBillboard.position.y;
                logoBillboard.LookAt(look);
                logoBillboard.Rotate(0f, 180f, 0f); // fix forward
            }
            if (textCanvas != null)
            {
                var ct = textCanvas.transform;
                var look = camPos;
                look.y = ct.position.y;
                ct.LookAt(look);
                ct.Rotate(0f, 180f, 0f);
            }
        }

        if (scaleTextWithDistance && _cam && textRoot)
        {
            float d = Vector3.Distance(_cam.transform.position, textRoot.position);
            float target = Mathf.Clamp(baseScale * (d / baseDistance), minScale, maxScale);
            textRoot.localScale = Vector3.one * target;
        }
    }

    IEnumerator AppearRoutine()
    {
        // Scale-up rings from 0
        float t = 0f;
        Vector3 startScaleRings = Vector3.zero;
        Vector3 targetScaleRings = Vector3.one;

        // Pillar height + fade-in
        Vector3 pillarStart = new Vector3(_pillarTargetScale.x, 0f, _pillarTargetScale.z);
        Vector3 pillarEnd = _pillarTargetScale;

        while (t < appearDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / appearDuration);

            // smooth pop
            float s = 1f - Mathf.Pow(1f - k, 4f);

            if (ringRoot) ringRoot.localScale = Vector3.Lerp(startScaleRings, targetScaleRings, s);

            if (pillar != null)
            {
                float y = Mathf.Lerp(pillarStart.y, pillarEnd.y, s);
                pillar.localScale = new Vector3(_pillarTargetScale.x, y, _pillarTargetScale.z);

                // Fade up pillar alpha
                float a = Mathf.Lerp(0f, pillarColor.a, s);
                SetRendererColor(pillarRenderer, new Color(pillarColor.r, pillarColor.g, pillarColor.b, a));
            }

            yield return null;
        }

        if (ringRoot) ringRoot.localScale = targetScaleRings;
        if (pillar != null)
        {
            pillar.localScale = pillarEnd;
            SetRendererColor(pillarRenderer, pillarColor);
        }
    }

    void SetRing(LineRenderer lr, float radius, Color col)
    {
        if (!lr) return;

        // Update color via material
        var mat = lr.material;
        if (mat != null)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        }

        // If it has ReticleCircle, adjust radius; otherwise scale the transform
        var rc = lr.GetComponent<ReticleCircle>();
        if (rc != null)
        {
            if (Mathf.Abs(rc.radius - radius) > 0.0001f)
            {
                rc.radius = radius;
                // rebuild circle
                int count = Mathf.Max(3, rc.segments);
                lr.positionCount = count + 1;
                lr.loop = true;
                for (int i = 0; i <= count; i++)
                {
                    float t = i / (float)count;
                    float ang = t * Mathf.PI * 2f;
                    float x = Mathf.Cos(ang) * rc.radius;
                    float z = Mathf.Sin(ang) * rc.radius;
                    lr.SetPosition(i, new Vector3(x, 0f, z));
                }
                lr.widthMultiplier = rc.lineWidth;
            }
        }
        else
        {
            // fallback: scale ring object
            lr.transform.localScale = Vector3.one * (radius / baseRingRadius);
        }
    }

    void SetRendererColor(Renderer r, Color c)
    {
        if (!r) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(_mpb);

        // Common URP properties
        if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor"))
            _mpb.SetColor("_BaseColor", c);
        else
            _mpb.SetColor("_Color", c);

        r.SetPropertyBlock(_mpb);
    }
}