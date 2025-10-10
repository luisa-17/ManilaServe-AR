
#if VUFORIA_PRESENT
using Vuforia;
#endif

using UnityEngine;
    using UnityEngine.UI;
    using TMPro;
    using System.Collections.Generic;
    using System.Collections;   // ✅ needed for IEnumerator
    using System.Linq;
    using UnityEngine.Rendering;
using System;
using Random = UnityEngine.Random;



public class SmartNavigationSystem : MonoBehaviour

{

    private Camera arCamera { get { return Camera.main; } }

    [Header("Vuforia Integration")]
    [Tooltip("Enable to use Vuforia image targets for positioning")]
    public bool useVuforiaPositioning = false;

    [Tooltip("Assign your Vuforia image target transforms here")]
    public Transform[] vuforiaImageTargets;

    [Tooltip("Names of the image targets (should match Vuforia database)")]
    public string[] imageTargetNames;

    [Header("Popup-first Guard")]
    public bool enforcePopupFirst = true;

    [Tooltip("Current detected image target position")]
    public Vector3 lastDetectedPosition = Vector3.zero;

    [Tooltip("Is any image target currently being tracked?")]
    public bool isImageTargetTracked = false;

    // Bake flag: true once the world has been placed (markerless or marker-based)
    [SerializeField] private bool worldBaked = false;

    [Header("World horizontal ground (ARCore)")]
    bool groundPlaneReady = false;
    Vector3 groundPlanePoint;
    Vector3 groundPlaneNormal = Vector3.up;

    [Header("Turn Hints (Optional)")]
    public TMPro.TMP_Text turnText; // assign DirectionText if you want
    [Range(1f, 10f)] public float turnAnnounceDistance = 3f;
    [Range(0.3f, 3f)] public float turnPassDistance = 0.7f;
    struct TurnEvent { public int index; public Vector3 pos; public int dir; /* -1 left, +1 right, 0 u/straight */ }
    readonly System.Collections.Generic.List<TurnEvent> _turns = new();
    int _nextTurn = -1;
    bool _turnAnnounced = false;

    private Queue<GameObject> arrowPool = new Queue<GameObject>();
    public int arrowPoolPrewarm = 24;

    [Header("Debug (optional)")]
    [Tooltip("Assign any debug/test GameObjects you want hidden while navigating")]
    public GameObject[] debugObjectsToHideDuringNavigation;

    private Dictionary<GameObject, bool> _debugOriginalActive;

    [HideInInspector] public string currentUserOffice = ""; // User's current office location
    [HideInInspector] public bool useOfficeAsStart = false; // Whether to use office position instead of camera


    // Auto-locate anchors if not assigned (so PathRoot has a valid parent)
    void ResolveAnchors()
    {
        if (!centerAnchor)
        {
            var go = GameObject.Find("center_anchor");
            if (go) centerAnchor = go.transform;
        }
        if (!contentAnchor)
        {
            // Prefer center_anchor/ContentAnchor if it exists
            if (centerAnchor)
            {
                var ca = centerAnchor.Find("ContentAnchor");
                if (ca) contentAnchor = ca;
            }
            if (!contentAnchor)
            {
                var go = GameObject.Find("ContentAnchor");
                if (go) contentAnchor = go.transform;
            }
        }
    }

    [Header("AR Ground Plane")]
    public float arGroundPlaneY = 0f; // Set by PlaceOnFloorARF after detection
    public bool useARGroundPlane = true; // Toggle to use AR plane instead of model floors

    [Header("Building Alignment")]
    public Transform worldRoot; // Drag your WorldRoot GameObject here

    [Header("Optional")]
    [Tooltip("Assign the AR Camera transform (optional). If empty, Camera.main is used.)")]
    public Transform userCamera;

    void OnEnable()
    {
#if VUFORIA_PRESENT
if (!hudOnly && useVuforiaPositioning && centerAnchor)
{
var ob = centerAnchor.GetComponent<Vuforia.ObserverBehaviour>();
if (ob) ob.OnTargetStatusChanged += OnCenterStatus;
}
#endif
    }

    void OnDisable()
    {
#if VUFORIA_PRESENT
if (!hudOnly && useVuforiaPositioning && centerAnchor)
{
var ob = centerAnchor.GetComponent<Vuforia.ObserverBehaviour>();
if (ob) ob.OnTargetStatusChanged -= OnCenterStatus;
}
#endif
    }

#if VUFORIA_PRESENT
void OnCenterStatus(Vuforia.ObserverBehaviour b, Vuforia.TargetStatus s)
{
if (hudOnly || !useVuforiaPositioning) return;
bool tracked = s.Status == Vuforia.Status.TRACKED || s.Status == Vuforia.Status.EXTENDED_TRACKED;
if (tracked && !worldBaked)
{
AlignAnchorYawToCamera();
SetFloorsVisible(false);
ClearPathArrows();
if (statusText) statusText.text = "Anchor found. Select an office.";
worldBaked = true;
b.OnTargetStatusChanged -= OnCenterStatus;
Debug.Log("[Anchor] baked under WorldRoot.");
}
}
#endif

    // add once near the other fields at top of SmartNavigationSystem class
    [HideInInspector] public bool startedFromOffice = false;

    // --- Arrow / path fields (keep exactly one copy of these) ---
    [Header("Proxy ground (flatten arrows)")]
    public Transform floorProxy;                 // created at runtime
    [Range(10f, 200f)] public float proxySize = 100f;


    [ContextMenu("Preset: ARCore + Vuforia (World)")]
    void PresetWorldAR()
    {
        hudOnly = false;
        useVuforiaPositioning = true;
        hideWorldWhenUnanchored = false; // don’t hide world on marker loss
        pathStyle = PathVisualStyle.Arrows;
        useProxyFloor = true;
        lockArrowsToFloor = true;
        Debug.Log("[Preset] World AR mode set. Ensure center_anchor + ContentAnchor assigned.");
    }

    [Header("UI Elements")]
    public Button navigateButton;
    public Button stopButton;
    public TextMeshProUGUI statusText;

    public bool hudOnly = true; // set true for HUD mode (no world meshes, no markers)

    [Header("HUD-only PDR (no ARCore)")]
    public bool hudUsePdr = true;                 // enable step-based progress in HUD
    public Transform hudStartWaypoint;            // optional: drag a known start (entrance)
    [HideInInspector] public Vector3 hudUserPos;  // virtual user position in world (HUD mode)
    public float stepLengthMeters = 0.75f;        // avg step length
    [Tooltip("Rotate building 'north' so device heading aligns with your map. 0 if already aligned.")]
    public float hudWorldNorthOffset = 0f;        // degrees
    SimpleStepEstimator stepper;                  // cached

    // ADDED: Optional direction UI elements (won't break if missing)
    [Header("Direction UI (Optional)")]
    public TextMeshProUGUI directionText;
    public TextMeshProUGUI distanceText;

    [Header("Enhanced 3D Arrow Navigation")]
    public GameObject arrowPrefab;               // arrow prefab to instantiate
    public Material arrowMaterial;
    public Color arrowColor = Color.cyan;
    public float arrowSpacing = 3f;
    public float arrowSize = 0.6f;
    public float groundOffset = 0.1f;
    public bool useGroundDetection = true;

    [Header("Arrow spawn / scale")]
    public float arrowBaseScale = 1.0f;
    public float arrowVerticalOffset = 0.02f;

    [HideInInspector] public List<GameObject> pathArrows = new List<GameObject>();
    [HideInInspector] public List<Vector3> currentPath = new List<Vector3>();

    // optional organization transform
    [SerializeField] private Transform pathRoot; // will be created if null

    [Header("Arrow Shape")]
    [Tooltip("Length of the shaft (Z) in meters")]
    public float shaftLength = 1.5f;
    [Tooltip("Width of the shaft (X) in meters")]
    public float shaftWidth = 0.4f;
    [Tooltip("Length of the head (Z) in meters")]
    public float headLength = 0.6f;
    [Tooltip("Width of the head (X) in meters")]
    public float headWidth = 0.8f;

    [Header("Arrow Orientation")]
    [Tooltip("Allow tilt on stairs to match ramp direction")]
    public bool tiltOnStairs = true;

    [Header("Arrow FX")]
    [Tooltip("Don’t spawn arrows within this distance of the camera")]
    [Range(0f, 2f)] public float arrowStartGap = 0.7f;

    [Tooltip("Arrow scale near the camera")]
    [Range(0.1f, 2f)] public float arrowNearScale = 0.7f;

    [Tooltip("Arrow scale far from the camera")]
    [Range(0.1f, 2f)] public float arrowFarScale = 0.45f;

    [Tooltip("Distance (m) over which we blend near->far scale")]
    [Range(2f, 30f)] public float arrowScaleDistance = 12f;

    [Tooltip("Animate arrows with a subtle pulse")]
    public bool arrowPulse = true;
    [Range(0f, 0.5f)] public float arrowPulseAmplitude = 0.12f;
    [Range(0.1f, 8f)] public float arrowPulseSpeed = 2f;

    [Tooltip("Recompute base scale as you move (smooth). If off, arrows keep the scale they had at spawn.")]
    public bool liveDistanceScaling = false;

    [Header("Target Marker")]
    public GameObject targetMarker;
    public Material targetMaterial;
    public Color targetColor = Color.red;
    public float bobSpeed = 2f;
    public float bobHeight = 0.2f;
    public float rotationSpeed = 90f;

    // Single source of truth helpers
    private bool ProxyReady => useProxyFloor && floorProxy != null;
    private float ProxyY => ProxyReady
        ? floorProxy.position.y + groundOffset
        : (worldRoot ? worldRoot.position.y + groundOffset : groundOffset);

    [Header("Wall Detection")]
    public LayerMask wallLayerMask = -1;
    public float wallCheckRadius = 1f;
    public bool enableWallDetection = true;
    public bool debugWallDetection = true;

    [Header("AR Ground Detection")]
    public LayerMask groundLayerMask = 1;
    public float groundRayDistance = 10f;

    [Header("Arrow Grounding")]
    public bool lockArrowsToFloor = true;  // always stick to a fixed floor height
    public float groundFloorY = 0f;
    public float secondFloorY = 10f;

    [Header("Floors")]
    public GameObject groundFloor;   // Drag your GroundFloor_Building object here
    public GameObject secondFloor;   // Drag your SecondFloor GameObject here in Inspector

    [Header("Departments")]
    public Transform[] departmentWaypoints;
    public Transform[] corridorWaypoints;
    public string[] departmentNames;

    [Header("Localization")]
    public bool requireInitialLocalization = true; // require scanning the anchor once
    private bool hasEverLocalized = false; // becomes true after first scan

    [Header("Connections")]
    public float connectionDistance = 3f; // tighter distance

    private string currentFloor = "Ground";  // or "Second"

    [Header("Pathfinding Modes")]
    public bool graphIsAuthoritative = true; // ← Make sure this is TRUE

    [Header("Path preferences")]
    public bool allowDirectStraightPath = false;
    public bool useLOSForSmoothing = false; // ← Make sure this is FALSE

    [Header("Anchors")]
    [SerializeField] private Transform contentAnchor; // drag center_anchor/ContentAnchor here


    // add near your other enums/headers
    public enum PathVisualStyle { Arrows, Dots, FlowLine }

    [Header("Path Visuals")]
    public PathVisualStyle pathStyle = PathVisualStyle.FlowLine;
    public Material flowLineMaterial; // URP/Unlit with dashed texture
    [Range(0.02f, 0.2f)] public float lineWidth = 0.06f;
    [Range(0.20f, 5f)] public float sampleStep = 0.5f;
    [Range(0.5f, 8f)] public float dashTiling = 3f;
    [Range(0.1f, 4f)] public float flowSpeed = 1.5f;

    // NEW: keep the line from starting too close to the camera
    [Range(0.2f, 2f)] public float startGapMeters = 0.6f;

    // internal
    LineRenderer flowLine;
    float flowT;

    // Anchor binding
    public Transform centerAnchor;                 // drag your Image Target (center_anchor)
    public bool centerAnchorIsOnWall = true;       // true if the print is on a wall
    public bool hideWorldWhenUnanchored = true;    // hide world unless the marker is tracked
    public Vector3 contentLocalOffset = new Vector3(0f, 0.6f, 0f); // push world out from wall (meters)
    public float faceUserYawBlend = 1f;            // 0..1; 1 = fully face user yaw

    // Current navigation state
    private bool isNavigating = false;
    private Vector3 targetDestination;
    private string currentDestination = "";
    private Camera playerCamera;

    // ADDED: Turn-by-turn variables (minimal addition)
    private int currentPathIndex = 0;
    private Vector3 lastPlayerPosition;

    // 3D Navigation objects
    private float targetBobOffset = 0f;
    private Vector3 originalTargetPosition;

    public FloorManager floorManager; // Assign this in the Inspector
    private Material arrowSharedMat;

    private Dictionary<string, GameObject> officeLookup = new Dictionary<string, GameObject>();

    // Returns all NavigationWaypoint components that actually live in the loaded scene
    // (filters out prefab assets and editor-only objects).
    static NavigationWaypoint[] GetAllRuntimeWaypoints()
    {
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();
        var list = new System.Collections.Generic.List<NavigationWaypoint>();
        foreach (var wp in all)
            if (wp && wp.gameObject.scene.IsValid())
                list.Add(wp);
        return list.ToArray();
    }

    [Header("Arrival Detection")]
    [Tooltip("Distance threshold to consider user has arrived at destination")]
    [Range(0.5f, 5f)] public float arrivalDistanceThreshold = 2.0f;

    [Tooltip("Time user must stay within threshold to confirm arrival")]
    [Range(0f, 3f)] public float arrivalConfirmTime = 1.0f;

    [Tooltip("Show arrival notification")]
    public bool enableArrivalNotification = true;

    private float timeWithinArrivalZone = 0f;
    private bool hasShownArrivalNotification = false;


    public List<Vector3> GetCurrentPathWorld() => currentPath;

    // Read-only access to the current target destination
    public Vector3 TargetDestination => targetDestination;

    // Final point helper (last path point if available, otherwise targetDestination)
    public Vector3 GetFinalPathPoint()
    {
        if (currentPath != null && currentPath.Count > 0)
            return currentPath[currentPath.Count - 1];
        return targetDestination;
    }

    public bool useProxyFloor = true;
    public bool pathPositionsAreLocalToWorldRoot = false;

