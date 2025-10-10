using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

public class PlaceOnFloorARF : MonoBehaviour
{
    [Header("AR Foundation")]
    public XROrigin origin;
    public ARRaycastManager raycastMgr;
    public ARPlaneManager planeMgr;
    public ARAnchorManager anchorMgr;
    public Camera arCamera;

    [Header("Your app refs")]
    public SmartNavigationSystem nav;     // assign SmartNavigationSystem
    public Transform worldRoot;           // WorldRoot
    public Transform contentAnchor;       // ContentAnchor (child of WorldRoot)

    [Header("UI (optional)")]
    public GameObject reticle;            // your ring; keep disabled in scene
    public TMP_Text instruction;          // “Move phone to find the floor”
    public Button placeButton;            // enabled when a floor hit is valid

    // NEW: Instructions panel + simple UI pulse during scanning
    [Header("Instruction UI / Scanning Pulse")]
    public GameObject instructionsPanel;           // root panel you want hidden on place
    public CanvasGroup instructionsCanvasGroup;    // add a CanvasGroup to the instructions panel and drag it here
    public bool autoHideInstructionsOnPlace = true;
    public bool pulseInstructionsDuringScan = true;
    [Range(0.5f, 6f)] public float pulseSpeed = 2.0f;
    [Range(0f, 1f)] public float pulseMinAlpha = 0.55f;
    [Range(0f, 1f)] public float pulseMaxAlpha = 1.0f;

    bool placed;
    Pose lastPose;
    bool hasPose;
    static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

    public FloorScanVisualizer scanViz; // drag your Reticle (has FloorScanVisualizer)
    [Range(1, 10)] public int stableFramesRequired = 3;
    [Range(0f, 30f)] public float maxTiltDegrees = 15f;
    int _validStreak;

    // track stable state so UI pulse can respond
    bool _isStable = false;

    void Awake()
    {
        if (!origin) origin = FindObjectOfType<XROrigin>();
        if (!raycastMgr) raycastMgr = origin ? origin.GetComponent<ARRaycastManager>() : FindObjectOfType<ARRaycastManager>();
        if (!planeMgr) planeMgr = origin ? origin.GetComponent<ARPlaneManager>() : FindObjectOfType<ARPlaneManager>();
        if (!anchorMgr) anchorMgr = origin ? origin.GetComponent<ARAnchorManager>() : FindObjectOfType<ARAnchorManager>();
        if (!arCamera) arCamera = origin ? origin.Camera : Camera.main;
    }

    void OnEnable()
    {
        if (!arCamera) arCamera = Camera.main;
        if (planeMgr) planeMgr.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        if (reticle) reticle.SetActive(false);
        if (instruction) instruction.text = "Move your phone to find the floor";
        if (placeButton)
        {
            placeButton.interactable = false;
            placeButton.onClick.AddListener(PlaceFromButton);
        }

        _validStreak = 0;
        if (reticle) reticle.SetActive(false);
        if (scanViz) scanViz.SetMode(FloorScanVisualizer.Mode.Scanning);

        // show instructions panel on enable
        if (instructionsPanel) instructionsPanel.SetActive(true);
        if (instructionsCanvasGroup) instructionsCanvasGroup.alpha = 1f;

        if (placeButton) placeButton.interactable = false;
        hasPose = false;
        _isStable = false;
    }

    void OnDisable()
    {
        if (placeButton) placeButton.onClick.RemoveListener(PlaceFromButton);
    }

