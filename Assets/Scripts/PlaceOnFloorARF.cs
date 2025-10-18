using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlaceOnFloorARF : MonoBehaviour
{
#if UNITY_EDITOR
[Header("Editor Simulation")]
public bool simulateInEditor = true;
public KeyCode simulatePlaceKey = KeyCode.P; // used when not on new Input System
public float editorPlaneY = 0f;              // fake floor height
public float editorDistance = 1.5f;          // place this far in front of camera
public bool snapToCameraForward = true;      // face the same direction as the camera
#endif

    [Header("AR Foundation")]
    public XROrigin origin;
    public ARRaycastManager raycastMgr;
    public ARPlaneManager planeMgr;
    public ARAnchorManager anchorMgr;
    public Camera arCamera;

    [Tooltip("Building forward (deg) when standing at the door and facing inside. 0=N, 90=E...")]
    public float hallwayHeadingDeg = 0f;
    public bool alignEntranceOnPlace = true;   // auto-align right after placing floor
    public bool useCompassIfAvailable = false; // set true if Player Settings -> Input Handling = Both
    public bool alignToCameraPosition = true;  // otherwise align to the plane hit position

    [Header("Auto Place (optional)")]
    public bool autoPlaceWhenStable = true;
    [Tooltip("If auto-place didn’t happen within this time, show the manual Place button.")]
    public float autoPlaceTimeout = 6f;
    float autoPlaceTimer = 0f;

    [Header("Your app refs")]
    public SmartNavigationSystem nav;     // assign SmartNavigationSystem
    public Transform worldRoot;           // WorldRoot
    public Transform contentAnchor;       // ContentAnchor (child of WorldRoot)

    [Header("UI (optional)")]
    public GameObject reticle;            // your ring; keep disabled in scene
    public TMP_Text instruction;          // “Move phone to find the floor”
    public UnityEngine.UI.Button placeButton;        // optional
    public TMPro.TMP_Text placeButtonLabel;          // optional label on the button

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

    [Header("Entrance")]
    public Transform entranceNode; // drag your Waypoint_Entrance_* here

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

        // UI init
        if (reticle) reticle.SetActive(false);
        if (instruction) instruction.text = "Move your phone to find the floor";

        // Button setup (no duplicates)
        if (placeButton)
        {
            placeButton.onClick.RemoveAllListeners();
            placeButton.onClick.AddListener(PlaceFromButton);
            placeButton.interactable = false;
            // Hide manual Place button if we will auto-place
            placeButton.gameObject.SetActive(!autoPlaceWhenStable);
        }

        autoPlaceTimer = 0f;
        _validStreak = 0;
        hasPose = false;
        _isStable = false;

        if (scanViz) scanViz.SetMode(FloorScanVisualizer.Mode.Scanning);
        if (instructionsPanel) instructionsPanel.SetActive(true);
        if (instructionsCanvasGroup) instructionsCanvasGroup.alpha = 1f;

        // Start heading service
        var hs = HeadingService.Instance ?? FindFirstObjectByType<HeadingService>();
        hs?.StartHeading();
    }

    void OnDisable()
    {
        if (placeButton) placeButton.onClick.RemoveAllListeners();

        // Stop heading service
        var hs = HeadingService.Instance ?? FindFirstObjectByType<HeadingService>();
        hs?.StopHeading();
    }

    void Update()
    {
        // --- Editor simulation (press P) ---
#if UNITY_EDITOR
    if (simulateInEditor && !placed)
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        pressed = Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame;
#else
        pressed = Input.GetKeyDown(simulatePlaceKey);
#endif
        if (pressed)
        {
            var camT = arCamera ? arCamera.transform : (Camera.main ? Camera.main.transform : null);
            if (camT != null)
            {
                Vector3 pos = camT.position + camT.forward * editorDistance;
                pos.y = editorPlaneY;

                Vector3 forward = snapToCameraForward
                                  ? Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized
                                  : Vector3.forward;
                if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;

                Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
                DoPlace(new Pose(pos, rot));
                return;
            }
        }
    }