    // Ensure pathArrows list exists and clear existing arrows safely
    public void ClearPathArrowsIfNeeded()
    {
        if (pathArrows == null) pathArrows = new System.Collections.Generic.List<GameObject>();
        for (int i = pathArrows.Count - 1; i >= 0; i--)
        {
            var go = pathArrows[i];
            if (go == null) continue;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        pathArrows.Clear();
    }

    public void CreateGroundArrowsForPath()
    {
        if (hudOnly) return;

        Debug.Log($"[ARROWS] CreateGroundArrowsForPath called");
        Debug.Log($"[ARROWS] currentPath count: {currentPath?.Count ?? 0}");
        Debug.Log($"[ARROWS] arrowPrefab: {(arrowPrefab != null ? "EXISTS" : "NULL")}");
        Debug.Log($"[ARROWS] pathRoot: {(pathRoot != null ? "EXISTS" : "NULL")}");

        EnsureActiveChain();
        ClearPathArrowsIfNeeded();
        ClearFlowLine();

        if (currentPath == null || currentPath.Count < 2)
        {
            Debug.LogWarning("[ARROWS] Path too short or null");
            return;
        }

        // Ensure prefab exists
        if (arrowPrefab == null)
        {
            Debug.LogError("[ARROWS] arrowPrefab is NULL! Creating default...");
            CreateEnhancedArrowPrefab();
            if (arrowPrefab == null)
            {
                Debug.LogError("[ARROWS] Failed to create arrow prefab!");
                return;
            }
        }

        switch (pathStyle)
        {
            case PathVisualStyle.Arrows:
                Debug.Log("[ARROWS] Creating arrows...");
                for (int i = 0; i < currentPath.Count - 1; i++)
                {
                    Debug.Log($"[ARROWS] Segment {i}: {currentPath[i]} → {currentPath[i + 1]}");
                    CreateGroundArrowsForSegment(currentPath[i], currentPath[i + 1]);
                }
                Debug.Log($"[ARROWS] Total arrows created: {pathArrows?.Count ?? 0}");

                // Force arrows visible
                if (pathArrows != null)
                {
                    foreach (var arrow in pathArrows)
                    {
                        if (arrow != null)
                        {
                            arrow.SetActive(true);
                            Debug.Log($"[ARROWS] Arrow at {arrow.transform.position} - Active: {arrow.activeSelf}");
                        }
                    }
                }
                break;

            case PathVisualStyle.FlowLine:
                BuildFlowLineFromCurrentPath();
                break;
        }
    }
    
    
    void Start()
    {
        playerCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        ResolveAnchors();

        // Set floor layers without touching camera culling mask
        int floorLayer = LayerMask.NameToLayer("FloorPlan");
        if (floorLayer >= 0)
        {
            if (groundFloor) SetLayerRecursively(groundFloor, floorLayer);
            if (secondFloor) SetLayerRecursively(secondFloor, floorLayer);

            // Ensure raycasts include the FloorPlan layer
            if ((groundLayerMask.value & (1 << floorLayer)) == 0)
                groundLayerMask |= (1 << floorLayer);
        }
        else
        {
            Debug.LogWarning("Layer 'FloorPlan' not found. Please add it in Project Settings > Tags and Layers.");
        }

        // Hide floors using renderer disable instead of culling mask
        SetFloorsVisible(false);

        if (!hudOnly)
        {
            if (hideWorldWhenUnanchored && contentAnchor)
                SetWorldVisible(false);
        }

        if (!hudOnly && contentAnchor && !contentAnchor.gameObject.activeSelf)
            contentAnchor.gameObject.SetActive(true);

        arrowSharedMat = arrowMaterial != null ? arrowMaterial : CreateArrowMaterial(arrowColor);

        CreateEnhancedArrowPrefab();
        CreateEnhancedTargetMarker();
        FindAllWaypoints();

        SetupUI();
        SetupVuforiaImageTargets();

        CacheAllOffices();

        var ui = FindFirstObjectByType<ManilaServeUI>();
        if (ui != null)
        {
            ui.PopulateOfficeDropdown();
        }

        if (hudOnly)
        {
            useVuforiaPositioning = false;
            var hider = FindFirstObjectByType<HideWorldVisuals>();
            if (hider) hider.hudOnly = true;
        }

        cachedWaypoints = GetAllRuntimeWaypoints();
        Debug.Log($"Smart Navigation ready (Vuforia: {useVuforiaPositioning})");
    }

    void SetupVuforiaImageTargets()
    {
        if (!useVuforiaPositioning) return;

        // Find all Vuforia image targets in the scene if not manually assigned
        if (vuforiaImageTargets == null || vuforiaImageTargets.Length == 0)
        {
            // Look for GameObjects with "ImageTarget" in their name
            GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            List<Transform> foundTargets = new List<Transform>();
            List<string> foundNames = new List<string>();

            foreach (GameObject obj in allObjects)
            {
                if (obj.name.ToLower().Contains("imagetarget") || obj.name.ToLower().Contains("target"))
                {
                    foundTargets.Add(obj.transform);
                    foundNames.Add(obj.name);
                }
            }

            vuforiaImageTargets = foundTargets.ToArray();
            imageTargetNames = foundNames.ToArray();
        }

        Debug.Log($"Found {vuforiaImageTargets.Length} Vuforia image targets");
    }

    // Call this method from your Vuforia tracking scripts when an image is detected
    [SerializeField] float repathDistThreshold = 0.25f; // meters
    [SerializeField] float repathAngleThreshold = 10f; // degrees
    Vector3 lastLocalizedCamPos;
    Quaternion lastLocalizedCamRot;

    public void OnImageTargetDetected(string targetName, Vector3 targetPoseFromRelay)
    {
        // HUD-only mode: ignore markers completely
        if (hudOnly || !useVuforiaPositioning) return;

        isImageTargetTracked = true;
        hasEverLocalized = true;

        if (!hudOnly && hideWorldWhenUnanchored && contentAnchor)
            contentAnchor.gameObject.SetActive(true);

        // Use CAMERA pose (not the marker)
        Vector3 camPos = arCamera ? arCamera.transform.position : targetPoseFromRelay;
        Quaternion camRot = arCamera ? arCamera.transform.rotation : Quaternion.identity;

        lastDetectedPosition = camPos;

        bool poseChanged =
            Vector3.Distance(lastLocalizedCamPos, camPos) > repathDistThreshold ||
            Quaternion.Angle(lastLocalizedCamRot, camRot) > repathAngleThreshold;

        if (statusText) statusText.text = $"Image detected: {targetName}";

        if (isNavigating && poseChanged)
        {
            lastLocalizedCamPos = camPos;
            lastLocalizedCamRot = camRot;

            CalculateWallAvoidingPath();
            if (!hudOnly)  // world mode only
            {
                ClearPathArrows();
                CreateGroundArrowsForPath();
            }
        }

        AlignAnchorYawToCamera(); // this snaps the content anchor to the real-world marker
    }


    void PrewarmArrowPool()
    {
        EnsurePathRoot();
        if (arrowPrefab == null) return;
        for (int i = 0; i < arrowPoolPrewarm; i++)
        {
            var go = Instantiate(arrowPrefab, pathRoot);
            go.SetActive(false);
            arrowPool.Enqueue(go);
        }
    }

    GameObject GetArrowFromPool()
    {
        if (arrowPool.Count > 0) return arrowPool.Dequeue();
        var go = Instantiate(arrowPrefab, pathRoot);
        go.SetActive(false);
        return go;
    }

    private NavigationWaypoint[] cachedWaypoints;

    void ReturnArrowToPool(GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        arrowPool.Enqueue(go);
    }

    public void OnImageTargetLost()
    {/*
        if (hudOnly || !useVuforiaPositioning) return;
        isImageTargetTracked = false;

        if (statusText != null && !isNavigating)
        {
            statusText.text = hasEverLocalized
                ? "Look at the sign to re-center"
                : "Point at the center sign to localize";
        }

        if (!hudOnly && hideWorldWhenUnanchored && contentAnchor)
            contentAnchor.gameObject.SetActive(false);

        // Optional world cleanup
        if (!hudOnly)
        {
            ClearPathArrows();
            // if (contentAnchor) contentAnchor.gameObject.SetActive(false);
        }
   */
    }


    Vector3 GetCurrentPosition()
    {
        // MODE 1: Manual office selection (if enabled)
        if (useOfficeAsStart && !string.IsNullOrEmpty(currentUserOffice))
        {
            var officeWP = FindTargetWaypointAdvanced(currentUserOffice);
            if (officeWP != null)
            {
                Debug.Log($"[GetCurrentPosition] Using manual office: {currentUserOffice} at {officeWP.transform.position}");
                return officeWP.transform.position;
            }
            else
            {
                Debug.LogWarning($"[GetCurrentPosition] Office waypoint not found: {currentUserOffice}, falling back to camera");
            }
        }

        // MODE 2: Auto-detection (camera position) - fallback or default
        if (arCamera)
        {
            Debug.Log($"[GetCurrentPosition] Using camera position: {arCamera.transform.position}");
            return arCamera.transform.position;
        }

        if (Camera.main) return Camera.main.transform.position;

        return Vector3.zero;
    }

    void CreateEnhancedArrowPrefab()
    {
        // If you assign a prefab in the Inspector, we won’t generate one
        if (arrowPrefab != null) return;

        arrowPrefab = new GameObject("ArrowPrefab");

        // Shaft
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaft.name = "Shaft";
        shaft.transform.SetParent(arrowPrefab.transform, false);
        shaft.transform.localScale = new Vector3(shaftWidth, 0.3f, shaftLength);
        // Centered shaft: sit at origin so the whole arrow is centered
        shaft.transform.localPosition = new Vector3(0f, 0f, shaftLength * 0.5f - 0.75f); // slight bias so head is front-most

        // Head
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.name = "Head";
        head.transform.SetParent(arrowPrefab.transform, false);
        head.transform.localScale = new Vector3(headWidth, 0.3f, headLength);
        head.transform.localPosition = new Vector3(0f, 0f, shaft.transform.localPosition.z + (shaftLength * 0.5f) + (headLength * 0.5f) - 0.05f);

        // Material (URP-safe)
        var mat = arrowSharedMat ??= CreateArrowMaterial(arrowColor);
        shaft.GetComponent<Renderer>().sharedMaterial = mat;
        head.GetComponent<Renderer>().sharedMaterial = mat;

        // No physics
        Destroy(shaft.GetComponent<Collider>());
        Destroy(head.GetComponent<Collider>());

        arrowPrefab.SetActive(false);
        Debug.Log("ArrowPrefab generated (runtime)");
    }

    void CreateEnhancedTargetMarker()
    {
        if (targetMarker != null)
        {
            Debug.Log("Target marker already exists");
            return;
        }

        Debug.Log("Creating enhanced target marker...");
        targetMarker = new GameObject("TargetMarker");

        // Base ring (cylinder)
        GameObject baseRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseRing.name = "BaseRing";
        baseRing.transform.SetParent(targetMarker.transform, false);
        baseRing.transform.localPosition = Vector3.zero;
        baseRing.transform.localScale = new Vector3(2.0f, 0.1f, 2.0f);

        // Beacon sphere
        GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        beacon.name = "Beacon";
        beacon.transform.SetParent(targetMarker.transform, false);
        beacon.transform.localPosition = new Vector3(0, 1.5f, 0);
        beacon.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

        // Create glowing material
        if (targetMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            targetMaterial = new Material(shader);
            targetMaterial.color = targetColor;

            // Add emission for glow
            if (targetMaterial.HasProperty("_EmissionColor"))
            {
                targetMaterial.EnableKeyword("_EMISSION");
                targetMaterial.SetColor("_EmissionColor", targetColor * 2f);
            }

            // Ensure it renders on top
            targetMaterial.renderQueue = 3000;
        }

        // Apply materials
        var baseRenderer = baseRing.GetComponent<Renderer>();
        var beaconRenderer = beacon.GetComponent<Renderer>();

        baseRenderer.material = targetMaterial;
        beaconRenderer.material = targetMaterial;

        // Remove colliders (no physics needed)
        if (baseRing.GetComponent<Collider>()) DestroyImmediate(baseRing.GetComponent<Collider>());
        if (beacon.GetComponent<Collider>()) DestroyImmediate(beacon.GetComponent<Collider>());

        targetMarker.SetActive(false);
        Debug.Log("✅ Enhanced target marker created successfully");
    }

    Material CreateArrowMaterial(Color c)
    {
        // Prefer URP; fallback to Built‑in so it also works in the Editor if URP isn’t active
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) shader = Shader.Find("Unlit/Color");

        var m = new Material(shader);

        // URP Unlit uses _BaseColor; Built-in Unlit uses color
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        else m.color = c;

        // Draw on top of camera feed reliably
        m.renderQueue = 3000;
        return m;
    }

    [ContextMenu("Auto-Connect Waypoints")]
    public void AutoConnectWaypoints()
    {
        var wps = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        int added = 0;
        for (int i = 0; i < wps.Length; i++)
        {
            for (int j = i + 1; j < wps.Length; j++)
            {
                var a = wps[i];
                var b = wps[j];

                float d = Vector3.Distance(a.transform.position, b.transform.position);
                if (d <= connectionDistance && IsPathClear(a.transform.position, b.transform.position))
                {
                    if (a.connectedWaypoints == null) a.connectedWaypoints = new System.Collections.Generic.List<NavigationWaypoint>();
                    if (b.connectedWaypoints == null) b.connectedWaypoints = new System.Collections.Generic.List<NavigationWaypoint>();
                    if (!a.connectedWaypoints.Contains(b)) a.connectedWaypoints.Add(b);
                    if (!b.connectedWaypoints.Contains(a)) b.connectedWaypoints.Add(a);
                    added++;
                }
            }
        }
        Debug.Log($"Auto-Connect Waypoints: added {added} links (<= {connectionDistance}m, LOS clear)");
    }

    public void FindAllWaypoints()
    {
        var allWaypoints = GetAllRuntimeWaypoints();

        // Debug dump (optional)
        foreach (var wp in allWaypoints)
            Debug.Log($"[WAYPOINT] {wp.waypointName} ({wp.waypointType}) at {wp.transform.position}");

        var foundDepartments = new System.Collections.Generic.List<Transform>();
        var foundNames = new System.Collections.Generic.List<string>();

        // ONLY include Office type waypoints for user selection
        foreach (var waypoint in allWaypoints)
        {
            if (waypoint.waypointType == WaypointType.Office) // ← ONLY OFFICES
            {
                foundDepartments.Add(waypoint.transform);
                string cleanName = !string.IsNullOrEmpty(waypoint.officeName) ? waypoint.officeName : waypoint.waypointName;
                foundNames.Add(cleanName);
                Debug.Log($"[OFFICE] Added to dropdown: {cleanName}");
            }
        }

        departmentWaypoints = foundDepartments.ToArray();
        departmentNames = foundNames.ToArray();

        Debug.Log($"Found {departmentWaypoints.Length} OFFICES for dropdown (filtered from {allWaypoints.Length} total waypoints)");
    }

    void Awake()
    {
        // Ensure departments are discovered before any UI tries to read them
        FindAllWaypoints();
    }

    void SetupUI()
    {
        // Find UI elements but don't set up navigate button listener
        if (navigateButton == null)
            navigateButton = GameObject.Find("NavigateButton")?.GetComponent<Button>();
        if (stopButton == null)
            stopButton = GameObject.Find("StopButton")?.GetComponent<Button>();
        if (statusText == null)
            statusText = GameObject.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        if (directionText == null)
            directionText = GameObject.Find("DirectionText")?.GetComponent<TextMeshProUGUI>();
        if (distanceText == null)
            distanceText = GameObject.Find("DistanceText")?.GetComponent<TextMeshProUGUI>();

        // REMOVED: navigateButton.onClick.AddListener(StartNavigation);
        // ManilaServeUI's popup will handle navigation now

        if (stopButton != null)
            stopButton.onClick.AddListener(StopNavigation);

        if (statusText != null)
        {
            if (useVuforiaPositioning)
            {
                statusText.text = "Point camera at office sign to detect location";
            }
            else
            {
                statusText.text = "Select an office to navigate";
            }
        }

        if (stopButton != null)
            stopButton.gameObject.SetActive(false);
    }

    void SetWorldVisible(bool on)
    {
        if (!contentAnchor) return;
        // Toggle only renderers/colliders so waypoints remain active
        foreach (var r in contentAnchor.GetComponentsInChildren<Renderer>(true))
            if (!r.GetComponentInParent<NavigationWaypoint>()) r.enabled = on;

        foreach (var c in contentAnchor.GetComponentsInChildren<Collider>(true))
            if (!c.GetComponentInParent<NavigationWaypoint>()) c.enabled = on;

        if (pathRoot) pathRoot.gameObject.SetActive(on);
    }

    // Always show the popup first; UI will call StartNavigationConfirmedByUI when user confirms
    public void StartNavigationToOffice(string officeName)
    {
        if (enforcePopupFirst)
        {
            var ui = FindFirstObjectByType<ManilaServeUI>();
            if (ui != null) { ui.ShowOfficeInfoPopup(officeName); return; }
            Debug.LogWarning("ManilaServeUI not found; proceeding without popup.");
        }
        StartNavigationProceed(officeName);
    }

    // Add to SmartNavigationSystem
    public bool IsWorldPlaced => worldBaked;

    public void StartNavigationConfirmedByUI(string officeName)
    {
        if (isNavigating) { Debug.Log("Already navigating. Press Cancel first."); return; }
        StartNavigationProceed(officeName);
    }

    public bool IsNavigating => isNavigating;

    // Everything that actually starts navigation lives here (moved from StartNavigationToOffice)
    public void StartNavigationProceed(string officeName)
    {
        // If navigation was started manually from an office dropdown, skip the automatic start.
        if (startedFromOffice)
        {
            Debug.Log("[NAV] StartNavigationProceed: manual start in effect, aborting automatic start.");
            startedFromOffice = false; // clear the flag so future automatic starts can run
            return;
        }

        if (!hudOnly && useVuforiaPositioning)
            AlignAnchorYawToCamera();

        // Mark localized so Vuforia requirement doesn’t block world mode
        hasEverLocalized = true;

        Debug.Log($"[NAV] Finding waypoint for: {officeName}");
        Transform targetWaypoint = FindTargetWaypointAdvanced(officeName);
        if (targetWaypoint == null)
        {
            if (!officeLookup.TryGetValue(officeName, out GameObject targetObject) || targetObject == null)
            {
                Debug.LogError($"[NAV] Waypoint not found for: {officeName}");
                return;
            }
            var waypoint = targetObject.GetComponent<NavigationWaypoint>();
            if (!waypoint)
            {
                waypoint = targetObject.AddComponent<NavigationWaypoint>();
                waypoint.waypointName = officeName;
                waypoint.officeName = officeName;
                waypoint.waypointType = WaypointType.Office;
            }
            targetWaypoint = targetObject.transform;
        }

        // Init nav state
        targetDestination = targetWaypoint.position;
        originalTargetPosition = targetDestination;
        currentDestination = officeName;

        // Keep floor plan hidden
        SetFloorsVisible(false);

        Debug.Log($"[NAV-DBG] targetWaypoint={targetWaypoint?.name} waypointWorldPos={targetWaypoint?.position} targetDestination(before)={targetDestination}");

        if (isNavigating)
        {
            Debug.Log("Already navigating. Press Cancel first.");
            return;
        }

        // Build route
        Debug.Log("[NAV] Calculating path...");
        CalculateWallAvoidingPath();   // must populate currentPath (world-space or local)
        BuildTurnEvents();

        // WORLD MODE: prepare path and show arrows
        if (!hudOnly)
        {
            EnsureActiveChain();
            ClearFlowLine();
            ClearPathArrows();

            // fallback if path came back empty (only if not manual start — already ensured above)
            if (currentPath == null || currentPath.Count < 2)
            {
                var a = GetGroundPosition(GetCurrentPosition());
                var b = GetGroundPosition(targetDestination);
                currentPath = new List<Vector3> { a, b };
            }

            // Snap each path point's Y to the ground plane BEFORE creating arrows
            for (int i = 0; i < currentPath.Count; i++)
            {
                Vector3 wp = currentPath[i];
                Vector3 ground = GetGroundPosition(wp);
                currentPath[i] = new Vector3(wp.x, ground.y, wp.z);
            }

            // ensure arrows drawn after snapping
            pathStyle = PathVisualStyle.Arrows;
            ClearPathArrows();
            CreateGroundArrowsForPath();
            ShowEnhancedTargetMarker();
        }

        // Proxy floor setup
        if (!hudOnly && useProxyFloor)
        {
            EnsureFloorProxy();
            if (floorProxy)
            {
                floorProxy.localPosition = Vector3.zero;
                int proxyLayer = LayerMask.NameToLayer("FloorProxy");
                if (proxyLayer >= 0) groundLayerMask |= (1 << proxyLayer);
            }
        }

        Debug.Log($"[NAV] Path calculated with {currentPath?.Count ?? 0} points");

        // Direction + UI
        currentPathIndex = 0;
        lastPlayerPosition = GetCurrentPosition();

        if (statusText) statusText.text = $"Navigating to {currentDestination}";
        if (navigateButton) navigateButton.gameObject.SetActive(false);
        if (stopButton) stopButton.gameObject.SetActive(true);

        isNavigating = true;

        string mode = hudOnly ? "HUD" : (useVuforiaPositioning ? "Vuforia" : "Camera");
        Debug.Log($"Navigation started to {currentDestination} ({mode} mode)");
    }
    // Public helper to ensure pathRoot exists and attach it under the given parent
    public void ReparentPathRoot(Transform parent)
    {
        // Ensure the pathRoot exists (this should be your existing helper)
        EnsurePathRoot();

        if (pathRoot == null) return;

        // Reparent under the provided parent and reset local transform so arrows align correctly
        pathRoot.SetParent(parent, false);
        pathRoot.localPosition = Vector3.zero;
        pathRoot.localRotation = Quaternion.identity;
        pathRoot.localScale = Vector3.one;
    }

    void CacheDebugOriginalStates()
    {
        if (_debugOriginalActive != null) return;
        _debugOriginalActive = new Dictionary<GameObject, bool>();
        if (debugObjectsToHideDuringNavigation == null) return;
        foreach (var go in debugObjectsToHideDuringNavigation)
            if (go != null && !_debugOriginalActive.ContainsKey(go))
                _debugOriginalActive[go] = go.activeSelf;
    }

    void SetDebugObjectsHidden(bool hide)
    {
        if (debugObjectsToHideDuringNavigation == null) return;
        CacheDebugOriginalStates();
        for (int i = 0; i < debugObjectsToHideDuringNavigation.Length; i++)
        {
            var go = debugObjectsToHideDuringNavigation[i];
            if (go == null) continue;
            if (hide) go.SetActive(false);
            else
            {
                if (_debugOriginalActive != null && _debugOriginalActive.TryGetValue(go, out bool original))
                    go.SetActive(original);
                else
                    go.SetActive(true);
            }
        }
    }

