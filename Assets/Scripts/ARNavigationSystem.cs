
    using UnityEngine;
    using UnityEngine.UI;
    using TMPro;
    using System.Collections.Generic;
    using System.Collections;   // ✅ needed for IEnumerator
    using System.Linq;
    using UnityEngine.Rendering;
    


public class SmartNavigationSystem : MonoBehaviour

{

    public Camera arCamera; // assign ARCamera in Inspector

    [Header("Vuforia Integration")]
    [Tooltip("Enable to use Vuforia image targets for positioning")]
    public bool useVuforiaPositioning = false;

    [Tooltip("Assign your Vuforia image target transforms here")]
    public Transform[] vuforiaImageTargets;

    [Tooltip("Names of the image targets (should match Vuforia database)")]
    public string[] imageTargetNames;

    [Header("Popup-first Guard")]
    public bool enforcePopupFirst = true;
    private bool _popupConfirmed = false;

    [Tooltip("Current detected image target position")]
    public Vector3 lastDetectedPosition = Vector3.zero;

    [Tooltip("Is any image target currently being tracked?")]
    public bool isImageTargetTracked = false;

    [Header("UI Elements")]
    public Button navigateButton;
    public Button stopButton;
    public TextMeshProUGUI statusText;

    // ADDED: Optional direction UI elements (won't break if missing)
    [Header("Direction UI (Optional)")]
    public TextMeshProUGUI directionText;
    public TextMeshProUGUI distanceText;

    [Header("Enhanced 3D Arrow Navigation")]
    public GameObject arrowPrefab;
    public Material arrowMaterial;
    public Color arrowColor = Color.cyan;
    public float arrowSpacing = 3f;
    public float arrowSize = 0.6f;
    public float groundOffset = 0.1f;
    public bool useGroundDetection = true;

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
    public bool graphIsAuthoritative = true; // trust waypoint links; skip LOS in A* neighbor expansion
    public bool useLOSForSmoothing = false; // keep LOS for path smoothing only

    [Header("Path preferences")]
    public bool allowDirectStraightPath = false; // block 100% straight start->end unless you explicitly turn it on

    [Header("Anchors")]
    [SerializeField] private Transform contentAnchor; // drag center_anchor/ContentAnchor here
    private Transform pathRoot; // created at runtime

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



    // Current navigation state
    private bool isNavigating = false;
    private Vector3 targetDestination;
    private string currentDestination = "";
    private Camera playerCamera;

    // ADDED: Turn-by-turn variables (minimal addition)
    private int currentPathIndex = 0;
    private Vector3 lastPlayerPosition;

    // 3D Navigation objects
    private List<GameObject> pathArrows = new List<GameObject>();
    private float targetBobOffset = 0f;
    private Vector3 originalTargetPosition;
    private List<Vector3> currentPath = new List<Vector3>();

    public FloorManager floorManager; // Assign this in the Inspector
    private Material arrowSharedMat;

    private Dictionary<string, GameObject> officeLookup = new Dictionary<string, GameObject>();


    void Start()
    {

        playerCamera = Camera.main ?? FindFirstObjectByType<Camera>();


        // Use inspector material if provided; otherwise create one
        arrowSharedMat = arrowMaterial != null ? arrowMaterial : CreateArrowMaterial(arrowColor);

        CreateEnhancedArrowPrefab();
        CreateEnhancedTargetMarker();
        FindAllWaypoints();

        SetupUI();
        SetupVuforiaImageTargets();
        if (groundFloor != null) groundFloor.SetActive(true);
        if (secondFloor != null) secondFloor.SetActive(false);

        CacheAllOffices();

        // ✅ Notify the UI once offices are ready
        var ui = FindFirstObjectByType<ManilaServeUI>();
        if (ui != null)
        {
            ui.PopulateOfficeDropdown();
        }

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
        if (!useVuforiaPositioning) return;
        // We localized via Vuforia
        isImageTargetTracked = true;
        hasEverLocalized = true;  // you added this flag above

        // Use the USER (camera) pose, not the marker pose
        Vector3 camPos = arCamera ? arCamera.transform.position : targetPoseFromRelay;
        Quaternion camRot = arCamera ? arCamera.transform.rotation : Quaternion.identity;

        // Update the position used by GetCurrentPosition()
        lastDetectedPosition = camPos;

        // Optional: only re-path if the pose changed significantly
        bool poseChanged = Vector3.Distance(lastLocalizedCamPos, camPos) > repathDistThreshold
                           || Quaternion.Angle(lastLocalizedCamRot, camRot) > repathAngleThreshold;

        if (statusText) statusText.text = $"Image detected: {targetName}";
        Debug.Log($"[Vuforia] Localized on '{targetName}' camPos={camPos}");

        if (isNavigating && poseChanged)
        {
            lastLocalizedCamPos = camPos;
            lastLocalizedCamRot = camRot;

            CalculateWallAvoidingPath();
            CreateGroundArrowsForPath();
        }
    }


