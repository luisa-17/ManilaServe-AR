
#if VUFORIA_PRESENT
using Vuforia;
#endif

    using UnityEngine;
    using UnityEngine.UI;
    using TMPro;
    using System.Collections.Generic;
    using System.Collections; 
    using System.Linq;
    using UnityEngine.Rendering;
    using System;
    using Random = UnityEngine.Random;
    using System.Text;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif




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

    // Force using waypoint graph (no direct shortcuts)
[SerializeField] private bool forceWaypointRouting = true;

// Path containers (add if you don’t already have them)
[SerializeField] private List<NavigationWaypoint> currentPathNodes = new List<NavigationWaypoint>();
[SerializeField] private List<Vector3> currentPath = new List<Vector3>();
private HashSet<int> stairsSegments = new HashSet<int>();
    [SerializeField] private bool drawRouteLine = false;



    // Tiny lift on stair segments (if you already have it, keep your value)
    [SerializeField] private float stairsExtraLift = 0.05f;

[Header("World horizontal ground (ARCore)")]
    bool groundPlaneReady = false;
    Vector3 groundPlanePoint;
    Vector3 groundPlaneNormal = Vector3.up;

    [Header("AR Ground Snap")]
    public UnityEngine.XR.ARFoundation.ARRaycastManager raycastMgr; // set by placement
    public bool preferARGroundRaycast = true;
    public float groundRayHeight = 2f;
    public float groundOffset = 0.02f;
    public float fallbackGroundY = 0f; // set on placement

    // ===== ENTRANCE START / DETECTION =====
    [Header("Entrance Start")]
    [Tooltip("If true, route start uses the entrance node when available; otherwise uses the camera pose.")]
    public bool startFromEntrance = true;

    [Tooltip("Entrance waypoint/node (your Waypoint_Entrance_*). Assign in Inspector or via PlaceOnFloorARF.")]
    public Transform entranceNode;

    [Tooltip("Label used by UI when showing entrance detected.")]
    public string entranceDisplayName = "City Hall Entrance";

    [Tooltip("Entrance detection radius in meters (planar).")]
    [Range(0.3f, 6f)] public float entranceDetectRadiusMeters = 2f;

    [Header("Arrows Root (World)")]
    public Transform worldRootForArrows; // assign ContentAnchor or WorldRoot in Inspector

    // Turn Hints (UI + settings    
    [Header("Turn Hints (Optional)")]
    public TMP_Text turnText;                    // assign in Inspector (or use UnityEngine.UI.Text)
    public TextMeshProUGUI headingToText; // new (assign in Inspector)
    [Range(1f, 10f)] public float turnAnnounceDistance = 3f;
    [Range(0.3f, 3f)] public float turnPassDistance = 0.7f;

    [Header("Turn Hints UI")]
    public GameObject turnPanel;                 // optional: panel to toggle visibility

    [Header("Turn Hints Settings")]
    public bool useCompassHeading = true;        // use HeadingService on device; editor falls back to camera yaw
    [Range(5, 30)] public float straightTolerance = 15f;
    [Range(25, 60)] public float slightTolerance = 45f;
    [Tooltip("Smooth the turn angle to avoid flicker (seconds)")]
    public float angleSmoothTau = 0.2f;


    // Internal state for turn guidance
    struct TurnEvent { public int index; public Vector3 pos; public int dir; } // -1 left, +1 right
    readonly List<TurnEvent> _turns = new List<TurnEvent>();
    int _nextTurn = -1;
    bool _turnAnnounced = false;
    float _angleSmoothedDeg = 0f;

    // Distance calibration and mode for UI
    [Header("Turn Hint Distance")]
    [Tooltip("If true, show distance ALONG the path instead of straight line.")]
    public bool usePathDistanceForHints = true;

    // Start-facing gate (optional)
    [Header("Start Facing Gate")]
    public bool requireFacingGate = true;                 // hide arrows until user faces route
    [Range(5f, 60f)] public float startFacingTolerance = 25f; // degrees
    [Range(0.5f, 10f)] public float gateRevealTimeout = 6f;   // seconds
    Coroutine facingGateRoutine = null;                        // store the running coroutine (optional)

    [Tooltip("Meters per Unity unit (1 if your world is meter-scaled). Adjust if your floorplan is not to real scale.")]
    public float metersPerUnit = 1f;

    // ===== Multi-floor routing =====
    [Header("Multi-floor")]
    [Tooltip("Y difference (meters) to consider two nodes on the same floor.")]
    public float floorMatchTolerance = 1.5f;

    [SerializeField] private float floorSplitY = 5f;
    private bool IsSecondFloor(Vector3 p) => p.y > floorSplitY;

    [Tooltip("If BFS fails across floors, horizontal radius (meters) to search stairs around start/goal.")]
    public float stairsSearchRadiusMeters = 30f;

    [Tooltip("Tokens used to detect stair nodes in name if stairId is not present.")]
    public string[] stairNameTokens = new[] { "stairs", "stair" };

    // ===== Stairs Handling =====
    [Header("Stairs Handling")]
    [Tooltip("World Y delta (meters) above which a segment is considered a stairs segment.")]
    public float stairsElevationDeltaMin = 0.5f;

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

    // Floorplan visibility control
    [Header("Floorplan Visibility")]
    [Tooltip("Root Transform of your 3D floor model (e.g., WorldRoot/ContentAnchor/GroundFloor_Building)")]
    public Transform floorsRoot;                // drag GroundFloor_Building here
    [Tooltip("Optional UI overlay for the floorplan image, if any")]
    public GameObject floorPlanUIRoot;          // drag the UI image/panel if you showed it before
    [Tooltip("Hide floors only while navigation is running")]
    public bool hideFloorsOnlyDuringNav = true;

    [Header("Progressive Arrows")]
    public bool progressiveReveal = true;
    public float revealAheadMeters = 10f;
    public float redrawMoveThresholdMeters = 0.5f;

    int _lastProjSeg = -1;
    Vector3 _lastProjPos;

    bool floorsWereVisibleBeforeNav = true;     // captured when nav starts
    bool navSessionActive = false;              // true between StartNavigationProceed and CancelNavigate/arrival

    [Header("Corridor Recentering")]
    public LayerMask wallsLayerMask;            // set to "Walls"
    [Range(0.5f, 5f)] public float corridorProbeHalfWidth = 1.5f;
    [Range(0f, 0.5f)] public float recenterRayOriginHeight = 0.4f;

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
    public float arrowSize = 0.6f;
    public bool useGroundDetection = true;
    public Transform arrowsParent;
    [Min(0)] public int arrowPoolPrewarm = 48;

    // Path Arrows spawn settings
    [Range(0.2f, 2f)] public float arrowSpacing = 0.8f;

    readonly List<GameObject> activeArrows = new List<GameObject>();
    Queue<GameObject> arrowPool;           // backing pool

    [Header("Arrow spawn / scale")]
    public float arrowBaseScale = 1.0f;
    public float arrowVerticalOffset = 0.02f;

    [HideInInspector] public List<GameObject> pathArrows = new List<GameObject>();

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


    [Header("Arrival Detection")]
    public float arrivalDistanceThreshold = 1.0f; // meters
    public float arrivalConfirmTime = 1.0f;       // seconds inside the zone before confirming
    public bool autoCancelOnArrival = true;
    public GameObject arrivalPanel;               // optional: assign your ArrivalPanel if you have one
    public ServiceArrivalManager serviceArrivalManager; // optional: assign if you want the popup

    // ---------------- Start override (for "I'm at <Office>") ----------------
    [SerializeField] bool useStartOverride = false;
    [SerializeField] Vector3 startOverrideWorldPos;
    [SerializeField] Transform startOverrideTransform; // for debug

    // Internal
    float timeWithinArrivalZone = 0f;
    bool hasReachedDestination = false;
    bool hasShownArrivalNotification = false;

    [SerializeField] private GameObject targetMarkerPrefab;
    [SerializeField] private Material routeMat;
    [SerializeField] private Color routeColor = new Color(0f, 0.65f, 1f, 0.85f);
    [SerializeField] private float routeWidth = 0.07f;

    private GameObject arrowGO;
    private GameObject targetMarkerGO;
    private LineRenderer routeLR;
    private Transform navRoot; // parent for nav visuals

    private NavigationWaypoint currentDestinationWaypoint = null;
    private float targetMarkerBaseY = float.NaN; // for bobbing

    // Call this from UI when user selects "I'm at <Office>"
    public bool SetStartOfficeByDisplay(string displayName)
    {
        var t = ResolveOfficeTransformByDisplay(displayName); // uses the office index
        if (!t)
        {
            Debug.LogError($"[NAV] Could not resolve start office '{displayName}'.");
            return false;
        }
        useStartOverride = true;
        startOverrideTransform = t;
        startOverrideWorldPos = t.position;
        Debug.Log($"[NAV] Start override set to '{displayName}' → {t.name} at {t.position}");
        return true;
    }

    // Call when user switches back to "Use My Current Location"
    public void ClearStartOverride()
    {
        useStartOverride = false;
        startOverrideTransform = null;
        Debug.Log("[NAV] Start override cleared (using camera position).");
    }

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

    // CanonicalName -> list of Office waypoints (active or inactive)
    Dictionary<string, List<NavigationWaypoint>> officeIndex = new Dictionary<string, List<NavigationWaypoint>>();

 
    public void BuildOfficeIndexFromScene()
    {
        if (officeIndex == null) officeIndex = new Dictionary<string, List<NavigationWaypoint>>();
        officeIndex.Clear();

        // Use Resources.FindObjectsOfTypeAll for robustness, as it finds inactive objects too
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();

        // Filter out non-scene objects (like prefabs) and non-office types
        foreach (var wp in all)
        {
            if (wp == null) continue;
            if (wp.waypointType != WaypointType.Office) continue;
            if (!wp.gameObject.scene.IsValid()) continue; // skip prefab assets

            // Use the most specific name available for display
            string display = !string.IsNullOrWhiteSpace(wp.officeName)
                ? wp.officeName
                : (!string.IsNullOrWhiteSpace(wp.waypointName) ? wp.waypointName : wp.name);

            string key = CanonicalOfficeKey(GetCorrectWaypointName(display));
            if (!officeIndex.TryGetValue(key, out var list))
            {
                list = new List<NavigationWaypoint>();
                officeIndex[key] = list;
            }
            list.Add(wp);
        }

        Debug.Log($"[INDEX] Built OfficeIndex: {officeIndex.Count} unique office names");
        foreach (var kv in officeIndex.Where(k => k.Value.Count > 1))
            Debug.LogWarning($"[INDEX] Duplicate '{kv.Key}' → {string.Join(", ", kv.Value.Select(v => v.name))}");
    }
    static string CanonicalOfficeKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim().ToLowerInvariant();
        s = s.Replace("waypoint_", "");
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    NavigationWaypoint ChooseBestOffice(List<NavigationWaypoint> list)
    {
        if (list == null || list.Count == 0) return null;
        if (list.Count == 1) return list[0];

        var cam = Camera.main ? Camera.main.transform : null;
        if (!cam) return list[0];

        float camY = cam.position.y;
        return list
            .OrderBy(wp => Mathf.Abs(wp.transform.position.y - camY))
            .ThenBy(wp => (wp.transform.position - cam.position).sqrMagnitude)
            .First();
    }

    float GetUserHeadingDeg()
    {
        var hs = HeadingService.Instance;
        if (useCompassHeading && hs != null && hs.IsRunning)
            return hs.GetHeading();

        Transform c = userCamera != null ? userCamera : (Camera.main ? Camera.main.transform : null);
        if (!c) return 0f;
        float y = c.eulerAngles.y; y %= 360f; if (y < 0) y += 360f;
        return y;
    }

    static string FormatDistance(float meters)
    {
        if (meters < 1f) return $"{Mathf.RoundToInt(meters * 100f)} cm";
        if (meters < 10f) return $"{meters:0.0} m";
        return $"{Mathf.RoundToInt(meters)} m";
    }

    public List<string> GetAllOfficeNamesFromIndex()
    {
        if (officeIndex == null || officeIndex.Count == 0) BuildOfficeIndexFromScene();

        return officeIndex.Values
            .Select(list => list[0])
            .Select(wp => string.IsNullOrWhiteSpace(wp.officeName) ? wp.waypointName : wp.officeName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    public void BuildOfficeIndex() => BuildOfficeIndexFromScene();
    static string Canonical(string s) => CanonicalOfficeKey(s);
    NavigationWaypoint ChooseBest(List<NavigationWaypoint> list) => ChooseBestOffice(list);

    public void CreateGroundArrowsForPath()
    {
        if (hudOnly) return;

        EnsureActiveChain();
        EnsureArrowPool();              // <— make sure pool exists/parent is set
        ClearPathArrowsIfNeeded();      // <— returns all previously spawned arrows to the pool
        ClearFlowLine();

        if (currentPath == null || currentPath.Count < 2)
        {
            Debug.LogWarning("[ARROWS] Path too short or null");
            return;
        }

        if (arrowPrefab == null)
        {
            Debug.LogError("[ARROWS] arrowPrefab is NULL. Assign a prefab in the Inspector.");
            return;
        }

        // Spawn per segment
        switch (pathStyle)
        {
            case PathVisualStyle.Arrows:
                for (int i = 0; i < currentPath.Count - 1; i++)
                    CreateGroundArrowsForSegment(currentPath[i], currentPath[i + 1]);
                break;

            case PathVisualStyle.FlowLine:
                BuildFlowLineFromCurrentPath();
                break;
        }

        // Force arrows visible
        if (pathArrows != null)
        {
            foreach (var a in pathArrows)
                if (a) a.SetActive(true);
        }
    }

    void Start()
    {
        playerCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        ResolveAnchors();

        if (hideFloorsOnlyDuringNav) SetFloorsVisible(true);

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

    void EnsureArrowPool()
    {
        if (arrowPool == null) arrowPool = new Queue<GameObject>(Mathf.Max(arrowPoolPrewarm, 0));

        if (!arrowsParent)
        {
            var pathRootGO = GameObject.Find("PathRoot");
            arrowsParent = pathRootGO ? pathRootGO.transform : this.transform;
        }

        if (arrowPool.Count == 0 && arrowPrefab)
        {
            for (int i = 0; i < arrowPoolPrewarm; i++)
            {
                var go = Instantiate(arrowPrefab, arrowsParent);
                go.SetActive(false);
                arrowPool.Enqueue(go);
            }
        }
    }


    GameObject RentArrow()
    {
        EnsureArrowPool();

        if (!arrowPrefab)
        {
            Debug.LogWarning("[ARROWS] arrowPrefab is not assigned.");
            return null;
        }

        GameObject go = null;

        // Dequeue until we find a non-destroyed object
        while (arrowPool != null && arrowPool.Count > 0 && go == null)
            go = arrowPool.Dequeue();

        if (!go) go = Instantiate(arrowPrefab, arrowsParent);

        go.SetActive(true);
        activeArrows.Add(go);
        return go;
    }

    void ReturnArrowToPool(GameObject go)
    {
        if (!go) return;
        go.SetActive(false);
        activeArrows.Remove(go);
        if (arrowPool == null) arrowPool = new Queue<GameObject>();
        arrowPool.Enqueue(go);
    }

    void ReturnAllArrowsToPool()
    {
        for (int i = activeArrows.Count - 1; i >= 0; i--)
            ReturnArrowToPool(activeArrows[i]);
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

    private void EnsureTargetMarker()
    {
        if (!targetMarker) CreateEnhancedTargetMarker();
    }

    private void PlaceTargetMarkerAt(Vector3 worldPos, bool preserveYIfElevated = true)
    {
        EnsureTargetMarker();

// Determine if this point is “elevated” (second floor) vs AR floor
float floorYAtXZ = GetGroundPosition(new Vector3(worldPos.x, 0f, worldPos.z)).y;
        bool elevated = Mathf.Abs(worldPos.y - floorYAtXZ) > 1.0f;

        Vector3 pos = (preserveYIfElevated && elevated) ? worldPos : GetGroundPosition(worldPos);
        targetMarker.transform.position = pos;
        targetMarkerBaseY = pos.y;
        targetMarker.SetActive(true);
    }

    private void HideTargetMarker()
    {
        if (targetMarker) targetMarker.SetActive(false);
        targetMarkerBaseY = float.NaN;
    }

    private void UpdateTargetMarkerAnimation()
    {
        if (!targetMarker || !targetMarker.activeSelf) return;
        if (float.IsNaN(targetMarkerBaseY)) targetMarkerBaseY = targetMarker.transform.position.y;


        // Bobbing
        var p = targetMarker.transform.position;
        p.y = targetMarkerBaseY + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        targetMarker.transform.position = p;

        // Spin
        targetMarker.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    private void ResetNavSessionBeforeNewRoute(NavigationWaypoint goal)
    {
        hasReachedDestination = false;
        hasShownArrivalNotification = false;
        timeWithinArrivalZone = 0f;

        currentDestinationWaypoint = goal;
        currentDestination = !string.IsNullOrEmpty(goal.officeName)
                            ? goal.officeName
                            : (!string.IsNullOrEmpty(goal.waypointName) ? goal.waypointName : goal.name);

        isNavigating = true;
    }

    // Toggle visibility null-safely
    void SetArrowsActive(bool on)
    {
        if (pathArrows == null) return;
        for (int i = pathArrows.Count - 1; i >= 0; i--)
        {
            var a = pathArrows[i];
            if (!a) { pathArrows.RemoveAt(i); continue; }
            a.SetActive(on);
        }
    }


    public Vector3 GetCurrentPosition()
    {
        if (useOfficeAsStart &&
        !string.IsNullOrEmpty(currentUserOffice) &&
        (!isNavigating || currentPath == null || currentPath.Count == 0))
        {
            var officeWP = FindTargetWaypointAdvanced(currentUserOffice);
            if (officeWP != null)
            {
                Debug.Log("[GetCurrentPosition] Using manual office: {currentUserOffice} at {officeWP.transform.position}"); return officeWP.transform.position;
            }
            else
            {
                Debug.LogWarning("[GetCurrentPosition] Office waypoint not found: {currentUserOffice}, falling back to camera");
            }
        }

        // Live AR camera during nav (or when no override)
        if (arCamera) return arCamera.transform.position;
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
        FindAllWaypoints();

        if (!raycastMgr) raycastMgr = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARRaycastManager>();

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
            stopButton.onClick.AddListener(() => StopNavigation(false)); // pass explicit default

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


    // Returns true if the user (camera) is within entranceDetectRadiusMeters of the entrance (XZ only).
    public bool IsAtEntrance(out float meters)
    {
        meters = 0f;
        if (entranceNode == null) return false;

        Vector3 cam = GetCurrentPosition();
        Vector3 e = entranceNode.position;
        Vector3 cf = new Vector3(cam.x, 0f, cam.z);
        Vector3 ef = new Vector3(e.x, 0f, e.z);

        float unityDist = Vector3.Distance(cf, ef);
        meters = unityDist * Mathf.Max(0.0001f, metersPerUnit); // metersPerUnit already in your class
        return meters <= entranceDetectRadiusMeters;
    }

    // Allows PlaceOnFloorARF or UI to set the entrance node after placement
    public void SetEntranceNode(Transform t)
    {
        entranceNode = t;
        Debug.Log($"[ENTRANCE] SetEntranceNode → {(t ? t.name : "NULL")}");
    }

    public void SetStartFromEntrance(bool on)
    {
        startFromEntrance = on;
        Debug.Log($"[ENTRANCE] startFromEntrance={on}");
    }



    Vector3 GetRoutingStartPosition()
    {
        // 1) Manual start (selected current office) takes precedence
        if (useOfficeAsStart && !string.IsNullOrEmpty(currentUserOffice))
        {
            var wpT = FindTargetWaypointAdvanced(currentUserOffice);
            if (wpT != null)
                return wpT.position;
            // if not found, fall through to entrance/camera
            Debug.LogWarning($"[GetRoutingStartPosition] Office waypoint not found for '{currentUserOffice}', using entrance/camera fallback.");
        }

        // 2) Entrance fallback (if you want “always start at entrance” for certain modes)
        if (startFromEntrance && entranceNode != null)
            return entranceNode.position;

        // 3) Live camera (auto-detect) fallback
        return GetCurrentPosition();
    }

    public void StartNavigationConfirmedByUI(string officeName)
    {
        if (isNavigating) { Debug.Log("Already navigating. Press Cancel first."); return; }
        StartNavigationProceed(officeName);
    }

    public bool IsNavigating => isNavigating;

    public void StartNavigationProceed(string officeName)
    {
        // Stop any previous gate coroutine (if running)
        try
        {
            if (facingGateRoutine != null) { StopCoroutine(facingGateRoutine); facingGateRoutine = null; }
        }
        catch { }

        // If something is still navigating, cleanly cancel so this start is fresh
        if (isNavigating) CancelNavigate();

        // 0) Clear turn/hint state and hide panel while building
        if (_turns != null) _turns.Clear();
        _nextTurn = 0;
        _turnAnnounced = false;
        ShowTurnUI(false);

        enabled = true; // ensure Update() will tick hints/progressive arrows

        // 1) Optional alignment for Vuforia
        if (!hudOnly && useVuforiaPositioning)
            AlignAnchorYawToCamera();

        hasEverLocalized = true;

        // 2) Resolve target waypoint or object
        Debug.Log($"[NAV] StartNavigationProceed → '{officeName}'");

        Transform targetWaypoint = FindTargetWaypointAdvanced(officeName);
        if (targetWaypoint == null)
        {
            if (officeLookup != null && officeLookup.TryGetValue(officeName, out GameObject targetObject) && targetObject)
            {
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
        }

        if (targetWaypoint == null)
        {
            Debug.LogError($"[NAV] Waypoint not found for: {officeName}");
            return;
        }

        // 3) Init session state
        targetDestination = targetWaypoint.position;
        originalTargetPosition = targetDestination;
        currentDestination = officeName;

        // Make sure arrows parent cannot be hidden by floors/Canvas
        EnsureArrowsParentVisible();

        // Hide floors only during an active nav session
        floorsWereVisibleBeforeNav = AreFloorsVisible();
        navSessionActive = true;
        if (hideFloorsOnlyDuringNav) SetFloorsVisible(false);

        // 4) Compute the route (BFS builder with safe fallbacks)
        Debug.Log("[NAV] Calculating path...");
        CalculateWallAvoidingPath(); // fills 'currentPath' (uses entrance or camera per GetRoutingStartPosition)

        // WORLD MODE visuals
        if (!hudOnly)
        {
            EnsureActiveChain();
            ClearFlowLine();
            ClearPathArrows();

            // Ensure we have some path points; fallback if needed
            if (currentPath == null || currentPath.Count < 2)
            {
                var a = GetGroundPosition(GetRoutingStartPosition());
                var b = GetGroundPosition(targetDestination);
                currentPath = new List<Vector3> { a, b };
                Debug.LogWarning("[NAV] Path builder returned empty. Using 2‑point fallback.");
            }

            // 5) Snap path Y to AR floor and optionally recenter to corridor
            SnapPathYsRespectingStairs(currentPath); 

            // Optional: center line to corridor midline (requires proper wall colliders + layer)
            try { RecenterPathToCorridor(currentPath); } catch { } // safe if you removed this helper

            // 6) Draw arrows (progressive window or full)
            pathStyle = PathVisualStyle.Arrows;
            ClearPathArrows();
            EnsureArrowsParentVisible(); // keep arrows in world space and active

            // Start near the user's projection onto the path (or entrance if GetRoutingStartPosition() returns that)
            Vector3 proj;
            int startSeg = FindClosestSegmentAndProjection(GetRoutingStartPosition(), out proj);
            if (startSeg < 0) startSeg = 0;

            if (progressiveReveal)
            {
                CreateProgressiveArrowsFromProjection(startSeg, proj, revealAheadMeters);
                _lastProjSeg = startSeg;
                _lastProjPos = proj;
            }
            else
            {
                CreateGroundArrowsForPathFromSegment(startSeg, proj); // legacy full draw
            }

            SetArrowsActive(true);
            int spawned = pathArrows?.Count ?? 0;

            // Fallbacks if nothing spawned
            if (spawned == 0)
            {
                Debug.LogWarning("[ARROWS] Spawned 0 – trying full path fallback.");
                CreateGroundArrowsForPathFromSegment(0, null);
                SetArrowsActive(true);
                spawned = pathArrows?.Count ?? 0;
            }
            if (spawned == 0 && currentPath != null && currentPath.Count >= 2)
            {
                Debug.LogWarning("[ARROWS] Full path fallback spawned 0 – trying minimal 2‑point arrow.");
                ClearPathArrows();
                var go = RentArrow();
                if (go)
                {
                    var a = GetGroundPosition(currentPath[0]);
                    var b = GetGroundPosition(currentPath[1]);
                    var dir = (b - a); dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
                    go.transform.SetPositionAndRotation(a, Quaternion.LookRotation(dir, Vector3.up));
                    if (pathArrows == null) pathArrows = new List<GameObject>();
                    pathArrows.Add(go);
                    SetArrowsActive(true);
                    spawned = 1;
                }
            }
            Debug.Log($"[ARROWS] spawned={spawned}, parentActive={arrowsParent?.gameObject.activeSelf}");

            // 7) Target marker (keep after arrows)
            ShowEnhancedTargetMarker();
        }

        // If hudOnly, still ensure we have a simple fallback path
        if (currentPath == null || currentPath.Count < 2)
        {
            var a = GetGroundPosition(GetRoutingStartPosition());
            var b = GetGroundPosition(targetDestination);
            currentPath = new List<Vector3> { a, b };
        }

        // 8) Turn hints from finalized path
        BuildTurnEventsFromPath(currentPath);
        _nextTurn = 0;
        _turnAnnounced = false;
        ShowTurnUI(true);

        // 9) Switch to live camera updates so distance/turn hints update while walking
        useOfficeAsStart = false;
        currentUserOffice = null;

        // 10) (Optional) proxy floor setup
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

        // 11) Final UI/buttons
        currentPathIndex = 0;
        lastPlayerPosition = GetCurrentPosition();

        if (statusText) statusText.text = $"Navigating to {currentDestination}";
        if (navigateButton) navigateButton.gameObject.SetActive(false);
        if (stopButton) stopButton.gameObject.SetActive(true);

        isNavigating = true; // Update() will now tick UpdateTurnHints/CheckArrival/ProgressiveArrowsTick
        string mode = hudOnly ? "HUD" : (useVuforiaPositioning ? "Vuforia" : "Camera");
        Debug.Log($"[NAV] Navigation started to '{currentDestination}' ({mode} mode)");
    }

    // Snap only flat segments to floor; preserve Y for any node adjacent to a stairs segment.
    void SnapPathYsRespectingStairs(List<Vector3> path)
    {
        if (path == null || path.Count < 2) return;
    
        // Mark nodes that must preserve Y if adjacent to a stairs segment
        bool[] preserveY = new bool[path.Count];

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = path[i];
            Vector3 b = path[i + 1];
            if (IsStairsSegment(a, b))
            {
                preserveY[i] = true;
                preserveY[i + 1] = true;
            }
        }

        for (int i = 0; i < path.Count; i++)
        {
            if (preserveY[i]) continue; // keep original Y on stair-adjacent nodes
            path[i] = GetGroundPosition(path[i]); // AR-floor snap for flat-only nodes
        }
    }


    void StartFacingGateFromSegment(int segIdx)
    {
        if (!requireFacingGate || currentPath == null || currentPath.Count < 2)
        {
            SetArrowsActive(true);
            return;
        }

        var hs = HeadingService.Instance;
        if (hs == null || !hs.IsRunning)
        {
            SetArrowsActive(true);
            return;
        }

        segIdx = Mathf.Clamp(segIdx, 0, currentPath.Count - 2);
        float bearing = BearingUtil.WorldBearingDeg(currentPath[segIdx], currentPath[segIdx + 1]);

        SetArrowsActive(false);
        if (facingGateRoutine != null) { StopCoroutine(facingGateRoutine); facingGateRoutine = null; }
        facingGateRoutine = StartCoroutine(WaitForFacingAndRevealArrows(bearing));
    }

    IEnumerator WaitForFacingAndRevealArrows(float targetBearing)
    {
        float t = 0f;
        while (t < gateRevealTimeout)
        {
            float heading = GetUserHeadingDeg();
            float err = Mathf.Abs(Mathf.DeltaAngle(heading, targetBearing));
            if (err <= startFacingTolerance) break;
            t += Time.deltaTime;
            yield return null;
        }
        SetArrowsActive(true);
        facingGateRoutine = null;
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

    void RealignCompassToFirstSegment()
    {
        var hs = HeadingService.Instance;
        if (hs == null || !hs.IsRunning || !hs.useCompassIfAvailable) return;
        if (currentPath == null || currentPath.Count < 2) return;

        // Bearing of first segment in world space
        float segBearing = BearingUtil.WorldBearingDeg(currentPath[0], currentPath[1]);

        // Raw device heading = displayed - offset
        float displayed = hs.GetHeading();
        float offset = hs.GetNorthOffset();
        float raw = (displayed - offset + 360f) % 360f;

        float delta = Mathf.DeltaAngle(raw, segBearing);
        hs.SetNorthOffset(offset - delta);

        Debug.Log($"[Compass] Realign to first segment: seg={segBearing:0.0} raw={raw:0.0} newOffset={hs.GetNorthOffset():0.0}");
    }


    private NavigationWaypoint[] GetAllRuntimeWaypoints()
    {
        return Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(wp => wp && wp.gameObject.scene.IsValid())
        .ToArray();
    }

    private void MarkStairsSegmentsFromNodes()
    {
        stairsSegments.Clear();
        if (currentPathNodes == null) return;

for (int i = 0; i < currentPathNodes.Count - 1; i++)
        {
            var a = currentPathNodes[i];
            var b = currentPathNodes[i + 1];
            if (a == null || b == null) continue;

            if (a.waypointType == WaypointType.Stairs ||
                b.waypointType == WaypointType.Stairs ||
                Mathf.Abs(a.transform.position.y - b.transform.position.y) > 1.0f)
            {
                stairsSegments.Add(i);
            }
        }
    }

    private bool IsStairsSegment(int segIndex) => stairsSegments.Contains(segIndex);

    private void ApplyGraphPath(List<NavigationWaypoint> nodes)
    {
        if (nodes == null || nodes.Count < 2)
        {
            Debug.LogWarning("ApplyGraphPath: invalid nodes");
            currentPathNodes = new List<NavigationWaypoint>();
            currentPath = new List<Vector3>();
            stairsSegments.Clear();
            return;
        }

        currentPathNodes = nodes;
        currentPath = nodes.Select(n => n.transform.position).ToList();
        MarkStairsSegmentsFromNodes();

        Debug.Log($"[Route] {nodes.Count} nodes\n{DescribePath(nodes)}");

        // If you have a LineRenderer, refresh here (optional):
        // RefreshRouteLineRenderer(); 
    }

    public bool BuildWaypointPathToOffice(string officeName)
    {
        if (string.IsNullOrEmpty(officeName))
        {
            Debug.LogWarning("BuildWaypointPathToOffice: officeName is null/empty");
            return false;
        }

        // Resolve goal waypoint (Office type)
        NavigationWaypoint goal = null;
        if (officeLookup != null && officeLookup.TryGetValue(officeName, out var go) && go)
            goal = go.GetComponent<NavigationWaypoint>();
        if (!goal)
        {
            var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
                .Where(wp => wp && wp.gameObject.scene.IsValid()).ToArray();
            goal = all.FirstOrDefault(wp => wp.officeName == officeName);
        }
        if (!goal)
        {
            Debug.LogError($"BuildWaypointPathToOffice: no waypoint found for '{officeName}'");
            return false;
        }

        // Choose start near player
        var waypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
            .Where(wp => wp && wp.gameObject.scene.IsValid()).ToArray();
        var playerPos = (playerCamera ? playerCamera.transform.position : Vector3.zero);
        var start = FindNearestWaypoint(waypoints, playerPos); // your helper

        if (!start)
        {
            Debug.LogError("BuildWaypointPathToOffice: no start waypoint near player");
            return false;
        }

        // A* (use your enhanced version or the basic one)
        var nodes = RunStrictWaypointAStar(start, goal);
        if (nodes == null || nodes.Count == 0)
        {
            Debug.LogError($"BuildWaypointPathToOffice: A* failed from {start.name} to {goal.name}");
            return false;
        }

        // Force use of waypoints — no direct-line fallback
        ApplyGraphPath(nodes);
        return true;
    }

    public static string DescribePath(IList<NavigationWaypoint> nodes)
    {
        if (nodes == null) return "(null)";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            sb.AppendLine($"{i:D2} | {n.name} | {n.waypointType} | y={n.transform.position.y:F2}");
        }
        return sb.ToString();
    }

    public string DetectNearestOfficeDisplayName(float maxRadius = 6f)
    {
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();
        var cam = Camera.main ? Camera.main.transform.position : Vector3.zero;

        float best = float.MaxValue;
        NavigationWaypoint bestWp = null;

        foreach (var wp in all)
        {
            if (wp == null || !wp.gameObject.scene.IsValid()) continue;
            if (wp.waypointType != WaypointType.Office) continue;

            float d = Vector3.Distance(new Vector3(cam.x, 0, cam.z), new Vector3(wp.transform.position.x, 0, wp.transform.position.z));
            if (d < best && d <= maxRadius)
            {
                best = d;
                bestWp = wp;
            }
        }
        return bestWp ? (string.IsNullOrWhiteSpace(bestWp.officeName) ? bestWp.waypointName : bestWp.officeName) : null;
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



    public void CalculateWallAvoidingPath()
    {
        if (currentPath == null) currentPath = new List<Vector3>();
        else currentPath.Clear();

        // Start/goal positions (snap to floor)
        Vector3 fromStart = GetGroundPosition(GetRoutingStartPosition()); // entrance or camera
        Vector3 toGoal = GetGroundPosition(targetDestination);

        // Get all scene waypoints (skip prefabs)
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
                 .Where(wp => wp != null && wp.gameObject.scene.IsValid())
                 .ToList();

        if (all == null || all.Count == 0)
        {
            Debug.LogWarning("[PATH] No waypoints in scene. Using direct fallback.");
            currentPath.Add(fromStart);
            currentPath.Add(toGoal);
            return;
        }

        // Find nearest graph nodes
        var start = FindNearestWaypoint(all, fromStart);
        var goal = FindNearestWaypoint(all, toGoal);

        if (start == null || goal == null)
        {
            Debug.LogWarning("[PATH] Start or goal waypoint is null. Using direct fallback.");
            currentPath.Add(fromStart);
            currentPath.Add(toGoal);
            return;
        }

        // 1) Try BFS on graph
        var nodes = BFSWaypoints(start, goal);
        if (nodes != null && nodes.Count > 0)
        {
            BuildPathFromNodes(nodes, fromStart, toGoal);
            return;
        }

        // 2) If BFS failed and we’re likely on different floors, try stair fallback
        if (Mathf.Abs(fromStart.y - toGoal.y) > floorMatchTolerance)
        {
            List<Vector3> mfPath;
            if (TryBuildMultiFloorPathFallback(all, fromStart, toGoal, out mfPath))
            {
                currentPath = mfPath;
                Debug.Log("[PATH] Multi-floor fallback succeeded (via stairs).");
                return;
            }
        }

        // 3) Final fallback (direct line)
        Debug.LogWarning("[PATH] BFS failed and multi-floor fallback failed. Using direct fallback.");
        currentPath.Add(fromStart);
        currentPath.Add(toGoal);
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

    private NavigationWaypoint GetWaypointFromUnknown(UnityEngine.Object o)
    {
        if (!o) return null;
        if (o is NavigationWaypoint wp) return wp;
        if (o is GameObject go) return go.GetComponent<NavigationWaypoint>();
        if (o is Component comp) return comp.GetComponent<NavigationWaypoint>();
        return null;
    }

    List<NavigationWaypoint> BFSWaypoints(NavigationWaypoint start, NavigationWaypoint goal)
    {
        var cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var visited = new HashSet<NavigationWaypoint>();
        var q = new Queue<NavigationWaypoint>();

        q.Enqueue(start);
        visited.Add(start);

        bool found = false;
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == null) continue;

            if (cur == goal) { found = true; break; }

            var neighbors = cur.connectedWaypoints;
            if (neighbors == null) continue;

            for (int i = 0; i < neighbors.Count; i++)
            {
                var nb = neighbors[i];
                if (nb == null || visited.Contains(nb)) continue;
                visited.Add(nb);
                cameFrom[nb] = cur;
                q.Enqueue(nb);
            }
        }

        if (!found) return null;

        var nodes = new List<NavigationWaypoint>();
        var node = goal;
        nodes.Add(node);
        while (cameFrom.TryGetValue(node, out var prev))
        {
            node = prev;
            nodes.Add(node);
        }
        nodes.Reverse();
        return nodes;
    }

    void BuildPathFromNodes(List<NavigationWaypoint> nodes, Vector3 fromStart, Vector3 toGoalWorld)
    {
        if (nodes == null || nodes.Count == 0)
        {
            currentPathNodes = new List<NavigationWaypoint>();
            currentPath = new List<Vector3>();
            stairsSegments.Clear();
            HideTargetMarker();
            isNavigating = false;
            return;
        }

        currentPathNodes = nodes;
        currentPath = new List<Vector3>();

        // Start anchor on ground (entrance/camera)
        if (Vector3.Distance(fromStart, nodes[0].transform.position) > 0.5f)
            currentPath.Add(GetGroundPosition(fromStart));

        // Waypoints as-is (keep Y for stairs/second floor)
        foreach (var wp in nodes)
            if (wp) currentPath.Add(wp.transform.position);

        // End anchor: only add if goal differs from last node
        var lastNodePos = nodes[nodes.Count - 1].transform.position;
        if (Vector3.Distance(lastNodePos, toGoalWorld) > 0.5f)
        {
            float floorYAtGoal = GetGroundPosition(new Vector3(toGoalWorld.x, 0f, toGoalWorld.z)).y;
            bool goalElevated = Mathf.Abs(toGoalWorld.y - floorYAtGoal) > 1.0f;

            currentPath.Add(goalElevated ? toGoalWorld : GetGroundPosition(toGoalWorld));
        }

        if (!forceWaypointRouting) SimplifyPathInPlace(currentPath, 0.05f);

        MarkStairsSegmentsFromNodes();

        // Place marker at final path point, preserve Y if elevated
        var finalPoint = currentPath[currentPath.Count - 1];
        PlaceTargetMarkerAt(finalPoint, preserveYIfElevated: true);
        Debug.Log($"[Marker] Final place at {finalPoint} (y={finalPoint.y:F2})");

        isNavigating = true;
    }

    bool TryBuildMultiFloorPathFallback(List<NavigationWaypoint> all, Vector3 fromStart, Vector3 toGoal, out List<Vector3> outPath)
    {
        outPath = null;

        var stairs = all.Where(wp => wp.waypointType == WaypointType.Stairs).ToList();
        if (stairs.Count == 0) return false;

        float mpu = Mathf.Max(0.0001f, metersPerUnit);
        float searchUnits = stairsSearchRadiusMeters / mpu;

        // Find stairs near start floor (prefer |Δy| small)
        var startStairsCandidates = stairs
            .OrderBy(wp => Mathf.Abs(wp.transform.position.y - fromStart.y))
            .ThenBy(wp => Vector3.Distance(new Vector3(wp.transform.position.x, 0, wp.transform.position.z),
                                           new Vector3(fromStart.x, 0, fromStart.z)))
            .Take(6) // limit work
            .ToList();

        // Find stairs near goal floor
        var goalStairsCandidates = stairs
            .OrderBy(wp => Mathf.Abs(wp.transform.position.y - toGoal.y))
            .ThenBy(wp => Vector3.Distance(new Vector3(wp.transform.position.x, 0, wp.transform.position.z),
                                           new Vector3(toGoal.x, 0, toGoal.z)))
            .Take(6)
            .ToList();

        // Try pair by stairId/name prefix first; else nearest across floors
        foreach (var s in startStairsCandidates)
        {
            var pair = FindPairedStairs(s, goalStairsCandidates);
            if (pair == null) continue;

            // BFS floor 1: start → s
            var s1 = FindNearestWaypoint(all, fromStart);
            var sGoal = s;
            var path1Nodes = BFSWaypoints(s1, sGoal);
            if (path1Nodes == null) continue;

            // BFS floor 2: pair → goal
            var g2 = FindNearestWaypoint(all, toGoal);
            var path2Nodes = BFSWaypoints(pair, g2);
            if (path2Nodes == null) continue;

            // Build combined world path: start -> ... s -> pair -> ... -> goal
            var path = new List<Vector3>();
            path.Add(GetGroundPosition(fromStart));
            foreach (var n in path1Nodes) path.Add(GetGroundPosition(n.transform.position));
            path.Add(GetGroundPosition(pair.transform.position));
            foreach (var n in path2Nodes) path.Add(GetGroundPosition(n.transform.position));
            path.Add(GetGroundPosition(toGoal));

            // Clean up: remove duplicates and very short steps
            SimplifyPathInPlace(path, 0.05f);
            outPath = path;
            return true;
        }

        return false;
    }

    NavigationWaypoint FindPairedStairs(NavigationWaypoint s1, List<NavigationWaypoint> goalStairs)
    {
        if (s1 == null || goalStairs == null || goalStairs.Count == 0) return null;

        // Try stairId field/property if you have it on NavigationWaypoint
        string id1 = ReadStairId(s1);
        if (!string.IsNullOrEmpty(id1))
        {
            var byId = goalStairs.FirstOrDefault(x => string.Equals(ReadStairId(x), id1, System.StringComparison.OrdinalIgnoreCase));
            if (byId) return byId;
        }

        // Try name prefix (strip floor tokens like -1st, -2nd, 1F, 2F, etc.)
        string prefix = NormalizeStairName(s1.name);
        var byPrefix = goalStairs.FirstOrDefault(x => string.Equals(NormalizeStairName(x.name), prefix, System.StringComparison.OrdinalIgnoreCase));
        if (byPrefix) return byPrefix;

        // Fallback: nearest by horizontal distance on other floor
        var baseY = s1.transform.position.y;
        var target = goalStairs
            .OrderBy(x => Mathf.Abs(x.transform.position.y - baseY)) // prefer different floor but close
            .ThenBy(x => Vector2.Distance(new Vector2(x.transform.position.x, x.transform.position.z),
                                          new Vector2(s1.transform.position.x, s1.transform.position.z)))
            .FirstOrDefault();

        return target;
    }

    // Reads a 'stairId' field/property if present; else empty
    string ReadStairId(NavigationWaypoint wp)
    {
        if (wp == null) return null;
        // If you added a public string stairId to NavigationWaypoint, this will work directly:
        // return wp.stairId;

        // Reflection – safe if not present
        var t = wp.GetType();
        var fi = t.GetField("stairId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (fi != null) return fi.GetValue(wp) as string;
        var pi = t.GetProperty("stairId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (pi != null) return pi.GetValue(wp, null) as string;
        return null;
    }

    string NormalizeStairName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        string n = name.ToLowerInvariant();

        // Quick filter: require token (stairs/stair) to be considered
        if (!stairNameTokens.Any(tok => n.Contains(tok))) return n;

        // strip common floor tokens
        n = n.Replace("-1st", "").Replace("-2nd", "").Replace("-3rd", "");
        n = n.Replace("1st", "").Replace("2nd", "").Replace("3rd", "");
        n = n.Replace("-1f", "").Replace("-2f", "").Replace("-3f", "");
        n = n.Replace("1f", "").Replace("2f", "").Replace("3f", "");
        n = n.Replace("floor", "");
        // collapse underscores/dashes
        n = n.Replace("__", "_").Replace("--", "-");
        // trim trailing separators
        return n.Trim('-', '_', ' ');
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
        bool fromSecondFloor = IsSecondFloor(from.transform.position);
        bool toSecondFloor = IsSecondFloor(to.transform.position);
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

    private void EnsureNavVisuals()
    {
        if (!navRoot)
        {
            var rootGO = GameObject.Find("NavigationVisuals");
            if (!rootGO)
            {
                rootGO = new GameObject("NavigationVisuals");
                rootGO.layer = LayerMask.NameToLayer("Default");
            }
            navRoot = rootGO.transform;
            if (contentAnchor) navRoot.SetParent(contentAnchor, false);
        }


// Route line disabled: remove any existing line object
if (routeLR)
        {
            if (Application.isPlaying) Destroy(routeLR.gameObject); else DestroyImmediate(routeLR.gameObject);
            routeLR = null;
        }
        var leftover = GameObject.Find("RouteLine");
        if (leftover)
        {
            if (Application.isPlaying) Destroy(leftover); else DestroyImmediate(leftover);
        }

        // Ensure the arrow exists (only one)
        if (!arrowGO)
        {
            if (arrowPrefab)
            {
                arrowGO = Instantiate(arrowPrefab, navRoot);
                arrowGO.layer = LayerMask.NameToLayer("Default");
                arrowGO.SetActive(false);
            }
            else
            {
                Debug.LogWarning("SmartNavigation: arrowPrefab not assigned; arrow will not render.");
            }
        }

        // Optional: target marker (keep if you want the goal pin)
        if (!targetMarkerGO && targetMarkerPrefab)
        {
            targetMarkerGO = Instantiate(targetMarkerPrefab, navRoot);
            targetMarkerGO.layer = LayerMask.NameToLayer("Default");
            targetMarkerGO.SetActive(false);
        }
    }

    private void ShowNavigationVisuals(bool show)
    {
        EnsureNavVisuals();
    
if (arrowGO) arrowGO.SetActive(show);

        if (targetMarkerGO)
        {
            targetMarkerGO.SetActive(show);
            if (show && currentPath != null && currentPath.Count > 0)
                targetMarkerGO.transform.position = currentPath[currentPath.Count - 1] + Vector3.up * 0.02f;
        }
    }
    private void RefreshRouteLineRenderer()
    {
       
    }

    private void UpdateArrowAndHints()
    {
        if (!playerCamera || currentPath == null || currentPath.Count < 2) return;

        Vector3 proj;
        int seg = FindClosestSegmentAndProjection(playerCamera.transform.position, out proj);
        if (seg >= 0)
        {
            if (arrowGO)
            {
                arrowGO.transform.position = proj;
                Vector3 a = currentPath[seg];
                Vector3 b = currentPath[seg + 1];
                Vector3 dir = b - a; dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f)
                    arrowGO.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            UpdateTurnHints();
        }
    }

    private float DistanceToGoalFrom(int segIndex, Vector3 fromPoint)
    {
        if (currentPath == null || currentPath.Count < 2) return 0f;
        float sum = 0f;

 
        // remaining on current segment
        Vector3 a = currentPath[segIndex];
        Vector3 b = currentPath[segIndex + 1];
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(fromPoint - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        Vector3 q = a + ab * t;
        sum += Vector3.Distance(q, b);

        for (int i = segIndex + 1; i < currentPath.Count - 1; i++)
            sum += Vector3.Distance(currentPath[i], currentPath[i + 1]);
        return sum;
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
    
    public void CreateGroundArrowsForPathFromSegment(int startSegment, Vector3? firstPosOverride = null)
    {
        EnsureArrowPool();
        if (currentPath == null || currentPath.Count < 2) return;
        if (pathArrows == null) pathArrows = new System.Collections.Generic.List<GameObject>();

        int segIdx = Mathf.Clamp(startSegment, 0, currentPath.Count - 2);

        // First segment can start from the projection point
        Vector3 segFrom = firstPosOverride.HasValue ? firstPosOverride.Value : currentPath[segIdx];
        Vector3 segTo = currentPath[segIdx + 1];
        CreateGroundArrowsForSegment(segFrom, segTo);

        // Remaining segments
        for (int j = segIdx + 1; j < currentPath.Count - 1; j++)
            CreateGroundArrowsForSegment(currentPath[j], currentPath[j + 1]);
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


    void EnsureArrowsParentVisible()
    {
        // Create PathRoot if missing
        if (!arrowsParent)
        {
            var pathRootGO = GameObject.Find("PathRoot");
            if (!pathRootGO) pathRootGO = new GameObject("PathRoot");
            Transform preferred = worldRootForArrows ? worldRootForArrows : transform;
            pathRootGO.transform.SetParent(preferred, false);
            arrowsParent = pathRootGO.transform;
        }

        // If arrowsParent ended under a Canvas or floors root, reparent it to world
        if (IsUnderCanvas(arrowsParent))
        {
            var preferred = worldRootForArrows ? worldRootForArrows : transform;
            arrowsParent.SetParent(preferred, true);
            Debug.Log("[ARROWS] Reparented PathRoot from Canvas to world.");
        }
        if (floorsRoot && IsDescendantOf(arrowsParent, floorsRoot))
        {
            var preferred = worldRootForArrows ? worldRootForArrows
                           : (floorsRoot.parent ? floorsRoot.parent : transform);
            arrowsParent.SetParent(preferred, true);
            Debug.Log("[ARROWS] Reparented PathRoot away from floorsRoot to avoid being hidden.");
        }

        if (!arrowsParent.gameObject.activeSelf)
            arrowsParent.gameObject.SetActive(true);
    }


    bool IsUnderCanvas(Transform t)
    {
        for (var cur = t; cur; cur = cur.parent)
            if (cur.GetComponent<Canvas>() != null) return true;
        return false;
    }
    bool IsDescendantOf(Transform t, Transform ancestor)
    {
        for (var cur = t ? t.parent : null; cur; cur = cur.parent)
            if (cur == ancestor) return true;
        return false;
    }


    void SimplifyPathInPlace(List<Vector3> path, float minStep)
    {
        if (path == null || path.Count < 2) return;
        for (int i = path.Count - 2; i >= 1; i--)
        {
            float d = Vector3.Distance(path[i], path[i - 1]);
            if (d < minStep) path.RemoveAt(i);
        }
    }

    // ---------- Nearest waypoint helpers (support both parameter orders) ----------

    // Core implementation (ground-projected XZ, null-safe, skip prefab assets)
    private NavigationWaypoint FindNearestWaypointCore(IEnumerable<NavigationWaypoint> waypoints, Vector3 position)
    {
        if (waypoints == null) return null;

        Vector3 pFlat = new Vector3(position.x, 0f, position.z);
        NavigationWaypoint nearest = null;
        float minSqr = float.MaxValue;

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;

            // Skip prefab assets (Resources.FindObjectsOfTypeAll returns them)
            if (!wp.gameObject.scene.IsValid()) continue;

            Vector3 w = wp.transform.position;
            Vector3 wFlat = new Vector3(w.x, 0f, w.z);

            float d2 = (wFlat - pFlat).sqrMagnitude;
            if (d2 < minSqr)
            {
                minSqr = d2;
                nearest = wp;
            }
        }
        return nearest;
    }

    // List-first (new calls)
    private NavigationWaypoint FindNearestWaypoint(List<NavigationWaypoint> waypoints, Vector3 position)
        => FindNearestWaypointCore(waypoints, position);

    // Array-first (some legacy calls)
    private NavigationWaypoint FindNearestWaypoint(NavigationWaypoint[] waypoints, Vector3 position)
        => FindNearestWaypointCore(waypoints, position);

    // Position-first (legacy calls) – list version
    private NavigationWaypoint FindNearestWaypoint(Vector3 position, List<NavigationWaypoint> waypoints)
        => FindNearestWaypointCore(waypoints, position);

    // Position-first (legacy calls) – array version
    private NavigationWaypoint FindNearestWaypoint(Vector3 position, NavigationWaypoint[] waypoints)
        => FindNearestWaypointCore(waypoints, position);

    public Vector3 GetGroundPosition(Vector3 worldPos)
    {
        if (preferARGroundRaycast && raycastMgr != null)
        {
            var hits = new System.Collections.Generic.List<UnityEngine.XR.ARFoundation.ARRaycastHit>(1);
            var ray = new Ray(worldPos + Vector3.up * groundRayHeight, Vector3.down);
            if (raycastMgr.Raycast(ray, hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon))
            {
                float y = hits[0].pose.position.y;
                return new Vector3(worldPos.x, y + groundOffset, worldPos.z);
            }
        }
        return new Vector3(worldPos.x, fallbackGroundY + groundOffset, worldPos.z);
    }

    // Spawns pooled arrows along a single segment, snapping each sample to AR floor.
    void CreateGroundArrowsForSegment(Vector3 from, Vector3 to)
    {
        bool stairs = IsStairsSegment(from, to);
    
        if (stairs)
        {
            float len = Vector3.Distance(from, to);
            if (len < 0.001f) return;
            Vector3 dir = (to - from) / len;
            float step = Mathf.Max(0.05f, arrowSpacing);

            for (float s = 0f; s <= len; s += step)
            {
                Vector3 sample = from + dir * s + Vector3.up * stairsExtraLift;
                var arrow = RentArrow(); if (!arrow) continue;
                arrow.transform.SetPositionAndRotation(sample, Quaternion.LookRotation(dir, Vector3.up));
                if (pathArrows == null) pathArrows = new List<GameObject>();
                pathArrows.Add(arrow);
            }
            return;
        }

        // Flat segment
        Vector3 seg = to - from; seg.y = 0f;
        float segLen = seg.magnitude;
        if (segLen < 0.001f) return;
        Vector3 fwd = seg / segLen;
        float flatStep = Mathf.Max(0.05f, arrowSpacing);

        for (float s = 0f; s <= segLen; s += flatStep)
        {
            Vector3 sample = GetGroundPosition(from + fwd * s);
            var arrow = RentArrow(); if (!arrow) continue;
            arrow.transform.SetPositionAndRotation(sample, Quaternion.LookRotation(fwd, Vector3.up));
            if (pathArrows == null) pathArrows = new List<GameObject>();
            pathArrows.Add(arrow);
        }
    }
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

    // Centers each path point between left/right walls if both are found.
    // Requires: wallsLayerMask, corridorProbeHalfWidth, recenterRayOriginHeight set in Inspector.
    public void RecenterPathToCorridor(List<Vector3> path)
    {
        if (path == null || path.Count < 2) return;

        for (int idx = 0; idx < path.Count; idx++)
        {
            Vector3 p = path[idx];

            // Estimate forward using neighbors
            Vector3 forward;
            if (idx == 0) forward = path[idx + 1] - p;
            else if (idx == path.Count - 1) forward = p - path[idx - 1];
            else forward = (path[idx + 1] - path[idx - 1]) * 0.5f;

            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) continue;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 origin = p + Vector3.up * recenterRayOriginHeight;

            bool hitL = Physics.Raycast(origin, -right, out var leftHit, corridorProbeHalfWidth, wallsLayerMask, QueryTriggerInteraction.Ignore);
            bool hitR = Physics.Raycast(origin, right, out var rightHit, corridorProbeHalfWidth, wallsLayerMask, QueryTriggerInteraction.Ignore);

            if (hitL && hitR)
            {
                Vector3 mid = (leftHit.point + rightHit.point) * 0.5f;
                mid.y = p.y;
                path[idx] = mid;
            }
            else if (hitL ^ hitR)
            {
                // Only one wall found → offset half width from that wall toward corridor center
                Vector3 wall = hitL ? leftHit.point : rightHit.point;
                float sign = hitL ? +1f : -1f;
                Vector3 mid = wall + right * (sign * corridorProbeHalfWidth);
                mid.y = p.y;
                path[idx] = mid;
            }
        }
    }

    static readonly Dictionary<int, string> OfficeIdToDisplay = new Dictionary<int, string>
        {
        { 0, "Civil Registry Office" },
        { 1, "City Treasurer" },
        { 2, "Mayor's Office" },
        { 3, "Office of the City Administrator" },
        { 4, "Cash Division" },
        { 5, "e-Services / Technical Support (EDP Office)" },
        { 6, "E-Business" },
        { 7, "Real Estate Division" },
        { 8, "License Division" },
        { 9, "S.M.A.R.T" },
        { 10, "Manila Department of Social Welfare (MDSW)" },
        { 11, "PWD" }, // keep only if you really have an Office waypoint for PWD
        { 12, "Manila Department of Social Welfare (MDSW)" }, // second MDSW entry
        { 13, "Manila Department of Social Welfare (MDSW)" }, // third MDSW entry
        { 14, "Office of Senior Citizens Affairs (OSCA)" },
        { 15, "Manila Health Department" },
        { 16, "Bureau of Permits (BPLO)" },
        };

    public Vector3 GetOfficeWaypointPosition(int officeId)
    {
        if (officeIndex == null || officeIndex.Count == 0)
            BuildOfficeIndex();
    
        if (!OfficeIdToDisplay.TryGetValue(officeId, out var display))
        {
            Debug.LogError($"[WAYPOINT] Unknown officeId {officeId}");
            return Vector3.zero;
        }

        string mapped = GetCorrectWaypointName(display); // your alias cleaner
        string key = Canonical(mapped);

        if (!officeIndex.TryGetValue(key, out var candidates) || candidates.Count == 0)
        {
            Debug.LogError($"[WAYPOINT] No Office waypoint matches '{display}' (mapped '{mapped}', key '{key}')");
            return Vector3.zero;
        }

        var chosen = ChooseBest(candidates);
        Debug.Log($"[WAYPOINT] id={officeId} '{display}' → {chosen.name} at {chosen.transform.position}");
        return chosen.transform.position;
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
            case 16: return "Waypoint_BureauOfPermits";

            default:
                Debug.LogWarning($"[WAYPOINT] Unknown office ID: {officeId}");
                return "";
        }
    }

    public void StartNavigationFromCurrentLocation(string officeName)
    {
        Debug.Log($"=== StartNavigationFromCurrentLocation → {officeName} ===");


        // 1) Resolve target office waypoint
        NavigationWaypoint goal = null;
        try
        {
            var raw = FindTargetWaypointAdvanced(officeName); // may be Transform or GameObject
            goal = GetWaypointFromUnknown(raw);
        }
        catch { /* ignore */ }

        if (!goal && officeLookup != null && officeLookup.TryGetValue(officeName, out var goalGO) && goalGO)
            goal = goalGO.GetComponent<NavigationWaypoint>();

        if (!goal)
        {
            var allScan = GetAllRuntimeWaypoints();
            goal = allScan.FirstOrDefault(wp =>
                   string.Equals(wp.officeName, officeName, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(wp.waypointName, officeName, System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(wp.name, officeName, System.StringComparison.OrdinalIgnoreCase));
        }

        if (!goal)
        {
            Debug.LogError($"StartNavigationFromCurrentLocation: no office waypoint found for '{officeName}'");
            return;
        }

        // 2) Reset session and push UI
        ResetNavSessionBeforeNewRoute(goal);
        PushDestinationInfoToUI(goal);

        // 3) Anchors
        Vector3 fromStart = GetGroundPosition(GetRoutingStartPosition()); // ground
        Vector3 toGoalWorld = goal.transform.position;                    // PRESERVE Y (2nd floor ok)

        // Keep legacy targetDestination at true goal (not ground)
        targetDestination = toGoalWorld;

        // Place marker now at real goal height
        PlaceTargetMarkerAt(toGoalWorld, preserveYIfElevated: true);
        Debug.Log($"[Marker] Initial place at {toGoalWorld} (y={toGoalWorld.y:F2})");

        // 4) Graph nodes
        var all = GetAllRuntimeWaypoints();
        var startNode = FindNearestWaypoint(all.ToList(), fromStart);
        var goalNode = goal;

        if (!startNode || !goalNode)
        {
            Debug.LogError("StartNavigationFromCurrentLocation: startNode or goalNode null.");
            if (!forceWaypointRouting)
            {
                currentPathNodes.Clear();
                currentPath = new List<Vector3> { fromStart, toGoalWorld };
                isNavigating = true;
            }
            else
            {
                isNavigating = false;
            }
            return;
        }

        // 5) Fix one-sided links, then BFS
        NavigationWaypoint.MakeLinksBidirectional(removeInvalid: true, deduplicate: true);

        var nodes = BFSWaypoints(startNode, goalNode);
        if (nodes == null || nodes.Count < 2)
        {
            Debug.LogWarning($"[PATH] Directed BFS failed {startNode.name} → {goalNode.name}. Trying undirected BFS…");
            nodes = BFSWaypointsUndirected(startNode, goalNode);
        }

        if (nodes == null || nodes.Count < 2)
        {
            if (forceWaypointRouting)
            {
                Debug.LogError($"BFS failed {startNode.name} → {goalNode.name}; forceWaypointRouting prevents 2-point fallback.");
                isNavigating = false;
                return;
            }

            currentPathNodes.Clear();
            currentPath = new List<Vector3> { fromStart, toGoalWorld };
            isNavigating = true;
            Debug.LogWarning("Fallback: 2-point path used (BFS failed, forceWaypointRouting=false).");
            return;
        }

        // 6) Build precise 3D path
        BuildPathFromNodes(nodes, fromStart, toGoalWorld);
    }

    // Returns the Transform of the office waypoint by the name you show to users.
    public Transform ResolveOfficeTransformByDisplay(string displayName)
    {
        if (officeIndex == null || officeIndex.Count == 0) BuildOfficeIndexFromScene();

        string mapped = GetCorrectWaypointName(displayName);   // your alias cleaner
        string key = CanonicalOfficeKey(mapped);

        if (!officeIndex.TryGetValue(key, out var list) || list.Count == 0)
        {
            Debug.LogError($"[OFFICE] No waypoint for '{displayName}' (mapped '{mapped}')");
            return null;
        }

        var chosen = ChooseBestOffice(list); // prefers same floor, then nearest
        return chosen ? chosen.transform : null;
    }

    private List<NavigationWaypoint> BFSWaypointsUndirected(NavigationWaypoint start, NavigationWaypoint goal)
    {
        if (!start || !goal) return null;

        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();
        var nodes = all.Where(wp => wp && wp.gameObject.scene.IsValid()).ToArray();

        // Build undirected adjacency
        var adj = new System.Collections.Generic.Dictionary<NavigationWaypoint, System.Collections.Generic.List<NavigationWaypoint>>();
        foreach (var n in nodes)
        {
            adj[n] = new System.Collections.Generic.List<NavigationWaypoint>();
        }
        foreach (var a in nodes)
        {
            var list = a.connectedWaypoints;
            if (list == null) continue;
            foreach (var b in list)
            {
                if (!b || !b.gameObject.scene.IsValid() || b == a) continue;
                if (!adj[a].Contains(b)) adj[a].Add(b);
                if (!adj[b].Contains(a)) adj[b].Add(a); // add reverse for search
            }
        }

        // BFS
        var q = new System.Collections.Generic.Queue<NavigationWaypoint>();
        var came = new System.Collections.Generic.Dictionary<NavigationWaypoint, NavigationWaypoint>();
        var seen = new System.Collections.Generic.HashSet<NavigationWaypoint>();

        q.Enqueue(start);
        seen.Add(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == goal)
            {
                var path = new System.Collections.Generic.List<NavigationWaypoint> { cur };
                while (came.TryGetValue(cur, out var prev))
                {
                    cur = prev;
                    path.Add(cur);
                }
                path.Reverse();
                return path;
            }

            foreach (var nb in adj[cur])
            {
                if (nb == null || seen.Contains(nb)) continue;
                seen.Add(nb);
                came[nb] = cur;
                q.Enqueue(nb);
            }
        }
        return null;
    }

    private Vector3 ClampToFloorBounds(Vector3 position)
    {
        // Choose which floor’s bounds to use based on height
        GameObject floorRoot = null;
        if (position.y > floorSplitY && secondFloor)
            floorRoot = secondFloor;
        else if (groundFloor)
            floorRoot = groundFloor;
        else
            return position; // No floor root available

        // Get world-space bounds for that floor
        Bounds floorBounds = GetFloorBounds(floorRoot);

        // Small margin so we don’t clamp-oscillate when right on the border
        const float margin = 0.05f;

        float minX = floorBounds.min.x + margin;
        float maxX = floorBounds.max.x - margin;
        float minZ = floorBounds.min.z + margin;
        float maxZ = floorBounds.max.z - margin;

        // Clamp X/Z only; keep the original Y (important for stairs/2nd floor)
        Vector3 clamped = new Vector3(
            Mathf.Clamp(position.x, minX, maxX),
            position.y,
            Mathf.Clamp(position.z, minZ, maxZ)
        );

        // Optional debug if we actually clamped something
        if (Mathf.Abs(clamped.x - position.x) > 0.001f || Mathf.Abs(clamped.z - position.z) > 0.001f)
            Debug.LogWarning($"Clamped position {position} to {clamped} (outside {floorRoot.name} bounds)");

        return clamped;
    }


    private void PushDestinationInfoToUI(NavigationWaypoint goal)
    {
        var services = goal.services != null ? goal.services.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
        : System.Array.Empty<string>();
        var displayName = !string.IsNullOrEmpty(goal.officeName)
        ? goal.officeName
        : (!string.IsNullOrEmpty(goal.waypointName) ? goal.waypointName : goal.name);

        // Try ManilaServeUI if present
        var ui = FindFirstObjectByType<ManilaServeUI>(FindObjectsInactive.Include);
        if (ui)
        {
            ui.SendMessage("SetSelectedOffice", displayName, SendMessageOptions.DontRequireReceiver);
            ui.SendMessage("SetSelectedOfficeServices", services, SendMessageOptions.DontRequireReceiver);
        }

        // Also prime ServiceArrivalManager so arrival popup knows the same name/services
#if UNITY_2022_2_OR_NEWER
var mgr = serviceArrivalManager ? serviceArrivalManager
: FindFirstObjectByType<ServiceArrivalManager>(FindObjectsInactive.Include);
#else
        var mgr = serviceArrivalManager ? serviceArrivalManager
        : FindObjectOfType<ServiceArrivalManager>(true);
#endif
        if (mgr)
        {
            mgr.SendMessage("SetPendingDestination", displayName, SendMessageOptions.DontRequireReceiver);
            mgr.SendMessage("SetPendingDestinationServices", services, SendMessageOptions.DontRequireReceiver);
        }
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

    bool AreFloorsVisible()
    {
        if (floorsRoot) return floorsRoot.gameObject.activeSelf;
        // If not assigned, assume visible
        return true;
    }

    void SetFloorsVisible(bool on)
    {
        if (floorsRoot) floorsRoot.gameObject.SetActive(on);
        if (floorPlanUIRoot) floorPlanUIRoot.SetActive(on);
        Debug.Log($"[FLOOR] SetFloorsVisible({on})");
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

    public void ClearPathArrows() => ClearPathArrowsIfNeeded();

    // Pooled and null-safe clear (do not Destroy)
    public void ClearPathArrowsIfNeeded()
    {
        if (pathArrows == null)
            pathArrows = new System.Collections.Generic.List<GameObject>();

        for (int i = pathArrows.Count - 1; i >= 0; i--)
        {
            var go = pathArrows[i];
            if (!go) { pathArrows.RemoveAt(i); continue; }
            ReturnArrowToPool(go); // pooled; not Destroy
            pathArrows.RemoveAt(i);
        }
        ReturnAllArrowsToPool();
    }


    public void StopNavigation(bool clearOfficeOverride = false)
    {
        Debug.Log("[NAV] StopNavigation()");

        // State
        isNavigating = false;
        hasReachedDestination = false;
        currentDestination = null;
        currentPathIndex = 0;

        // Clear data
        currentPath?.Clear();
        currentPathNodes?.Clear();

        // Clear visuals
        ClearPathArrows();     // destroys/detaches all path arrow instances
        ClearFlowLine();       // hides/destroys any line renderer/flow line
        HideTargetMarker();    // new helper below

        // Optional HUD/world chain
        // If you have EnsureActiveChain/ClearActiveChain, call the clear here:
        // ClearActiveChain();

        // Optional: also clear manual-start override when explicitly requested
        if (clearOfficeOverride)
        {
            useOfficeAsStart = false;
            currentUserOffice = null;
        }
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
        // Always bob/spin the target marker if it’s active
        AnimateEnhancedTargetMarker();

// Not navigating? Nothing to update.
if (!isNavigating) return;

        // If we already reached the destination, stop updating guidance
        if (hasReachedDestination) return;

        // Must have a usable path
        if (currentPath == null || currentPath.Count < 2)
        {
            // Hide/idle arrow visuals if your implementation uses this
            UpdateArrowVisibility();
            return;
        }

        // Drive your existing arrow system (no route line)
        ProgressiveArrowsTick();            // maintain/spawn breadcrumb arrows along currentPath
        AnimateArrowPath();                  // animate arrow movement
        UpdateArrowBaseScalesIfEnabled();    // optional scaling
        UpdateArrowVisibility();             // show/hide as needed

        // Guidance UI
        UpdateDirections();                  // heading/next segment UI
        UpdateTurnHints();                   // text hint: Turn left/right, Go straight

        // Arrival check last so final hint shows before popup
        CheckArrivalAtDestination();
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
    void EnsureTurnUI()
    {
        // If you forgot to wire fields in the Inspector, try to find them by name
        if (!turnText)
        {
            var go = GameObject.Find("DirectionsText"); // rename to your text object name
            if (go) turnText = go.GetComponent<TMPro.TMP_Text>();
        }
        if (!turnPanel && turnText)
            turnPanel = turnText.transform.parent.gameObject; // assume the text is inside the panel
    }

    void ShowTurnUI(bool show)
    {
        EnsureTurnUI();
        if (turnPanel) turnPanel.SetActive(show);
        else if (turnText) turnText.gameObject.SetActive(show);
    }

    void UpdateTurnHints()
    {
        EnsureTurnUI();

// Only hide when NOT navigating
if (!isNavigating)
        {
            ShowTurnUI(false);
            if (turnText) turnText.text = "";
            if (headingToText) headingToText.text = ""; // clear heading
            return;
        }

        // Always show while navigating
        ShowTurnUI(true);

        // Resolve destination name for "Heading to ..."
        string destName = currentDestination;
        if (string.IsNullOrEmpty(destName) && currentDestinationWaypoint != null)
        {
            destName = !string.IsNullOrEmpty(currentDestinationWaypoint.officeName)
                ? currentDestinationWaypoint.officeName
                : (!string.IsNullOrEmpty(currentDestinationWaypoint.waypointName)
                    ? currentDestinationWaypoint.waypointName
                    : currentDestinationWaypoint.name);
        }
        if (headingToText)
            headingToText.text = string.IsNullOrEmpty(destName) ? "" : $"<< Heading to {destName} >>";

        Vector3 camWorld = GetCurrentPosition();
        Vector3 camFlat = new Vector3(camWorld.x, 0f, camWorld.z);

        // If you have a path, use it to get destination fallback
        Vector3 destPos = camWorld;
        if (currentPath != null && currentPath.Count > 0)
            destPos = currentPath[currentPath.Count - 1];

        // When we DO have a turn list, maintain progress
        if (_turns != null && _turns.Count > 0)
        {
            if (_nextTurn < 0) _nextTurn = 0;

            // Advance to next turn if close
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
        }

        // Pick the guidance target
        bool hasTurns = _turns != null && _turns.Count > 0 && _nextTurn >= 0 && _nextTurn < _turns.Count;
        Vector3 nextPos = hasTurns ? _turns[_nextTurn].pos : destPos;

        // Direction to target
        Vector3 nextFlat = new Vector3(nextPos.x, 0f, nextPos.z);
        Vector3 toNext = nextFlat - camFlat;
        float d = Mathf.Max(0.0001f, toNext.magnitude);
        Vector3 toNextN = toNext / d;

        // Heading (compass with fallback to camera yaw)
        float headingDeg = GetUserHeadingDeg();
        Vector3 headingV = BearingUtil.YawToVector(headingDeg);

        // Signed angle user->route and smoothing
        float signedAngle = Vector3.SignedAngle(headingV, toNextN, Vector3.up);
        float k = angleSmoothTau <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / angleSmoothTau);
        _angleSmoothedDeg = Mathf.LerpAngle(_angleSmoothedDeg, signedAngle, k);
        float absA = Mathf.Abs(_angleSmoothedDeg);

        // Instruction (unchanged)
        string instruction;
        if (absA <= straightTolerance) instruction = hasTurns ? "Go straight" : "Proceed to destination";
        else if (absA <= slightTolerance) instruction = _angleSmoothedDeg > 0 ? "Slight left" : "Slight right";
        else if (absA <= 135f) instruction = _angleSmoothedDeg > 0 ? "Turn left" : "Turn right";
        else instruction = "Make a U-turn";

        // ----- Distance (along path with calibration) -----
        int targetIndex = -1;
        if (currentPath != null && currentPath.Count > 0)
        {
            if (hasTurns)
            {
                targetIndex = _turns[_nextTurn].index;
                if (targetIndex <= 0) targetIndex = FindNearestPathIndex(nextPos);
                targetIndex = Mathf.Clamp(targetIndex, 0, currentPath.Count - 1);
            }
            else
            {
                targetIndex = currentPath.Count - 1; // destination
            }
        }

        float dMeters;
        if (usePathDistanceForHints && targetIndex >= 0 && currentPath != null && currentPath.Count >= 2)
        {
            Vector3 proj;
            int segIdx = FindClosestSegmentAndProjection(camWorld, out proj);
            float along = DistanceAlongPathFrom(proj, segIdx, targetIndex); // unity units
            dMeters = along * Mathf.Max(0.0001f, metersPerUnit);
        }
        else
        {
            // fallback: straight-line
            float direct = Vector3.Distance(camFlat, nextFlat);
            dMeters = direct * Mathf.Max(0.0001f, metersPerUnit);
        }

        string distStr = dMeters < 1f ? $"{Mathf.RoundToInt(dMeters * 100f)} cm"
                        : (dMeters < 10f ? $"{dMeters:0.0} m" : $"{Mathf.RoundToInt(dMeters)} m");

        // Upcoming preview only if there’s another turn ahead
        string upcoming = "";
        if (hasTurns && _nextTurn + 1 < _turns.Count)
        {
            Vector3 after = _turns[_nextTurn + 1].pos;
            Vector3 seg1 = (nextPos - camWorld); seg1.y = 0f;
            Vector3 seg2 = (after - nextPos); seg2.y = 0f;
            if (seg1.sqrMagnitude > 1e-4f && seg2.sqrMagnitude > 1e-4f)
            {
                float turnAngle = Vector3.SignedAngle(seg1.normalized, seg2.normalized, Vector3.up);
                float a2 = Mathf.Abs(turnAngle);
                if (a2 <= straightTolerance) upcoming = "Then: continue straight";
                else if (a2 <= slightTolerance) upcoming = turnAngle > 0 ? "Then: slight left" : "Then: slight right";
                else if (a2 <= 135f) upcoming = turnAngle > 0 ? "Then: turn left" : "Then: turn right";
                else upcoming = "Then: make a U-turn";
            }
        }

        // Update UI (always shown during nav)
        if (turnText)
            turnText.text = string.IsNullOrEmpty(upcoming)
                ? $"{instruction} • {distStr}"
                : $"{instruction} • {distStr} ({upcoming})";

        // Optional: announce as you approach the next turn/destination
        if (!_turnAnnounced && dMeters <= turnAnnounceDistance)
        {
            _turnAnnounced = true;
            // TODO: audio cue / TTS
        }
    }
    void CreateProgressiveArrowsFromProjection(int startSegment, Vector3 projPos, float aheadMeters)
    {
        EnsureArrowPool();
        ClearPathArrowsIfNeeded();
        if (currentPath == null || currentPath.Count < 2) return;

        startSegment = Mathf.Clamp(startSegment, 0, currentPath.Count - 2);

        float mpu = Mathf.Max(0.0001f, metersPerUnit);
        float remainingUnits = Mathf.Max(0.5f, aheadMeters) / mpu;
        float step = Mathf.Max(0.05f, arrowSpacing);

        // First segment from projection
        Vector3 a = projPos;
        Vector3 b = currentPath[startSegment + 1];
        Vector3 seg = b - a; seg.y = 0f;
        float segLen = seg.magnitude;
        Vector3 fwd = segLen > 1e-4f ? seg / segLen : Vector3.forward;

        for (float s = 0f; s <= Mathf.Min(segLen, remainingUnits); s += step)
        {
            Vector3 sample = GetGroundPosition(a + fwd * s);
            var go = RentArrow(); if (!go) continue;
            go.transform.SetPositionAndRotation(sample, Quaternion.LookRotation(fwd, Vector3.up));
            if (pathArrows == null) pathArrows = new List<GameObject>();
            pathArrows.Add(go);
        }
        remainingUnits -= Mathf.Min(segLen, remainingUnits);
        if (remainingUnits <= 0f) return;

        // Remaining segments forward
        for (int j = startSegment + 1; j < currentPath.Count - 1 && remainingUnits > 0f; j++)
        {
            a = currentPath[j];
            b = currentPath[j + 1];
            seg = b - a; seg.y = 0f;
            segLen = seg.magnitude;
            fwd = segLen > 1e-4f ? seg / segLen : fwd;

            for (float s = 0f; s <= Mathf.Min(segLen, remainingUnits); s += step)
            {
                Vector3 sample = GetGroundPosition(a + fwd * s);
                var go = RentArrow(); if (!go) continue;
                go.transform.SetPositionAndRotation(sample, Quaternion.LookRotation(fwd, Vector3.up));
                if (pathArrows == null) pathArrows = new List<GameObject>();
                pathArrows.Add(go);
            }
            remainingUnits -= Mathf.Min(segLen, remainingUnits);
        }
    }

    void ProgressiveArrowsTick()
    {
        if (!isNavigating || !progressiveReveal || currentPath == null || currentPath.Count < 2) return;

        Vector3 proj;
        int seg = FindClosestSegmentAndProjection(GetCurrentPosition(), out proj);
        if (seg < 0) return;

        float mpu = Mathf.Max(0.0001f, metersPerUnit);
        float movedMeters = Vector3.Distance(new Vector3(proj.x, 0, proj.z), new Vector3(_lastProjPos.x, 0, _lastProjPos.z)) * mpu;

        if (seg != _lastProjSeg || movedMeters >= redrawMoveThresholdMeters)
        {
            CreateProgressiveArrowsFromProjection(seg, proj, revealAheadMeters);
            SetArrowsActive(true);
            _lastProjSeg = seg;
            _lastProjPos = proj;
        }
    }
    // Returns the index of the segment [i..i+1] closest to 'p' and the clamped projection point on that segment
    int FindClosestSegmentAndProjection(Vector3 p, out Vector3 proj)
    {
        proj = p;
        if (currentPath == null || currentPath.Count < 2) return -1;

        int bestIdx = -1;
        float bestSqr = float.MaxValue;

        const float elevatedTol = 1.0f;

        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            Vector3 a = currentPath[i];
            Vector3 b = currentPath[i + 1];

            float ayGround = GetGroundPosition(new Vector3(a.x, 0f, a.z)).y;
            float byGround = GetGroundPosition(new Vector3(b.x, 0f, b.z)).y;
            bool aElevated = Mathf.Abs(a.y - ayGround) > elevatedTol;
            bool bElevated = Mathf.Abs(b.y - byGround) > elevatedTol;

            bool use3D = IsStairsSegment(i) || (aElevated && bElevated);

            if (use3D)
            {
                Vector3 ab = b - a;
                float ab2 = ab.sqrMagnitude;
                if (ab2 < 1e-6f) continue;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab2);
                Vector3 q = a + ab * t;
                float d2 = (p - q).sqrMagnitude;
                if (d2 < bestSqr)
                {
                    bestSqr = d2;
                    bestIdx = i;
                    proj = q + Vector3.up * stairsExtraLift;
                }
            }
            else
            {
                Vector3 aF = new Vector3(a.x, 0f, a.z);
                Vector3 bF = new Vector3(b.x, 0f, b.z);
                Vector3 pF = new Vector3(p.x, 0f, p.z);

                Vector3 abF = bF - aF;
                float abF2 = abF.sqrMagnitude;
                if (abF2 < 1e-6f) continue;
                float t = Mathf.Clamp01(Vector3.Dot(pF - aF, abF) / abF2);
                Vector3 qF = aF + abF * t;
                Vector3 q = new Vector3(qF.x, 0f, qF.z);
                q = GetGroundPosition(q);
                float d2 = (pF - qF).sqrMagnitude;
                if (d2 < bestSqr)
                {
                    bestSqr = d2;
                    bestIdx = i;
                    proj = q;
                }
            }
        }

        return bestIdx;
    }
    int FindNearestPathIndex(Vector3 p)
    {
        if (currentPath == null || currentPath.Count == 0) return -1;

        float best = float.MaxValue;
        int bestIdx = 0;

        Vector3 pFlat = new Vector3(p.x, 0f, p.z);
        for (int i = 0; i < currentPath.Count; i++)
        {
            Vector3 v = currentPath[i];
            v.y = 0f;
            float d2 = (v - pFlat).sqrMagnitude;
            if (d2 < best)
            {
                best = d2;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    // Distance along the path from a point on segment 'segIdx' to the vertex at 'targetIndex'
    float DistanceAlongPathFrom(Vector3 projOnSeg, int segIdx, int targetIndex)
    {
        if (currentPath == null || currentPath.Count == 0) return 0f;
        targetIndex = Mathf.Clamp(targetIndex, 0, currentPath.Count - 1);
        segIdx = Mathf.Clamp(segIdx, 0, currentPath.Count - 2);

        // If the user is already beyond the target segment, distance is zero
        if (segIdx >= targetIndex) return 0f;

        float sum = 0f;

        // from projection to end of current segment
        Vector3 a = projOnSeg;
        Vector3 b = currentPath[segIdx + 1];
        sum += Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));

        // whole segments between current+1 and targetIndex
        for (int i = segIdx + 1; i < targetIndex; i++)
        {
            Vector3 p0 = currentPath[i];
            Vector3 p1 = currentPath[i + 1];
            sum += Vector3.Distance(new Vector3(p0.x, 0, p0.z), new Vector3(p1.x, 0, p1.z));
        }

        return sum;
    }

    // Build list of turn points from a path (skip tiny bends)
    public void BuildTurnEventsFromPath(List<Vector3> pathPoints, float minTurnAngle = 25f)
    {
        _turns.Clear();
        _nextTurn = 0;
        _turnAnnounced = false;

        if (pathPoints == null || pathPoints.Count < 3) return;

        for (int i = 1; i < pathPoints.Count - 1; i++)
        {
            Vector3 a = pathPoints[i - 1];
            Vector3 b = pathPoints[i];
            Vector3 c = pathPoints[i + 1];

            Vector3 v1 = b - a; v1.y = 0f;
            Vector3 v2 = c - b; v2.y = 0f;
            if (v1.sqrMagnitude < 1e-4f || v2.sqrMagnitude < 1e-4f) continue;

            float ang = Vector3.SignedAngle(v1.normalized, v2.normalized, Vector3.up); // + left, - right
            if (Mathf.Abs(ang) >= minTurnAngle)
            {
                _turns.Add(new TurnEvent { index = i, pos = b, dir = ang > 0 ? +1 : -1 });
            }
        }
    }

    void AnimateEnhancedTargetMarker()
    {
        if (!targetMarker || !targetMarker.activeSelf) return;

        // Only bob on Y and spin. Never change X/Z.
        Vector3 p = targetMarker.transform.position;
        float baseY = float.IsNaN(targetMarkerBaseY) ? p.y : targetMarkerBaseY;
        p.y = baseY + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        targetMarker.transform.position = p;

        targetMarker.transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
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
        // Renderers only (skip nav visuals and scripts)
        var renderers = FindObjectsOfType<Renderer>(true);
        // Find your nav visuals root once
        var navRootGO = GameObject.Find("NavigationVisuals");
        Transform navRoot = navRootGO ? navRootGO.transform : null;
    
const float splitY = 5f; // unify threshold

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // Skip navigation visuals (arrow, marker, etc.)
            if (navRoot && r.transform.IsChildOf(navRoot)) continue;

            // Only toggle second-floor renderers
            if (r.transform.position.y > splitY)
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
        {
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
        }

        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(start, end, t / duration);
            foreach (var r in renderers)
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        var c = mat.color; c.a = a; mat.color = c;
                    }
                }
            }
            yield return null;
        }

        if (fadeIn)
        {
            // Restore Opaque
            foreach (var r in renderers)
            {
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
        }
        else
        {
            // Keep logic alive; hide visuals only if this floor hosts waypoints
            bool containsWaypoints = floor.GetComponentsInChildren<NavigationWaypoint>(true).Length > 0;

            if (!hudOnly && !containsWaypoints)
            {
                // Safe to deactivate if no waypoints under this root
                floor.SetActive(false);
            }
            else
            {
                // Hide visuals only (waypoints, triggers remain active)
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


    [ContextMenu("Nav/Rebuild Connections (LOS)")]
    public void RebuildConnectionsLOS()
    {
        int links = NavigationWaypoint.RebuildConnectionsAll(); // uses each waypoint's connectionDistance + LOS
        Debug.Log($"[GRAPH] Rebuilt links with LOS. New links added: {links}");
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
        if (officeIndex == null || officeIndex.Count == 0) BuildOfficeIndex();

        var names = officeIndex.Values
            .Select(list => list[0])
            .Select(wp => string.IsNullOrWhiteSpace(wp.officeName) ? wp.waypointName : wp.officeName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        Debug.Log($"GetAllOfficeNames returning {names.Count}");
        return names;
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

        Vector3 user = GetCurrentPosition();
        Vector3 dest = (currentPath != null && currentPath.Count > 0)
            ? currentPath[currentPath.Count - 1]
            : targetDestination;

        Vector3 uf = new Vector3(user.x, 0, user.z);
        Vector3 df = new Vector3(dest.x, 0, dest.z);

        float distMeters = Vector3.Distance(uf, df) * Mathf.Max(0.0001f, metersPerUnit);
        if (distMeters <= arrivalDistanceThreshold)
        {
            timeWithinArrivalZone += Time.deltaTime;
            if (timeWithinArrivalZone >= arrivalConfirmTime && !hasShownArrivalNotification)
                OnArrivalAtDestination();
        }
        else
        {
            timeWithinArrivalZone = 0f;
        }
    }

    public void CancelNavigate(bool preserveArrivalUI = false)
    {
        Debug.Log("[NAV] CancelNavigate");

        // Stop any running gate coroutine
        try
        {
            if (facingGateRoutine != null) { StopCoroutine(facingGateRoutine); facingGateRoutine = null; }
        }
        catch { }

        bool hadSession = navSessionActive || isNavigating;

        isNavigating = false;
        navSessionActive = false;

        // Clear visuals (pooled; no Destroy)
        ClearFlowLine();
        ClearPathArrowsIfNeeded();
        SetArrowsActive(false);

        // Deactivate target marker if you use one
        if (targetMarker) targetMarker.SetActive(false);

        // Reset turn hints
        if (_turns != null) _turns.Clear();
        _nextTurn = -1;
        _turnAnnounced = false;
        ShowTurnUI(false);
        if (turnText) turnText.text = "";

        // Optional HUD text fields you had
        if (directionText) directionText.text = "";
        if (distanceText) distanceText.text = "";

        // Unsubscribe HUD stepper if you hooked it
        if (stepper != null) stepper.OnDistance -= OnHudMeters;

        // Path state
        currentPathIndex = 0;
        currentPath = null;
        lastPlayerPosition = GetCurrentPosition();

        // Restore floors only if a nav session really started
        if (hideFloorsOnlyDuringNav && hadSession)
            SetFloorsVisible(floorsWereVisibleBeforeNav);

        // Restore buttons/status
        if (statusText)
        {
            statusText.text = useVuforiaPositioning
                ? (isImageTargetTracked ? "Ready to navigate" : "Point camera at office sign")
                : "Ready to navigate";
        }
        if (navigateButton) navigateButton.gameObject.SetActive(true);
        if (stopButton) stopButton.gameObject.SetActive(false);

        Debug.Log("[NAV] Navigation canceled and UI reset.");
    }

    void OnArrivalAtDestination()
    {
        hasReachedDestination = true;
        hasShownArrivalNotification = true;
        Debug.Log($"[ARRIVAL] Arrived at {currentDestination}");

#if UNITY_ANDROID || UNITY_IOS
    Handheld.Vibrate();
#endif
        // --- START FIX: Bulletproof Manager Lookup ---
        ServiceArrivalManager mgr = serviceArrivalManager;

        // If not assigned in Inspector, search the entire scene (active or inactive)
        if (mgr == null)
        {
            // Use Resources.FindObjectsOfTypeAll as the most robust way to find inactive objects
            mgr = Resources.FindObjectsOfTypeAll<ServiceArrivalManager>()
                            .FirstOrDefault(m => m.gameObject.scene.IsValid());
        }
        // --- END FIX ---

        // Show popup (preferred path)
        if (mgr != null)
        {
            // If we are here, the manager instance is found. Proceed to show the popup.
            mgr.ShowArrivalPopup(currentDestination);

            // If we auto-cancel, delay the cancellation to give the popup time to display.
            if (autoCancelOnArrival)
            {
                isNavigating = false;
                navSessionActive = false;
                StartCoroutine(DelayedStopNavigation(0.5f));
            }
        }
        else if (arrivalPanel)
        {
            // Fallback to simple panel activation
            arrivalPanel.SetActive(true);
            if (autoCancelOnArrival) StartCoroutine(DelayedStopNavigation(arrivalConfirmTime));
        }
        else
        {
            Debug.LogError("[ARRIVAL] CRITICAL: ServiceArrivalManager NOT FOUND or popup system misconfigured. Cannot display service arrival UI.");
            if (autoCancelOnArrival) StartCoroutine(DelayedStopNavigation(0.5f));
        }

        // Keep floors hidden while popup is handled
        if (hideFloorsOnlyDuringNav) SetFloorsVisible(false);

        timeWithinArrivalZone = 0f;
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
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
    // Canonical office display names in your scene:
    { "Civil Registry", "Civil Registry Office" },
    { "Civil Registry Office", "Civil Registry Office" },

    { "City Treasurer", "City Treasurer" },

    { "Mayor's Office", "Mayor's Office" },
    { "Office of the City Mayor", "Mayor's Office" },

    { "Office of the City Administrator", "Office of the City Administrator" },
    { "Office of the City Admin", "Office of the City Administrator" },

    { "Cash Division", "Cash Division" },

    { "EDP", "e-Services / Technical Support (EDP Office)" },
    { "e-Services / Technical Support (EDP Office)", "e-Services / Technical Support (EDP Office)" },

    { "E-Business", "E-Business" },

    { "Real Estate", "Real Estate Division" },
    { "Real Estate Division", "Real Estate Division" },

    { "License Division", "License Division" },
    { "License Div", "License Division" },

    { "SMART", "S.M.A.R.T" },
    { "S.M.A.R.T", "S.M.A.R.T" },

    { "Bureau of Permits", "Bureau of Permits (BPLO)" },
    { "BPLO", "Bureau of Permits (BPLO)" },
    { "Bureau of Permits (BPLO)", "Bureau of Permits (BPLO)" },

    { "Manila Health Department", "Manila Health Department" },
    { "MHD", "Manila Health Department" },

    { "Manila Department of Social Welfare (MDSW)", "Manila Department of Social Welfare (MDSW)" },
    { "MDSW", "Manila Department of Social Welfare (MDSW)" },
    { "MDSW 108", "Manila Department of Social Welfare (MDSW)" },
    { "MDSW-108", "Manila Department of Social Welfare (MDSW)" },
    { "MDSW 109", "Manila Department of Social Welfare (MDSW)" },
    { "MDSW-109", "Manila Department of Social Welfare (MDSW)" },
    { "MDSW 111", "Manila Department of Social Welfare (MDSW)" },
    { "MDSW-111", "Manila Department of Social Welfare (MDSW)" },

    { "Office of Senior Citizens Affairs (OSCA)", "Office of Senior Citizens Affairs (OSCA)" },
    { "OSCA", "Office of Senior Citizens Affairs (OSCA)" },
    };

        if (map.TryGetValue(officeName, out var mapped))
            return mapped;

        return officeName; // unknown label passes through
    }

    // ===== TEMP STUBS (so the project compiles) =====
    void RepairWaypointGraph() { }  // no-op
    List<NavigationWaypoint> GetAllWaypointsIncludeInactive()
    {
        // Fallback to active scene objects only (enough to compile)
        return FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None).ToList();
    }
    void EnsurePinnedBidirectional(List<NavigationWaypoint> all) { } // no-op

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

    [ContextMenu("Nav/Debug/Report Stair Nodes")]
    public void DebugReportStairNodes()
    {
        var sb = new StringBuilder();
        var all = GetSceneWaypoints();
        var stairs = all.Where(wp => wp.waypointType == WaypointType.Stairs).ToList();

        if (stairs.Count == 0)
        {
            Debug.Log("[STAIRS] No stair nodes found in scene.");
            return;
        }

        sb.AppendLine($"[STAIRS] Report ({stairs.Count} nodes, vTol={floorMatchTolerance:0.00}m)");
        foreach (var s in stairs)
        {
            if (!s) continue;

            float sy = s.transform.position.y;
            var neighbors = s.connectedWaypoints ?? new List<NavigationWaypoint>();

            // Pairs = connected stairs
            var stairPairs = neighbors.Where(n => n && n.waypointType == WaypointType.Stairs).ToList();

            // Same-floor corridor neighbors
            var sameFloorCorr = neighbors.Where(n =>
                    n && n.waypointType != WaypointType.Stairs &&
                    Mathf.Abs(n.transform.position.y - sy) <= floorMatchTolerance)
                .Select(n => n.name).ToList();

            // Illegal cross-floor non-stairs links
            var crossFloorNonStairs = neighbors.Where(n =>
                    n && n.waypointType != WaypointType.Stairs &&
                    Mathf.Abs(n.transform.position.y - sy) > floorMatchTolerance)
                .Select(n => n.name).ToList();

            sb.AppendLine($"- {s.name}  y={sy:0.00}");
            if (stairPairs.Count > 0)
            {
                foreach (var p in stairPairs)
                {
                    float dy = Mathf.Abs(p.transform.position.y - sy);
                    sb.AppendLine($"   Pair: {p.name}  Δy={dy:0.00}");
                }
            }
            else
            {
                sb.AppendLine("   Pair: NONE (warning)");
            }

            sb.AppendLine($"   same-floor corridors: {(sameFloorCorr.Count > 0 ? string.Join(", ", sameFloorCorr) : "NONE (warning)")}");
            if (crossFloorNonStairs.Count > 0)
                sb.AppendLine($"   ILLEGAL cross-floor non-stairs links: {string.Join(", ", crossFloorNonStairs)}");
        }

        Debug.Log(sb.ToString());
    }

    [ContextMenu("Nav/Debug/Report Cross-Floor Non-Stairs Links")]
    public void DebugReportCrossFloorNonStairsLinks()
    {
        var all = GetSceneWaypoints();
        int count = 0;
        var sb = new StringBuilder();
        sb.AppendLine("[STAIRS] Cross-floor NON-stairs links (should be zero):");

        foreach (var a in all)
        {
            if (!a || a.connectedWaypoints == null) continue;

            foreach (var b in a.connectedWaypoints)
            {
                if (!b) continue;
                if (a.waypointType == WaypointType.Stairs || b.waypointType == WaypointType.Stairs) continue;

                float dy = Mathf.Abs(a.transform.position.y - b.transform.position.y);
                if (dy > floorMatchTolerance)
                {
                    count++;
                    sb.AppendLine($"- {a.name} (y={a.transform.position.y:0.00}) ↔ {b.name} (y={b.transform.position.y:0.00})  Δy={dy:0.00}");
                }
            }
        }

        if (count == 0) Debug.Log("[STAIRS] No illegal cross-floor non-stairs links.");
        else Debug.Log(sb.ToString());
    }

    [ContextMenu("Nav/Debug/Report Current Path Stair Segments")]
    public void DebugReportCurrentPathStairs()
    {
        if (currentPath == null || currentPath.Count < 2)
        {
            Debug.Log("[PATH] currentPath empty.");
            return;
        }
        var sb = new StringBuilder();
        int stairsSegs = 0;
        for (int i = 0; i < currentPath.Count - 1; i++)
        {
            var a = currentPath[i];
            var b = currentPath[i + 1];
            if (IsStairsSegment(a, b))
            {
                stairsSegs++;
                sb.AppendLine("seg {i} → {i + 1} Δy={(b.y - a.y):0.00} A={a} B={b}");
            }
        }
        if (stairsSegs == 0) Debug.Log("[PATH] No stairs segments in currentPath."); else Debug.Log("[PATH] Stairs segments found: {stairsSegs}\n{sb}");
    }

    // Get all waypoints from the active scene (skip prefab assets)
    List<NavigationWaypoint> GetSceneWaypoints()
    {
        return Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(wp => wp != null && wp.gameObject.scene.IsValid())
        .ToList();
    }

    // Detect a stairs segment (keep this consistent with your stair drawing logic)
    bool IsStairsSegment(Vector3 a, Vector3 b)
    {
        if (Mathf.Abs(b.y - a.y) >= stairsElevationDeltaMin) return true; // vertical delta

        // Optional: nearest stair nodes heuristic
        var all = GetSceneWaypoints();
        var aw = FindNearestWaypoint(all, a);
        var bw = FindNearestWaypoint(all, b);
        if (aw && aw.waypointType == WaypointType.Stairs) return true;
        if (bw && bw.waypointType == WaypointType.Stairs) return true;
        return false;
    }

    [ContextMenu("Nav/Debug/Report Nodes With Zero Neighbors")]
    public void DebugReportNodesWithZeroNeighbors()
    {
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(wp => wp != null && wp.gameObject.scene.IsValid()).ToList();

        var sb = new StringBuilder();
        int zero = 0;
        foreach (var wp in all)
        {
            if (wp.connectedWaypoints == null || wp.connectedWaypoints.Count == 0)
            {
                zero++;
                sb.AppendLine($"- {wp.name} (y={wp.transform.position.y:0.00}) dist={wp.connectionDistance:0.0} type={wp.waypointType}");
            }
        }

        Debug.Log(zero == 0
            ? "[GRAPH] No nodes with zero neighbors."
            : $"[GRAPH] Nodes with ZERO neighbors: {zero}\n{sb}");
    }

    [ContextMenu("Nav/Debug/Report Link Counts By Floor")]
    public void DebugReportLinkCountsByFloor()
    {
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(wp => wp != null && wp.gameObject.scene.IsValid()).ToList();


        var floors = all.GroupBy(wp => Mathf.Round(wp.transform.position.y * 10f) / 10f)
                        .OrderBy(g => g.Key)
                        .Select(g => new { y = g.Key, count = g.Count(), links = g.Sum(w => (w.connectedWaypoints?.Count ?? 0)) })
                        .ToList();
        var sb = new StringBuilder();
        foreach (var f in floors)
            sb.AppendLine($"y={f.y:0.00} → nodes={f.count}, total links={f.links}");

        Debug.Log($"[GRAPH] Link counts by floor:\n{sb}");
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