    void Update()
    {
        if (placed || !raycastMgr) return;

        var screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        bool hasHit = raycastMgr.Raycast(screen, hits, TrackableType.PlaneWithinPolygon);

        if (hasHit)
        {
            var hit = hits[0];
            Pose p = hit.pose;

            // remember the last pose so PlaceFromButton can use it
            lastPose = p;
            hasPose = true;

            // Put reticle flat on the plane and facing camera-forward projected onto the plane
            Vector3 camForward = (arCamera != null) ? arCamera.transform.forward : (Camera.main != null ? Camera.main.transform.forward : Vector3.forward);
            Vector3 camUp = (arCamera != null) ? arCamera.transform.up : (Camera.main != null ? Camera.main.transform.up : Vector3.up);
            Vector3 forwardOnPlane = Vector3.ProjectOnPlane(camForward, p.up);
            if (forwardOnPlane.sqrMagnitude < 1e-6f) forwardOnPlane = Vector3.ProjectOnPlane(camUp, p.up);
            Quaternion rot = Quaternion.LookRotation(forwardOnPlane.normalized, p.up);

            // If your reticle prefab needs a model-space correction, multiply rot by a corrective rotation here.
            // e.g. rot = rot * Quaternion.Euler(-90f, 0f, 0f);  // only if needed

            if (reticle != null)
                reticle.transform.SetPositionAndRotation(p.position, rot);

            reticle?.SetActive(true);

            // horizontal enough?
            float tilt = Vector3.Angle(p.up, Vector3.up);
            bool horizontal = tilt <= maxTiltDegrees;

            _validStreak = horizontal ? _validStreak + 1 : 0;
            bool stable = _validStreak >= stableFramesRequired;
            _isStable = stable;

            if (scanViz) scanViz.SetMode(stable ? FloorScanVisualizer.Mode.Ready : FloorScanVisualizer.Mode.Scanning);
            if (instruction) instruction.text = stable ? "Tap Place to anchor" : "Move your phone to find the floor";
            if (placeButton) placeButton.interactable = stable;
        }
        else
        {
            hasPose = false;
            _validStreak = 0;
            if (reticle) reticle.SetActive(false);
            if (scanViz) scanViz.SetMode(FloorScanVisualizer.Mode.Scanning);
            if (instruction) instruction.text = "Move your phone to find the floor";
            if (placeButton) placeButton.interactable = false;
            _isStable = false;
        }

        // UI pulse while scanning
        if (pulseInstructionsDuringScan && instructionsCanvasGroup != null && !placed)
        {
            if (!_isStable)
            {
                float phase = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
                instructionsCanvasGroup.alpha = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, phase);
            }
            else
            {
                instructionsCanvasGroup.alpha = 1f;
            }
        }
    }

    private void PlaceFromButton()
    {
        if (placed) return; // already anchored
        if (!hasPose) return; // no raycast hit yet
        if (_validStreak < stableFramesRequired) return; // not stable/ready
        DoPlace(lastPose);                   // place at the last valid pose
    }

    void DoPlace(Pose pose)
    {
        if (placed) return;

        // 1) Create/attach an anchor
        ARAnchor anchor = null;
        if (anchorMgr != null && planeMgr != null && hits != null && hits.Count > 0)
        {
            var plane = planeMgr.GetPlane(hits[0].trackableId);
            if (plane != null) anchor = anchorMgr.AttachAnchor(plane, pose);
        }
        if (anchor == null)
        {
            var go = new GameObject("ARF_Anchor");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            anchor = go.AddComponent<ARAnchor>();
        }

        // 2) Parent content to anchor
        if (worldRoot != null) worldRoot.SetParent(anchor.transform, false);
        if (contentAnchor != null) contentAnchor.SetParent(worldRoot, false);

        // 3) Let nav system run and ALIGN when it completes
        if (nav != null)
        {
            // SmartNavigationSystem.PlaceWorld(Vector3, Quaternion, Action onComplete)
            nav.PlaceWorld(pose.position, pose.rotation, () =>
            {
                // run alignment after nav finishes
                StartCoroutine(AlignWorldRootToPlaneCoroutine(pose.position));
            });
        }
        else
        {
            // fallback if nav unavailable
            StartCoroutine(AlignWorldRootToPlaneCoroutine(pose.position));
        }

        // 4) Stop detection & hide scanning UI
        if (planeMgr != null) planeMgr.requestedDetectionMode = PlaneDetectionMode.None;
        if (reticle != null) reticle.SetActive(false);
        if (scanViz != null) scanViz.SetMode(FloorScanVisualizer.Mode.Hidden);
        if (placeButton != null) placeButton.interactable = false;
        if (autoHideInstructionsOnPlace && instructionsPanel != null) instructionsPanel.SetActive(false);
        if (instructionsCanvasGroup != null) instructionsCanvasGroup.alpha = 0f;
        if (instruction != null) instruction.text = "Floor set. Select an office.";

        placed = true;
    }

    System.Collections.IEnumerator AlignWorldRootToPlaneCoroutine(Vector3 planePosition)
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        GameObject measure = (contentAnchor != null) ? contentAnchor.gameObject : (worldRoot != null ? worldRoot.gameObject : null);
        if (measure == null)
        {
            Debug.LogWarning("AlignWorldRoot: nothing to measure.");
            yield break;
        }

        float worldMinY = ComputeLowestWorldY(measure);
        float planeY = planePosition.y;

        // ✅ ADD THIS SECTION: Tell navigation system about AR ground plane
        if (nav != null)
        {
            nav.arGroundPlaneY = planeY;
            nav.useARGroundPlane = true;

            Debug.Log($"[AR] Set navigation ground plane Y to: {planeY}");

            nav.SnapPathYsToPlaneY(planeY);
            nav.ReparentPathRoot(worldRoot);

            // Don't create arrows yet - wait for user to select offices
            // nav.CreateGroundArrowsForPath(); ← REMOVE THIS LINE if present
        }

        float delta = planeY - worldMinY;

        if (Mathf.Abs(delta) > 0.0001f && worldRoot != null)
        {
            worldRoot.transform.position += new Vector3(0f, delta, 0f);
            Debug.Log($"AlignWorldRoot: moved worldRoot by {delta:F3} (minY {worldMinY:F3} -> planeY {planeY:F3})");
        }
        else
        {
            Debug.Log($"AlignWorldRoot: no adjustment needed (delta={delta:F4}).");
        }
    }

    // --- single helper to compute lowest Y (supports Renderers, MeshFilters, LineRenderers) ---
    float ComputeLowestWorldY(GameObject root)
    {
        if (root == null) return 0f;
        float minY = float.MaxValue;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            foreach (var r in renderers)
                if (r != null) minY = Mathf.Min(minY, r.bounds.min.y);
            if (minY != float.MaxValue) return minY;
        }

        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        if (filters != null && filters.Length > 0)
        {
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                Vector3 worldMin = mf.transform.TransformPoint(b.min);
                minY = Mathf.Min(minY, worldMin.y);
            }
            if (minY != float.MaxValue) return minY;
        }

        var lines = root.GetComponentsInChildren<LineRenderer>(true);
        if (lines != null && lines.Length > 0)
        {
            foreach (var lr in lines)
            {
                if (lr == null) continue;
                int c = lr.positionCount;
                for (int i = 0; i < c; i++)
                {
                    Vector3 p = lr.transform.TransformPoint(lr.GetPosition(i));
                    minY = Mathf.Min(minY, p.y);
                }
            }
            if (minY != float.MaxValue) return minY;
        }

        // fallback to transform position
        return root.transform.position.y;
    }

    // Optional: call this to undo placement and re-enable scanning/UI
    // NOTE: this destroys the anchor if created and re-enables plane detection.
    public void ResetPlacement()
    {
        if (!placed) return;

        // Try to destroy a parent anchor if present
        if (worldRoot && worldRoot.parent)
        {
            var anchorTrans = worldRoot.parent;
            var anchor = anchorTrans.GetComponent<ARAnchor>();
            // detach worldRoot so nav visuals are not lost
            worldRoot.SetParent(null, true);
            if (anchor) Destroy(anchor.gameObject);
        }

        // re-enable plane detection & visuals
        if (planeMgr) planeMgr.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        if (scanViz) scanViz.SetMode(FloorScanVisualizer.Mode.Scanning);
        if (reticle) reticle.SetActive(false);

        if (instructionsPanel) instructionsPanel.SetActive(true);
        if (instructionsCanvasGroup) instructionsCanvasGroup.alpha = 1f;
        if (instruction) instruction.text = "Move your phone to find the floor";
        if (placeButton) placeButton.interactable = false;

        placed = false;
        _validStreak = 0;
        hasPose = false;
        _isStable = false;
    }
}