    public void StartNavigationConfirmedByUI(string officeName)
    {
        _popupConfirmed = true;
        StartNavigationToOffice(officeName);
    }

    // Call this method from your Vuforia tracking scripts when tracking is lost
    public void OnImageTargetLost()
    {
        if (!useVuforiaPositioning) return;

        isImageTargetTracked = false;

        if (statusText != null && !isNavigating)
        {
            statusText.text = hasEverLocalized
                ? "Look at the sign to re-center"
                : "Point at the center sign to localize";
        }

        Debug.Log("Vuforia image target tracking lost");
    }

    // Get current position based on tracking method
    Vector3 GetCurrentPosition()
    {
        if (useVuforiaPositioning && isImageTargetTracked)
        {
            // Use last detected Vuforia position
            return lastDetectedPosition;
        }
        else if (playerCamera != null)
        {
            // Fallback to camera position
            return playerCamera.transform.position;
        }

        return Vector3.zero;
    }

    void CreateEnhancedArrowPrefab()
    {
        if (arrowPrefab == null)
        {
            arrowPrefab = new GameObject("ArrowPrefab");

            GameObject arrowShaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrowShaft.transform.SetParent(arrowPrefab.transform);
            arrowShaft.transform.localPosition = Vector3.zero;
            arrowShaft.transform.localScale = new Vector3(0.4f, 0.3f, 1.5f);

            GameObject arrowHead = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrowHead.transform.SetParent(arrowPrefab.transform);
            arrowHead.transform.localPosition = new Vector3(0, 0, 0.9f);
            arrowHead.transform.localScale = new Vector3(0.8f, 0.3f, 0.6f);

            // Use the URP-safe shared material
            var mat = arrowSharedMat ??= CreateArrowMaterial(arrowColor);
            Renderer s = arrowShaft.GetComponent<Renderer>();
            Renderer h = arrowHead.GetComponent<Renderer>();
            s.sharedMaterial = mat;
            h.sharedMaterial = mat;

            Destroy(arrowShaft.GetComponent<Collider>());
            Destroy(arrowHead.GetComponent<Collider>());

            arrowPrefab.SetActive(false);

            Debug.Log("New ArrowPrefab created with URP-safe material");
        }
    }

