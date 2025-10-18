using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
[ExecuteAlways]
#endif
public class FloorplanGuideHelper : MonoBehaviour
{
    [Header("Assign the node that contains your floorplan meshes/lines")]
    public Transform floorplanRoot; // e.g. WorldRoot/ContentAnchor/GroundFloor_Building

    [Header("How it looks while visible")]
    public Color tint = new Color(0f, 0.7f, 1f, 0.25f); // semi-transparent cyan
    public bool affectMeshRenderers = true;
    public bool affectLineRenderers = true;
    public string moveToLayer = "Ignore Raycast"; // avoids blocking clicks

    [Header("Control")]
    public bool visible = true;
    public bool restoreOriginalOnHide = true;

    [System.Serializable]
    class Rec { public Renderer r; public Material[] mats; public bool enabled; public int layer; }
    List<Rec> _records = new List<Rec>();
    Material _runtimeMat;
    Shader _unlitShader;
    static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int _colorId = Shader.PropertyToID("_Color");

    void OnEnable()
    {
        if (visible) Apply(true);
    }
    void OnDisable()
    {
        if (restoreOriginalOnHide) Apply(false);
    }
#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) Apply(visible);
    }
#endif

    [ContextMenu("Show Guide")]
    public void ShowGuide() => Apply(true);

    [ContextMenu("Hide Guide")]
    public void HideGuide() => Apply(false);

    public void Toggle() => Apply(!visible);

    void EnsureMat()
    {
        if (_runtimeMat != null) { SetColor(_runtimeMat, tint); return; }
        _unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (_unlitShader == null) _unlitShader = Shader.Find("Unlit/Color"); // fallback
        _runtimeMat = new Material(_unlitShader);
        SetColor(_runtimeMat, tint);
        _runtimeMat.renderQueue = 3000; // Transparent
    }
    void SetColor(Material m, Color c)
    {
        if (m.HasProperty(_baseColorId)) m.SetColor(_baseColorId, c);
        else if (m.HasProperty(_colorId)) m.SetColor(_colorId, c);
    }

    void Apply(bool show)
    {
        if (!floorplanRoot) return;

        if (show)
        {
            EnsureMat();
            _records.Clear();

            foreach (var r in floorplanRoot.GetComponentsInChildren<Renderer>(true))
            {
                bool isLine = r is LineRenderer;
                if (!affectLineRenderers && isLine) continue;
                if (!affectMeshRenderers && !isLine) continue;

                _records.Add(new Rec { r = r, mats = r.sharedMaterials, enabled = r.enabled, layer = r.gameObject.layer });

                r.enabled = true;
                if (!(r is ParticleSystemRenderer))
                    r.sharedMaterial = _runtimeMat;

                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                if (!string.IsNullOrEmpty(moveToLayer))
                {
                    int layer = LayerMask.NameToLayer(moveToLayer);
                    if (layer >= 0) r.gameObject.layer = layer;
                }

                // Line width/color tweak (optional)
                if (isLine)
                {
                    var lr = r as LineRenderer;
                    lr.widthMultiplier = Mathf.Max(0.02f, lr.widthMultiplier);
                    lr.startColor = lr.endColor = tint;
                }
            }

            visible = true;
        }
        else
        {
            if (restoreOriginalOnHide)
            {
                foreach (var rec in _records)
                {
                    if (!rec.r) continue;
                    rec.r.sharedMaterials = rec.mats;
                    rec.r.enabled = rec.enabled;
                    rec.r.gameObject.layer = rec.layer;
                }
            }
            visible = false;
        }
    }

#if UNITY_EDITOR
    // Handy: press G in editor to toggle
    void Update()
    {
        if (!Application.isPlaying && UnityEngine.Input.GetKeyDown(KeyCode.G))
            Toggle();
    }
#endif
}