    void BuildTurnEvents()
    {
        _turns.Clear(); _nextTurn = -1; _turnAnnounced = false;
        if (currentPath == null || currentPath.Count < 3) return;
        for (int i = 0; i < currentPath.Count - 2; i++)
        {
            Vector3 a = currentPath[i];
            Vector3 b = currentPath[i + 1];
            Vector3 c = currentPath[i + 2];

            Vector3 ab = new Vector3(b.x - a.x, 0f, b.z - a.z);
            Vector3 bc = new Vector3(c.x - b.x, 0f, c.z - b.z);
            if (ab.sqrMagnitude < 0.05f || bc.sqrMagnitude < 0.05f) continue;

            float ang = Vector3.SignedAngle(ab, bc, Vector3.up);
            float abs = Mathf.Abs(ang);

            if (abs < 25f) continue;                  // ignore tiny bends
            int dir = (abs > 135f) ? 0 : (ang > 0f ? +1 : -1); // 0 = U/around, +1 right, -1 left

            _turns.Add(new TurnEvent { index = i + 1, pos = b, dir = dir });
        }
        if (_turns.Count > 0) _nextTurn = 0;
        if (turnText) turnText.text = "";             // clear UI
    }
    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (!go) return;
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }

    Transform FindTargetWaypointAdvanced(string officeName)
    {
        // First, try to map the office name to the correct waypoint name
        string waypointOfficeName = GetCorrectWaypointName(officeName);

        var allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
            .Where(w => w.gameObject.scene.name != null && w.waypointType == WaypointType.Office)
            .ToArray();

        Debug.Log($"FindTargetWaypointAdvanced: Looking for '{waypointOfficeName}' (mapped from '{officeName}') among {allWaypoints.Length} office waypoints");

        // Special handling for Social Welfare (pick the best one)
        if (waypointOfficeName.Equals("Social Welfare", StringComparison.OrdinalIgnoreCase))
        {
            var socialWelfareWaypoints = allWaypoints
                .Where(w => w.officeName != null && w.officeName.Contains("Social Welfare"))
                .ToArray();

            if (socialWelfareWaypoints.Length > 0)
            {
                var preferred = socialWelfareWaypoints.FirstOrDefault(w => w.name.Contains("SocialWelfare3"))
                               ?? socialWelfareWaypoints.First();
                Debug.Log($"Selected Social Welfare waypoint: {preferred.name}");
                return preferred.transform;
            }
        }

        // Exact match by officeName
        foreach (var wp in allWaypoints)
        {
            if (!string.IsNullOrEmpty(wp.officeName) &&
                wp.officeName.Equals(waypointOfficeName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"Exact match found: {wp.name} for '{waypointOfficeName}'");
                return wp.transform;
            }
        }

        // Partial match by officeName
        foreach (var wp in allWaypoints)
        {
            if (!string.IsNullOrEmpty(wp.officeName))
            {
                string normalizedWaypoint = Normalize(wp.officeName);
                string normalizedSearch = Normalize(waypointOfficeName);

                if (normalizedWaypoint.Contains(normalizedSearch) || normalizedSearch.Contains(normalizedWaypoint))
                {
                    Debug.Log($"Partial match found: {wp.name} for '{waypointOfficeName}'");
                    return wp.transform;
                }
            }
        }

        Debug.LogError($"❌ No waypoint found for office: '{officeName}' (mapped to '{waypointOfficeName}')");

        // Debug: Show all available office names
        Debug.Log("Available office waypoints:");
        foreach (var wp in allWaypoints.Take(10))
        {
            Debug.Log($"  - {wp.name}: officeName='{wp.officeName}'");
        }

        return null;
    }
    private float lastPathCalcTime = -999f;
    public float minPathRecalcInterval = 0.6f; // seconds

    void CalculateWallAvoidingPath()
    {

        currentPath.Clear();
        Vector3 startPos = GetGroundPosition(GetCurrentPosition());
        Vector3 targetPos = GetGroundPosition(targetDestination);

        Debug.Log("=== CALCULATING ADVANCED WALL-AVOIDING PATH ===");
        Debug.Log($"From: {startPos} To: {targetPos}");

  
         NavigationWaypoint[] allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(w => w.gameObject.scene.name != null)
        .ToArray();

         if (cachedWaypoints == null) cachedWaypoints = GetAllRuntimeWaypoints();
                    allWaypoints = cachedWaypoints;
       
        // ADD THIS DEBUG
        Debug.Log($"Total waypoints found: {allWaypoints.Length}");
        int corridorCount = allWaypoints.Count(w => w.waypointType == WaypointType.Corridor || w.waypointType == WaypointType.Junction);
        Debug.Log($"Corridor/Junction waypoints: {corridorCount}");

        if (allWaypoints.Length == 0)
        {
            Debug.LogWarning("No waypoints found - using direct path");
            currentPath.Add(startPos);
            currentPath.Add(targetPos);
            return;
        }

        // 🔹 Check floor difference
        bool startIsSecond = startPos.y > 5f;
        bool targetIsSecond = targetPos.y > 5f;

        if (startIsSecond != targetIsSecond)
        {
            Debug.Log("Cross-floor navigation required - inserting stair waypoints");

            // Collect stairs
            var stairs = allWaypoints.Where(wp => wp.waypointType == WaypointType.Stairs).ToArray();
            Debug.Log($"Found {stairs.Length} stair waypoints in scene");

            foreach (var s in stairs)
            {
                Debug.Log($"[STAIR] {s.waypointName} at {s.transform.position}");
            }

            if (stairs.Length >= 2)
            {
                // Nearest stair on player's current floor
                var nearestStartStair = stairs
                    .Where(s => (startIsSecond && s.transform.position.y > 5f) ||
                                (!startIsSecond && s.transform.position.y <= 5f))
                    .OrderBy(s => Vector3.Distance(startPos, s.transform.position))
                    .FirstOrDefault();

                // Nearest stair on destination floor
                var nearestTargetStair = stairs
                    .Where(s => (targetIsSecond && s.transform.position.y > 5f) ||
                                (!targetIsSecond && s.transform.position.y <= 5f))
                    .OrderBy(s => Vector3.Distance(targetPos, s.transform.position))
                    .FirstOrDefault();

                if (nearestStartStair != null && nearestTargetStair != null)
                {
                    // Path pieces
                    var pathToStair = FindComplexPath(startPos, nearestStartStair.transform.position, allWaypoints);
                    var stairToStair = FindComplexPath(nearestStartStair.transform.position, nearestTargetStair.transform.position, allWaypoints);
                    var stairToTarget = FindComplexPath(nearestTargetStair.transform.position, targetPos, allWaypoints);

                    // Merge, skip duplicates
                    currentPath.AddRange(pathToStair);

                    // Instead of a direct line, generate a sloped stair path
                    List<Vector3> stairRamp = GenerateStairRamp(
                        nearestStartStair.transform.position,
                        nearestTargetStair.transform.position,
                        12 // number of intermediate points, tweak for smoother arrows
                    );

                    currentPath.AddRange(stairRamp.Skip(1)); // skip(1) so we don’t duplicate the bottom point

                    // Trigger floor fade when switching floors
                    if (floorManager != null)
                    {
                        bool goingUp = targetPos.y > startPos.y;
                        if (!hudOnly && floorManager != null)
                            floorManager.CrossFadeFloors(goingUp);
                    }


                    if (stairToTarget.Count > 0) currentPath.AddRange(stairToTarget.Skip(1));

                    Debug.Log($"✅ Cross-floor path created with {currentPath.Count} points");
                    return;
                }
            }
        }

        currentPath = FindComplexPath(startPos, targetPos, allWaypoints);

        Debug.Log($"[NAV-DBG] currentPath count={currentPath?.Count}");
        if (currentPath != null && currentPath.Count > 0)
            Debug.Log($"[NAV-DBG] path final point={currentPath[currentPath.Count - 1]} targetDestination(after)={targetDestination}");

    }


    void RequestRecalculatePath()
    {
        if (Time.time - lastPathCalcTime < minPathRecalcInterval) return;
        lastPathCalcTime = Time.time;
        CalculateWallAvoidingPath();
        if (!hudOnly)
        {
            ClearPathArrows();
            CreateGroundArrowsForPath();
        }
    }

    private Vector3 smoothedPosition;
    public float posSmoothSpeed = 8f; // higher = faster follow

    public bool TryGetNextPoint(out Vector3 next, float minDistance = 1f)
    {
        next = Vector3.zero;
        if (currentPath == null || currentPath.Count == 0) return false;
        Vector3 pos = GetCurrentPosition();
        for (int i = 0; i < currentPath.Count; i++)
        {
            if (Vector3.Distance(pos, currentPath[i]) > minDistance)
            {
                next = currentPath[i];
                return true;
            }
        }
        next = currentPath[currentPath.Count - 1];
        return true;
    }

    public bool TryGetNextPointInfo(out Vector3 next, out string nextName, float minDistance = 1f)
    {
        nextName = null;
        if (!TryGetNextPoint(out next, minDistance)) return false;
        var all = GetAllRuntimeWaypoints();
        NavigationWaypoint best = null;
        float bestD = float.MaxValue;
        foreach (var w in all)
        {
            float d = (w.transform.position - next).sqrMagnitude;
            if (d < bestD) { bestD = d; best = w; }
        }
        nextName = best ? (!string.IsNullOrEmpty(best.waypointName) ? best.waypointName : best.officeName) : null;
        return true;
    }
    // Creates a smooth ramp path between bottom and top stair
    private List<Vector3> GenerateStairRamp(Vector3 bottom, Vector3 top, int steps = 10)
    {
        List<Vector3> ramp = new List<Vector3>();
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 stairPoint = Vector3.Lerp(bottom, top, t);
            ramp.Add(stairPoint);
        }
        Debug.Log($"Generated {ramp.Count} interpolated stair points");
        return ramp;


    }

    private List<Vector3> FindComplexPath(Vector3 start, Vector3 target, NavigationWaypoint[] waypoints)
    {
        Debug.Log($"=== FindComplexPath: {start} → {target} ===");

        // Find nearest waypoints
        NavigationWaypoint startWP = FindNearestAccessibleWaypoint(start, waypoints);
        NavigationWaypoint targetWP = FindNearestAccessibleWaypoint(target, waypoints);

        if (startWP == null || targetWP == null)
        {
            Debug.LogError($"Could not find accessible waypoints! Start: {startWP?.name}, Target: {targetWP?.name}");
            return new List<Vector3> { start, target };
        }

        Debug.Log($"Using waypoints: {startWP.name} → {targetWP.name}");

        // If same waypoint, direct path
        if (startWP == targetWP)
        {
            return new List<Vector3> { start, target };
        }

        // Enhanced A* that guarantees shortest path
        var openSet = new List<NavigationWaypoint> { startWP };
        var closedSet = new HashSet<NavigationWaypoint>();
        var cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var gScore = new Dictionary<NavigationWaypoint, float>();
        var fScore = new Dictionary<NavigationWaypoint, float>();

        // Initialize all scores to infinity
        foreach (var wp in waypoints)
        {
            gScore[wp] = Mathf.Infinity;
            fScore[wp] = Mathf.Infinity;
        }

        gScore[startWP] = 0f;
        fScore[startWP] = Vector3.Distance(startWP.transform.position, targetWP.transform.position);

        while (openSet.Count > 0)
        {
            // Always pick the node with LOWEST fScore (most promising)
            NavigationWaypoint current = openSet[0];
            for (int i = 1; i < openSet.Count; i++)
            {
                if (fScore[openSet[i]] < fScore[current])
                    current = openSet[i];
            }

            // Found the target!
            if (current == targetWP)
            {
                var path = ReconstructOptimalPath(cameFrom, current, start, target);
                Debug.Log($"✅ SHORTEST path found with {path.Count} points, total distance: {CalculatePathDistance(path):F2}m");
                return path;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            // Explore all connected neighbors
            if (current.connectedWaypoints != null)
            {
                foreach (var neighbor in current.connectedWaypoints)
                {
                    if (neighbor == null || closedSet.Contains(neighbor)) continue;

                    // Calculate actual distance cost
                    float movementCost = Vector3.Distance(current.transform.position, neighbor.transform.position);
                    float tentativeGScore = gScore[current] + movementCost;

                    if (!openSet.Contains(neighbor))
                    {
                        openSet.Add(neighbor);
                    }
                    else if (tentativeGScore >= gScore[neighbor])
                    {
                        continue; // Not a better path
                    }

                    // This is the best path to neighbor so far
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;

                    // Heuristic: straight-line distance to target
                    float heuristic = Vector3.Distance(neighbor.transform.position, targetWP.transform.position);
                    fScore[neighbor] = gScore[neighbor] + heuristic;
                }
            }
        }

        Debug.LogWarning("⚠ No path found through waypoints - using direct path");
        return new List<Vector3> { start, target };
    }

    private List<Vector3> ReconstructOptimalPath(Dictionary<NavigationWaypoint, NavigationWaypoint> cameFrom,
                                               NavigationWaypoint current,
                                               Vector3 start,
                                               Vector3 target)
    {
        var waypointPath = new List<NavigationWaypoint>();

        // Build the waypoint chain backwards
        while (cameFrom.ContainsKey(current))
        {
            waypointPath.Insert(0, current);
            current = cameFrom[current];
        }
        waypointPath.Insert(0, current); // Add start waypoint

        // Convert to world positions
        var path = new List<Vector3> { start };

        foreach (var wp in waypointPath)
        {
            path.Add(wp.transform.position);
        }

        path.Add(target);

        // Log the optimal path
        string pathStr = string.Join(" → ", waypointPath.Select(w => w.name));
        Debug.Log($"🎯 OPTIMAL PATH: {pathStr}");

        return path;
    }

    private float CalculatePathDistance(List<Vector3> path)
    {
        float totalDistance = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            totalDistance += Vector3.Distance(path[i], path[i + 1]);
        }
        return totalDistance;
    }
    private NavigationWaypoint FindNearestOfficeWaypoint(Vector3 position, NavigationWaypoint[] waypoints)
    {
        // Only consider office waypoints for start/end points
        var officeWaypoints = waypoints.Where(w => w.waypointType == WaypointType.Office).ToArray();

        NavigationWaypoint nearest = null;
        float minDist = Mathf.Infinity;

        foreach (var wp in officeWaypoints)
        {
            float dist = Vector3.Distance(position, wp.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = wp;
            }
        }

        Debug.Log($"Nearest office waypoint to {position}: {nearest?.name} (distance: {minDist:F2}m)");
        return nearest;
    }

    private List<NavigationWaypoint> RunStrictWaypointAStar(NavigationWaypoint start, NavigationWaypoint goal)
    {
        Debug.Log($"Enhanced A* pathfinding: {start.name} → {goal.name}");

        var openSet = new List<NavigationWaypoint> { start };
        var closedSet = new HashSet<NavigationWaypoint>();
        var cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var gScore = new Dictionary<NavigationWaypoint, float> { [start] = 0f };
        var fScore = new Dictionary<NavigationWaypoint, float>();

        fScore[start] = GetEnhancedHeuristic(start, goal);

        int iterations = 0;
        while (openSet.Count > 0 && iterations < 500)
        {
            iterations++;
            var current = openSet.OrderBy(wp => fScore.GetValueOrDefault(wp, Mathf.Infinity)).First();

            if (current == goal)
            {
                var path = new List<NavigationWaypoint> { current };
                while (cameFrom.ContainsKey(current))
                {
                    current = cameFrom[current];
                    path.Insert(0, current);
                }
                Debug.Log($"✅ Enhanced A* found path: {string.Join(" → ", path.Select(w => w.name))}");
                return path;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            if (current.connectedWaypoints == null) continue;

            foreach (var neighbor in current.connectedWaypoints)
            {
                if (neighbor == null || closedSet.Contains(neighbor)) continue;

                float movementCost = Vector3.Distance(current.transform.position, neighbor.transform.position);

                // Add smart penalties to guide the pathfinding
                float penalty = GetSmartMovementPenalty(current, neighbor, goal);
                float tentativeG = gScore.GetValueOrDefault(current, Mathf.Infinity) + movementCost + penalty;

                if (!openSet.Contains(neighbor))
                    openSet.Add(neighbor);
                else if (tentativeG >= gScore.GetValueOrDefault(neighbor, Mathf.Infinity))
                    continue;

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                fScore[neighbor] = tentativeG + GetEnhancedHeuristic(neighbor, goal);
            }
        }

        Debug.LogError($"Enhanced A* failed after {iterations} iterations!");
        return null;
    }

    private float GetSmartMovementPenalty(NavigationWaypoint from, NavigationWaypoint to, NavigationWaypoint goal)
    {
        float penalty = 0f;

        // MAJOR PENALTY: Avoid going through office waypoints unless it's the destination
        if (to.waypointType == WaypointType.Office && to != goal)
        {
            penalty += 15f; // Heavy penalty for routing through offices
            Debug.Log($"  Penalty +15 for routing through office {to.name}");
        }

        // BONUS: Prefer corridor/junction waypoints for routing
        if (to.waypointType == WaypointType.Corridor || to.waypointType == WaypointType.Junction)
        {
            penalty -= 5f; // Bonus for using corridors
            Debug.Log($"  Bonus -5 for using corridor {to.name}");
        }

        // PENALTY: Discourage office-to-office jumps
        if (from.waypointType == WaypointType.Office && to.waypointType == WaypointType.Office && to != goal)
        {
            penalty += 10f; // Penalty for office-to-office routing
            Debug.Log($"  Penalty +10 for office-to-office jump {from.name} → {to.name}");
        }

        return penalty;
    }

    private NavigationWaypoint FindSmartWaypoint(Vector3 position, NavigationWaypoint[] waypoints, string purpose)
    {
        bool isSecondFloor = position.y > 5f;
        NavigationWaypoint bestOffice = null;
        NavigationWaypoint bestCorridor = null;
        NavigationWaypoint bestAny = null;

        float bestOfficeScore = Mathf.Infinity;
        float bestCorridorScore = Mathf.Infinity;
        float bestAnyScore = Mathf.Infinity;

        Debug.Log($"Finding {purpose} waypoint for {position} (floor: {(isSecondFloor ? "Second" : "Ground")})");

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;

            bool wpSecondFloor = wp.transform.position.y > 5f;
            float distance = Vector3.Distance(position, wp.transform.position);

            // Base score is distance
            float score = distance;

            // Floor preference (same floor gets big bonus)
            if (wpSecondFloor == isSecondFloor)
                score *= 0.3f; // Strong preference for same floor
            else
                score *= 2.0f; // Penalty for different floor

            // Connectivity bonus
            int connections = wp.connectedWaypoints?.Count ?? 0;
            if (connections > 0)
                score *= (1.0f - (connections * 0.05f)); // More connections = better

            // Track best by type
            if (wp.waypointType == WaypointType.Office && score < bestOfficeScore)
            {
                bestOfficeScore = score;
                bestOffice = wp;
            }
            else if ((wp.waypointType == WaypointType.Corridor || wp.waypointType == WaypointType.Junction) && score < bestCorridorScore)
            {
                bestCorridorScore = score;
                bestCorridor = wp;
            }

            if (score < bestAnyScore)
            {
                bestAnyScore = score;
                bestAny = wp;
            }
        }

        // For pathfinding, prefer corridors unless we're very close to an office
        NavigationWaypoint selected;
        if (purpose == "target" && bestOffice != null && bestOfficeScore < bestCorridorScore * 0.5f)
        {
            selected = bestOffice; // Close to target office
        }
        else if (bestCorridor != null && bestCorridorScore < bestOfficeScore * 1.5f)
        {
            selected = bestCorridor; // Prefer corridors for routing
        }
        else
        {
            selected = bestOffice ?? bestAny; // Fallback
        }

        Debug.Log($"Selected {purpose}: {selected?.name} (type: {selected?.waypointType}, score: {(selected == bestOffice ? bestOfficeScore : selected == bestCorridor ? bestCorridorScore : bestAnyScore):F2})");
        return selected;
    }

    private NavigationWaypoint FindStrictNearestWaypoint(Vector3 position, NavigationWaypoint[] waypoints)
    {
        bool positionOnSecondFloor = position.y > 5f;
        NavigationWaypoint bestSameFloor = null;
        NavigationWaypoint bestAnyFloor = null;
        float minSameFloorDist = Mathf.Infinity;
        float minAnyFloorDist = Mathf.Infinity;

        Debug.Log($"Finding waypoint for position {position} (floor: {(positionOnSecondFloor ? "Second" : "Ground")})");

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;

            bool wpOnSecondFloor = wp.transform.position.y > 5f;
            float dist = Vector3.Distance(position, wp.transform.position);

            // Track closest on any floor
            if (dist < minAnyFloorDist)
            {
                minAnyFloorDist = dist;
                bestAnyFloor = wp;
            }

            // Prefer same floor
            if (wpOnSecondFloor == positionOnSecondFloor && dist < minSameFloorDist)
            {
                minSameFloorDist = dist;
                bestSameFloor = wp;
            }
        }

        NavigationWaypoint selected = bestSameFloor ?? bestAnyFloor;
        Debug.Log($"Selected waypoint: {selected?.name} at {selected?.transform.position} (type: {selected?.waypointType})");

        return selected;
    }
    private List<NavigationWaypoint> RunOptimizedAStar(NavigationWaypoint start, NavigationWaypoint goal)
    {
        var openSet = new List<NavigationWaypoint> { start };
        var closedSet = new HashSet<NavigationWaypoint>();
        var cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var gScore = new Dictionary<NavigationWaypoint, float> { [start] = 0f };
        var fScore = new Dictionary<NavigationWaypoint, float>();

        fScore[start] = GetSmartHeuristic(start, goal);

        int iterations = 0;
        while (openSet.Count > 0 && iterations < 500)
        {
            iterations++;

            // Get waypoint with lowest fScore
            NavigationWaypoint current = openSet.OrderBy(wp => fScore.GetValueOrDefault(wp, Mathf.Infinity)).First();

            if (current == goal)
            {
                Debug.Log($"✅ A* success in {iterations} iterations");
                return ReconstructWaypointPath(cameFrom, current);
            }

            openSet.Remove(current);
            closedSet.Add(current);

            // Only check connected waypoints
            if (current.connectedWaypoints == null) continue;

            foreach (var neighbor in current.connectedWaypoints)
            {
                if (neighbor == null || closedSet.Contains(neighbor)) continue;

                float movementCost = Vector3.Distance(current.transform.position, neighbor.transform.position);

                // Add smart penalties
                float penalty = GetSmartPenalty(current, neighbor, goal);
                float tentativeG = gScore.GetValueOrDefault(current, Mathf.Infinity) + movementCost + penalty;

                if (!openSet.Contains(neighbor))
                    openSet.Add(neighbor);
                else if (tentativeG >= gScore.GetValueOrDefault(neighbor, Mathf.Infinity))
                    continue;

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                fScore[neighbor] = tentativeG + GetSmartHeuristic(neighbor, goal);
            }
        }

        Debug.LogWarning($"A* failed after {iterations} iterations");
        return null;
    }

    private float GetSmartHeuristic(NavigationWaypoint from, NavigationWaypoint to)
    {
        float distance = Vector3.Distance(from.transform.position, to.transform.position);

        // Floor change penalty
        bool fromSecond = from.transform.position.y > 5f;
        bool toSecond = to.transform.position.y > 5f;
        if (fromSecond != toSecond)
            distance += 8f; // Penalty for floor changes

        return distance;
    }

    private float GetSmartPenalty(NavigationWaypoint from, NavigationWaypoint to, NavigationWaypoint goal)
    {
        float penalty = 0f;

        // Prefer staying in corridors for routing
        if (from.waypointType == WaypointType.Corridor && to.waypointType == WaypointType.Office && to != goal)
            penalty += 3f; // Avoid going into offices unless it's the destination

        // Slight penalty for direction changes (smoother paths)
        // This encourages straighter paths through corridors

        return penalty;
    }

    private List<NavigationWaypoint> ReconstructWaypointPath(Dictionary<NavigationWaypoint, NavigationWaypoint> cameFrom, NavigationWaypoint current)
    {
        var path = new List<NavigationWaypoint> { current };

        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }

        Debug.Log($"Waypoint path: {string.Join(" → ", path.Select(w => w.name))}");
        return path;
    }

    private List<NavigationWaypoint> RunStrictAStar(NavigationWaypoint start, NavigationWaypoint goal, NavigationWaypoint[] allWaypoints)
    {
        Debug.Log($"Running STRICT A* from {start.name} to {goal.name}");

        var openSet = new List<NavigationWaypoint> { start };
        var closedSet = new HashSet<NavigationWaypoint>();
        var cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var gScore = new Dictionary<NavigationWaypoint, float> { [start] = 0f };
        var fScore = new Dictionary<NavigationWaypoint, float> { [start] = Vector3.Distance(start.transform.position, goal.transform.position) };

        int iterations = 0;
        int maxIterations = 1000; // Prevent infinite loops

        while (openSet.Count > 0 && iterations < maxIterations)
        {
            iterations++;

            // Get waypoint with lowest fScore
            NavigationWaypoint current = openSet.OrderBy(wp => fScore.GetValueOrDefault(wp, Mathf.Infinity)).First();

            if (current == goal)
            {
                Debug.Log($"✅ STRICT A* found path in {iterations} iterations");
                return ReconstructStrictPath(cameFrom, current);
            }

            openSet.Remove(current);
            closedSet.Add(current);

            // CRITICAL: Only check EXPLICITLY connected waypoints
            if (current.connectedWaypoints == null || current.connectedWaypoints.Count == 0)
            {
                Debug.Log($"⚠ {current.name} has no connections!");
                continue;
            }

            Debug.Log($"Checking {current.connectedWaypoints.Count} connections from {current.name}");

            foreach (var neighbor in current.connectedWaypoints)
            {
                if (neighbor == null)
                {
                    Debug.LogWarning($"Null connection found in {current.name}");
                    continue;
                }

                if (closedSet.Contains(neighbor)) continue;

                float movementCost = Vector3.Distance(current.transform.position, neighbor.transform.position);
                float tentativeG = gScore.GetValueOrDefault(current, Mathf.Infinity) + movementCost;

                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
                }
                else if (tentativeG >= gScore.GetValueOrDefault(neighbor, Mathf.Infinity))
                {
                    continue;
                }

                // This path to neighbor is better
                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                fScore[neighbor] = tentativeG + Vector3.Distance(neighbor.transform.position, goal.transform.position);

                Debug.Log($"  Added {neighbor.name} to path (g={tentativeG:F1}, f={fScore[neighbor]:F1})");
            }
        }

        Debug.LogError($"STRICT A* failed after {iterations} iterations");
        return null;
    }

    private List<NavigationWaypoint> ReconstructStrictPath(Dictionary<NavigationWaypoint, NavigationWaypoint> cameFrom, NavigationWaypoint current)
    {
        var path = new List<NavigationWaypoint> { current };

        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }

        Debug.Log($"Reconstructed waypoint path: {string.Join(" → ", path.Select(w => w.name))}");
        return path;
    }

    private List<Vector3> BuildStrictPath(Vector3 start, Vector3 target, List<NavigationWaypoint> waypointPath)
    {
        var path = new List<Vector3>();

        // Always start with actual start position
        path.Add(start);

        // Add each waypoint position (NO smoothing, NO shortcuts)
        foreach (var wp in waypointPath)
        {
            Vector3 wpPos = wp.transform.position;
            // Keep waypoint at its exact position - don't modify Y
            path.Add(wpPos);
        }

        // Always end with actual target position
        path.Add(target);

        Debug.Log($"Built strict path with {path.Count} points (no smoothing applied)");
        return path;
    }

    private List<Vector3> BuildSmoothPath(Vector3 start, Vector3 target, List<NavigationWaypoint> waypointPath)
    {
        var path = new List<Vector3> { start };

        // Add all waypoint positions
        foreach (var wp in waypointPath)
        {
            path.Add(wp.transform.position);
        }

        path.Add(target);

        // Apply CONSERVATIVE smoothing only if enabled
        if (useLOSForSmoothing)
        {
            ConservativeSmoothing(path);
        }

        return path;
    }

    private void ConservativeSmoothing(List<Vector3> path)
    {
        if (path.Count <= 2) return;

        bool improved = true;
        int iterations = 0;
        int maxIterations = 2; // Very limited smoothing

        while (improved && iterations < maxIterations)
        {
            improved = false;
            iterations++;

            for (int i = 0; i < path.Count - 2; i++)
            {
                Vector3 from = path[i];
                Vector3 to = path[i + 2];

                // Only smooth if:
                // 1. Clear line of sight
                // 2. Not skipping too much distance
                float directDist = Vector3.Distance(from, to);
                float waypointDist = Vector3.Distance(from, path[i + 1]) + Vector3.Distance(path[i + 1], to);

                if (IsPathClear(from, to) && directDist < waypointDist * 1.1f) // Only 10% improvement
                {
                    path.RemoveAt(i + 1);
                    improved = true;
                    break;
                }
            }
        }

        Debug.Log($"Conservative smoothing: {iterations} iterations");
    }

    float GetEnhancedHeuristic(NavigationWaypoint from, NavigationWaypoint to)
    {
        float distance = Vector3.Distance(from.transform.position, to.transform.position);

        // Add penalty for floor changes
        bool fromSecondFloor = from.transform.position.y > 5f;
        bool toSecondFloor = to.transform.position.y > 5f;
        if (fromSecondFloor != toSecondFloor)
            distance += 5f; // Penalty for floor change

        return distance;
    }

    float GetMovementPenalty(NavigationWaypoint from, NavigationWaypoint to)
    {
        float penalty = 0f;

        // Prefer corridor/junction waypoints over office waypoints for pathfinding
        if (to.waypointType == WaypointType.Office && from.waypointType != WaypointType.Office)
            penalty += 2f;

        // Small penalty for direction changes (smoother paths)
        // This would require tracking previous direction, simplified for now

        return penalty;
    }

    private List<Vector3> ReconstructWaypointPath(Dictionary<NavigationWaypoint, NavigationWaypoint> cameFrom,
                                             NavigationWaypoint current,
                                             Vector3 start,
                                             Vector3 target)
    {
        var path = new List<Vector3>();
        var waypointPath = new List<NavigationWaypoint>();

        // Build waypoint chain
        while (cameFrom.ContainsKey(current))
        {
            waypointPath.Insert(0, current);
            current = cameFrom[current];
        }
        waypointPath.Insert(0, current); // Add start waypoint

        // Convert to world positions
        path.Add(start); // Actual start position

        foreach (var wp in waypointPath)
        {
            path.Add(wp.transform.position);
        }

        path.Add(target); // Actual target position

        Debug.Log($"Reconstructed path: {string.Join(" → ", waypointPath.Select(w => w.name))}");
        return path;
    }

    // --- helper to reconstruct path ---
    private List<Vector3> ReconstructPath(Dictionary<NavigationWaypoint, NavigationWaypoint> cameFrom,
                                             NavigationWaypoint current,
                                             Vector3 start,
                                             Vector3 target)
    {
        List<Vector3> path = new List<Vector3>();
        path.Add(target); // final world position

        while (cameFrom.ContainsKey(current))
        {
            // include every graph node in order
            path.Insert(0, current.transform.position);
            current = cameFrom[current];
        }

        path.Insert(0, start); // starting world position

        // Only smooth if you explicitly allow it
        if (useLOSForSmoothing)
            OptimizePath(path);

        Debug.Log($"✅ Path built with {path.Count} points");
        return path;
    }

    private bool IsPathClear(Vector3 from, Vector3 to)
    {
        if (!enableWallDetection) return true;

        // Project to ground to avoid incorrect checks due to height differences
        Vector3 a = GetGroundPosition(from);
        Vector3 b = GetGroundPosition(to);
        Vector3 dir = b - a;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        int mask = (int)wallLayerMask;

        // Raycast at mid height first (fast check)
        Vector3 rayStart = a + Vector3.up * 0.6f;
        Vector3 rayDir = (b + Vector3.up * 0.6f) - rayStart;
        if (Physics.Raycast(rayStart, rayDir.normalized, rayDir.magnitude, mask))
        {
            if (debugWallDetection) Debug.Log($"[WallBlock] Raycast blocked between {from} and {to}");
            return false;
        }

        // Robust sampling: sphere checks along the path (catches thin walls / odd geometry)
        float sampleSpacing = 0.5f; // meters between samples (tweakable)
        int samples = Mathf.Max(1, Mathf.CeilToInt(dist / sampleSpacing));

        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / (float)samples;
            Vector3 samplePos = Vector3.Lerp(a, b, t) + Vector3.up * 0.5f; // sample slightly above ground
            if (Physics.CheckSphere(samplePos, Mathf.Max(0.05f, wallCheckRadius), mask))
            {
                if (debugWallDetection) Debug.Log($"[WallBlock] Sphere blocked at {samplePos} between {from} and {to}");
                return false;
            }
        }

        return true;
    }

    void EnsureFloorProxy()
    {
        if (!worldRoot) return;
        if (floorProxy) return;

        var go = new GameObject("FloorProxy");
        go.transform.SetParent(worldRoot, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localPosition = Vector3.zero;

        var bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(proxySize, 0.02f, proxySize);
        bc.center = Vector3.zero;
        bc.isTrigger = false;

        // Set layer but DO NOT modify camera culling mask
        int layer = LayerMask.NameToLayer("FloorProxy");
        if (layer >= 0)
        {
            go.layer = layer;
            // ⭐ DO NOT TOUCH CAMERA CULLING MASK!
        }
        else
        {
            Debug.LogWarning("Add layer 'FloorProxy' in Project Settings > Tags and Layers");
        }

        // Make sure no renderer shows
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        floorProxy = go.transform;

        // Ensure ground raycasts can hit the proxy
        if (layer >= 0)
            groundLayerMask |= (1 << layer);
    }

    private NavigationWaypoint FindNearestAccessibleWaypoint(Vector3 position, NavigationWaypoint[] waypoints)
    {
        NavigationWaypoint nearest = null;
        float minDist = Mathf.Infinity;

        // Determine which floor we're on
        bool isOnSecondFloor = position.y > 5f;

        // First pass: try waypoints on the same floor with line of sight
        foreach (var wp in waypoints)
        {
            bool wpOnSecondFloor = wp.transform.position.y > 5f;
            if (wpOnSecondFloor != isOnSecondFloor) continue; // Skip different floors

            float dist = Vector3.Distance(position, wp.transform.position);
            bool hasLineOfSight = IsPathClear(position, wp.transform.position);

            if (hasLineOfSight && dist < minDist)
            {
                minDist = dist;
                nearest = wp;
            }
        }

        // Second pass: same floor, any waypoint
        if (nearest == null)
        {
            foreach (var wp in waypoints)
            {
                bool wpOnSecondFloor = wp.transform.position.y > 5f;
                if (wpOnSecondFloor != isOnSecondFloor) continue;

                float dist = Vector3.Distance(position, wp.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = wp;
                }
            }
        }

        // Third pass: any floor if we still haven't found anything
        if (nearest == null)
        {
            foreach (var wp in waypoints)
            {
                float dist = Vector3.Distance(position, wp.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = wp;
                }
            }
        }

        Debug.Log($"Selected waypoint: {nearest?.name} (floor: {(nearest?.transform.position.y > 5f ? "Second" : "Ground")})");
        return nearest;
    }
    public void PlaceWorld(Vector3 pos, Quaternion rot, System.Action onComplete)
    {
        if (!worldRoot || !contentAnchor) return;
        hudOnly = false;                                   // enter world mode

        // Place/rotate the world (worldRoot will be a child of the AR anchor set by the AR script)
        worldRoot.position = pos;                          // <- fixed: use pos (method parameter)
        float camYaw = arCamera ? arCamera.transform.eulerAngles.y : 0f;
        worldRoot.rotation = Quaternion.Euler(0f, camYaw, 0f);

        // Ensure contentAnchor is parented to the anchored world
        contentAnchor.SetParent(worldRoot, false);

        // --- NEW: ensure PathRoot exists and parent it under contentAnchor so arrows follow the anchor ---
        EnsurePathRoot(); // creates pathRoot if missing
        if (pathRoot != null)
        {
            pathRoot.SetParent(contentAnchor, false);
            pathRoot.localPosition = Vector3.zero;
            pathRoot.localRotation = Quaternion.identity;
        }

        SetFloorsVisible(false);

        useProxyFloor = true;
        EnsureFloorProxy();
        if (floorProxy)
        {
            // Make sure the proxy remains childed to the worldRoot (so grounding stays correct)
            floorProxy.SetParent(worldRoot, false);
            floorProxy.localPosition = Vector3.zero;

            int proxyLayer = LayerMask.NameToLayer("FloorProxy");
            if (proxyLayer >= 0) groundLayerMask |= (1 << proxyLayer);
        }

        if (statusText) statusText.text = "Floor set. Select an office.";
        worldBaked = true;

        onComplete?.Invoke();
    }

    // Add this anywhere in SmartNavigationSystem class
    public void PlaceWorld(Vector3 pos, Quaternion rot)
    {
        // call the new API with no callback
        PlaceWorld(pos, rot, null);
    }

    List<NavigationWaypoint> AStarPathfinding(NavigationWaypoint start, NavigationWaypoint goal)
    {
        var openSet = new List<NavigationWaypoint> { start };
        var cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var gScore = new Dictionary<NavigationWaypoint, float>();
        var fScore = new Dictionary<NavigationWaypoint, float>();

        foreach (var wp in FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None))
        {
            gScore[wp] = Mathf.Infinity;
            fScore[wp] = Mathf.Infinity;
        }

        gScore[start] = 0;
        fScore[start] = Vector3.Distance(start.transform.position, goal.transform.position);

        while (openSet.Count > 0)
        {
            NavigationWaypoint current = openSet.OrderBy(wp => fScore[wp]).First();

            if (current == goal)
            {
                List<NavigationWaypoint> path = new List<NavigationWaypoint>();
                while (cameFrom.ContainsKey(current))
                {
                    path.Insert(0, current);
                    current = cameFrom[current];
                }
                path.Insert(0, start);
                return path;
            }

            openSet.Remove(current);

            foreach (var neighbor in current.connectedWaypoints)
            {
                float tentativeG = gScore[current] + Vector3.Distance(current.transform.position, neighbor.transform.position);

                if (tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = gScore[neighbor] + Vector3.Distance(neighbor.transform.position, goal.transform.position);

                    if (!openSet.Contains(neighbor))
                        openSet.Add(neighbor);
                }
            }
        }

        Debug.LogWarning("⚠ No path found with A*");
        return new List<NavigationWaypoint>();
    }

    void OptimizePath(List<Vector3> path)
    {
        // DISABLED: No path optimization to prevent wall-cutting
        Debug.Log("Path optimization DISABLED to maintain waypoint accuracy");
        return;
    }

    // ✅ Required helper for pathfinding
    private NavigationWaypoint FindNearestWaypoint(Vector3 position, NavigationWaypoint[] waypoints)
    {
        NavigationWaypoint nearest = null;
        float minDist = Mathf.Infinity;

        foreach (var wp in waypoints)
        {
            float dist = Vector3.Distance(position, wp.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = wp;
            }
        }

        return nearest;
    }

    public Vector3 GetGroundPosition(Vector3 worldPos)
    {
        // For waypoint positions, keep them exactly where they are
        // Only adjust Y for non-waypoint positions if needed

        if (useProxyFloor && floorProxy)
        {
            // Keep X,Z but use proxy Y + offset
            return new Vector3(worldPos.x, floorProxy.position.y + groundOffset, worldPos.z);
        }

        // For waypoints, keep original position
        return worldPos;
    }

    public void CreateGroundArrowsForSegment(Vector3 start, Vector3 end)
    {
        if (hudOnly) return;

        Debug.Log($"[SEGMENT] Creating arrows from {start} to {end}");

        // ✅ CHANGED: Use GetValidFloorPosition instead of ClampToFloorBounds
        Vector3 worldStart = GetValidFloorPosition(start);
        Vector3 worldEnd = GetValidFloorPosition(end);

        Debug.Log($"[SEGMENT] AR-adjusted Start: {worldStart}, End: {worldEnd}");

        Vector3 seg = worldEnd - worldStart;
        float dist = seg.magnitude;

        Debug.Log($"[SEGMENT] Distance: {dist}m, Spacing: {arrowSpacing}m");

        if (dist < 0.01f)
        {
            Debug.LogWarning("[SEGMENT] Distance too short, skipping");
            return;
        }

        EnsurePathRoot();
        if (!arrowPrefab)
        {
            Debug.LogError("[SEGMENT] No arrowPrefab!");
            return;
        }

        if (!pathRoot)
        {
            Debug.LogError("[SEGMENT] pathRoot is NULL!");
            return;
        }

        int num = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(0.05f, arrowSpacing)));
        Debug.Log($"[SEGMENT] Creating {num} arrows");

        Vector3 direction = seg.normalized;
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        Quaternion modelCorrection = Quaternion.Euler(-90f, 0f, 0f);

        for (int i = 0; i < num; i++)
        {
            float t = (i + 1f) / (num + 1f);
            Vector3 arrowPos = Vector3.Lerp(worldStart, worldEnd, t);

            // ✅ CHANGED: Only call GetValidFloorPosition once
            arrowPos = GetValidFloorPosition(arrowPos);

            GameObject go = Instantiate(arrowPrefab, arrowPos, rotation * modelCorrection, pathRoot);
            go.name = $"Arrow_{i}_{Time.frameCount}";

            // ✅ CHANGED: Increased minimum scale for visibility
            float baseScale = Mathf.Clamp(arrowSize, 0.3f, 1.0f);
            go.transform.localScale = Vector3.one * baseScale;

            var mat = arrowSharedMat ??= CreateArrowMaterial(arrowColor);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = mat;
                r.enabled = true;
            }

            go.SetActive(true);
            pathArrows.Add(go);

            Debug.Log($"[ARROW] ✅ Created '{go.name}' at {arrowPos}, Scale: {baseScale}");
        }

        Debug.Log($"[SEGMENT] Completed. Total arrows: {pathArrows.Count}");
    }

    /// <summary>
    /// Aligns the building model so the specified office position is at AR origin
    /// Call this when user selects their current office location
    /// </summary>
    public void AlignBuildingToUserPosition(Vector3 userOfficePosition)
    {
        if (worldRoot == null)
        {
            Debug.LogError("[ALIGN] No worldRoot assigned! Drag WorldRoot to SmartNavigationSystem inspector.");
            return;
        }

        Debug.Log($"[ALIGN] User office position: {userOfficePosition}");
        Debug.Log($"[ALIGN] WorldRoot position before: {worldRoot.position}");

        // Calculate offset to move office to AR origin (0,0,0)
        Vector3 offset = -userOfficePosition;

        // Keep Y at AR ground level
        offset.y = arGroundPlaneY - userOfficePosition.y;

        // Move the entire building
        worldRoot.position += offset;

        Debug.Log($"[ALIGN] Applied offset: {offset}");
        Debug.Log($"[ALIGN] WorldRoot position after: {worldRoot.position}");
        Debug.Log($"[ALIGN] User office now at AR origin (should be near 0,0,0)");

        // Update path positions if they exist
        if (currentPath != null && currentPath.Count > 0)
        {
            for (int i = 0; i < currentPath.Count; i++)
            {
                currentPath[i] += offset;
            }
            Debug.Log($"[ALIGN] Updated {currentPath.Count} path waypoints with offset");
        }

        // Recreate arrows at new positions
        if (pathArrows != null && pathArrows.Count > 0)
        {
            Debug.Log("[ALIGN] Clearing old arrows to recreate at new positions");
            ClearPathArrowsIfNeeded();
        }
    }

    /// <summary>
    /// Gets the world position of an office waypoint by ID
    /// You need to customize the office ID → waypoint name mapping
    /// </summary>
    public Vector3 GetOfficeWaypointPosition(int officeId)
    {
        string waypointName = GetWaypointNameFromOfficeId(officeId);

        if (string.IsNullOrEmpty(waypointName))
        {
            Debug.LogError($"[WAYPOINT] No waypoint name for office ID {officeId}");
            return Vector3.zero;
        }

        GameObject waypoint = GameObject.Find(waypointName);

        if (waypoint != null)
        {
            Debug.Log($"[WAYPOINT] Found '{waypointName}' at {waypoint.transform.position}");
            return waypoint.transform.position;
        }

        Debug.LogError($"[WAYPOINT] GameObject '{waypointName}' not found in scene!");
        return Vector3.zero;
    }

    /// <summary>
    /// Maps office ID to waypoint GameObject name
    /// Matches your exact scene hierarchy waypoint names
    /// </summary>
    string GetWaypointNameFromOfficeId(int officeId)
    {
        switch (officeId)
        {
            case 0: return "Waypoint Civil Registry";
            case 1: return "Waypoint_ET_City Treasurer";
            case 2: return "Waypoint_MayorOffice-2nd";
            case 3: return "Waypoint_CityAdmin-2nd";
            case 4: return "Waypoint_CashDivision";
            case 5: return "Waypoint_EDP";
            case 6: return "Waypoint_E-Business";
            case 7: return "Waypoint_RealEstateDivision";
            case 8: return "Waypoint_LicenseDiv";
            case 9: return "Waypoint_EA_SMART";
            case 10: return "Waypoint SocialWelfare";
            case 11: return "Waypoint_PWD";
            case 12: return "Waypoint SocialWelfare2";
            case 13: return "Waypoint SocialWelfare3";
            case 14: return "Waypoint_EA_OSCA";
            case 15: return "Waypoint_ManilaHD";
            case 16: return "Waypoint_BureuOfPermits";

            default:
                Debug.LogWarning($"[WAYPOINT] Unknown office ID: {officeId}");
                return "";
        }
    }


    public void StartNavigationFromCurrentLocation(string toOfficeName)
    {
        Debug.Log($"=== StartNavigationFromCurrentLocation → {toOfficeName} ===");

        StopNavigation();

        // Use camera position as start (don't set currentUserOffice)
        useOfficeAsStart = false;
        currentUserOffice = "";

        var toWP = FindTargetWaypointAdvanced(toOfficeName);
        if (toWP == null)
        {
            Debug.LogError($"❌ Could not find waypoint: {toOfficeName}");
            return;
        }

        Debug.Log($"✅ Found destination waypoint: {toWP.name} at {toWP.transform.position}");

        // Set navigation target
        targetDestination = toWP.transform.position;
        originalTargetPosition = targetDestination;
        currentDestination = toOfficeName;

        // Calculate path - GetCurrentPosition() will use camera position
        Debug.Log($"[NAV] Calculating path from camera...");
        CalculateWallAvoidingPath();

        if (currentPath == null || currentPath.Count < 2)
        {
            Debug.LogError("❌ Path calculation failed!");
            return;
        }

        Debug.Log($"✅ Path calculated with {currentPath.Count} points");

        // WORLD MODE: Create arrows immediately
        if (!hudOnly)
        {
            EnsureActiveChain();
            ClearPathArrows();
            ClearFlowLine();

            pathStyle = PathVisualStyle.Arrows;
            CreateGroundArrowsForPath();

            Debug.Log($"✅ Created {pathArrows.Count} arrows");

            ShowEnhancedTargetMarker();
        }

        // Update UI
        isNavigating = true;
        hasReachedDestination = false;
        currentPathIndex = 0;

        if (statusText) statusText.text = $"Navigating to {currentDestination}";
        if (navigateButton) navigateButton.gameObject.SetActive(false);
        if (stopButton) stopButton.gameObject.SetActive(true);

        Debug.Log("=== Navigation from current location started ===");
    }

    private Vector3 ClampToFloorBounds(Vector3 position)
    {
        // Get the appropriate floor bounds
        Bounds floorBounds;
        if (position.y > 5f && secondFloor)
        {
            floorBounds = GetFloorBounds(secondFloor);
        }
        else if (groundFloor)
        {
            floorBounds = GetFloorBounds(groundFloor);
        }
        else
        {
            return position; // No floor to clamp to
        }

        // Clamp X and Z to floor bounds, keep original Y
        Vector3 clamped = new Vector3(
            Mathf.Clamp(position.x, floorBounds.min.x, floorBounds.max.x),
            position.y,
            Mathf.Clamp(position.z, floorBounds.min.z, floorBounds.max.z)
        );

        if (Vector3.Distance(position, clamped) > 0.1f)
        {
            Debug.LogWarning($"Clamped position {position} to {clamped} (outside floor bounds)");
        }

        return clamped;
    }

    private Vector3 GetValidFloorPosition(Vector3 position)
    {
        // Priority 1: Use AR detected ground plane
        if (useARGroundPlane && arGroundPlaneY != 0f)
        {
            return new Vector3(position.x, arGroundPlaneY + 0.05f, position.z);
        }

        // Priority 2: Use floor proxy if set
        if (useProxyFloor && floorProxy)
        {
            return new Vector3(position.x, floorProxy.position.y + groundOffset, position.z);
        }

        // Priority 3: Keep original position
        return position;
    }

    void AnimateArrowPath()
    {
        if (!arrowPulse || pathArrows == null || pathArrows.Count == 0) return;

        float t = Time.time * arrowPulseSpeed;

        for (int i = 0; i < pathArrows.Count; i++)
        {
            var arrow = pathArrows[i];
            if (!arrow) continue;

            var ai = arrow.GetComponent<ArrowInstance>();
            if (ai == null)
            {
                ai = arrow.AddComponent<ArrowInstance>();
                ai.baseScale = arrowSize;
                ai.phase = Random.value * Mathf.PI * 2f;
            }

            // Smooth pulsing animation
            float wave = 1f + Mathf.Sin(t + ai.phase) * arrowPulseAmplitude;
            float finalScale = ai.baseScale * wave;
            arrow.transform.localScale = Vector3.one * finalScale;
        }
    }

    void UpdateArrowBaseScalesIfEnabled()
    {
        if (!liveDistanceScaling || pathArrows == null || !arCamera) return;

        for (int i = 0; i < pathArrows.Count; i++)
        {
            var a = pathArrows[i];
            if (!a) continue;
            var ai = a.GetComponent<ArrowInstance>();
            if (ai == null) continue;

            float d = Vector3.Distance(a.transform.position, arCamera.transform.position);
            float blend = Mathf.InverseLerp(0f, Mathf.Max(0.01f, arrowScaleDistance), d);
            float distScale = Mathf.Lerp(arrowNearScale, arrowFarScale, blend);
            float targetBase = Mathf.Clamp(arrowSize * distScale, 0.05f, 0.8f);

            // Smoothly converge so it doesn’t pop
            ai.baseScale = Mathf.Lerp(ai.baseScale, targetBase, Time.deltaTime * 3f);
        }
    }

    void SetFloorsVisible(bool visible)
    {
        if (groundFloor)
            foreach (var r in groundFloor.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        if (secondFloor)
            foreach (var r in secondFloor.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
    }

    void EnsurePathRoot()
    {
        if (hudOnly) return;
        if (!contentAnchor) ResolveAnchors();
        if (!contentAnchor)
        {
            Debug.LogError("[PathRoot] ContentAnchor not assigned/found.");
            return;
        }
        if (pathRoot) return;
        if (!contentAnchor.gameObject.activeSelf) contentAnchor.gameObject.SetActive(true);

        var go = new GameObject("PathRoot");
        go.transform.SetParent(contentAnchor, false);
        pathRoot = go.transform;

    }

    void EnsureLine()
    {
        if (flowLine != null) return;
        EnsurePathRoot();
        var go = new GameObject("FlowLine");
        go.transform.SetParent(pathRoot, false);

        flowLine = go.AddComponent<LineRenderer>();
        flowLine.useWorldSpace = true;
        flowLine.loop = false;
        flowLine.textureMode = LineTextureMode.Tile;
        flowLine.numCornerVertices = 4;
        flowLine.numCapVertices = 4;
        flowLine.alignment = LineAlignment.TransformZ;  // key change

        flowLine.widthMultiplier = lineWidth;
        flowLine.widthCurve = new AnimationCurve(
            new Keyframe(0f, lineWidth),
            new Keyframe(1f, lineWidth)
        );

        if (flowLineMaterial) flowLine.material = flowLineMaterial;
        else
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) shader = Shader.Find("Unlit/Color");
            flowLine.material = new Material(shader);
        }
    }

    void ClearFlowLine()
    {
        if (flowLine) flowLine.positionCount = 0; // keep and reuse
    }

    readonly List<Vector3> _resampled = new List<Vector3>();
    List<Vector3> BuildResampledPoints(List<Vector3> src, float stepMeters)
    {
        _resampled.Clear();
        if (src == null || src.Count == 0) return _resampled;

        Vector3 camPos = arCamera ? arCamera.transform.position : GetCurrentPosition();

        for (int i = 0; i < src.Count - 1; i++)
        {
            Vector3 a = src[i];
            Vector3 b = src[i + 1];
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.05f, stepMeters)));

            for (int k = 0; k < n; k++)
            {
                float t = k / (float)n;
                Vector3 p = Vector3.Lerp(a, b, t);
                p = GetGroundPosition(p);

                // NEW: skip points too close to the camera (prevents big vertical ribbon)
                if (Vector3.Distance(p, camPos) < startGapMeters) continue;

                _resampled.Add(p);
            }
        }

        // add last point if it is not too close
        Vector3 last = GetGroundPosition(src[src.Count - 1]);
        if (Vector3.Distance(last, camPos) >= startGapMeters)
            _resampled.Add(last);

        return _resampled;
    }

    void UpdateLeadArrowToCamera()
    {
        if (!isNavigating || arCamera == null || currentPath.Count < 2 || pathArrows.Count == 0)
            return;

        Vector3 camGround = GetGroundPosition(arCamera.transform.position);
        Vector3 toFirst = currentPath[1] - camGround;
        if (toFirst.sqrMagnitude < 0.01f) return;

        Vector3 dir = new Vector3(toFirst.x, 0f, toFirst.z).normalized;
        Vector3 pos = camGround + dir * Mathf.Max(arrowStartGap, arrowSpacing * 0.5f);

        // Lock to proxy/worldRoot Y so it never floats
        if (ProxyReady || lockArrowsToFloor)
            pos.y = ProxyY;

        var a0 = pathArrows[0];
        if (!a0) return;

        a0.transform.position = pos;
        a0.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }
    void BuildFlowLineFromCurrentPath()
    {
        if (hudOnly) return; // HUD: no line renderer

        EnsureLine();
        var pts = BuildResampledPoints(currentPath, sampleStep);

        flowLine.positionCount = pts.Count;
        if (pts.Count > 0) flowLine.SetPositions(pts.ToArray());
        flowLine.widthMultiplier = lineWidth;

        if (flowLine.material)
        {
            // URP Unlit uses _BaseMap/_BaseColor
            if (flowLine.material.HasProperty("_BaseMap"))
                flowLine.material.SetTextureScale("_BaseMap", new Vector2(dashTiling, 1f));
            if (flowLine.material.HasProperty("_BaseColor"))
                flowLine.material.SetColor("_BaseColor", arrowColor);
        }

        flowT = 0f;
    }

    void OnCenterAnchorTracked()
    {
        AlignAnchorYawToCamera();

        // After AlignAnchorYawToCamera();
        SetFloorsVisible(false);  // keep floor plan hidden
                                  // Optional: hide any path that may exist (safety)
        ClearPathArrows();

        // Inform the user
        if (statusText) statusText.text = "Anchor found. Select an office.";

    }

    void ShowEnhancedTargetMarker()
    {
        Debug.Log($"ShowEnhancedTargetMarker: targetDestination = {targetDestination}");

        if (!targetMarker)
        {
            Debug.Log("Creating target marker...");
            CreateEnhancedTargetMarker();
        }

        if (!targetMarker)
        {
            Debug.LogError("Failed to create target marker!");
            return;
        }

        // Use the EXACT targetDestination position
        Vector3 markerPosition = targetDestination;

        // Only adjust Y for ground level, keep X,Z exact
        markerPosition = new Vector3(targetDestination.x, GetGroundPosition(targetDestination).y, targetDestination.z);

        Debug.Log($"Placing target marker at: {markerPosition}");

        if (!hudOnly && pathRoot)
        {
            targetMarker.transform.SetParent(pathRoot, true);
        }

        targetMarker.transform.position = markerPosition;
        originalTargetPosition = markerPosition;
        targetBobOffset = 0f;
        targetMarker.SetActive(true);

        Debug.Log($"✅ Target marker placed at: {targetMarker.transform.position}");
    }

    void ClearPathArrows()
    {
        if (pathArrows != null)
        {
            foreach (var a in pathArrows)
                if (a) Destroy(a);
            pathArrows.Clear();
        }
        if (pathRoot)
        {
            for (int i = pathRoot.childCount - 1; i >= 0; i--)
                Destroy(pathRoot.GetChild(i).gameObject);
        }
    }

    public void StopNavigation()
    {
        startedFromOffice = false;
        currentUserOffice = "";
        useOfficeAsStart = false;

        // Reset arrival detection
        hasReachedDestination = false;
        hasShownArrivalNotification = false;
        timeWithinArrivalZone = 0f;

        Debug.Log("[NAV] StopNavigation() called");
        isNavigating = false;
        ClearPathArrows();

        if (targetMarker) targetMarker.SetActive(false);
        currentPath.Clear();

        if (directionText) directionText.text = "";
        if (distanceText) distanceText.text = "";

        if (statusText)
        {
            statusText.text = useVuforiaPositioning
                ? (isImageTargetTracked ? "Ready to navigate" : "Point camera at office sign")
                : "Ready to navigate";
        }

        if (navigateButton) navigateButton.gameObject.SetActive(true);
        if (stopButton) stopButton.gameObject.SetActive(false);

        SetFloorsVisible(false);

        if (stepper) stepper.OnDistance -= OnHudMeters;

        _turns.Clear();
        _nextTurn = -1;
        _turnAnnounced = false;
        if (turnText) turnText.text = "";

        Debug.Log("[NAV] StopNavigation finished");
    }


    public void StartNavigation()
    {
        var ui = FindFirstObjectByType<ManilaServeUI>();
        if (ui == null)
        {
            Debug.LogError("ManilaServeUI not found!");
            return;
        }

        string selectedDept = ui.GetSelectedOffice();
        if (string.IsNullOrEmpty(selectedDept))
        {
            Debug.LogWarning("StartNavigation called but no office selected");
            return;
        }

        Debug.Log($"StartNavigation requested for: {selectedDept} — showing popup only");
        ui.ShowOfficeInfoPopup(selectedDept);
    }

    // ADDED: Simple turn-by-turn direction calculation
    void UpdateDirections()
    {
        if (!isNavigating || currentPath.Count <= 1 || directionText == null) return;

        Vector3 currentPos = GetCurrentPosition();

        // Find next waypoint ahead of current position
        Vector3 nextPoint = Vector3.zero;
        bool foundNext = false;

        for (int i = currentPathIndex; i < currentPath.Count; i++)
        {
            float distanceToPoint = Vector3.Distance(currentPos, currentPath[i]);

            if (distanceToPoint > 1f) // Found a point that's not too close
            {
                nextPoint = currentPath[i];
                currentPathIndex = i;
                foundNext = true;
                break;
            }
        }

        if (!foundNext && currentPath.Count > 0)
        {
            nextPoint = currentPath[currentPath.Count - 1]; // Use final destination
        }

        if (foundNext || currentPath.Count > 0)
        {
            // Calculate direction
            Vector3 playerForward = playerCamera != null ? playerCamera.transform.forward : Vector3.forward;
            Vector3 directionToNext = (nextPoint - currentPos).normalized;

            // Remove Y component for 2D direction calculation
            playerForward.y = 0;
            directionToNext.y = 0;

            playerForward.Normalize();
            directionToNext.Normalize();

            // Calculate angle between player forward and direction to next point
            float angle = Vector3.SignedAngle(playerForward, directionToNext, Vector3.up);

            // Determine turn direction
            string direction;
            if (Mathf.Abs(angle) < 30f)
                direction = "Continue straight ahead";
            else if (angle > 0)
            {
                if (angle > 135f) direction = "Turn around";
                else if (angle > 90f) direction = "Turn sharp right";
                else if (angle > 45f) direction = "Turn right";
                else direction = "Turn slightly right";
            }
            else
            {
                if (angle < -135f) direction = "Turn around";
                else if (angle < -90f) direction = "Turn sharp left";
                else if (angle < -45f) direction = "Turn left";
                else direction = "Turn slightly left";
            }

            directionText.text = direction;

            // Update distance if UI element exists
            if (distanceText != null)
            {
                float distance = Vector3.Distance(currentPos, nextPoint);
                if (distance > 1f)
                    distanceText.text = $"{distance:F1}m";
                else
                    distanceText.text = "Almost there";
            }
        }
    }

    void Update()
    {
        if (!isNavigating || hasReachedDestination) return;

        // Check arrival
        CheckArrivalAtDestination();

        // Update arrow visibility (your existing code)
        if (currentPath == null || currentPath.Count == 0)
        {
            Vector3 userPos = GetUserPosition();
            if (userPos != Vector3.zero)
            {
                CalculateAndShowPath(userPos, targetDestination);
            }
        }
        else
        {
            UpdateArrowVisibility();
        }

        // Update directions and animations (your existing code)
        UpdateDirections();
        AnimateEnhancedTargetMarker();
        AnimateArrowPath();
        UpdateArrowBaseScalesIfEnabled();
        UpdateTurnHints();
    }


    public void StartNavigationFromOfficeName(string fromOfficeName, string toOfficeName)
    {
        Debug.Log($"=== StartNavigationFromOfficeName: {fromOfficeName} → {toOfficeName} ===");

        StopNavigation();

        // Store the user's current office and enable office-based start
        currentUserOffice = fromOfficeName;
        useOfficeAsStart = true;

        var fromWP = FindTargetWaypointAdvanced(fromOfficeName);
        var toWP = FindTargetWaypointAdvanced(toOfficeName);

        if (fromWP == null || toWP == null)
        {
            Debug.LogError($"❌ Could not find waypoints: {fromOfficeName} → {toOfficeName}");
            return;
        }

        Debug.Log($"✅ Found waypoints: {fromWP.name} at {fromWP.transform.position} → {toWP.name} at {toWP.transform.position}");

        // Set navigation target
        targetDestination = toWP.transform.position;
        originalTargetPosition = targetDestination;
        currentDestination = toOfficeName;

        // Calculate path - GetCurrentPosition() will now use fromOfficeName
        Debug.Log($"[NAV] Calculating path...");
        CalculateWallAvoidingPath();

        if (currentPath == null || currentPath.Count < 2)
        {
            Debug.LogError("❌ Path calculation failed!");
            return;
        }

        Debug.Log($"✅ Path calculated with {currentPath.Count} points");

        // WORLD MODE: Create arrows immediately
        if (!hudOnly)
        {
            EnsureActiveChain();
            ClearPathArrows();
            ClearFlowLine();

            // Force arrow creation
            pathStyle = PathVisualStyle.Arrows;
            CreateGroundArrowsForPath();

            Debug.Log($"✅ Created {pathArrows.Count} arrows");

            // Show target marker
            ShowEnhancedTargetMarker();
        }

        // Update UI
        isNavigating = true;
        hasReachedDestination = false;
        currentPathIndex = 0;

        if (statusText) statusText.text = $"Navigating to {currentDestination}";
        if (navigateButton) navigateButton.gameObject.SetActive(false);
        if (stopButton) stopButton.gameObject.SetActive(true);

        Debug.Log("=== Navigation started successfully ===");
    }
    // simple flag used by navigation flow
    private bool hasReachedDestination = false;

    // Return a usable user position (prefers lastPlayerPosition if available, otherwise camera)
    public Vector3 GetUserPosition()
    {
        // lastPlayerPosition exists in your class; use it if navigation started
        try
        {
            // prefer lastPlayerPosition if it was set
            if (lastPlayerPosition != Vector3.zero) return lastPlayerPosition;
        }
        catch { /* ignore if not present */ }

        // fallback to AR/main camera
        if (arCamera != null) return arCamera.transform.position;
        if (Camera.main != null) return Camera.main.transform.position;
        return Vector3.zero;
    }

    // Basic arrow visibility updater (keeps arrows active when reasonably near camera)
    public void UpdateArrowVisibility()
    {
        if (pathArrows == null || pathArrows.Count == 0) return;
        Camera cam = (arCamera != null) ? arCamera : Camera.main;
        if (cam == null) return;

        float visibleDist = 50f; // meters, adjust as needed
        for (int i = 0; i < pathArrows.Count; i++)
        {
            var go = pathArrows[i];
            if (go == null) continue;
            float d = Vector3.Distance(cam.transform.position, go.transform.position);
            bool show = d <= visibleDist;
            if (go.activeSelf != show) go.SetActive(show);
        }
    }

    // ----------------- Paste this once inside SmartNavigationSystem class -----------------

    /// <summary>
    /// Create arrows for the currentPath (uses existing CreateGroundArrowsForSegment).
    /// This is the canonical zero-arg wrapper used by other code.
    /// </summary>
    public void CreateArrowsForPath()
    {
        if (hudOnly) return;

        // Ensure we have a place to put arrows
        EnsurePathRoot();
        ClearPathArrowsIfNeeded();

        if (currentPath == null || currentPath.Count < 2)
        {
            Debug.LogWarning("CreateArrowsForPath: currentPath is empty or too short.");
            return;
        }

        // Instantiate arrows along each segment
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            CreateGroundArrowsForSegment(currentPath[i], currentPath[i + 1]);
        }
    }

    /// <summary>
    /// Overload: allow callers to pass a path (List<Vector3>). This was added because some code
    /// previously called CreateArrowsForPath(path).
    /// </summary>
    public void CreateArrowsForPath(System.Collections.Generic.List<Vector3> path)
    {
        if (path == null || path.Count < 2)
        {
            Debug.LogWarning("CreateArrowsForPath(path): provided path is empty or too short.");
            return;
        }

        // Replace currentPath with provided path, then delegate to zero-arg method
        currentPath = new System.Collections.Generic.List<Vector3>(path);
        CreateArrowsForPath();
    }
    private void CalculateAndShowPath(Vector3 start, Vector3 target)
    {
        Debug.Log($"🔄 Calculating immediate path: {start} → {target}");

        // Get all waypoints
        var allWaypoints = GetAllRuntimeWaypoints();
        if (allWaypoints == null || allWaypoints.Length == 0)
        {
            Debug.LogError("❌ No waypoints found!");
            return;
        }

        // Calculate path
        List<Vector3> newPath = FindComplexPath(start, target, allWaypoints);

        if (newPath != null && newPath.Count > 0)
        {
            currentPath = newPath;
            currentPathIndex = 0;

            // IMMEDIATELY create arrows
            CreateArrowsForPath(currentPath);

            Debug.Log($"✅ Path calculated with {currentPath.Count} points - arrows created immediately");

            // Log the path for debugging
            for (int i = 0; i < currentPath.Count; i++)
            {
                Debug.Log($"  Path[{i}]: {currentPath[i]}");
            }
        }
        else
        {
            Debug.LogError("❌ Failed to calculate path");
        }
    }



    [ContextMenu("Test Specific Navigation")]
    void TestSpecificNavigation()
    {
        Debug.Log("=== TESTING SPECIFIC NAVIGATION ===");

        // Test EDP to License Division specifically
        string start = "EDP";
        string dest = "License Division";

        Debug.Log($"Testing navigation: {start} → {dest}");

        // Clear any existing navigation
        StopNavigation();

        // Start the navigation
        StartNavigationFromOfficeName(start, dest);

        // Check the results
        Debug.Log($"After navigation start:");
        Debug.Log($"  targetDestination: {targetDestination}");
        Debug.Log($"  currentDestination: '{currentDestination}'");
        Debug.Log($"  Path end point: {(currentPath?.Count > 0 ? currentPath[currentPath.Count - 1].ToString() : "No path")}");
    }
    string NormalizeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant().Trim();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\s_\-]+", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9]", "");
        return s;
    }

    // real-time turn hint updater
    void UpdateTurnHints()
    {
        if (!isNavigating || _turns == null || _turns.Count == 0)
        {
            if (turnText) turnText.text = "";
            return;
        }

        // clamp/ensure index
        if (_nextTurn < 0) _nextTurn = 0;

        // If we already passed all turns
        if (_nextTurn >= _turns.Count)
        {
            if (turnText) turnText.text = "Continue straight to destination";
            return;
        }

        // Get user position (your existing method)
        Vector3 camWorld = GetCurrentPosition();
        Vector3 camFlat = new Vector3(camWorld.x, 0f, camWorld.z);

        // Get camera forward (flattened). Use assigned userCamera else Camera.main else fallback.
        Vector3 camForward;
        if (userCamera != null) camForward = userCamera.forward;
        else if (Camera.main != null) camForward = Camera.main.transform.forward;
        else camForward = transform.forward;
        Vector3 camForwardFlat = new Vector3(camForward.x, 0f, camForward.z);
        if (camForwardFlat.sqrMagnitude < 0.0001f) camForwardFlat = Vector3.forward;
        camForwardFlat.Normalize();

        // Advance _nextTurn if we've passed it
        while (_nextTurn < _turns.Count)
        {
            Vector3 turnPos = _turns[_nextTurn].pos;
            Vector3 turnFlat = new Vector3(turnPos.x, 0f, turnPos.z);
            float passDist = Vector3.Distance(camFlat, turnFlat);
            if (passDist <= turnPassDistance)
            {
                _nextTurn++;
                _turnAnnounced = false;
            }
            else break;
        }

        if (_nextTurn >= _turns.Count)
        {
            if (turnText) turnText.text = "Continue straight to destination";
            return;
        }

        // Compute direction + distance to next turn
        Vector3 nextPos = _turns[_nextTurn].pos;
        Vector3 nextFlat = new Vector3(nextPos.x, 0f, nextPos.z);
        Vector3 toNext = nextFlat - camFlat;
        float d = toNext.magnitude;
        if (d < 0.0001f) d = 0.0001f;
        Vector3 toNextN = toNext.normalized;

        float angle = Vector3.SignedAngle(camForwardFlat, toNextN, Vector3.up);
        float absA = Mathf.Abs(angle);

        string instruction;
        float straightAngle = 15f;
        float slightAngle = 45f;
        if (absA <= straightAngle) instruction = "Go straight";
        else if (absA <= slightAngle) instruction = angle > 0 ? "Slight left" : "Slight right";
        else if (absA <= 135f) instruction = angle > 0 ? "Turn left" : "Turn right";
        else instruction = "Make a U-turn";

        string distStr = d < 1f ? $"{Mathf.RoundToInt(d * 100f)} cm" : $"{d:F1} m";

        // upcoming turn hint
        string upcoming = "";
        if (_nextTurn + 1 < _turns.Count)
        {
            Vector3 after = _turns[_nextTurn + 1].pos;
            Vector3 afterFlat = new Vector3(after.x - nextPos.x, 0f, after.z - nextPos.z);
            if (afterFlat.sqrMagnitude > 0.0001f)
            {
                float turnAngle = Vector3.SignedAngle(toNextN, afterFlat.normalized, Vector3.up);
                float a2 = Mathf.Abs(turnAngle);
                if (a2 <= straightAngle) upcoming = "Then: continue straight";
                else if (a2 <= slightAngle) upcoming = turnAngle > 0 ? "Then: slight left" : "Then: slight right";
                else if (a2 <= 135f) upcoming = turnAngle > 0 ? "Then: turn left" : "Then: turn right";
                else upcoming = "Then: make a U-turn";
            }
        }

        if (turnText)
        {
            turnText.text = string.IsNullOrEmpty(upcoming) ? $"{instruction} • {distStr}" : $"{instruction} • {distStr} ({upcoming})";
        }

        // keep announce logic if desired
        if (!_turnAnnounced && d <= turnAnnounceDistance)
        {
            _turnAnnounced = true;
            // Optional: call audio announce here
        }
    }

    void AnimateEnhancedTargetMarker()
    {
        if (targetMarker == null || !targetMarker.activeInHierarchy) return;

        targetBobOffset += Time.deltaTime * bobSpeed;
        float bobY = Mathf.Sin(targetBobOffset) * bobHeight;

        // Animate position (bobbing)
        Vector3 basePos = originalTargetPosition;
        Vector3 animatedPos = basePos + new Vector3(0, bobY, 0);
        targetMarker.transform.position = animatedPos;

        // Animate rotation
        targetMarker.transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);

        // Optional: Scale pulse
        float scaleMultiplier = 1f + Mathf.Sin(targetBobOffset * 1.5f) * 0.1f;
        targetMarker.transform.localScale = Vector3.one * scaleMultiplier;
    }
    // === Compatibility Methods for UI Scripts ===
    public static bool IsAnyNavigationActive()
    {
        SmartNavigationSystem instance = FindFirstObjectByType<SmartNavigationSystem>();
        return instance != null && instance.isNavigating;
    }

    public bool CanStartNavigation()
    {
        // You could expand this (e.g., block if Vuforia required but no target tracked)
        return !isNavigating;
    }

    public string GetNavigationStatus()
    {
        if (isNavigating)
        {
            if (!string.IsNullOrEmpty(currentDestination))
                return $"Navigating to {currentDestination}";
            else
                return "Navigation active";
        }
        else
        {
            return useVuforiaPositioning
                ? (isImageTargetTracked ? "Ready to navigate" : "Point camera at office sign")
                : "Idle";
        }
    }

    void UpdateFloorVisibility()
    {
        if (groundFloor == null || secondFloor == null) return;

        bool pathHasSecondFloor = false;

        foreach (var pos in currentPath)
        {
            if (pos.y > 5f) // adjust threshold to match your floor height
            {
                pathHasSecondFloor = true;
                break;
            }
        }
        if (pathHasSecondFloor)
        {
            if (currentFloor != "Second")
            {
                currentFloor = "Second";
                CrossfadeFloors(groundFloor, secondFloor, 1.5f);
            }
        }
        else
        {
            if (currentFloor != "Ground")
            {
                currentFloor = "Ground";
                CrossfadeFloors(secondFloor, groundFloor, 1.5f);
            }
        }
    }


    void SetFloorVisible(GameObject floor, bool visible)
    {
        if (floor == null) return;

        Renderer[] renderers = floor.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            // Skip any renderer on a waypoint GameObject
            if (renderer.GetComponent<NavigationWaypoint>() == null)
            {
                renderer.enabled = visible;
            }
        }

        Debug.Log($"Floor {floor.name} renderers: {(visible ? "shown" : "hidden")}");
    }

    void ShowOnlyFloor(string floorName)
    {
        if (groundFloor != null)
            SetFloorVisible(groundFloor, floorName == "Ground");

        if (secondFloor != null)
            SetFloorVisible(secondFloor, floorName == "Second");
    }

    void DebugSecondFloorRenderers()
    {
        if (secondFloor == null)
        {
            Debug.LogWarning("SecondFloor object is not assigned!");
            return;
        }

        var renderers = secondFloor.GetComponentsInChildren<MeshRenderer>(true);
        Debug.Log($"Found {renderers.Length} MeshRenderers under SecondFloor:");

        foreach (var r in renderers)
        {
            Debug.Log($"Renderer: {r.gameObject.name}, enabled = {r.enabled}");
        }
    }

    void HideSecondFloorByHeight(bool visible)
    {
        var renderers = FindObjectsOfType<MeshRenderer>(true);
        foreach (var r in renderers)
        {
            if (r.transform.position.y > 5f) // adjust threshold = floor height
            {
                r.enabled = visible;
            }
        }
    }
    private void CrossfadeFloors(GameObject fromFloor, GameObject toFloor, float duration = 1.5f)
    {
        if (fromFloor != null)
            StartCoroutine(FadeFloor(fromFloor, false, duration));
        if (toFloor != null)
            StartCoroutine(FadeFloor(toFloor, true, duration));
    }


    private IEnumerator FadeFloor(GameObject floor, bool fadeIn, float duration = 1f)
    {
        if (floor == null) yield break;
        Renderer[] renderers = floor.GetComponentsInChildren<Renderer>(true);
        float start = fadeIn ? 0f : 1f;
        float end = fadeIn ? 1f : 0f;
        float t = 0f;

        if (fadeIn) floor.SetActive(true);

        // Switch to Fade blend
        foreach (var r in renderers)
            foreach (var mat in r.materials)
            {
                mat.SetFloat("_Mode", 2); // Fade
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }

        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(start, end, t / duration);
            foreach (var r in renderers)
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        var c = mat.color; c.a = a; mat.color = c;
                    }
                }
            yield return null;
        }

        if (fadeIn)
        {
            // Restore Opaque
            foreach (var r in renderers)
                foreach (var mat in r.materials)
                {
                    mat.SetFloat("_Mode", 0); // Opaque
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = -1;

                    if (mat.HasProperty("_Color"))
                    {
                        var c = mat.color; c.a = 1f; mat.color = c;
                    }
                }
        }
        else
        {
            // WORLD mode may deactivate; HUD keeps object active but hidden
            if (!hudOnly)
            {
                floor.SetActive(false);
            }
            else
            {
                foreach (var r in renderers) r.enabled = false;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        NavigationWaypoint wp = other.GetComponent<NavigationWaypoint>();
        if (wp != null && wp.waypointType == WaypointType.Stairs)
        {
            if (currentFloor == "Ground")
            {
                currentFloor = "Second";
                CrossfadeFloors(groundFloor, secondFloor, 1.5f);
                Debug.Log("Switching UP to Second Floor (fade)");
            }
            else if (currentFloor == "Second")
            {
                currentFloor = "Ground";
                CrossfadeFloors(secondFloor, groundFloor, 1.5f);
                Debug.Log("Switching DOWN to Ground Floor (fade)");
            }
        }
    }

    public void CacheAllOffices()
    {
        officeLookup.Clear();

        // ✅ Use new Unity API
        NavigationWaypoint[] allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();

        foreach (var wp in allWaypoints)
        {
            // Debug every waypoint for visibility
            Debug.Log($"Waypoint found: {wp.gameObject.name}, type={wp.waypointType}, officeName={wp.officeName}");

            // Skip corridors and stairs
            if (wp.waypointType == WaypointType.Corridor || wp.waypointType == WaypointType.Stairs)
                continue;

            // Use officeName if available, fallback to GameObject name
            string name = !string.IsNullOrEmpty(wp.officeName) ? wp.officeName : wp.gameObject.name;

            if (!string.IsNullOrEmpty(name))
            {
                if (!officeLookup.ContainsKey(name)) // prevent duplicates
                {
                    officeLookup[name] = wp.gameObject;
                }
                else
                {
                    Debug.LogWarning($"⚠ Duplicate office name skipped: {name}");
                }
            }
        }

        Debug.Log($"✅ Cached {officeLookup.Count} offices (includes inactive floors).");
    }

    void AlignAnchorYawToCamera()
    {
        if (!contentAnchor || !arCamera || !centerAnchor) return;
        // Compute desired world pose for the content, based on the marker

        Vector3 anchorPos = centerAnchor.position;
        Quaternion anchorRot = centerAnchor.rotation;

        Quaternion layFlat = centerAnchorIsOnWall ? Quaternion.Euler(0f, 0f, 0f) : Quaternion.identity;
        float yaw = arCamera.transform.eulerAngles.y * Mathf.Clamp01(faceUserYawBlend);
        Quaternion faceUser = Quaternion.Euler(0f, yaw, 0f);

        Quaternion desiredRot = anchorRot * faceUser * layFlat;
        Vector3 desiredPos = anchorPos + (desiredRot * Vector3.up) * contentLocalOffset.y; // push out from wall

        if (worldRoot)
        {
            worldRoot.position = desiredPos;
            worldRoot.rotation = desiredRot;
            contentAnchor.SetParent(worldRoot, false);
        }
        else
        {
            contentAnchor.SetParent(centerAnchor, false);
            contentAnchor.SetPositionAndRotation(desiredPos, desiredRot);
        }

        if (!contentAnchor.gameObject.activeSelf) contentAnchor.gameObject.SetActive(true);
        Debug.Log($"[Anchor] placed. pos={desiredPos}, rot={desiredRot.eulerAngles}");
    }

    public List<string> GetAllOfficeNames()
    {
        var officeNames = new List<string>();
        var allWaypoints = GetAllRuntimeWaypoints();

        foreach (var wp in allWaypoints)
        {
            // STRICT FILTER: Only Office type waypoints
            if (wp.waypointType != WaypointType.Office) continue;

            // Get the display name (prefer officeName, fallback to waypointName)
            string displayName = !string.IsNullOrEmpty(wp.officeName) ? wp.officeName : wp.waypointName;

            // Clean up the name
            if (displayName.StartsWith("Waypoint_"))
                displayName = displayName.Replace("Waypoint_", "");

            if (!string.IsNullOrEmpty(displayName) && !officeNames.Contains(displayName))
            {
                officeNames.Add(displayName);
                Debug.Log($"[OFFICE-ONLY] Added: {displayName} (from waypoint: {wp.name})");
            }
        }

        Debug.Log($"GetAllOfficeNames returning {officeNames.Count} OFFICES ONLY");
        return officeNames.OrderBy(name => name).ToList();
    }

    NavigationWaypoint GetNearestWaypoint()
{
    var all = GetAllRuntimeWaypoints();
    NavigationWaypoint nearest = null;
    float minDist = Mathf.Infinity;
    Vector3 playerPos = GetCurrentPosition();

    foreach (var wp in all)
    {
        float d = Vector3.Distance(playerPos, wp.transform.position);
        if (d < minDist)
        {
            minDist = d;
            nearest = wp;
        }
    }
    return nearest;
}

    [ContextMenu("Debug WorldRoot Transform")]
    void DebugWorldRootTransform()
    {
        Debug.Log("=== WORLDROOT TRANSFORM DEBUG ===");

        if (worldRoot)
        {
            Debug.Log($"WorldRoot position: {worldRoot.position}");
            Debug.Log($"WorldRoot rotation: {worldRoot.rotation.eulerAngles}");
            Debug.Log($"WorldRoot scale: {worldRoot.localScale}");
        }

        if (contentAnchor)
        {
            Debug.Log($"ContentAnchor position: {contentAnchor.position}");
            Debug.Log($"ContentAnchor rotation: {contentAnchor.rotation.eulerAngles}");
            Debug.Log($"ContentAnchor scale: {contentAnchor.localScale}");
        }

        if (pathRoot)
        {
            Debug.Log($"PathRoot position: {pathRoot.position}");
            Debug.Log($"PathRoot rotation: {pathRoot.rotation.eulerAngles}");
            Debug.Log($"PathRoot scale: {pathRoot.localScale}");
            Debug.Log($"PathRoot parent: {pathRoot.parent?.name}");
        }

        // Check if transforms are causing the offset
        if (worldRoot && contentAnchor)
        {
            Vector3 testPoint = new Vector3(0, 0, 0);
            Vector3 worldPoint = worldRoot.TransformPoint(testPoint);
            Vector3 contentPoint = contentAnchor.TransformPoint(testPoint);

            Debug.Log($"Test point (0,0,0) → WorldRoot: {worldPoint}, ContentAnchor: {contentPoint}");
        }
    }
    [ContextMenu("Validate Waypoint Positions")]
    void ValidateWaypointPositions()
    {
        Debug.Log("=== WAYPOINT POSITION VALIDATION ===");

        var allWaypoints = GetAllRuntimeWaypoints();
        int outsideCount = 0;

        foreach (var wp in allWaypoints)
        {
            bool insideFloor = IsPointInsideFloorBounds(wp.transform.position);
            if (!insideFloor)
            {
                outsideCount++;
                Debug.LogWarning($"❌ Waypoint {wp.name} is OUTSIDE floor bounds at {wp.transform.position}");
            }
            else
            {
                Debug.Log($"✅ Waypoint {wp.name} is inside floor bounds");
            }
        }

        Debug.Log($"Summary: {outsideCount} waypoints are outside floor bounds out of {allWaypoints.Length} total");

        if (outsideCount > 0)
        {
            Debug.LogError("Some waypoints are outside the floor plan! This will cause arrows to appear outside the building.");
        }
    }
    // Add this test method to SmartNavigationSystem
    [ContextMenu("Test Create Visible Arrow")]
    void TestCreateVisibleArrow()
    {
        // Create a simple cube right in front of camera
        GameObject testArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        testArrow.name = "TEST_ARROW";

        // Position it 3 meters in front of AR camera
        if (arCamera != null)
        {
            testArrow.transform.position = arCamera.transform.position + arCamera.transform.forward * 3f;
        }
        else
        {
            testArrow.transform.position = new Vector3(0, 1, 5);
        }

        // Make it bright magenta and huge
        testArrow.transform.localScale = Vector3.one * 2f;
        Renderer r = testArrow.GetComponent<Renderer>();
        r.material.color = Color.magenta;

        Debug.Log($"Test arrow created at {testArrow.transform.position}");
    }

    void CheckArrivalAtDestination()
    {
        if (!isNavigating || hasReachedDestination) return;

        Vector3 userPos = GetUserPosition();

        // Calculate distance to final destination
        float distanceToTarget = Vector3.Distance(
            new Vector3(userPos.x, 0, userPos.z),
            new Vector3(targetDestination.x, 0, targetDestination.z)
        );

        Debug.Log($"[ARRIVAL] Distance to target: {distanceToTarget:F2}m (threshold: {arrivalDistanceThreshold}m)");

        // Check if within arrival zone
        if (distanceToTarget <= arrivalDistanceThreshold)
        {
            timeWithinArrivalZone += Time.deltaTime;
            Debug.Log($"[ARRIVAL] Within zone for {timeWithinArrivalZone:F2}s (need {arrivalConfirmTime}s)");

            // Confirm arrival after staying in zone for required time
            if (timeWithinArrivalZone >= arrivalConfirmTime && !hasShownArrivalNotification)
            {
                OnArrivalAtDestination();
            }
        }
        else
        {
            // Reset timer if user moves away
            if (timeWithinArrivalZone > 0)
            {
                Debug.Log("[ARRIVAL] Left arrival zone, resetting timer");
            }
            timeWithinArrivalZone = 0f;
        }
    }

    void OnArrivalAtDestination()
    {
        Debug.Log($"[ARRIVAL] ✅ USER ARRIVED AT: {currentDestination}");

        hasReachedDestination = true;
        hasShownArrivalNotification = true;

        // Hide arrows and target marker
        ClearPathArrows();
        if (targetMarker) targetMarker.SetActive(false);

        // Update status
        if (statusText)
            statusText.text = $"You have arrived at {currentDestination}!";

        // Show service selection popup (NEW!)
        var arrivalManager = FindFirstObjectByType<ServiceArrivalManager>();
        if (arrivalManager)
        {
            arrivalManager.ShowArrivalPopup(currentDestination);
        }

        else
        {
            // Fallback to simple arrival notification
            var ui = FindFirstObjectByType<ManilaServeUI>();
            if (ui != null)
            {
                ui.ShowArrivalConfirmation(currentDestination);
            }
        }

        // Play arrival sound if you have one (optional)
        // AudioSource.PlayClipAtPoint(arrivalSound, Camera.main.transform.position);

        // Stop navigation after a short delay
        StartCoroutine(DelayedStopNavigation(5f)); // Increased to 5s to allow time to select service
    }

    IEnumerator DelayedStopNavigation(float delay)
    {
        yield return new WaitForSeconds(delay);

        Debug.Log("[ARRIVAL] Auto-stopping navigation after arrival");
        StopNavigation();

        // Reset arrival flags
        hasReachedDestination = false;
        hasShownArrivalNotification = false;
    }

    class ArrowInstance : MonoBehaviour
    {
        public float baseScale;  // world scale to stick to
        public float phase;      // random phase so arrows don’t pulse in sync
    }

    void EnsureActiveChain()
    {
        if (hudOnly) return; // HUD: never toggle world parents
        if (contentAnchor && !contentAnchor.gameObject.activeSelf)
            contentAnchor.gameObject.SetActive(true);
        if (pathRoot && !pathRoot.gameObject.activeSelf)
            pathRoot.gameObject.SetActive(true);
    }

    void OnHudMeters(float meters)
    {
        if (!(hudOnly && hudUsePdr && isNavigating)) return;

        // Heading: prefer DeviceHeading (compass); fallback to camera yaw
        float heading = DeviceHeading.Instance ? DeviceHeading.Instance.YawDeg :
                        (arCamera ? arCamera.transform.eulerAngles.y : 0f);

        // Convert to radians and align your building to true north if needed
        float yaw = (heading - hudWorldNorthOffset) * Mathf.Deg2Rad;

        // "Forward" in world when phone faces heading (north-aligned): (sin, 0, cos)
        Vector3 stepDir = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

        // Advance virtual position by step distance
        hudUserPos += stepDir * meters;

        // Optional small re-path to adapt to drift (inexpensive)
        if (Time.frameCount % 15 == 0)
            CalculateWallAvoidingPath();

        // Update UI hints
        UpdateDirections();

        // Arrival logic
        float remain = Vector3.Distance(
            new Vector3(hudUserPos.x, 0f, hudUserPos.z),
            new Vector3(targetDestination.x, 0f, targetDestination.z)
        );

        if (distanceText) distanceText.text = remain > 1f ? $"{remain:F1} m" : "Almost there";

        if (remain < 1.5f)
            StopNavigation();
    }

    // Call this from PlaceOnFloorARF after alignment
    public void SnapPathYsToPlaneY(float planeY)
    {
        if (currentPath == null || currentPath.Count == 0) return;

        for (int i = 0; i < currentPath.Count; i++)
        {
            Vector3 wp = currentPath[i];
            currentPath[i] = new Vector3(wp.x, planeY, wp.z);
        }

        // Make the path endpoint the canonical navigation target
        targetDestination = currentPath[currentPath.Count - 1];

        Debug.Log($"SnapPathYsToPlaneY: snapped path to Y={planeY:F3}, targetDestination={targetDestination}");
    }
    // Add this method to SmartNavigationSystem
    private string GetCorrectWaypointName(string officeName)
    {
        // Map user-friendly names to actual waypoint office names
        var nameMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // User name → Waypoint officeName
        { "EDP", "EDP" },
        { "License Division", "License Division" },
        { "Manila Health Department", "Manila Health Department" },
        { "Cash Division", "Cash Division" },
        { "Civil Registry", "Civil Registry" },
        { "Bureau of Permits", "Bureu of Permits" }, // Note: typo in waypoint
        { "Bureu of Permits", "Bureu of Permits" }, // Handle the typo
        { "City Treasurer", "City Treasurer" },
        { "Social Welfare", "Social Welfare" },
        { "OSCA", "OSCA" },
        { "Real Estate Division", "Real Estate Division" },
        { "E-Business", "E-Business" },
        { "Mayor's Office", "Mayor's Office" },
        { "Office of the City Admin", "Office of the City Admin" },
        
        // Add Firebase names that might be selected
        { "Manila Health Department (MHD)", "Manila Health Department" },
        { "Civil Registry Office", "Civil Registry" },
        { "Bureau of Permits (BPLO)", "Bureu of Permits" },
        { "Treasurer & Assessor's Office", "City Treasurer" },
        { "Manila Department of Social Welfare (MDSW)", "Social Welfare" },
        { "Office of Senior Citizens Affairs (OSCA)", "OSCA" },
        { "e-Services / Technical Support (EDP Office)", "EDP" },
        { "Office of the City Mayor", "Mayor's Office" },
    };

        if (nameMapping.TryGetValue(officeName, out string mappedName))
        {
            Debug.Log($"Mapped '{officeName}' → '{mappedName}'");
            return mappedName;
        }

        return officeName; // Return original if no mapping found
    }

    [ContextMenu("Debug Waypoint Connections")]
    void DebugWaypointConnections()
    {
        var allWaypoints = GetAllRuntimeWaypoints();
        Debug.Log($"=== WAYPOINT CONNECTION DEBUG ===");

        foreach (var wp in allWaypoints)
        {
            if (wp.connectedWaypoints == null || wp.connectedWaypoints.Count == 0)
            {
                Debug.LogWarning($"⚠ {wp.name} has NO connections!");
            }
            else
            {
                string connections = string.Join(", ", wp.connectedWaypoints.Select(c => c ? c.name : "NULL"));
                Debug.Log($"✓ {wp.name} → [{connections}]");
            }
        }
    }

    [ContextMenu("Debug Target Marker")]
    void DebugTargetMarker()
    {
        Debug.Log($"=== TARGET MARKER DEBUG ===");
        Debug.Log($"targetMarker exists: {targetMarker != null}");
        if (targetMarker)
        {
            Debug.Log($"targetMarker active: {targetMarker.activeInHierarchy}");
            Debug.Log($"targetMarker position: {targetMarker.transform.position}");
            Debug.Log($"targetMarker parent: {targetMarker.transform.parent?.name}");
        }
        Debug.Log($"targetDestination: {targetDestination}");
        Debug.Log($"originalTargetPosition: {originalTargetPosition}");
        Debug.Log($"isNavigating: {isNavigating}");
        Debug.Log($"hudOnly: {hudOnly}");
        Debug.Log($"worldBaked: {worldBaked}");
    }

    [ContextMenu("Debug Waypoint Network")]
    void DebugWaypointNetwork()
    {
        var allWaypoints = GetAllRuntimeWaypoints();
        Debug.Log($"=== WAYPOINT NETWORK DEBUG ===");

        int totalConnections = 0;
        foreach (var wp in allWaypoints)
        {
            if (wp.connectedWaypoints == null || wp.connectedWaypoints.Count == 0)
            {
                Debug.LogError($"❌ {wp.name} ({wp.waypointType}) has NO connections!");
            }
            else
            {
                totalConnections += wp.connectedWaypoints.Count;
                string connections = string.Join(", ", wp.connectedWaypoints.Select(c => c ? c.name : "NULL"));
                Debug.Log($"✅ {wp.name} ({wp.waypointType}) → [{connections}]");
            }
        }

        Debug.Log($"Total waypoints: {allWaypoints.Length}, Total connections: {totalConnections}");
    }

    [ContextMenu("List All Office Waypoints")]
    void ListAllOfficeWaypoints()
    {
        Debug.Log("=== ALL OFFICE WAYPOINTS ===");
        var allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();

        foreach (var wp in allWaypoints)
        {
            if (wp.gameObject.scene.name == null) continue; // Skip prefabs
            if (wp.waypointType == WaypointType.Office)
            {
                Debug.Log($"OFFICE: {wp.name} | officeName: '{wp.officeName}' | waypointName: '{wp.waypointName}' | Position: {wp.transform.position}");
            }
        }
    }
    [ContextMenu("Debug Navigation Path")]
    void DebugNavigationPath()
    {
        Debug.Log("=== NAVIGATION PATH DEBUG ===");

        // Test EDP to License Division
        string startOffice = "EDP";
        string targetOffice = "License Division";

        Debug.Log($"Testing: {startOffice} → {targetOffice}");

        // Find the waypoints
        var startWP = FindTargetWaypointAdvanced(startOffice);
        var targetWP = FindTargetWaypointAdvanced(targetOffice);

        Debug.Log($"Start waypoint: {startWP?.name} at {startWP?.transform.position}");
        Debug.Log($"Target waypoint: {targetWP?.name} at {targetWP?.transform.position}");

        if (startWP && targetWP)
        {
            // Test the pathfinding
            var allWaypoints = GetAllRuntimeWaypoints();
            var path = FindComplexPath(startWP.transform.position, targetWP.transform.position, allWaypoints);

            Debug.Log($"Generated path has {path.Count} points:");
            for (int i = 0; i < path.Count; i++)
            {
                Debug.Log($"  Path[{i}]: {path[i]}");

                // Find nearest waypoint to each path point
                var nearestWP = FindNearestWaypoint(path[i], allWaypoints);
                Debug.Log($"    Nearest waypoint: {nearestWP?.name} (distance: {Vector3.Distance(path[i], nearestWP.transform.position):F2}m)");
            }

            // Check if path goes through correct waypoints
            Debug.Log("Expected path should go through waypoints like:");
            Debug.Log("EDP → ET_CorridorEDP → ET_CorridorLong → LicenseDiv");
        }
    }

    [ContextMenu("Test Enhanced EDP to License Path")]
    void TestEnhancedEDPToLicensePath()
    {
        Debug.Log("=== TESTING ENHANCED EDP TO LICENSE DIVISION PATH ===");

        var allWaypoints = GetAllRuntimeWaypoints();
        var edp = allWaypoints.FirstOrDefault(w => w.name.Contains("EDP"));
        var license = allWaypoints.FirstOrDefault(w => w.name.Contains("LicenseDiv"));

        if (edp && license)
        {
            var waypointPath = RunStrictWaypointAStar(edp, license);
            if (waypointPath != null)
            {
                Debug.Log($"Enhanced A* waypoint path: {string.Join(" → ", waypointPath.Select(w => w.name))}");

                // Check if it uses corridors
                var corridorCount = waypointPath.Count(w => w.waypointType == WaypointType.Corridor || w.waypointType == WaypointType.Junction);
                Debug.Log($"Path uses {corridorCount} corridor/junction waypoints out of {waypointPath.Count} total");
            }
        }
    }

    [ContextMenu("Debug Arrow Positions")]
    void DebugArrowPositions()
    {
        Debug.Log("=== ARROW POSITION DEBUG ===");

        if (currentPath == null || currentPath.Count == 0)
        {
            Debug.LogError("No current path to debug!");
            return;
        }

        Debug.Log($"Current path has {currentPath.Count} points:");
        for (int i = 0; i < currentPath.Count; i++)
        {
            Vector3 pathPoint = currentPath[i];
            Vector3 groundPos = GetGroundPosition(pathPoint);

            Debug.Log($"Path[{i}]: {pathPoint} → Ground: {groundPos}");

            // Check if this point is inside your floor bounds
            bool insideFloor = IsPointInsideFloorBounds(pathPoint);
            Debug.Log($"  Inside floor bounds: {insideFloor}");
        }

        // Debug your floor plan bounds
        DebugFloorPlanBounds();
    }

    [ContextMenu("Debug Floor Plan Bounds")]
    void DebugFloorPlanBounds()
    {
        Debug.Log("=== FLOOR PLAN BOUNDS DEBUG ===");

        if (groundFloor)
        {
            var bounds = GetFloorBounds(groundFloor);
            Debug.Log($"Ground floor bounds: {bounds}");
        }

        if (secondFloor)
        {
            var bounds = GetFloorBounds(secondFloor);
            Debug.Log($"Second floor bounds: {bounds}");
        }

        // Debug waypoint positions relative to floor
        var allWaypoints = GetAllRuntimeWaypoints();
        Debug.Log($"Checking {allWaypoints.Length} waypoints against floor bounds:");

        foreach (var wp in allWaypoints.Take(5)) // Check first 5 waypoints
        {
            bool insideFloor = IsPointInsideFloorBounds(wp.transform.position);
            Debug.Log($"Waypoint {wp.name} at {wp.transform.position}: Inside floor = {insideFloor}");
        }
    }

    private Bounds GetFloorBounds(GameObject floor)
    {
        var renderers = floor.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds();

        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    private bool IsPointInsideFloorBounds(Vector3 point)
    {
        // Check against ground floor
        if (groundFloor)
        {
            var bounds = GetFloorBounds(groundFloor);
            if (bounds.Contains(point)) return true;
        }

        // Check against second floor
        if (secondFloor)
        {
            var bounds = GetFloorBounds(secondFloor);
            if (bounds.Contains(point)) return true;
        }

        return false;
    }

    [ContextMenu("Fix ContentAnchor Rotation")]
    void FixContentAnchorRotation()
    {
        Debug.Log("=== FIXING CONTENTANCHOR ROTATION ===");

        if (contentAnchor)
        {
            Debug.Log($"Before: ContentAnchor rotation = {contentAnchor.rotation.eulerAngles}");
            contentAnchor.rotation = Quaternion.identity; // Reset to (0,0,0)
            Debug.Log($"After: ContentAnchor rotation = {contentAnchor.rotation.eulerAngles}");
        }
        else
        {
            Debug.LogWarning("ContentAnchor is null!");
        }

        if (pathRoot)
        {
            Debug.Log($"Before: PathRoot rotation = {pathRoot.rotation.eulerAngles}");
            pathRoot.rotation = Quaternion.identity; // Reset to (0,0,0)
            Debug.Log($"After: PathRoot rotation = {pathRoot.rotation.eulerAngles}");
        }
        else
        {
            Debug.LogWarning("PathRoot is null!");
        }

        // If you're currently navigating, refresh the arrows
        if (isNavigating)
        {
            Debug.Log("Refreshing navigation arrows...");
            ClearPathArrows();
            CreateGroundArrowsForPath();
        }
    }
    [ContextMenu("Debug Current Navigation")]
    void DebugCurrentNavigation()
    {
        Debug.Log("=== CURRENT NAVIGATION DEBUG ===");
        Debug.Log($"targetDestination: {targetDestination}");
        Debug.Log($"currentDestination: '{currentDestination}'");
        Debug.Log($"isNavigating: {isNavigating}");
        Debug.Log($"startedFromOffice: {startedFromOffice}");

        // Get UI selections from ManilaServeUI
        var ui = FindFirstObjectByType<ManilaServeUI>();
        if (ui != null)
        {
            Debug.Log($"UI selectedOffice: '{ui.GetSelectedOffice()}'");
            Debug.Log($"UI currentOfficeStatus: '{ui.GetCurrentOfficeStatus()}'");
        }
        else
        {
            Debug.LogWarning("ManilaServeUI not found!");
        }

        // Check what the last navigation call was
        if (currentPath != null && currentPath.Count > 0)
        {
            Debug.Log($"Current path has {currentPath.Count} points:");
            Debug.Log($"  Start: {currentPath[0]}");
            Debug.Log($"  End: {currentPath[currentPath.Count - 1]}");

            // Find which waypoints these correspond to
            var allWaypoints = GetAllRuntimeWaypoints();
            var startWP = FindNearestWaypoint(currentPath[0], allWaypoints);
            var endWP = FindNearestWaypoint(currentPath[currentPath.Count - 1], allWaypoints);

            Debug.Log($"  Start waypoint: {startWP?.name}");
            Debug.Log($"  End waypoint: {endWP?.name}");

            // Check if end waypoint matches what we expect
            var expectedEndWP = FindTargetWaypointAdvanced(currentDestination);
            Debug.Log($"  Expected end waypoint: {expectedEndWP?.name}");
            Debug.Log($"  End waypoint matches expected: {endWP == expectedEndWP}");
        }
        else
        {
            Debug.LogWarning("No current path!");
        }
    }

    [ContextMenu("Debug Last Pathfinding")]
    void DebugLastPathfinding()
    {
        Debug.Log("=== LAST PATHFINDING DEBUG ===");

        if (string.IsNullOrEmpty(currentDestination))
        {
            Debug.LogWarning("No current destination set!");
            return;
        }

        // Find the destination waypoint
        var destWP = FindTargetWaypointAdvanced(currentDestination);
        if (destWP == null)
        {
            Debug.LogError($"Could not find waypoint for destination: '{currentDestination}'");
            return;
        }

        Debug.Log($"Destination '{currentDestination}' maps to waypoint: {destWP.name} at {destWP.transform.position}");

        // Check if our path actually ends at this waypoint
        if (currentPath != null && currentPath.Count > 0)
        {
            Vector3 pathEnd = currentPath[currentPath.Count - 1];
            float distanceToDestWP = Vector3.Distance(pathEnd, destWP.transform.position);

            Debug.Log($"Path ends at: {pathEnd}");
            Debug.Log($"Distance to destination waypoint: {distanceToDestWP:F2}m");

            if (distanceToDestWP > 1f)
            {
                Debug.LogError($"❌ Path does NOT end at the correct destination waypoint!");

                // Find what waypoint the path actually ends at
                var allWaypoints = GetAllRuntimeWaypoints();
                var actualEndWP = FindNearestWaypoint(pathEnd, allWaypoints);
                Debug.LogError($"Path actually ends near: {actualEndWP?.name}");
            }
            else
            {
                Debug.Log($"✅ Path correctly ends at destination waypoint");
            }
        }
    }

    [ContextMenu("Test Fixed Navigation")]
    void TestFixedNavigation()
    {
        Debug.Log("=== TESTING FIXED NAVIGATION ===");

        // Clear any existing navigation
        StopNavigation();

        // Force the UI to have correct selections
        var ui = FindFirstObjectByType<ManilaServeUI>();
        if (ui != null)
        {
            ui.SetCurrentOfficeForTesting("EDP");
        }

        // Test the navigation with exact office names
        StartNavigationFromOfficeName("EDP", "License Division");

        Debug.Log("=== FIXED NAVIGATION TEST COMPLETE ===");
    }
    [ContextMenu("Test All Office Mappings")]
    void TestAllOfficeMappings()
    {
        Debug.Log("=== TESTING ALL OFFICE MAPPINGS ===");

        // Get all office names from UI
        var ui = FindFirstObjectByType<ManilaServeUI>();
        if (ui == null)
        {
            Debug.LogError("ManilaServeUI not found!");
            return;
        }

        var allOfficeNames = GetAllOfficeNames();
        Debug.Log($"Testing {allOfficeNames.Count} office names:");

        foreach (var officeName in allOfficeNames)
        {
            var waypoint = FindTargetWaypointAdvanced(officeName);
            if (waypoint != null)
            {
                Debug.Log($"✅ '{officeName}' → {waypoint.name}");
            }
            else
            {
                Debug.LogError($"❌ '{officeName}' → NOT FOUND");
            }
        }
    }
    [ContextMenu("Test Manila Health Department")]
    void TestManilaHealthDepartment()
    {
        Debug.Log("=== TESTING MANILA HEALTH DEPARTMENT ===");

        StopNavigation();

        // Test finding the waypoint
        var mhdWaypoint = FindTargetWaypointAdvanced("Manila Health Department");
        if (mhdWaypoint)
        {
            Debug.Log($"✅ Found Manila Health Department: {mhdWaypoint.name} at {mhdWaypoint.transform.position}");

            // Test navigation to it
            StartNavigationFromOfficeName("EDP", "Manila Health Department");
        }
        else
        {
            Debug.LogError("❌ Could not find Manila Health Department waypoint!");
        }
    }

    [ContextMenu("Debug Dropdown Mapping")]
    void DebugDropdownMapping()
    {
        Debug.Log("=== DROPDOWN DEBUG ===");

        // Check your dropdown options
        var dropdown = GetComponent<TMP_Dropdown>(); // or however you access it
        if (dropdown != null)
        {
            for (int i = 0; i < dropdown.options.Count; i++)
            {
                Debug.Log($"Dropdown[{i}]: '{dropdown.options[i].text}'");
            }
        }

        // Check your office waypoints
        var allWaypoints = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        var officeWaypoints = allWaypoints.Where(w => w.waypointType == WaypointType.Office).ToArray();

        Debug.Log($"Found {officeWaypoints.Length} office waypoints:");
        foreach (var wp in officeWaypoints)
        {
            Debug.Log($"  - {wp.name} (office: '{wp.officeName}')");
        }
    }

    [ContextMenu("Test EDP to License (Office Start)")]
    void TestOfficeToOfficeNavigation()
    {
        Debug.Log("=== TESTING OFFICE-TO-OFFICE NAVIGATION ===");

        // Stop any existing navigation
        StopNavigation();

        // Test the navigation
        StartNavigationFromOfficeName("EDP", "License Division");

        // Wait a frame then check results
        StartCoroutine(VerifyNavigationAfterFrame());
    }

    [ContextMenu("Test Camera to License (Current Location)")]
    void TestCurrentLocationNavigation()
    {
        Debug.Log("=== TESTING CURRENT LOCATION NAVIGATION ===");

        StopNavigation();
        StartNavigationFromCurrentLocation("License Division");
        StartCoroutine(VerifyNavigationAfterFrame());
    }

    IEnumerator VerifyNavigationAfterFrame()
    {
        yield return new WaitForEndOfFrame();

        Debug.Log("=== NAVIGATION VERIFICATION ===");
        Debug.Log($"isNavigating: {isNavigating}");
        Debug.Log($"currentUserOffice: '{currentUserOffice}'");
        Debug.Log($"useOfficeAsStart: {useOfficeAsStart}");
        Debug.Log($"targetDestination: {targetDestination}");
        Debug.Log($"currentPath count: {currentPath?.Count ?? 0}");
        Debug.Log($"pathArrows count: {pathArrows?.Count ?? 0}");

        if (pathArrows != null && pathArrows.Count > 0)
        {
            Debug.Log("✅ Arrows created successfully!");
            for (int i = 0; i < Mathf.Min(3, pathArrows.Count); i++)
            {
                var arrow = pathArrows[i];
                if (arrow)
                {
                    Debug.Log($"  Arrow {i}: {arrow.name} at {arrow.transform.position}, active: {arrow.activeSelf}");
                }
            }
        }
        else
        {
            Debug.LogError("❌ NO ARROWS CREATED!");

            // Debug why
            Debug.Log($"hudOnly: {hudOnly}");
            Debug.Log($"arrowPrefab: {(arrowPrefab ? "EXISTS" : "NULL")}");
            Debug.Log($"pathRoot: {(pathRoot ? pathRoot.name : "NULL")}");
            Debug.Log($"contentAnchor: {(contentAnchor ? contentAnchor.name : "NULL")}");

            if (pathRoot)
            {
                Debug.Log($"pathRoot active: {pathRoot.gameObject.activeInHierarchy}");
                Debug.Log($"pathRoot children: {pathRoot.childCount}");
            }
        }
    }


    [ContextMenu("🧪 Test Arrival (Teleport to Target)")]
    void TestArrival()
    {
        if (!isNavigating)
        {
            Debug.LogWarning("Not navigating - start navigation first!");
            return;
        }

        // Teleport camera to destination for testing
        if (arCamera)
        {
            Vector3 testPos = targetDestination + new Vector3(0, arCamera.transform.position.y, 0);
            arCamera.transform.position = testPos;
            Debug.Log($"[TEST] Teleported to {testPos}");
        }
    }

    [ContextMenu("🔍 Show Arrival Status")]
    void DebugArrivalStatus()
    {
        Debug.Log("=== ARRIVAL DEBUG ===");
        Debug.Log($"isNavigating: {isNavigating}");
        Debug.Log($"hasReachedDestination: {hasReachedDestination}");
        Debug.Log($"hasShownArrivalNotification: {hasShownArrivalNotification}");
        Debug.Log($"timeWithinArrivalZone: {timeWithinArrivalZone:F2}s");
        Debug.Log($"arrivalDistanceThreshold: {arrivalDistanceThreshold}m");
        Debug.Log($"arrivalConfirmTime: {arrivalConfirmTime}s");

        if (isNavigating)
        {
            Vector3 userPos = GetUserPosition();
            float dist = Vector3.Distance(
                new Vector3(userPos.x, 0, userPos.z),
                new Vector3(targetDestination.x, 0, targetDestination.z)
            );
            Debug.Log($"Current distance to target: {dist:F2}m");
            Debug.Log($"Within arrival zone: {dist <= arrivalDistanceThreshold}");
        }
    }

    // Add this method to your SmartNavigationSystem class
    static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return s.ToLowerInvariant()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace("'", "")
                .Replace(".", "");
    }
}