    void CreateEnhancedTargetMarker()
    {
        if (targetMarker == null)
        {
            targetMarker = new GameObject("TargetMarker");

            GameObject baseRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseRing.transform.SetParent(targetMarker.transform);
            baseRing.transform.localPosition = Vector3.zero;
            baseRing.transform.localScale = new Vector3(2.5f, 0.1f, 2.5f);

            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.transform.SetParent(targetMarker.transform);
            beacon.transform.localPosition = new Vector3(0, 2f, 0);
            beacon.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);

            if (targetMaterial == null)
            {
                targetMaterial = new Material(Shader.Find("Standard"));
                targetMaterial.color = targetColor;
                targetMaterial.EnableKeyword("_EMISSION");
                targetMaterial.SetColor("_EmissionColor", targetColor * 2f);
            }

            baseRing.GetComponent<Renderer>().material = targetMaterial;
            beacon.GetComponent<Renderer>().material = targetMaterial;

            Destroy(baseRing.GetComponent<Collider>());
            Destroy(beacon.GetComponent<Collider>());

            targetMarker.SetActive(false);
        }
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
        NavigationWaypoint[] allWaypoints = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);

        // 🔎 DEBUG: Dump all waypoints
        foreach (var wp in allWaypoints)
        {
            Debug.Log($"[WAYPOINT] {wp.waypointName} ({wp.waypointType}) at {wp.transform.position}");
        }

        List<Transform> foundDepartments = new List<Transform>();
        List<Transform> foundCorridors = new List<Transform>();
        List<string> foundNames = new List<string>();

        foreach (NavigationWaypoint waypoint in allWaypoints)
        {
            if (waypoint.waypointType == WaypointType.Office)
            {
                foundDepartments.Add(waypoint.transform);
                string cleanName = !string.IsNullOrEmpty(waypoint.officeName) ? waypoint.officeName : waypoint.waypointName;
                foundNames.Add(cleanName);
            }
            else if (waypoint.waypointType == WaypointType.Corridor || waypoint.waypointType == WaypointType.Junction)
            {
                foundCorridors.Add(waypoint.transform);
            }
        }

        departmentWaypoints = foundDepartments.ToArray();
        corridorWaypoints = foundCorridors.ToArray();
        departmentNames = foundNames.ToArray();

        Debug.Log($"Found {departmentWaypoints.Length} departments, {corridorWaypoints.Length} corridor waypoints");
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

    public void StartNavigationToOffice(string officeName)
    {
        // Ensure anchor is active and aligned
        AlignAnchorYawToCamera();

        // TEMPORARY: Skip popup and Firebase check for testing
        _popupConfirmed = true;
        hasEverLocalized = true;
        Debug.Log($"[NAV] StartNavigationToOffice called for: {officeName}");

        if (enforcePopupFirst && !_popupConfirmed)
        {
            Debug.Log("[NAV] Blocked - popup confirmation required");
            var ui = FindFirstObjectByType<ManilaServeUI>();
            if (ui != null) ui.ShowOfficeInfoPopup(officeName);
            return;
        }
        _popupConfirmed = false;

        if (useVuforiaPositioning && requireInitialLocalization && !hasEverLocalized)
        {
            Debug.LogWarning("[NAV] Blocked - Vuforia localization required");
            if (statusText != null)
                statusText.text = "Point the camera at the center sign to localize first";
            return;
        }

        Debug.Log($"[NAV] Finding waypoint for: {officeName}");
        Transform targetWaypoint = FindTargetWaypointAdvanced(officeName);

        if (targetWaypoint == null)
        {
            Debug.LogError($"[NAV] Waypoint not found for: {officeName}");
            return;
        }

        Debug.Log($"[NAV] ✓ Waypoint found at: {targetWaypoint.position}");
        targetDestination = targetWaypoint.position;
        originalTargetPosition = targetDestination;
        currentDestination = officeName;
        isNavigating = true;

        Debug.Log("[NAV] Calculating path...");
        CalculateWallAvoidingPath();

        Debug.Log($"[NAV] Path calculated with {currentPath.Count} points");
        CreateGroundArrowsForPath();

        Debug.Log($"[NAV] Arrows created: {pathArrows.Count} arrows");
        ShowEnhancedTargetMarker();

        if (enforcePopupFirst && !_popupConfirmed)
        {
            var ui = FindFirstObjectByType<ManilaServeUI>();
            if (ui != null) ui.ShowOfficeInfoPopup(officeName);
            Debug.Log($"Blocked direct navigation to '{officeName}' — popup confirmation required");
            return;
        }
        _popupConfirmed = false; // reset after a confirmed start

        // Check if Vuforia positioning is required but not active
        if (useVuforiaPositioning && requireInitialLocalization && !hasEverLocalized)
        {
            if (statusText != null)
                statusText.text = "Point the camera at the center sign to localize first";
            Debug.LogWarning("Vuforia required for initial localization: no target seen yet");
            return;
        }

      targetWaypoint = FindTargetWaypointAdvanced(officeName);

        if (targetWaypoint == null)
        {
            // ✅ Look in cached dictionary instead of GameObject.Find
            if (!officeLookup.TryGetValue(officeName, out GameObject targetObject) || targetObject == null)
            {
                Debug.LogError($"Office {officeName} not found in cached lookup!");
                return;
            }

            NavigationWaypoint waypoint = targetObject.GetComponent<NavigationWaypoint>();
            if (waypoint == null)
            {
                waypoint = targetObject.AddComponent<NavigationWaypoint>();
                waypoint.waypointName = officeName;
                waypoint.officeName = officeName;
                waypoint.waypointType = WaypointType.Office;
            }

            targetWaypoint = targetObject.transform;
        }

        if (targetWaypoint != null)
        {
            targetDestination = targetWaypoint.position;
            originalTargetPosition = targetDestination;
            currentDestination = officeName;
            isNavigating = true;

            // Determine starting floor (hide the other floor)
            if (GetCurrentPosition().y > 5f)
            {
                currentFloor = "Second";
                SetFloorVisible(groundFloor, false);
                SetFloorVisible(secondFloor, true);
            }
            else
            {
                currentFloor = "Ground";
                SetFloorVisible(groundFloor, true);
                SetFloorVisible(secondFloor, false);
            }

            CalculateWallAvoidingPath();
            CreateGroundArrowsForPath();
            ShowEnhancedTargetMarker();

            // ADDED: Initialize direction tracking
            currentPathIndex = 0;
            lastPlayerPosition = GetCurrentPosition();

            if (statusText != null)
                statusText.text = $"Navigating to {currentDestination}";
            if (navigateButton != null)
                navigateButton.gameObject.SetActive(false);
            if (stopButton != null)
                stopButton.gameObject.SetActive(true);

            string trackingMode = useVuforiaPositioning ? "Vuforia" : "Camera";
            Debug.Log($"Navigation started to {currentDestination} using {trackingMode} tracking");
        }
    }


    Transform FindTargetWaypointAdvanced(string officeName)
    {
        // Use Resources to find ALL waypoints including inactive ones
        NavigationWaypoint[] allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();

        foreach (NavigationWaypoint waypoint in allWaypoints)
        {
            // Skip prefabs
            if (waypoint.gameObject.scene.name == null) continue;

            if (!string.IsNullOrEmpty(waypoint.officeName) && waypoint.officeName.Equals(officeName, System.StringComparison.OrdinalIgnoreCase))
            {
                return waypoint.transform;
            }
        }

        string searchName = officeName.ToLower().Replace(" ", "").Replace("-", "");

        foreach (NavigationWaypoint waypoint in allWaypoints)
        {
            // Skip prefabs
            if (waypoint.gameObject.scene.name == null) continue;

            if (!string.IsNullOrEmpty(waypoint.waypointName))
            {
                string cleanWaypointName = waypoint.waypointName.ToLower().Replace(" ", "").Replace("-", "").Replace("_", "");
                if (cleanWaypointName.Contains(searchName) || searchName.Contains(cleanWaypointName))
                {
                    return waypoint.transform;
                }
            }

            string cleanGameObjectName = waypoint.gameObject.name.ToLower().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (cleanGameObjectName.Contains(searchName) || searchName.Contains(cleanGameObjectName))
            {
                return waypoint.transform;
            }
        }

        return null;
    }

    void CalculateWallAvoidingPath()
    {
        currentPath.Clear();
        Vector3 startPos = GetGroundPosition(GetCurrentPosition());
        Vector3 targetPos = GetGroundPosition(targetDestination);

        Debug.Log("=== CALCULATING ADVANCED WALL-AVOIDING PATH ===");
        Debug.Log($"From: {startPos} To: {targetPos}");

        NavigationWaypoint[] allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
            .Where(w => w.gameObject.scene.name != null) // Exclude prefabs
            .ToArray();

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
                        floorManager.CrossFadeFloors(goingUp);
                    }


                    if (stairToTarget.Count > 0) currentPath.AddRange(stairToTarget.Skip(1));

                    Debug.Log($"✅ Cross-floor path created with {currentPath.Count} points");
                    return;
                }
            }
        }

        // 🔹 Same floor → allow direct line ONLY if you explicitly enable it
        //if (allowDirectStraightPath && IsPathClear(startPos, targetPos))
        //{
        //    currentPath.Add(startPos);
        //    currentPath.Add(targetPos);
        //    return;
        //}

        currentPath = FindComplexPath(startPos, targetPos, allWaypoints);

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
        List<Vector3> path = new List<Vector3>();

        // Step 1: get nearest waypoints
        NavigationWaypoint startWP = FindNearestWaypoint(start, waypoints);
        NavigationWaypoint targetWP = FindNearestWaypoint(target, waypoints);

        if (startWP == null || targetWP == null)
        {
            Debug.LogError("Could not find start/target waypoint!");
            return new List<Vector3> { start, target };
        }

        // Step 2: Prepare search structures
        HashSet<NavigationWaypoint> visited = new HashSet<NavigationWaypoint>();
        Dictionary<NavigationWaypoint, NavigationWaypoint> cameFrom = new Dictionary<NavigationWaypoint, NavigationWaypoint>();

        Dictionary<NavigationWaypoint, float> gScore = new Dictionary<NavigationWaypoint, float>();
        Dictionary<NavigationWaypoint, float> fScore = new Dictionary<NavigationWaypoint, float>();

        foreach (var wp in waypoints)
        {
            gScore[wp] = Mathf.Infinity;
            fScore[wp] = Mathf.Infinity;
        }

        gScore[startWP] = 0f;
        fScore[startWP] = Vector3.Distance(startWP.transform.position, target);

        List<NavigationWaypoint> openSet = new List<NavigationWaypoint> { startWP };

        // Step 3: A* loop
        while (openSet.Count > 0)
        {
            NavigationWaypoint current = openSet.OrderBy(wp => fScore[wp]).First();

            if (current == targetWP)
            {
                return ReconstructPath(cameFrom, current, start, target);
            }

            openSet.Remove(current);
            visited.Add(current);

            if (current.connectedWaypoints == null) continue;

            foreach (var neighbor in current.connectedWaypoints)
            {
                if (neighbor == null || visited.Contains(neighbor)) continue;

                // Graph-authoritative: skip per-edge LOS/geometry checks in neighbor expansion
                if (!graphIsAuthoritative)
                {
                    // legacy safety check (keep OFF when your graph is clean)
                    if (!IsPathClear(current.transform.position, neighbor.transform.position))
                        continue;
                }

                float tentativeG = gScore[current] + Vector3.Distance(current.transform.position, neighbor.transform.position);

                if (tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;

                    // weighted heuristic (your existing behavior)
                    float distanceToTarget = Vector3.Distance(neighbor.transform.position, target);
                    float totalScore = tentativeG + distanceToTarget * 1.2f;

                    // preference for corridor/junction nodes
                    if (neighbor.waypointType == WaypointType.Corridor || neighbor.waypointType == WaypointType.Junction)
                    {
                        totalScore *= 0.8f;
                    }

                    fScore[neighbor] = totalScore;

                    if (!openSet.Contains(neighbor))
                        openSet.Add(neighbor);
                }
            }
        }

        Debug.LogWarning("⚠ No valid path found — falling back to straight line");
        return new List<Vector3> { start, target };
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


    private NavigationWaypoint FindNearestAccessibleWaypoint(Vector3 position, NavigationWaypoint[] waypoints)
    {
        NavigationWaypoint nearestClear = null;
        NavigationWaypoint nearestAny = null;
        float minClearDist = Mathf.Infinity;
        float minAnyDist = Mathf.Infinity;

        foreach (var wp in waypoints)
        {
            float dist = Vector3.Distance(position, wp.transform.position);

            // Track absolute nearest
            if (dist < minAnyDist)
            {
                minAnyDist = dist;
                nearestAny = wp;
            }

            // Track nearest with wall-clear line of sight
            if (IsPathClear(position, wp.transform.position) && dist < minClearDist)
            {
                minClearDist = dist;
                nearestClear = wp;
            }
        }

        // Prefer clear waypoint, otherwise fallback to any
        return nearestClear != null ? nearestClear : nearestAny;
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
        if (path == null || path.Count <= 2) return;

        Debug.Log("Optimizing path (graph-authoritative smoothing)...");
        bool optimized = true;

        while (optimized && path.Count > 2)
        {
            optimized = false;

            for (int i = 0; i < path.Count - 2; i++)
            {
                Vector3 from = GetGroundPosition(path[i]);
                Vector3 to = GetGroundPosition(path[i + 2]);

                // Only remove the middle point if a direct connection is safe
                bool losOK = !useLOSForSmoothing || IsPathClear(from, to);
                if (losOK)
                {
                    path.RemoveAt(i + 1);
                    optimized = true;
                    break;
                }
            }
        }

        Debug.Log($"✅ Path optimized to {path.Count} points");
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
        if (!useGroundDetection)
        {
            // Snap to known floor levels
            if (Mathf.Abs(worldPos.y - 0f) < 3f)
                return new Vector3(worldPos.x, groundOffset, worldPos.z); // ground floor
            else if (Mathf.Abs(worldPos.y - 10f) < 3f)
                return new Vector3(worldPos.x, 10f + groundOffset, worldPos.z); // second floor
            else
                return new Vector3(worldPos.x, worldPos.y + groundOffset, worldPos.z); // fallback
        }

        // Raycast to ground collider
        RaycastHit hit;
        Vector3 rayStart = worldPos + Vector3.up * 2f;

        if (Physics.Raycast(rayStart, Vector3.down, out hit, groundRayDistance, groundLayerMask))
        {
            return hit.point + Vector3.up * groundOffset;
        }

        // Fallback
        return new Vector3(worldPos.x, worldPos.y + groundOffset, worldPos.z);
    }

    void CreateGroundArrowsForPath()
    {
        EnsureActiveChain();
        ClearPathArrows();
        ClearFlowLine();

        if (currentPath == null || currentPath.Count < 2) return;

        Debug.Log($"[ARROWS] Creating arrows for path with {currentPath.Count} points");

        switch (pathStyle)
        {
            case PathVisualStyle.FlowLine:
                BuildFlowLineFromCurrentPath();
                break;

            case PathVisualStyle.Arrows:
            case PathVisualStyle.Dots:
                for (int i = 0; i < currentPath.Count - 1; i++)
                {
                    Debug.Log($"[ARROWS] Segment {i}: from {currentPath[i]} to {currentPath[i + 1]}");
                    CreateGroundArrowsForSegment(currentPath[i], currentPath[i + 1]);
                }
                Debug.Log($"[ARROWS] Total arrows created: {pathArrows.Count}");
                break;
        }
    }

    void CreateGroundArrowsForSegment(Vector3 start, Vector3 end)
    {
        Vector3 seg = end - start;
        float segmentDistance = seg.magnitude;
        if (segmentDistance < 0.01f) return;

        // Make sure we have a parent under ContentAnchor
        EnsurePathRoot(); // creates "PathRoot" once and parents it to contentAnchor

        int numArrows = Mathf.Max(1, Mathf.FloorToInt(segmentDistance / arrowSpacing));

        // Height change → stair segment
        bool isStairSegment = Mathf.Abs(start.y - end.y) > 2f;

        // Precompute stable rotations
        Vector3 flatDir = new Vector3(seg.x, 0f, seg.z);
        if (flatDir.sqrMagnitude < 1e-4f) flatDir = Vector3.forward; // avoid zero vector
        Quaternion flatRot = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
        Quaternion stairRot = Quaternion.LookRotation(seg.normalized, Vector3.up);

        for (int i = 0; i < numArrows; i++)
        {
            float t = (i + 1f) / (numArrows + 1f);
            Vector3 pos = Vector3.Lerp(start, end, t);

            // Grounding
            pos = GetGroundPosition(pos);

            // Force exact floor if requested
            if (lockArrowsToFloor)
            {
                float floorY = (currentFloor == "Second" ? secondFloorY : groundFloorY) + groundOffset;
                pos.y = floorY;
            }
            else if (!isStairSegment)
            {
                // your original fixed heights
                if (Mathf.Abs(start.y - 0f) < 3f) pos.y = groundOffset;
                else if (Mathf.Abs(start.y - 10f) < 3f) pos.y = 10f + groundOffset;
            }

            // Skip if inside a wall
            if (enableWallDetection && Physics.CheckSphere(pos + Vector3.up * 0.3f, 0.1f, wallLayerMask))
                continue;

            // Skip arrows too close to camera
            if (arCamera && Vector3.Distance(pos, arCamera.transform.position) < arrowStartGap)
                continue;

            // Pick rotation (declare once, assign here)
            Quaternion rot = lockArrowsToFloor ? flatRot : (isStairSegment ? stairRot : flatRot);

            // Instantiate
            var arrowGo = Instantiate(arrowPrefab, pos, rot, pathRoot);

            // Distance-based base scale (then clamped)
            float baseScale = arrowSize;
            if (arCamera)
            {
                float d = Vector3.Distance(pos, arCamera.transform.position);
                float blend = Mathf.InverseLerp(0f, Mathf.Max(0.01f, arrowScaleDistance), d);
                float distScale = Mathf.Lerp(arrowNearScale, arrowFarScale, blend);
                baseScale *= distScale;
            }
            baseScale = Mathf.Clamp(baseScale, 0.05f, 0.8f);
            arrowGo.transform.localScale = Vector3.one * baseScale;

            // Remember per‑arrow base scale for pulsing
            var ai = arrowGo.AddComponent<ArrowInstance>();
            ai.baseScale = baseScale;
            ai.phase = Random.value * 6.28318f;

            // Material
            var mat = arrowSharedMat ??= CreateArrowMaterial(arrowColor);
            foreach (var r in arrowGo.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;

            // Force activation just in case
            if (!arrowGo.activeSelf) arrowGo.SetActive(true);

            pathArrows.Add(arrowGo);
        }

    }
        void AnimateArrowPath()
    {
        if (!arrowPulse || pathArrows == null || pathArrows.Count == 0) return;

        float t = Time.time * arrowPulseSpeed;

        for (int i = 0; i < pathArrows.Count; i++)
        {
            var a = pathArrows[i];
            if (!a) continue;

            var ai = a.GetComponent<ArrowInstance>();
            if (ai == null) continue;

            float wave = 1f + Mathf.Sin(t + ai.phase) * arrowPulseAmplitude;
            float s = ai.baseScale * wave;
            a.transform.localScale = Vector3.one * s;
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

    void EnsurePathRoot()
    {
        if (pathRoot != null) return;
        EnsureActiveChain(); // make sure parent anchor is on
        var go = new GameObject("PathRoot");
        if (contentAnchor) go.transform.SetParent(contentAnchor, false);
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
        flowLine.alignment = LineAlignment.View; // ok to keep

        // Narrower width + fade in near the camera
        flowLine.widthMultiplier = lineWidth; // e.g., 0.03–0.06
        flowLine.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0f),             // start invisible
            new Keyframe(0.1f, lineWidth),    // fade in quickly
            new Keyframe(1f, lineWidth)
        );

        if (flowLineMaterial) flowLine.material = flowLineMaterial;
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

    void BuildFlowLineFromCurrentPath()
    {
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


    void ShowEnhancedTargetMarker()
    {
        Vector3 groundTargetPos = GetGroundPosition(originalTargetPosition);
        targetMarker.transform.position = groundTargetPos;
        targetMarker.SetActive(true);
        targetBobOffset = 0f;
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
        isNavigating = false;
        ClearPathArrows();

        if (targetMarker != null)
        {
            targetMarker.SetActive(false);
        }

        currentPath.Clear();

        // ADDED: Clear direction UI if it exists
        if (directionText != null)
            directionText.text = "";
        if (distanceText != null)
            distanceText.text = "";

        if (statusText != null)
        {
            if (useVuforiaPositioning)
            {
                statusText.text = isImageTargetTracked ? "Ready to navigate" : "Point camera at office sign";
            }
            else
            {
                statusText.text = "Navigation stopped";
            }
        }
        if (navigateButton != null)
            navigateButton.gameObject.SetActive(true);
        if (stopButton != null)
            stopButton.gameObject.SetActive(false);

        SetFloorVisible(secondFloor, false);
        ShowOnlyFloor("Ground");  // default to ground floor view


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
        if (isNavigating)
        {
            AnimateEnhancedTargetMarker();
            UpdateDirections();

            if (pathStyle == PathVisualStyle.Arrows)
            {
                UpdateArrowBaseScalesIfEnabled();
                AnimateArrowPath();
            }

            UpdateDirections();

            Vector3 currentPos = GetCurrentPosition();
            float distance = Vector3.Distance(currentPos, targetDestination);

            if (distance < 2.0f)
            {
                // ADDED: Show arrival notification if UI exists
                if (directionText != null)
                    directionText.text = "🎉 You have arrived!";
                if (distanceText != null)
                    distanceText.text = "Welcome!";

                ManilaServePrototypeUI ui = FindFirstObjectByType<ManilaServePrototypeUI>();
                if (ui != null)
                {
                    ui.ShowArrivalConfirmation(currentDestination);
                }

                StopNavigation();
            }

            // Recalculate path if user moves significantly
            if (Time.frameCount % 60 == 0) // Every second
            {
                Vector3 pathStartPos = GetGroundPosition(GetCurrentPosition());
                if (currentPath.Count > 0)
                {
                    float distanceFromPath = Vector3.Distance(pathStartPos, currentPath[0]);
                    if (distanceFromPath > 3f) // If user moved 3m+ from start of path
                    {
                        Debug.Log("User moved significantly - recalculating path");
                        CalculateWallAvoidingPath();
                        CreateGroundArrowsForPath();
                        currentPathIndex = 0; // Reset direction tracking
                    }
                }
            }

            // 🔹 NEW: Handle floor switching via stair waypoints
            NavigationWaypoint nearest = GetNearestWaypoint();
            if (nearest != null && nearest.waypointType == WaypointType.Stairs)
            {
                if (currentFloor == "Ground" && nearest.transform.position.y > 5f) // going up
                {
                    currentFloor = "Second";
                    SetFloorVisible(groundFloor, false);
                    SetFloorVisible(secondFloor, true);
                    Debug.Log("Switched to SECOND floor");
                }
                else if (currentFloor == "Second" && nearest.transform.position.y <= 5f) // going down
                {
                    currentFloor = "Ground";
                    SetFloorVisible(groundFloor, true);
                    SetFloorVisible(secondFloor, false);
                    Debug.Log("Switched to GROUND floor");
                }
            }
        }
        if (pathStyle == PathVisualStyle.FlowLine && flowLine && flowLine.material)
        {
            flowT += Time.deltaTime * flowSpeed;
            if (flowLine.material.HasProperty("_BaseMap"))
        {
            // move texture to create the flow
            var off = flowLine.material.GetTextureOffset("_BaseMap");
            flowLine.material.SetTextureOffset("_BaseMap", new Vector2(-flowT, off.y));
            var scale = flowLine.material.GetTextureScale("_BaseMap");
            flowLine.material.SetTextureScale("_BaseMap", new Vector2(dashTiling, scale.y));
        }
        else
        {
            // fallback for legacy shaders
            var off = flowLine.material.mainTextureOffset;
            flowLine.material.mainTextureOffset = new Vector2(-flowT, off.y);
            var scale = flowLine.material.mainTextureScale;
            flowLine.material.mainTextureScale = new Vector2(dashTiling, scale.y);
        }
    }
}



    void AnimateEnhancedTargetMarker()
    {
        if (targetMarker != null && targetMarker.activeInHierarchy)
        {
            targetBobOffset += Time.deltaTime * bobSpeed;
            float bobY = Mathf.Sin(targetBobOffset) * bobHeight;

            Vector3 groundPos = GetGroundPosition(originalTargetPosition);
            targetMarker.transform.position = groundPos + new Vector3(0, bobY, 0);

            targetMarker.transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
        }
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

        Renderer[] renderers = floor.GetComponentsInChildren<Renderer>();
        float start = fadeIn ? 0f : 1f;
        float end = fadeIn ? 1f : 0f;
        float t = 0f;

        if (fadeIn) floor.SetActive(true);

        // 🔹 Step 1: Switch to Fade mode for all materials
        foreach (var r in renderers)
        {
            foreach (var mat in r.materials)
            {
                mat.SetFloat("_Mode", 2); // 2 = Fade
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
        }

        // 🔹 Step 2: Fade loop
        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(start, end, t / duration);
            foreach (var r in renderers)
            {
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.color;
                        c.a = alpha;
                        mat.color = c;
                    }
                }
            }
            yield return null;
        }

        // 🔹 Step 3: Restore state
        if (fadeIn)
        {
            // Restore to Opaque after fade in
            foreach (var r in renderers)
            {
                foreach (var mat in r.materials)
                {
                    mat.SetFloat("_Mode", 0); // 0 = Opaque
                    mat.SetInt("_SrcBlend", (int)BlendMode.One);
                    mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = -1;

                    if (mat.HasProperty("_Color"))
                    {
                        Color c = mat.color;
                        c.a = 1f;
                        mat.color = c;
                    }
                }
            }
        }
        else
        {
            // Disable floor after fading out
            floor.SetActive(false);
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
        if (!contentAnchor || !arCamera) return;
        var yawOnly = Quaternion.Euler(0f, arCamera.transform.eulerAngles.y, 0f);
        contentAnchor.rotation = yawOnly;
        Debug.Log("[ANCHOR] Yaw aligned to camera");
    }

    public List<string> GetAllOfficeNames()
    {
        return new List<string>(officeLookup.Keys);
    }


    NavigationWaypoint GetNearestWaypoint()
    {
        NavigationWaypoint[] all = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        NavigationWaypoint nearest = null;
        float minDist = Mathf.Infinity;
        Vector3 playerPos = GetCurrentPosition();

        foreach (var wp in all)
        {
            float dist = Vector3.Distance(playerPos, wp.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = wp;
            }
        }

        return nearest;
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

    class ArrowInstance : MonoBehaviour
    {
        public float baseScale;  // world scale to stick to
        public float phase;      // random phase so arrows don’t pulse in sync
    }

    void EnsureActiveChain()
    {
        if (contentAnchor && !contentAnchor.gameObject.activeSelf)
            contentAnchor.gameObject.SetActive(true);
        if (pathRoot && !pathRoot.gameObject.activeSelf)
            pathRoot.gameObject.SetActive(true);
    }
}