#endif

        // --- Tap-to-place fallback (optional) ---
        if (!placed && raycastMgr != null && TryGetTouchPosition(out var touchPos))
        {
            if (raycastMgr.Raycast(touchPos, hits, TrackableType.PlaneWithinPolygon))
            {
                var hit = hits[0];
                lastPose = hit.pose;
                hasPose = true;
                _validStreak = stableFramesRequired; // treat as stable
                DoPlace(lastPose);
                return;
            }
        }

        // --- Auto place when stable ---
        if (!placed && autoPlaceWhenStable && hasPose && _validStreak >= stableFramesRequired)
        {
            DoPlace(lastPose);
            return;
        }

        if (placed || !raycastMgr) return;

        // Center-screen raycast
        var screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        bool hasHit = raycastMgr.Raycast(screen, hits, TrackableType.PlaneWithinPolygon);

        if (hasHit)
        {
            var hit = hits[0];
            Pose p = hit.pose;

            lastPose = p;
            hasPose = true;

            // Reticle aligned to plane and camera-facing on plane
            Vector3 camForward = (arCamera != null) ? arCamera.transform.forward
                                 : (Camera.main != null ? Camera.main.transform.forward : Vector3.forward);
            Vector3 camUp = (arCamera != null) ? arCamera.transform.up
                           : (Camera.main != null ? Camera.main.transform.up : Vector3.up);

            Vector3 forwardOnPlane = Vector3.ProjectOnPlane(camForward, p.up);
            if (forwardOnPlane.sqrMagnitude < 1e-6f) forwardOnPlane = Vector3.ProjectOnPlane(camUp, p.up);
            Quaternion rot = Quaternion.LookRotation(forwardOnPlane.normalized, p.up);

            if (reticle) reticle.transform.SetPositionAndRotation(p.position, rot);
            if (reticle && !reticle.activeSelf) reticle.SetActive(true);

            // Stability gating
            float tilt = Vector3.Angle(p.up, Vector3.up);
            bool horizontal = tilt <= maxTiltDegrees;

            _validStreak = horizontal ? _validStreak + 1 : 0;
            _isStable = _validStreak >= stableFramesRequired;

            if (scanViz) scanViz.SetMode(_isStable ? FloorScanVisualizer.Mode.Ready : FloorScanVisualizer.Mode.Scanning);
            if (instruction) instruction.text = _isStable ? "Tap Place to anchor" : "Move your phone to find the floor";
            if (placeButton) placeButton.interactable = _isStable;

            // Reveal manual place after timeout (if auto-place didn’t happen)
            if (autoPlaceWhenStable && !_isStable)
                autoPlaceTimer += Time.deltaTime;
            if (placeButton && autoPlaceWhenStable && autoPlaceTimer >= autoPlaceTimeout)
                placeButton.gameObject.SetActive(true);
        }
        else
        {
            hasPose = false;
            _validStreak = 0;
            _isStable = false;
            if (reticle) reticle.SetActive(false);
            if (scanViz) scanViz.SetMode(FloorScanVisualizer.Mode.Scanning);
            if (instruction) instruction.text = "Move your phone to find the floor";
            if (placeButton) placeButton.interactable = false;
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

        // Tell nav which entrance node to use for "start from entrance"
        if (nav != null && entranceNode != null)
        {
            var mi = nav.GetType().GetMethod("SetEntranceNode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (mi != null) { try { mi.Invoke(nav, new object[] { entranceNode }); } catch { } }
        }

        // 1) Create/attach an anchor at the hit pose
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

        // Tell nav where the real floor Y is and give it the AR raycaster
        if (nav != null)
        {
            TrySetNav("fallbackGroundY", pose.position.y);                     // default Y if raycast misses
            TrySetNav("preferARGroundRaycast", true);
            var rm = raycastMgr ? raycastMgr : FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARRaycastManager>();
            TrySetNav("raycastMgr", rm);
        }

        // 2) Parent content to the anchor
        if (worldRoot != null) worldRoot.SetParent(anchor.transform, false);
        if (contentAnchor != null) contentAnchor.SetParent(worldRoot, false);

        // 3) Decide which floor Y to use for vertical snap
        // On device: use AR plane Y (pose.y). In Editor P-sim: use current model base Y.
        float yForSnap = pose.position.y;
#if UNITY_EDITOR
    if (simulateInEditor)
    {
        GameObject measure = (contentAnchor != null) ? contentAnchor.gameObject
                            : (worldRoot      != null) ? worldRoot.gameObject
                            : null;
        if (measure != null)
        {
            float modelMinY = ComputeLowestWorldY(measure);
            if (!float.IsNaN(modelMinY) && !float.IsInfinity(modelMinY))
                yForSnap = modelMinY;
        }
    }
#endif

        // 4) Push floor info into nav so arrows snap to the same floor (Editor + Device)
        if (nav != null)
        {
            TrySetNav("fallbackGroundY", yForSnap);          // preferred pattern
            TrySetNav("arGroundPlaneY", yForSnap);           // legacy field (if you have it)
            TrySetNav("preferARGroundRaycast", true);        // make nav use AR planes when available

            var rm = raycastMgr ? raycastMgr : FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARRaycastManager>();
            TrySetNav("raycastMgr", rm);
        }

        // 5) After nav positions its content, align + vertical snap, then hide scanning UI
        System.Action afterNav = () =>
        {
            AlignEntranceAfterPlacement(pose);

            // Pass the adjusted Y for snapping (Editor uses model base; device uses AR plane)
            var snapPos = new Vector3(pose.position.x, yForSnap, pose.position.z);
            StartCoroutine(AlignWorldRootToPlaneCoroutine(snapPos));

            // Build any nav caches after rebase
            if (nav != null)
            {
                var mi = nav.GetType().GetMethod("BuildOfficeIndexFromScene",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (mi != null) { try { mi.Invoke(nav, null); } catch { } }
            }

            // Stop detection & hide scanning UI
            if (planeMgr != null) planeMgr.requestedDetectionMode = PlaneDetectionMode.None;
            if (reticle != null) reticle.SetActive(false);
            if (scanViz != null) scanViz.SetMode(FloorScanVisualizer.Mode.Hidden);
            if (placeButton != null) placeButton.interactable = false;
            if (autoHideInstructionsOnPlace && instructionsPanel != null) instructionsPanel.SetActive(false);
            if (instructionsCanvasGroup != null) instructionsCanvasGroup.alpha = 0f;
            if (instruction != null) instruction.text = "Floor set. Select an office.";

            placed = true;

            // Repurpose Place button as Re‑align (optional)
            if (placeButton)
            {
                placeButton.gameObject.SetActive(true);
                placeButton.onClick.RemoveAllListeners();
                placeButton.onClick.AddListener(ReAlignAtCamera);
                var lbl = placeButton.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (lbl) lbl.text = "Re‑align";
                placeButton.interactable = true;
            }
        };

        // 6) Let nav place content, then run afterNav
        if (nav != null)
        {
            // SmartNavigationSystem.PlaceWorld(Vector3 pos, Quaternion rot, Action onComplete)
            nav.PlaceWorld(pose.position, pose.rotation, afterNav);
        }
        else
        {
            afterNav();
        }
    }

    void TrySetNav<T>(string memberName, T value)
    {
        try
        {
            if (nav == null) return;
            var t = nav.GetType();

            var fi = t.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fi != null && (value == null || fi.FieldType.IsAssignableFrom(typeof(T))))
            {
                fi.SetValue(nav, value);
                return;
            }

            var pi = t.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (pi != null && pi.CanWrite && (value == null || pi.PropertyType.IsAssignableFrom(typeof(T))))
            {
                pi.SetValue(nav, value, null);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlaceOnFloorARF] TrySetNav {memberName} failed: {ex.Message}");
        }
    }


    void TryCallNav(string methodName, params object[] args)
    {
        try
        {
            if (nav == null) return;
            var t = nav.GetType();
            var mi = t.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (mi != null) mi.Invoke(nav, args);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlaceOnFloorARF] TryCallNav {methodName} failed: {ex.Message}");
        }
    }

    // Optional tap-to-place helper
    bool TryGetTouchPosition(out Vector2 pos)
    {
#if ENABLE_INPUT_SYSTEM
    pos = default;
    var ts = UnityEngine.InputSystem.Touchscreen.current;
    if (ts == null) return false;
    if (!ts.primaryTouch.press.wasPressedThisFrame) return false;
    pos = ts.primaryTouch.position.ReadValue();
    return true;
#else
        if (Input.touchCount == 0) { pos = default; return false; }
        var t = Input.GetTouch(0);
        if (t.phase != TouchPhase.Began) { pos = default; return false; }
        pos = t.position;
        return true;
#endif
    }

    // Re‑align world root at current camera pose (no new anchor)
    public void ReAlignAtCamera()
    {
        if (!arCamera) arCamera = Camera.main;
        if (!arCamera) return;

        Vector3 fwdOnPlane = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up).normalized;
        if (fwdOnPlane.sqrMagnitude < 1e-6f) fwdOnPlane = Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(fwdOnPlane, Vector3.up);
        var pose = new Pose(arCamera.transform.position, rot);

        AlignEntranceAfterPlacement(pose);

        // Re-snap content to plane height
        StartCoroutine(AlignWorldRootToPlaneCoroutine(pose.position));

        // Rebuild nav indices if needed
        if (nav != null)
        {
            var mi = nav.GetType().GetMethod("BuildOfficeIndexFromScene",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (mi != null) { try { mi.Invoke(nav, null); } catch { } }
        }
    }

    System.Collections.IEnumerator AlignWorldRootToPlaneCoroutine(Vector3 planePosition)
    {
        // Let tracking settle
        yield return null;
        yield return new WaitForEndOfFrame();

        GameObject measure = (contentAnchor != null) ? contentAnchor.gameObject
                          : (worldRoot != null) ? worldRoot.gameObject
                          : null;
        if (measure == null)
        {
            Debug.LogWarning("AlignWorldRoot: nothing to measure.");
            yield break;
        }

        float worldMinY = ComputeLowestWorldY(measure);
        float planeY = planePosition.y;

        // Inform nav about floor Y + AR raycaster (reflection: no compile-time dependency)
        if (nav != null)
        {
            TrySetNav("fallbackGroundY", planeY);
            TrySetNav("arGroundPlaneY", planeY);
            TrySetNav("preferARGroundRaycast", true);

            var rm = raycastMgr ? raycastMgr : FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARRaycastManager>();
            TrySetNav("raycastMgr", rm);

            Debug.Log($"[AR] Provided planeY={planeY:F3} to nav; AR raycast mgr set = {(rm != null)}");

            // Optional hooks if your nav exposes them
            TryCallNav("SnapPathYsToPlaneY", planeY);
            TryCallNav("ReparentPathRoot", worldRoot);
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

    void AlignEntranceAfterPlacement(Pose planePose)
    {
        if (!alignEntranceOnPlace || entranceNode == null || worldRoot == null) return;

        var cam = arCamera ? arCamera.transform : (Camera.main ? Camera.main.transform : null);
        if (!cam) return;

        // Heading: compass (optional) or camera yaw
        float deviceHeading;
        if (useCompassIfAvailable)
        {
#if !ENABLE_INPUT_SYSTEM
            Input.compass.enabled = true;
            deviceHeading = Input.compass.trueHeading;   // 0..360
#else
        deviceHeading = cam.eulerAngles.y;           // fallback
#endif
        }
        else
        {
            deviceHeading = cam.eulerAngles.y;           // not using compass
        }

        float yawDelta = useCompassIfAvailable
            ? Mathf.DeltaAngle(deviceHeading, hallwayHeadingDeg)   // steer to true hallway direction
            : 0f;                                                  // just match the camera facing


        Vector3 targetPos = alignToCameraPosition ? cam.position : planePose.position;
        Quaternion targetRot = Quaternion.Euler(0f, cam.eulerAngles.y + yawDelta, 0f);

        MakeEntranceAppearAt(worldRoot, entranceNode, targetPos, targetRot);

        var hs = HeadingService.Instance ?? FindFirstObjectByType<HeadingService>();
        hs?.StartHeading();
    }

    // Moves/rotates contentRoot so that 'entrance' ends up at targetPos/targetRot (XROrigin-friendly)
    static void MakeEntranceAppearAt(Transform contentRoot, Transform entrance, Vector3 targetPos, Quaternion targetRot)
    {
        if (contentRoot == null || entrance == null) return;

        // Entrance expressed in the contentRoot's space
        Vector3 localPos = contentRoot.InverseTransformPoint(entrance.position);
        Quaternion localRot = Quaternion.Inverse(contentRoot.rotation) * entrance.rotation;

        // Solve new root transform so entrance reaches the desired pose:
        // targetRot = R_root' * localRot  =>  R_root' = targetRot * Inverse(localRot)
        Quaternion newRootRot = targetRot * Quaternion.Inverse(localRot);

        // targetPos = P_root' + R_root' * localPos  =>  P_root' = targetPos - R_root' * localPos
        Vector3 newRootPos = targetPos - (newRootRot * localPos);

        contentRoot.SetPositionAndRotation(newRootPos, newRootRot);
    }

}