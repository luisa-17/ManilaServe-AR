using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.RegularExpressions;
using UnityEngine.Events;


public class ManilaServeUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown officeDropdown;
    public Button navigateButton;
    public Button stopButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI directionText;
    public TextMeshProUGUI distanceText;

    [Header("Navigation System")]
    public SmartNavigationSystem nav; 

    [Header("Firebase Integration")]
    public FirebaseOfficeManager firebaseManager;
    private Dictionary<string, FirebaseOfficeManager.Office> firebaseOffices;

    [Header("Filters (Optional)")]
    public TMP_InputField officeSearchInput;
    public Toggle currentFloorOnlyToggle;
    
    [Header("Location Display")]
    public TextMeshProUGUI currentLocationLabel;

    [Header("Arrival Notification")]
    public GameObject arrivalPanel;
    public TextMeshProUGUI arrivalText;
    public Button arrivalOKButton;
    public float arrivalDisplayTime = 3f;
    public CanvasGroup arrivalCanvasGroup;

    // ⭐ NEW: Hybrid Location System
    [Header("Location Selection Mode")]
    public Toggle useCurrentLocationToggle;      // Radio button: "Use My Current Location"
    public Toggle useSpecificOfficeToggle;       // Radio button: "I'm At A Specific Office"
    public GameObject currentLocationPanel;      // Panel containing auto-detected label
    public GameObject specificOfficePanel;       // Panel containing office dropdown

    [Header("Auto-Detection Display")]
    public TextMeshProUGUI autoDetectedLocationLabel; // Shows "📍 Near: EDP Office"

    [Header("Manual Office Selection")]
    public TMP_Dropdown currentOfficeDropdown;   // Dropdown for manual selection
    private string selectedCurrentOffice = "";   // Selected office name

    private bool useAutoDetection = true;        // Which mode is active

    // Entrance display options (you can tweak in Inspector)
    [SerializeField] string entranceDisplayName = "City Hall Entrance";
    [SerializeField, Range(0.5f, 6f)] float entranceNearRadiusMeters = 2.5f;

    // Office “near” radius (replace your hardcoded 2f with this)
    [SerializeField, Range(1f, 10f)] float officeNearRadiusMeters = 5f;

    [Header("Navigation Status")]
    public GameObject navigationStatusPanel;
    public TextMeshProUGUI navigationStatusText;

    SmartNavigationSystem navigationSystem;
    string selectedOffice = "";
    bool isNavigationActive = false;

    private Dictionary<string, GameObject> officeLookup = new Dictionary<string, GameObject>();


    [Header("Dropdown Styling")]
    public Sprite customDropdownSprite;
    public Color customBackgroundColor = Color.white;
    public Color customTextColor = Color.black;
    public Sprite customArrowSprite;
    public Color customArrowColor = Color.black;
    public Color customDropdownBgColor = Color.white;
    public Color customItemColor = Color.white;
    public Color customItemTextColor = Color.black;
    public TMP_FontAsset customFont;
    public float customFontSize = 30f;

    [Header("Search/Select Mode Toggle")]
    public Button searchModeButton;
    public Button selectModeButton;
    public GameObject searchPanel;
    public GameObject selectPanel;
    public TMP_InputField directSearchInput;
    public Button executeSearchButton;

    [Header("Mode Button Styling")]
    public Color activeModeColor = new Color(0.2f, 0.7f, 1f, 1f);
    public Color inactiveModeColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    bool isSearchMode = false;

    [Header("Mode Button Custom Sprites")]
    public Sprite searchButtonNormalSprite;
    public Sprite searchButtonActiveSprite;
    public Sprite searchButtonHoverSprite;
    public Sprite searchButtonPressedSprite;
    public Sprite selectButtonNormalSprite;
    public Sprite selectButtonActiveSprite;
    public Sprite selectButtonHoverSprite;
    public Sprite selectButtonPressedSprite;

    [Header("Search Results UI")]
    public GameObject searchResultsPanel;
    public Transform searchResultsContent;
    public GameObject searchResultItemPrefab;
    public int maxSearchResults = 5;
    public float searchResultFontSize = 30f; // Add this field


    [System.Serializable]
    public class OfficeInfo
    {
        public string officeName;
        public string roomNumber;
        public string directory;
        public string[] services;

        public OfficeInfo(string name, string room, string dir, string[] serviceList)
        {
            officeName = name;
            roomNumber = room;
            directory = dir;
            services = serviceList;
        }
    }

    [Header("Office Info Popup")]
    public GameObject officeInfoPopup;
    public TextMeshProUGUI popupOfficeTitleText;
    public TextMeshProUGUI popupRoomNumberText;  // Change from popupRoomInfo
    public TextMeshProUGUI popupDirectoryText;    // Change from popupDirectoryInfo
    public TextMeshProUGUI popupServicesText;     // Change from popupServicesInfo
    public Button popupNavigateButton;
    public Button popupCloseButton;

    [Header("Office Database")]
    public OfficeInfo[] officeDatabase = new OfficeInfo[]
    {
    new OfficeInfo("Bureau of Permits", "Room 101", "Ground Floor - East Wing", new string[] { "Building Permits", "Business Permits", "Construction Permits" }),
    new OfficeInfo("Cash Division", "Room 205", "Second Floor - Finance Wing", new string[] { "Payment Processing", "Cash Collections", "Receipt Issuance" }),
    new OfficeInfo("City Treasurer", "Room 203", "Second Floor - Finance Wing", new string[] { "Tax Collection", "Municipal Funds", "Financial Reports" }),
        // Add more offices here
    };


    // Destination Office dropdown (Select an Office)
    private readonly HashSet<int> nonSelectableIndices = new HashSet<int>();
    private int lastValidOfficeIndex = 0;
    private bool suppressOfficeSelection = false;

    // Current Office dropdown (Select Current Office)
    private readonly HashSet<int> currentHeaderIndices = new HashSet<int>();
    private int lastValidCurrentIndex = 0;
    private bool suppressCurrentSelection = false;


    readonly Dictionary<string, NavigationWaypoint> waypointCache = new Dictionary<string, NavigationWaypoint>();
    const float FloorThresholdY = 5f;


    Color headerTextColor = new Color(1f, 1f, 1f, 0.6f); // dimmed
    Color headerBgColor = new Color(0f, 0f, 0f, 0f); // transparent (or a subtle tint)

    // Cache for search functionality
    private List<string> allAvailableOffices = new List<string>();
    private List<string> cachedOfficeNames = new List<string>();

    private Dictionary<string, string> officeNameMappings;

    void Awake()
    {
        // Auto-find if not assigned
        if (!officeDropdown) officeDropdown = GameObject.Find("OfficeDropdown")?.GetComponent<TMP_Dropdown>();
        if (!navigateButton) navigateButton = GameObject.Find("NavigateButton")?.GetComponent<Button>();
        if (!stopButton) stopButton = GameObject.Find("StopButton")?.GetComponent<Button>();
        if (!statusText) statusText = GameObject.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        if (!directionText) directionText = GameObject.Find("DirectionText")?.GetComponent<TextMeshProUGUI>();
        if (!distanceText) distanceText = GameObject.Find("DistanceText")?.GetComponent<TextMeshProUGUI>();

        if (!officeSearchInput) officeSearchInput = GameObject.Find("OfficeSearchInput")?.GetComponent<TMP_InputField>();
        if (!currentFloorOnlyToggle) currentFloorOnlyToggle = GameObject.Find("FilterCurrentFloorToggle")?.GetComponent<Toggle>();

        // ⭐ NEW: Auto-find hybrid location system elements
        if (!useCurrentLocationToggle) useCurrentLocationToggle = GameObject.Find("UseCurrentLocationToggle")?.GetComponent<Toggle>();
        if (!useSpecificOfficeToggle) useSpecificOfficeToggle = GameObject.Find("UseSpecificOfficeToggle")?.GetComponent<Toggle>();
        if (!currentLocationPanel) currentLocationPanel = GameObject.Find("CurrentLocationPanel");
        if (!specificOfficePanel) specificOfficePanel = GameObject.Find("SpecificOfficePanel");
        if (!autoDetectedLocationLabel) autoDetectedLocationLabel = GameObject.Find("AutoDetectedLocationLabel")?.GetComponent<TextMeshProUGUI>();
        if (!currentOfficeDropdown) currentOfficeDropdown = GameObject.Find("Current officedropdown")?.GetComponent<TMP_Dropdown>();
    }

    void Start()
    {
        // ✅ ADD THIS at the beginning:
        Debug.Log("[UI] === ManilaServeUI Start ===");

        if (nav == null)
        {
            nav = FindObjectOfType<SmartNavigationSystem>();
            if (nav != null)
                Debug.Log($"[UI] ✅ Found SmartNavigationSystem on: '{nav.gameObject.name}'");
            else
                Debug.LogError("[UI] ❌ SmartNavigationSystem NOT FOUND!");
        }

        SetupDropdowns();

        navigationSystem = FindFirstObjectByType<SmartNavigationSystem>();
        InitializeOfficeNameMappings();

        // Wire UI
        if (navigateButton)
        {
            navigateButton.onClick.RemoveAllListeners();
            navigateButton.onClick.AddListener(OnNavigateClicked);
        }
        if (stopButton)
        {
            stopButton.onClick.RemoveAllListeners();
            stopButton.onClick.AddListener(OnStopClicked);
            stopButton.gameObject.SetActive(false);
        }
        if (officeDropdown)
        {
            officeDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
            officeDropdown.onValueChanged.AddListener(OnOfficeSelected);
        }

        // ⭐ NEW: Setup location mode toggles (REPLACES old toggle setup)
        if (useCurrentLocationToggle)
        {
            useCurrentLocationToggle.onValueChanged.RemoveAllListeners();
            useCurrentLocationToggle.onValueChanged.AddListener(OnUseCurrentLocationToggled);
            useCurrentLocationToggle.isOn = true; // Default to auto-detection
        }

        if (useSpecificOfficeToggle)
        {
            useSpecificOfficeToggle.onValueChanged.RemoveAllListeners();
            useSpecificOfficeToggle.onValueChanged.AddListener(OnUseSpecificOfficeToggled);
            useSpecificOfficeToggle.isOn = false;
        }

        // Setup current office dropdown
        if (currentOfficeDropdown)
        {
            currentOfficeDropdown.onValueChanged.RemoveAllListeners();
            currentOfficeDropdown.onValueChanged.AddListener(OnCurrentOfficeSelected);
        }

        // ⭐ Show correct panel based on default mode
        UpdateLocationModeUI();

        // Setup search/filters
        if (officeSearchInput)
        {
            officeSearchInput.onValueChanged.RemoveAllListeners();
            officeSearchInput.onValueChanged.AddListener(_ => PopulateOfficeDropdown());
        }
        if (currentFloorOnlyToggle)
        {
            currentFloorOnlyToggle.onValueChanged.RemoveAllListeners();
            currentFloorOnlyToggle.onValueChanged.AddListener(_ => PopulateOfficeDropdown());
        }

        if (navigationStatusPanel) navigationStatusPanel.SetActive(true);

        SetupModeButtons();
        SetupCustomButtonSprites();

        ShowLoadingPlaceholder();
        StartCoroutine(WaitAndPopulate());
        UpdateNavigationUI();

        CustomizeDropdownAppearance();
        StartCoroutine(WaitForFirebaseData());
        FirebaseOfficeManager.OnOfficeDataLoaded += OnFirebaseDataLoaded;

        Debug.Log("ManilaServeUI initialized with hybrid location mode");
    }
   
    
    IEnumerator WaitForFirebaseData()
    {
        float timeout = 10f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (firebaseManager != null && firebaseManager.GetAllOfficeNames().Count > 0)
            {
                Debug.Log($"✓ Firebase loaded: {firebaseManager.GetAllOfficeNames().Count} offices");
                yield break;
            }
            elapsed += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (firebaseManager == null || firebaseManager.GetAllOfficeNames().Count == 0)
        {
            Debug.LogError("✗ Firebase failed to load offices after 10 seconds!");
        }
    }

    IEnumerator WaitAndPopulate()
    {
        // Wait for SmartNavigationSystem
        float timeout = 4f, t = 0f;
        while (!navigationSystem && t < timeout)
        {
            navigationSystem = FindFirstObjectByType<SmartNavigationSystem>();
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Wait a bit for offices to be cached
        t = 0f;
        while (navigationSystem && navigationSystem.GetAllOfficeNames().Count == 0 && t < timeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        PopulateOfficeDropdown();
    }

    // ⭐ ADD THESE NEW METHODS:

    void OnUseCurrentLocationToggled(bool isOn)
    {
        if (!isOn) return; // Only act when turning ON

        useAutoDetection = true;
        selectedCurrentOffice = ""; // Clear manual selection

        if (useSpecificOfficeToggle) useSpecificOfficeToggle.isOn = false;

        UpdateLocationModeUI();
        UpdateNavigationUI();

        Debug.Log("Switched to: Use Current Location (auto-detection)");
    }

    void OnUseSpecificOfficeToggled(bool isOn)
    {
        if (!isOn) return;

// Close any open dropdown list before switching UI
ForceCloseCurrentOfficeDropdown();

        useAutoDetection = false;
        if (useCurrentLocationToggle) useCurrentLocationToggle.isOn = false;

        UpdateLocationModeUI();
        UpdateNavigationUI();

        Debug.Log("Switched to: Use Specific Office (manual selection)");
    }

    void ForceCloseCurrentOfficeDropdown()
    {
        if (!currentOfficeDropdown) return;

// Ask TMP to close
currentOfficeDropdown.Hide();

        // Destroy any stray runtime list instance
        var root = currentOfficeDropdown.transform.root;
        foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == "TMP Dropdown List" || rt.name == "Dropdown List")
                Destroy(rt.gameObject);
    }

    void UpdateLocationModeUI()
    {
        if (currentLocationPanel)
        {
            currentLocationPanel.SetActive(useAutoDetection);
        }

        if (specificOfficePanel)
        {
            specificOfficePanel.SetActive(!useAutoDetection);
        }

        // Update status text
        if (statusText && !isNavigationActive)
        {
            if (useAutoDetection)
            {
                statusText.text = "Select destination to navigate from current location";
            }
            else
            {
                statusText.text = "Select your current office, then destination";
            }
        }
    }

    void UpdateAutoDetectedLocation()
    {
        if (!autoDetectedLocationLabel) return;
        if (!navigationSystem) navigationSystem = FindFirstObjectByType<SmartNavigationSystem>();

        // Camera position (use nav’s current position; falls back to Camera.main)
        Vector3 camPos = navigationSystem ? navigationSystem.GetCurrentPosition()
                                          : (Camera.main ? Camera.main.transform.position : Vector3.zero);

        if (camPos == Vector3.zero)
        {
            autoDetectedLocationLabel.text = "Current Location";
            autoDetectedLocationLabel.color = Color.white;
            return;
        }

        float mpu = navigationSystem ? Mathf.Max(0.0001f, navigationSystem.metersPerUnit) : 1f;

        // 1) Detect if we are near the ENTRANCE
        Transform entranceT = ResolveEntranceNode();
        bool atEntrance = false;
        float entranceMeters = float.MaxValue;

        if (entranceT != null)
        {
            Vector3 cf = new Vector3(camPos.x, 0f, camPos.z);
            Vector3 ef = new Vector3(entranceT.position.x, 0f, entranceT.position.z);
            entranceMeters = Vector3.Distance(cf, ef) * mpu;
            atEntrance = entranceMeters <= entranceNearRadiusMeters;
        }

        // 2) Detect nearest OFFICE within officeNearRadiusMeters
        var allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
            .Where(w => w != null && w.gameObject.scene.IsValid() && w.waypointType == WaypointType.Office)
            .ToArray();

        string nearestOffice = "";
        float nearestOfficeMeters = float.MaxValue;

        for (int i = 0; i < allWaypoints.Length; i++)
        {
            var wp = allWaypoints[i];
            Vector3 cf = new Vector3(camPos.x, 0f, camPos.z);
            Vector3 wf = new Vector3(wp.transform.position.x, 0f, wp.transform.position.z);

            float meters = Vector3.Distance(cf, wf) * mpu;
            string officeName = !string.IsNullOrEmpty(wp.officeName) ? wp.officeName : wp.waypointName;

            if (meters < nearestOfficeMeters)
            {
                nearestOfficeMeters = meters;
                nearestOffice = officeName;
            }
        }

        bool nearOffice = !string.IsNullOrEmpty(nearestOffice) && nearestOfficeMeters <= officeNearRadiusMeters;

        // 3) Reflect in UI and inform nav how to start
        if (atEntrance)
        {
            // Show entrance and set nav to route from entrance
            autoDetectedLocationLabel.text = $"📍 Entrance — {entranceDisplayName}";
            autoDetectedLocationLabel.color = Color.green;
            if (navigationSystem) navigationSystem.SetStartFromEntrance(true);
        }
        else if (nearOffice)
        {
            // Show nearest office and route from camera
            autoDetectedLocationLabel.text = $"📍 Near: {nearestOffice}";
            autoDetectedLocationLabel.color = Color.green;
            if (navigationSystem) navigationSystem.SetStartFromEntrance(false);
        }
        else
        {
            // Default current location (camera)
            autoDetectedLocationLabel.text = "📍 Current Location";
            autoDetectedLocationLabel.color = Color.white;
            if (navigationSystem) navigationSystem.SetStartFromEntrance(false);
        }
    }

    // Try to use nav.entranceNode; otherwise find a waypoint named like "Waypoint_Entrance_*"
    Transform ResolveEntranceNode()
    {
        if (navigationSystem && navigationSystem.entranceNode != null)
            return navigationSystem.entranceNode;

        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
            .Where(w => w != null && w.gameObject.scene.IsValid())
            .ToArray();

        // Common naming pattern
        var byName = all.FirstOrDefault(w => w.name.IndexOf("Entrance", System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (byName) return byName.transform;

        // Fallback: any marker tagged as Entrance if you use tags
        var tagged = GameObject.FindGameObjectWithTag("Entrance");
        return tagged ? tagged.transform : null;
    }
    void ShowLoadingPlaceholder()
    {
        if (!officeDropdown) return;
        officeDropdown.ClearOptions();
        officeDropdown.AddOptions(new List<string> { "Loading offices..." });
        officeDropdown.value = 0;
        officeDropdown.RefreshShownValue();
        officeDropdown.interactable = false;
    }

    static bool IsDividerOrPlaceholder(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var plain = StripRichText(s).Trim();
        if (plain.Equals("Select Current Office", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (plain.Equals("No offices found", System.StringComparison.OrdinalIgnoreCase)) return true;
        if ((plain.StartsWith("—") && plain.EndsWith("—")) || (plain.StartsWith("--") && plain.EndsWith("--")))
            return true;
        return false;
    }

    static string StripRichText(string s) => Regex.Replace(s ?? "", "<.*?>", string.Empty);

    public void PopulateOfficeDropdown()
    {
        if (!officeDropdown) return;

        Debug.Log("=== PopulateOfficeDropdown START ===");

        // Get ONLY office names from SmartNavigationSystem
        List<string> officeNames = navigationSystem ? navigationSystem.GetAllOfficeNames() : new List<string>();
        Debug.Log($"Got {officeNames.Count} office names from SmartNavigationSystem");

        if (officeNames.Count == 0)
        {
            Debug.LogWarning("No office names received from SmartNavigationSystem!");
            ShowLoadingPlaceholder();
            return;
        }

        // Filter
        string filter = Normalize(officeSearchInput ? officeSearchInput.text : null);
        bool currentFloorOnly = currentFloorOnlyToggle && currentFloorOnlyToggle.isOn;
        string currentFloor = GetCurrentFloorLabel();

        List<string> ground = new List<string>();
        List<string> second = new List<string>();
        List<string> unknown = new List<string>();

        foreach (var officeName in officeNames)
        {
            if (!string.IsNullOrEmpty(filter) && !Normalize(officeName).Contains(filter)) continue;

            string floor = GetOfficeFloorLabel(officeName);
            if (currentFloorOnly && floor != currentFloor) continue;

            if (floor == "Ground") ground.Add(officeName);
            else if (floor == "Second") second.Add(officeName);
            else unknown.Add(officeName);
        }

        // Build options
        officeDropdown.ClearOptions();
        nonSelectableIndices.Clear();

        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("Select an Office"));

        int idx = 1;
        void AddSection(string header, List<string> items)
        {
            if (items.Count == 0) return;

            // Centered header; mark as non-selectable
            options.Add(new TMP_Dropdown.OptionData($"<align=center><b>— {header} —</b></align>"));
            nonSelectableIndices.Add(idx);
            idx++;

            foreach (var name in items)
            {
                options.Add(new TMP_Dropdown.OptionData(name));
                idx++;
            }
        }

        AddSection("Ground Floor", ground);
        AddSection("Second Floor", second);
        AddSection("Unknown Floor", unknown);

        if (options.Count == 1)
        {
            options.Add(new TMP_Dropdown.OptionData("No offices found"));
            nonSelectableIndices.Add(1);
        }

        officeDropdown.AddOptions(options);

        // Reset selection to the prompt
        lastValidOfficeIndex = 0;
        officeDropdown.SetValueWithoutNotify(0);
        officeDropdown.RefreshShownValue();

        // Enable only if there’s at least one real office
        officeDropdown.interactable = options.Count > 1 && !StripRichText(options[1].text).StartsWith("No offices");

        // Wire handler (guard headers)
        officeDropdown.onValueChanged.RemoveListener(OnOfficeDropdownChanged);
        officeDropdown.onValueChanged.AddListener(OnOfficeDropdownChanged);

        // Style caption + template so runtime rows inherit correct font/size/colors
        CustomizeDropdownAppearance();

        // Cache for search; use original names (not the filtered lists)
        cachedOfficeNames = new List<string>(officeNames);

        // Also refresh current office dropdown
        PopulateCurrentOfficeDropdown();

        Debug.Log($"=== PopulateOfficeDropdown COMPLETE: {ground.Count} ground, {second.Count} second, {unknown.Count} unknown ===");
    }

    private bool suppressSelection = false;

    void OnOfficeDropdownChanged(int index)
    {
        if (suppressOfficeSelection) return;

        string raw = officeDropdown.options[index].text;
        bool isBlocked = index == 0 || nonSelectableIndices.Contains(index) || IsDividerOrPlaceholder(raw);

        if (isBlocked)
        {
            suppressOfficeSelection = true;
            officeDropdown.SetValueWithoutNotify(lastValidOfficeIndex);
            officeDropdown.RefreshShownValue();
            suppressOfficeSelection = false;

            StartCoroutine(ReopenDropdown(officeDropdown));
            return;
        }

        // Accept real office
        lastValidOfficeIndex = index;

        string officeName = StripRichText(raw).Trim();
        ShowOfficeInfoPopup(officeName); // your existing logic
    }

    System.Collections.IEnumerator ReopenDropdownNextFrame()
    {
        yield return null; // wait for TMP to close the popup
        officeDropdown.Show(); // re-open so user can pick another
    }

    System.Collections.IEnumerator ReopenDropdown(TMP_Dropdown dd)
    {
        yield return null; // wait one frame for TMP to close popup
        dd.Show();
    }

    public void PopulateCurrentOfficeDropdown()
    {
        if (!currentOfficeDropdown) return;

        // Use the same source list you use elsewhere (cachedOfficeNames or waypointCache)
        var names = (cachedOfficeNames != null && cachedOfficeNames.Count > 0)
            ? cachedOfficeNames
            : waypointCache.Keys.ToList();

        // Partition by floor (reuse your GetOfficeFloorLabel)
        var ground = new List<string>();
        var second = new List<string>();
        var unknown = new List<string>();
        foreach (var n in names)
        {
            var f = GetOfficeFloorLabel(n);
            if (f == "Ground") ground.Add(n);
            else if (f == "Second") second.Add(n);
            else unknown.Add(n);
        }

        // Build options
        currentOfficeDropdown.ClearOptions();
        currentHeaderIndices.Clear();

        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("Select Current Office"));

        int idx = 1;
        void AddSection(string header, List<string> list)
        {
            if (list.Count == 0) return;
            options.Add(new TMP_Dropdown.OptionData($"<align=center><b>— {header} —</b></align>"));
            currentHeaderIndices.Add(idx); // mark header index
            idx++;
            foreach (var n in list) { options.Add(new TMP_Dropdown.OptionData(n)); idx++; }
        }

        AddSection("Ground Floor", ground);
        AddSection("Second Floor", second);
        AddSection("Unknown Floor", unknown);

        if (options.Count == 1)
        {
            options.Add(new TMP_Dropdown.OptionData("No offices found"));
            currentHeaderIndices.Add(1);
        }

        currentOfficeDropdown.AddOptions(options);

        // Reset selection and show prompt
        lastValidCurrentIndex = 0;
        currentOfficeDropdown.SetValueWithoutNotify(0);
        currentOfficeDropdown.RefreshShownValue();

        // Make it clickable only if we have real items
        currentOfficeDropdown.interactable = options.Count > 1 &&
            !StripRichText(options[1].text).StartsWith("No offices", System.StringComparison.OrdinalIgnoreCase);

        // Wire handler
        currentOfficeDropdown.onValueChanged.RemoveListener(OnCurrentOfficeSelected);
        currentOfficeDropdown.onValueChanged.AddListener(OnCurrentOfficeSelected);

        // Optional: style caption + template so runtime rows inherit correct font/size
        CustomizeDropdownAppearance();
    }

    List<TMP_Dropdown.OptionData> BuildOptionsByFloor(IEnumerable<string> names, HashSet<int> headerIndices, string prompt)
    {
        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData(prompt));

        var ground = new List<string>();
        var second = new List<string>();
        var unknown = new List<string>();
        foreach (var n in names)
        {
            var f = GetOfficeFloorLabel(n); // your existing method
            if (f == "Ground") ground.Add(n);
            else if (f == "Second") second.Add(n);
            else unknown.Add(n);
        }

        int idx = 1;
        void AddSection(string header, List<string> list)
        {
            if (list.Count == 0) return;
            options.Add(new TMP_Dropdown.OptionData($"<align=center><b>— {header} —</b></align>"));
            headerIndices.Add(idx); idx++;
            foreach (var n in list) { options.Add(new TMP_Dropdown.OptionData(n)); idx++; }
        }

        AddSection("Ground Floor", ground);
        AddSection("Second Floor", second);
        AddSection("Unknown Floor", unknown);

        if (options.Count == 1)
        {
            options.Add(new TMP_Dropdown.OptionData("No offices found"));
            headerIndices.Add(1);
        }
        return options;
    }

    void SetupDropdown(TMP_Dropdown dd, List<TMP_Dropdown.OptionData> options, UnityAction<int> onChanged, out int lastValidIndex)
    {
        dd.ClearOptions();
        dd.AddOptions(options);
        dd.SetValueWithoutNotify(0);
        dd.RefreshShownValue();


        dd.onValueChanged.RemoveAllListeners();
        dd.onValueChanged.AddListener(onChanged);

        lastValidIndex = 0; // start on prompt
    }


    void ApplyDropdownStyle(TMP_Dropdown dd, TMP_FontAsset font, float size, Color itemColor, Color itemTextColor)
    {
        if (!dd) return;

        // Caption (selected value on button)
        if (dd.captionText)
        {
            if (font) dd.captionText.font = font;
            dd.captionText.enableAutoSizing = false;
            dd.captionText.fontSize = size;
            dd.captionText.overflowMode = TextOverflowModes.Ellipsis;
            dd.captionText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        // Template item label
        TMP_Text templateLabel = dd.itemText;
        if (!templateLabel && dd.template)
        {
            var path = dd.template.Find("Viewport/Content/Item/Item Label");
            if (path) templateLabel = path.GetComponent<TextMeshProUGUI>();
        }

        if (templateLabel)
        {
            if (font) templateLabel.font = font;
            templateLabel.enableAutoSizing = false;
            templateLabel.fontSize = size;
            templateLabel.overflowMode = TextOverflowModes.Ellipsis;
            templateLabel.alignment = TextAlignmentOptions.MidlineLeft;

            // Make sure the Dropdown uses this label as the template source
            dd.itemText = templateLabel;

            // Row height (so 30pt text isn’t cramped)
            var item = templateLabel.transform.parent; // "Item" (Toggle)
            if (item)
            {
                var le = item.GetComponent<LayoutElement>() ?? item.gameObject.AddComponent<LayoutElement>();
                le.minHeight = Mathf.Max(36, size + 10);

                var itemBG = item.GetComponent<Image>();
                if (itemBG) itemBG.color = itemColor;
            }
        }

        // Optional visuals
        if (dd.template)
        {
            var templateBG = dd.template.GetComponent<Image>();
            if (templateBG) templateBG.color = itemColor;
        }
    }


    void OnOfficeSelected(int index)
    {
        Debug.Log("===== OnOfficeSelected CALLED =====");
        Debug.Log($"Selected index: {index}");

        if (!officeDropdown)
        {
            Debug.LogError("officeDropdown is null!");
            return;
        }

        if (index < 0 || index >= officeDropdown.options.Count)
        {
            Debug.LogError($"Invalid index {index}, dropdown has {officeDropdown.options.Count} options");
            return;
        }

        string selectedText = officeDropdown.options[index].text;
        Debug.Log($"Selected text: '{selectedText}'");

        // Placeholder/header guards
        if (index <= 0 || (nonSelectableIndices != null && nonSelectableIndices.Contains(index)))
        {
            Debug.Log("Index is placeholder or non-selectable");
            selectedOffice = "";
            UpdateNavigationUI();
            return;
        }
        if (selectedText == "No offices found" || selectedText == "Loading offices..." || selectedText.StartsWith("—"))
        {
            Debug.Log("Selected text is header or placeholder");
            selectedOffice = "";
            UpdateNavigationUI();
            return;
        }

        Debug.Log($"✅ Valid office selected: '{selectedText}'");

        // Persist selection (if other UI depends on it)
        selectedOffice = selectedText;
        UpdateNavigationUI();

        // Start nav using your existing API; internally it resolves via GetOfficeWaypointPosition()
        if (!navigationSystem) navigationSystem = FindFirstObjectByType<SmartNavigationSystem>();
        if (!navigationSystem)
        {
            Debug.LogError("SmartNavigationSystem not found in scene.");
            return;
        }

        navigationSystem.StartNavigationToOffice(selectedText);
    }

    void RefreshOfficeDropdown()
    {
        if (!navigationSystem) navigationSystem = FindFirstObjectByType<SmartNavigationSystem>();

        // Use index-based list (not GetAllOfficeNames which might differ)
        var names = navigationSystem.GetAllOfficeNamesFromIndex();

        officeDropdown.onValueChanged.RemoveListener(OnOfficeSelected);
        officeDropdown.ClearOptions();

        // Optional: add a placeholder at index 0
        var options = new List<TMPro.TMP_Dropdown.OptionData> { new TMPro.TMP_Dropdown.OptionData("— Select office —") };
        options.AddRange(names.ConvertAll(n => new TMPro.TMP_Dropdown.OptionData(n)));
        officeDropdown.AddOptions(options);

        officeDropdown.value = 0;
        officeDropdown.RefreshShownValue();

        officeDropdown.onValueChanged.AddListener(OnOfficeSelected);
    }

    void OnNavigateClicked()
    {
        if (string.IsNullOrEmpty(selectedOffice))
        {
            ShowSearchFeedback("Please select a destination office first", Color.yellow);
            return;
        }
        if (!navigationSystem)
        {
            ShowSearchFeedback("Navigation system not found", Color.red);
            return;
        }

        StartNavigationWithOffices();
    }

    void OnStopClicked()
    {
        if (navigationSystem) navigationSystem.StopNavigation();
        isNavigationActive = false;
        UpdateNavigationUI();
    }


    public void ShowArrivalConfirmation(string officeName)
    {
        if (!arrivalPanel || !arrivalText)
        {
            Debug.LogWarning("Arrival UI elements not assigned!");
            return;
        }

        Debug.Log($"[UI] Showing arrival confirmation for: {officeName}");

        // Set arrival message
        arrivalText.text = $"🎯 You have arrived at\n{officeName}!";
        arrivalText.fontSize = 40;
        arrivalText.color = Color.white;

        // Show panel
        arrivalPanel.SetActive(true);

        // Fade in
        if (arrivalCanvasGroup)
        {
            arrivalCanvasGroup.alpha = 0f;
            StartCoroutine(FadeInArrival());
        }

        // Auto-hide after delay
        StartCoroutine(AutoHideArrivalPanel());
    }

    IEnumerator FadeInArrival()
    {
        float duration = 0.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            arrivalCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / duration);
            yield return null;
        }

        arrivalCanvasGroup.alpha = 1f;
    }

    IEnumerator AutoHideArrivalPanel()
    {
        // Wait for display time
        yield return new WaitForSeconds(arrivalDisplayTime);

        // Fade out
        if (arrivalPanel && arrivalPanel.activeInHierarchy && arrivalCanvasGroup)
        {
            float fade = 0.5f;
            float t = 0f;

            while (t < fade)
            {
                t += Time.deltaTime;
                arrivalCanvasGroup.alpha = 1f - Mathf.Clamp01(t / fade);
                yield return null;
            }

            arrivalPanel.SetActive(false);
            arrivalCanvasGroup.alpha = 0f;
        }
    }

    void OnArrivalOKClicked()
    {
        if (arrivalPanel) arrivalPanel.SetActive(false);
        StopAllCoroutines();
    }

    void UpdateNavigationUI()
    {
        bool hasDestination = !string.IsNullOrEmpty(selectedOffice);

        // ⭐ NEW: Check if start location is valid based on mode
        bool hasValidStart = useAutoDetection || !string.IsNullOrEmpty(selectedCurrentOffice);

        bool canNavigate = hasDestination && hasValidStart &&
                           (navigationSystem == null || navigationSystem.CanStartNavigation());

        bool isNav = SmartNavigationSystem.IsAnyNavigationActive();

        if (navigateButton)
        {
            navigateButton.gameObject.SetActive(!isNav);
            navigateButton.interactable = canNavigate;
        }

        if (stopButton)
        {
            stopButton.gameObject.SetActive(isNav);
        }

        // ⭐ Smart status messages based on mode
        if (statusText && !isNav)
        {
            if (!hasDestination)
            {
                statusText.text = "Select a destination office";
            }
            else if (useAutoDetection)
            {
                statusText.text = $"Ready to navigate from current location to {selectedOffice}";
            }
            else if (string.IsNullOrEmpty(selectedCurrentOffice))
            {
                statusText.text = "Select your current office first";
            }
            else
            {
                statusText.text = $"Ready to navigate from {selectedCurrentOffice} to {selectedOffice}";
            }
        }

        UpdateNavigationStatusDisplay();
    }

    void UpdateNavigationStatusDisplay()
    {
        if (!navigationStatusText) return;

        if (SmartNavigationSystem.IsAnyNavigationActive())
        {
            navigationStatusText.text = navigationSystem
                ? navigationSystem.GetNavigationStatus()
                : "Navigation active";
        }
        else
        {
            navigationStatusText.text = "Ready to navigate";
        }
    }

    void Update()
    {
        // Update navigation status periodically (every 30 frames ~ 0.5 seconds)
        if (Time.frameCount % 30 == 0)
        {
            bool was = isNavigationActive;
            isNavigationActive = SmartNavigationSystem.IsAnyNavigationActive();
            if (was != isNavigationActive) UpdateNavigationUI();
            UpdateNavigationStatusDisplay();
        }

        // ⭐ NEW: Update auto-detected location (only when in auto-detection mode)
        if (useAutoDetection && Time.frameCount % 15 == 0 && autoDetectedLocationLabel && navigationSystem)
        {
            UpdateAutoDetectedLocation();
        }

        // Handle search results panel click-outside-to-close
        if (searchResultsPanel && searchResultsPanel.activeInHierarchy)
        {
            // Check for mouse/touch input
            if (UnityEngine.InputSystem.Mouse.current != null &&
                UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                RectTransform panelRect = searchResultsPanel.GetComponent<RectTransform>();

                // If clicked outside the panel, close it
                if (!RectTransformUtility.RectangleContainsScreenPoint(panelRect, mousePos))
                {
                    searchResultsPanel.SetActive(false);
                }
            }

            // Alternative: Handle touch input for mobile devices
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                Vector2 touchPos = Input.GetTouch(0).position;
                RectTransform panelRect = searchResultsPanel.GetComponent<RectTransform>();

                if (!RectTransformUtility.RectangleContainsScreenPoint(panelRect, touchPos))
                {
                    searchResultsPanel.SetActive(false);
                }
            }
        }
    }

    void OnDestroy()
    {
        if (navigateButton) navigateButton.onClick.RemoveAllListeners();
        if (stopButton) stopButton.onClick.RemoveAllListeners();
        if (arrivalOKButton) arrivalOKButton.onClick.RemoveAllListeners();
        if (officeDropdown) officeDropdown.onValueChanged.RemoveAllListeners();
        if (currentOfficeDropdown) currentOfficeDropdown.onValueChanged.RemoveAllListeners(); // Add this line
        if (officeSearchInput) officeSearchInput.onValueChanged.RemoveAllListeners();
        if (currentFloorOnlyToggle) currentFloorOnlyToggle.onValueChanged.RemoveAllListeners();

        FirebaseOfficeManager.OnOfficeDataLoaded -= OnFirebaseDataLoaded;
    }

    void BuildWaypointCache()
    {
        waypointCache.Clear();
        var wps = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        Debug.Log($"Found {wps.Length} waypoints in scene");

        foreach (var wp in wps)
        {
            // More flexible checking - include all waypoints with office names
            if (!string.IsNullOrEmpty(wp.officeName))
            {
                string key = wp.officeName;
                if (!waypointCache.ContainsKey(key))
                {
                    waypointCache.Add(key, wp);
                    Debug.Log($"Cached waypoint: {key} (Type: {wp.waypointType})");
                }
            }
            else if (!string.IsNullOrEmpty(wp.waypointName))
            {
                string key = wp.waypointName;
                if (!waypointCache.ContainsKey(key))
                {
                    waypointCache.Add(key, wp);
                    Debug.Log($"Cached waypoint by name: {key} (Type: {wp.waypointType})");
                }
            }
        }

        Debug.Log($"Waypoint cache has {waypointCache.Count} offices");
    }

    NavigationWaypoint FindOfficeWaypoint(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (waypointCache.TryGetValue(name, out var cached) && cached) return cached;

        var wps = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        string norm = Normalize(name);
        return wps.FirstOrDefault(w =>
            w.waypointType == WaypointType.Office &&
            (Normalize(w.officeName) == norm || Normalize(w.name) == norm));
    }

    string GetOfficeFloorLabel(string officeName)
    {
        var wp = FindOfficeWaypoint(officeName);
        if (!wp) return "Unknown";
        return wp.transform.position.y > FloorThresholdY ? "Second" : "Ground";
    }

    string GetCurrentFloorLabel()
    {
        var cam = Camera.main;
        float y = cam ? cam.transform.position.y : 0f;
        return y > FloorThresholdY ? "Second" : "Ground";
    }

    // Add this to ManilaServeUI class if it doesn't exist
    static string Normalize(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" :
        s.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

    void CustomizeDropdownAppearance()
    {
        var dd = officeDropdown;
        if (!dd) return;

        // Button background
        var bgImage = dd.GetComponent<Image>();
        if (bgImage)
        {
            if (customDropdownSprite) bgImage.sprite = customDropdownSprite;
            bgImage.color = customBackgroundColor;
        }

        // Caption (selected value on the button)
        TMP_Text cap = dd.captionText;
        if (cap)
        {
            if (customFont) cap.font = customFont;
            cap.enableAutoSizing = false;
            cap.fontSize = customFontSize;
            cap.color = customTextColor;
            cap.richText = true;
            cap.overflowMode = TextOverflowModes.Ellipsis;
        }

        // Arrow
        var arrow = dd.transform.Find("Arrow");
        if (arrow)
        {
            var arrowImage = arrow.GetComponent<Image>();
            if (arrowImage)
            {
                if (customArrowSprite) arrowImage.sprite = customArrowSprite;
                arrowImage.color = customArrowColor;
            }
        }

        // Template list background
        if (dd.template)
        {
            var templateBg = dd.template.GetComponent<Image>();
            if (templateBg) templateBg.color = customDropdownBgColor;

            // EXPLICITLY find the template item and its label (this is what all rows clone)
            var item = dd.template.Find("Viewport/Content/Item");
            if (item)
            {
                var label = item.Find("Item Label")?.GetComponent<TMP_Text>();
                if (label)
                {
                    if (customFont) label.font = customFont;
                    label.enableAutoSizing = false;       // avoid shrinking to 14 at runtime
                    label.fontSize = customFontSize;
                    label.color = customItemTextColor;
                    label.richText = true;
                    label.overflowMode = TextOverflowModes.Ellipsis;

                    // Ensure the dropdown uses THIS label as the clone source
                    dd.itemText = label;
                }

                // Row height to fit the font
                var le = item.GetComponent<LayoutElement>() ?? item.gameObject.AddComponent<LayoutElement>();
                le.minHeight = Mathf.Max(40, (int)customFontSize + 10);

                // Optional: per-row background color
                var itemBgImage = item.GetComponent<Image>();
                if (itemBgImage) itemBgImage.color = customItemColor;
            }

            // Scrollbar and viewport colors (optional)
            var scrollbar = dd.template.GetComponentInChildren<Scrollbar>(true);
            if (scrollbar)
            {
                var scrollbarImage = scrollbar.GetComponent<Image>();
                if (scrollbarImage) scrollbarImage.color = customDropdownBgColor;

                var handleImage = scrollbar.handleRect ? scrollbar.handleRect.GetComponent<Image>() : null;
                if (handleImage) handleImage.color = customArrowColor;
            }
            var viewport = dd.template.Find("Viewport");
            if (viewport)
            {
                var viewportImage = viewport.GetComponent<Image>();
                if (viewportImage) viewportImage.color = customDropdownBgColor;
            }
        }
    }

    void SetupModeButtons()
    {
        if (searchModeButton)
        {
            searchModeButton.onClick.RemoveAllListeners();
            searchModeButton.onClick.AddListener(() => SwitchToSearchMode());
        }

        if (selectModeButton)
        {
            selectModeButton.onClick.RemoveAllListeners();
            selectModeButton.onClick.AddListener(() => SwitchToSelectMode());
        }

        if (executeSearchButton)
        {
            executeSearchButton.onClick.RemoveAllListeners();
            executeSearchButton.onClick.AddListener(ExecuteDirectSearch);
        }

        if (directSearchInput)
        {
            directSearchInput.onEndEdit.RemoveAllListeners();
            directSearchInput.onEndEdit.AddListener((_) => ExecuteDirectSearch());

            // Add real-time search
            directSearchInput.onValueChanged.RemoveAllListeners();
            directSearchInput.onValueChanged.AddListener(OnSearchTextChanged);
        }
        SwitchToSelectMode();
    }

    void OnSearchTextChanged(string searchText)
    {
        if (!isSearchMode) return;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            if (searchResultsPanel) searchResultsPanel.SetActive(false);
            return;
        }

        var matches = GetSearchMatches(searchText);
        ShowSearchResults(matches);
    }

    public void SwitchToSearchMode()
    {
        isSearchMode = true;

        if (searchPanel) searchPanel.SetActive(true);
        if (selectPanel) selectPanel.SetActive(false);

        if (searchModeButton) searchModeButton.gameObject.SetActive(false);
        if (selectModeButton) selectModeButton.gameObject.SetActive(true);

        selectedOffice = "";
        UpdateNavigationUI();
        if (directSearchInput) directSearchInput.Select();

        Debug.Log("Switched to Search Mode");
    }

    public void SwitchToSelectMode()
    {
        isSearchMode = false;

        if (searchPanel) searchPanel.SetActive(false);
        if (selectPanel) selectPanel.SetActive(true);

        if (selectModeButton) selectModeButton.gameObject.SetActive(false);
        if (searchModeButton) searchModeButton.gameObject.SetActive(true);

        if (directSearchInput) directSearchInput.text = "";

        Debug.Log("Switched to Select Mode");
    }

    void UpdateModeButtonColors()
    {
        if (searchModeButton)
        {
            var searchImage = searchModeButton.GetComponent<Image>();
            if (searchImage)
            {
                searchImage.sprite = isSearchMode ? searchButtonActiveSprite : searchButtonNormalSprite;
            }
        }

        if (selectModeButton)
        {
            var selectImage = selectModeButton.GetComponent<Image>();
            if (selectImage)
            {
                selectImage.sprite = !isSearchMode ? selectButtonActiveSprite : selectButtonNormalSprite;
            }
        }
    }

    void ExecuteDirectSearch()
    {
        Debug.Log("=== ExecuteDirectSearch called ===");

        if (!isSearchMode)
        {
            Debug.Log("Not in search mode");
            return;
        }

        if (!directSearchInput)
        {
            Debug.Log("DirectSearchInput is null");
            ShowSearchFeedback("Search input not found", Color.red);
            return;
        }

        string searchQuery = directSearchInput.text.Trim();
        Debug.Log($"Search query: '{searchQuery}'");

        if (string.IsNullOrEmpty(searchQuery))
        {
            ShowSearchFeedback("Please enter a search term", Color.yellow);
            return;
        }

        ShowSearchFeedback("Searching...", Color.white);

        string matchedOffice = FindBestOfficeMatch(searchQuery);
        Debug.Log($"Match result: '{matchedOffice}'");

        if (!string.IsNullOrEmpty(matchedOffice))
        {
            selectedOffice = matchedOffice;
            UpdateNavigationUI();
            ShowSearchFeedback($"✓ Found: {matchedOffice}", Color.green);

            Debug.Log($"Selected office for navigation: {selectedOffice}");

            // Skip waypoint popup since you don't have waypoints
            // The navigation system will handle the pathfinding
        }
        else
        {
            ShowSearchFeedback($"✗ No office found matching '{searchQuery}'", Color.red);
            Debug.Log($"Available offices: {string.Join(", ", cachedOfficeNames)}");
        }
    }
    List<string> GetAvailableOfficeNames()
    {
        return cachedOfficeNames.Where(office => !string.IsNullOrWhiteSpace(office)).ToList();
    }

    // ADD this debug method for testing:
    [ContextMenu("Debug Search System")]
    void DebugSearchSystem()
    {
        Debug.Log("=== SEARCH DEBUG ===");
        Debug.Log($"Cached offices: {cachedOfficeNames.Count}");
        Debug.Log($"Office list: {string.Join(", ", cachedOfficeNames)}");
        Debug.Log($"Search mode: {isSearchMode}");
        Debug.Log($"Search input exists: {directSearchInput != null}");
        Debug.Log($"Search button exists: {executeSearchButton != null}");

        if (directSearchInput)
        {
            Debug.Log($"Current search text: '{directSearchInput.text}'");
        }
    }

    string FindBestOfficeMatch(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return "";

        Debug.Log($"Searching in {cachedOfficeNames.Count} cached offices");
        Debug.Log($"Available offices: {string.Join(", ", cachedOfficeNames)}");

        if (cachedOfficeNames.Count == 0)
        {
            Debug.Log("No cached offices available");
            return "";
        }

        string normalizedSearch = Normalize(searchText);
        Debug.Log($"Normalized search: '{normalizedSearch}'");

        // 1. Exact match
        var exact = cachedOfficeNames.FirstOrDefault(office => Normalize(office) == normalizedSearch);
        if (!string.IsNullOrEmpty(exact))
        {
            Debug.Log($"Exact match: {exact}");
            return exact;
        }

        // 2. Contains match
        var contains = cachedOfficeNames.FirstOrDefault(office => Normalize(office).Contains(normalizedSearch));
        if (!string.IsNullOrEmpty(contains))
        {
            Debug.Log($"Contains match: {contains}");
            return contains;
        }

        // 3. Starts with match
        var startsWith = cachedOfficeNames.FirstOrDefault(office => Normalize(office).StartsWith(normalizedSearch));
        if (!string.IsNullOrEmpty(startsWith))
        {
            Debug.Log($"Starts with match: {startsWith}");
            return startsWith;
        }

        Debug.Log("No matches found");
        return "";
    }

    void SetupCustomButtonSprites()
    {
        if (searchModeButton && searchButtonNormalSprite)
        {
            var searchImage = searchModeButton.GetComponent<Image>();
            var searchButton = searchModeButton.GetComponent<Button>();

            if (searchImage && searchButton)
            {
                searchImage.sprite = searchButtonNormalSprite;

                var spriteState = searchButton.spriteState;
                spriteState.highlightedSprite = searchButtonHoverSprite;
                spriteState.pressedSprite = searchButtonPressedSprite;
                spriteState.selectedSprite = searchButtonActiveSprite;
                searchButton.spriteState = spriteState;
            }
        }

        if (selectModeButton && selectButtonNormalSprite)
        {
            var selectImage = selectModeButton.GetComponent<Image>();
            var selectButton = selectModeButton.GetComponent<Button>();

            if (selectImage && selectButton)
            {
                selectImage.sprite = selectButtonNormalSprite;

                var spriteState = selectButton.spriteState;
                spriteState.highlightedSprite = selectButtonHoverSprite;
                spriteState.pressedSprite = selectButtonPressedSprite;
                spriteState.selectedSprite = selectButtonActiveSprite;
                selectButton.spriteState = spriteState;
            }
        }
    }
    void ShowSearchFeedback(string message, Color color)
    {
        if (statusText)
        {
            statusText.text = message;
            statusText.color = color;
        }
        Debug.Log($"Search feedback: {message}");
    }

    void ShowSearchResults(List<string> matches)
    {
        if (!searchResultsPanel || !searchResultsContent) return;

        // Clear existing results
        foreach (Transform child in searchResultsContent)
        {
            Destroy(child.gameObject);
        }

        if (matches.Count == 0)
        {
            searchResultsPanel.SetActive(false);
            return;
        }

        searchResultsPanel.SetActive(true);

        // Create result buttons
        int displayCount = Mathf.Min(matches.Count, maxSearchResults);
        for (int i = 0; i < displayCount; i++)
        {
            string officeName = matches[i];
            CreateSearchResultItem(officeName);
        }
    }

    void CreateSearchResultItem(string officeName)
    {
        // Create properly styled button
        GameObject item = new GameObject("SearchResult");
        item.transform.SetParent(searchResultsContent, false);

        // Add LayoutElement to control height
        var layoutElement = item.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 35; // Fixed height for each item
        layoutElement.flexibleWidth = 1; // Allow flexible width

        // Add Image for background
        var image = item.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f); // Dark background

        // Add Button component
        var itemButton = item.AddComponent<Button>();

        // Create text child object
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(item.transform, false);

        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(15, 2); // Left and bottom padding
        textRect.offsetMax = new Vector2(-15, -2); // Right and top padding

        var itemText = textObj.AddComponent<TextMeshProUGUI>();
        itemText.text = officeName;
        itemText.fontSize = 30;
        itemText.color = Color.white;
        itemText.alignment = TextAlignmentOptions.MidlineLeft;

        // Set button hover colors
        var colors = itemButton.colors;
        colors.normalColor = Color.clear;
        colors.highlightedColor = new Color(0.3f, 0.6f, 1f, 0.3f);
        colors.pressedColor = new Color(0.3f, 0.6f, 1f, 0.5f);
        itemButton.colors = colors;

        // Set up click action
        itemButton.onClick.RemoveAllListeners();
        itemButton.onClick.AddListener(() => SelectSearchResult(officeName));
    }
    void SelectSearchResult(string officeName)
    {
        selectedOffice = officeName;
        if (directSearchInput) directSearchInput.text = officeName;
        if (searchResultsPanel) searchResultsPanel.SetActive(false);

        HandleOfficePicked(officeName); // Same flow as dropdown
        Debug.Log($"User selected: {officeName}");
    }

    void StyleCurrentOfficeTemplate()
    {
        if (!currentOfficeDropdown) return;

        // Caption (button text)
        var cap = currentOfficeDropdown.captionText;
        if (cap != null)
        {
            if (customFont) cap.font = customFont;
            cap.enableAutoSizing = false;
            cap.fontSize = customFontSize;              // e.g., 30
            cap.color = customTextColor;                // e.g., black
            cap.richText = true;
            cap.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        }

        // Template item label (what runtime rows clone)
        var itemLabel = currentOfficeDropdown.itemText;
        if (itemLabel != null)
        {
            if (customFont) itemLabel.font = customFont;
            itemLabel.enableAutoSizing = false;         // avoid shrinking to 14
            itemLabel.fontSize = customFontSize;
            itemLabel.color = customItemTextColor;      // e.g., black
            itemLabel.richText = true;
            itemLabel.overflowMode = TMPro.TextOverflowModes.Ellipsis;

            // Ensure row height fits the font
            var row = itemLabel.transform.parent;       // "Item" (Toggle)
            if (row)
            {
                var le = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
                le.minHeight = Mathf.Max(40, (int)customFontSize + 10);
            }
        }
    }
    List<string> GetSearchMatches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return new List<string>();

        string normalizedSearch = Normalize(searchText);
        var matches = new List<string>();

        // Get all matching offices
        foreach (var office in cachedOfficeNames)
        {
            if (Normalize(office).Contains(normalizedSearch))
            {
                matches.Add(office);
            }
        }

        return matches.OrderBy(office => office).ToList();
    }

    [ContextMenu("Debug All Waypoints")]
    void DebugAllWaypoints()
    {
        Debug.Log("=== ALL WAYPOINTS DEBUG ===");
        var allWaypoints = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        Debug.Log($"Total waypoints found: {allWaypoints.Length}");

        foreach (var wp in allWaypoints)
        {
            Debug.Log($"Waypoint: {wp.name} | OfficeName: '{wp.officeName}' | Type: {wp.waypointType} | WaypointName: '{wp.waypointName}'");
        }
 
    }

    public string GetSelectedOffice()
    {
        return selectedOffice;
    }

    OfficeInfo GetOfficeInfo(string officeName)
    {
        foreach (var office in officeDatabase)
        {
            if (office.officeName.Equals(officeName, System.StringComparison.OrdinalIgnoreCase))
            {
                return office;
            }
        }

        // Return default info if not found
        return new OfficeInfo(officeName, "Room not specified", "Directory not available", new string[] { "Information not available" });
    }

    void OnFirebaseDataLoaded(Dictionary<string, FirebaseOfficeManager.Office> offices)
    {
        firebaseOffices = offices;
        Debug.Log($"Received {offices.Count} offices from Firebase");
    }

    public void ShowOfficeInfoPopup(string officeName)
    {
        if (!officeInfoPopup) return;

        if (IsDividerOrPlaceholder(officeName))
        {
            Debug.Log($"[OfficePopup] Ignored divider/placeholder: {officeName}");
            return;
        }

        Debug.Log($"ShowOfficeInfoPopup called for: '{officeName}'");

        // Try to get office from Firebase with mapping
        FirebaseOfficeManager.Office firebaseOffice = null;
        if (firebaseManager != null)
        {
            // Step 1: Try mapped name first
            string searchName = officeName;
            if (officeNameMappings != null && officeNameMappings.ContainsKey(officeName))
            {
                searchName = officeNameMappings[officeName];
                Debug.Log($"Mapped '{officeName}' → '{searchName}'");
            }

            // Step 2: Try to get from Firebase
            firebaseOffice = firebaseManager.GetOfficeByName(searchName);

            // Step 3: Fallback to original name if mapping didn't work
            if (firebaseOffice == null && searchName != officeName)
            {
                firebaseOffice = firebaseManager.GetOfficeByName(officeName);
            }

            // Step 4: Last resort - aggressive partial matching
            if (firebaseOffice == null)
            {
                var allOffices = firebaseManager.GetAllOfficeNames();
                string normalized = Normalize(searchName);

                foreach (var dbOfficeName in allOffices)
                {
                    if (Normalize(dbOfficeName).Contains(normalized) ||
                        normalized.Contains(Normalize(dbOfficeName)))
                    {
                        firebaseOffice = firebaseManager.GetOfficeByName(dbOfficeName);
                        Debug.Log($"Partial match: '{searchName}' → '{dbOfficeName}'");
                        break;
                    }
                }
            }
        }

        // Populate UI with Firebase data or fallback messages
        if (firebaseOffice != null)
        {
            Debug.Log($"✓ Using Firebase data for: {firebaseOffice.OfficeName}");

            if (popupOfficeTitleText) popupOfficeTitleText.text = firebaseOffice.OfficeName;
            if (popupRoomNumberText) popupRoomNumberText.text = $"Location: {firebaseOffice.Location}";
            if (popupDirectoryText) popupDirectoryText.text = $"Head: {firebaseOffice.Head}\nPhone: {firebaseOffice.Phone}";

            if (popupServicesText)
            {
                if (firebaseOffice.Services != null && firebaseOffice.Services.Count > 0)
                {
                    var serviceList = firebaseOffice.Services.Select(s => s.ServiceName).ToList();
                    popupServicesText.text = "Services:\n• " + string.Join("\n• ", serviceList);
                }
                else
                {
                    popupServicesText.text = "Services: None available";
                }
            }
        }
        else
        {
            Debug.LogWarning($"✗ No Firebase data found for: '{officeName}'");
            Debug.Log($"Available Firebase offices: {string.Join(", ", firebaseManager?.GetAllOfficeNames() ?? new List<string>())}");

            if (popupOfficeTitleText) popupOfficeTitleText.text = officeName;
            if (popupRoomNumberText) popupRoomNumberText.text = "Location: Not available";
            if (popupDirectoryText) popupDirectoryText.text = "Contact info not available";
            if (popupServicesText) popupServicesText.text = "Services: Not available";
        }

        // Setup buttons
        if (popupNavigateButton)
        {
            popupNavigateButton.onClick.RemoveAllListeners();
            popupNavigateButton.onClick.AddListener(() => StartNavigationFromPopup(officeName));
        }

        if (popupCloseButton)
        {
            popupCloseButton.onClick.RemoveAllListeners();
            popupCloseButton.onClick.AddListener(CloseOfficeInfoPopup);
        }

        officeInfoPopup.SetActive(true);
    }

    public void CloseOfficeInfoPopup()
    {
        if (officeInfoPopup) officeInfoPopup.SetActive(false);
    }

    void StartNavigationFromPopup(string destinationOffice)
    {
        selectedOffice = destinationOffice;
        CloseOfficeInfoPopup();

        if (!navigationSystem)
        {
            ShowSearchFeedback("Navigation system not found", Color.red);
            return;
        }

        // ⭐ Choose navigation mode based on toggle
        if (useAutoDetection)
        {
            // MODE 1: Auto-Detection (Camera Position)
            Debug.Log($"[AUTO] Starting navigation from current camera location to: {destinationOffice}");
            navigationSystem.StartNavigationFromCurrentLocation(destinationOffice);
            ShowSearchFeedback($"Navigating from current location to {destinationOffice}", Color.green);
        }
        else
        {
            // MODE 2: Manual Selection (Office Dropdown)
            if (string.IsNullOrEmpty(selectedCurrentOffice))
            {
                ShowSearchFeedback("Please select your current office first", Color.yellow);
                return;
            }

            Debug.Log($"[MANUAL] Starting navigation from {selectedCurrentOffice} to: {destinationOffice}");

            // Use office-to-office navigation
            navigationSystem.currentUserOffice = selectedCurrentOffice;
            navigationSystem.useOfficeAsStart = true;
            navigationSystem.StartNavigationFromOfficeName(selectedCurrentOffice, destinationOffice);

            ShowSearchFeedback($"Navigating from {selectedCurrentOffice} to {destinationOffice}", Color.green);
        }

        isNavigationActive = true;
        UpdateNavigationUI();
    }

    // ⭐ REPLACE WITH THIS:
    void StartNavigationWithOffices()
    {
        if (string.IsNullOrEmpty(selectedOffice))
        {
            ShowSearchFeedback("Please select a destination office first", Color.yellow);
            return;
        }

        if (!navigationSystem)
        {
            ShowSearchFeedback("Navigation system not found", Color.red);
            return;
        }

        // Use the hybrid system (auto-detection vs manual selection)
        if (useAutoDetection)
        {
            // Auto-detection mode
            Debug.Log($"[AUTO] Starting navigation from current location to: {selectedOffice}");
            navigationSystem.StartNavigationFromCurrentLocation(selectedOffice);
            ShowSearchFeedback($"Navigating from current location to {selectedOffice}", Color.green);
        }
        else
        {
            // Manual selection mode
            if (string.IsNullOrEmpty(selectedCurrentOffice))
            {
                ShowSearchFeedback("Please select your current office first", Color.yellow);
                return;
            }

            Debug.Log($"[MANUAL] Starting navigation from {selectedCurrentOffice} to: {selectedOffice}");
            navigationSystem.currentUserOffice = selectedCurrentOffice;
            navigationSystem.useOfficeAsStart = true;
            navigationSystem.StartNavigationFromOfficeName(selectedCurrentOffice, selectedOffice);
            ShowSearchFeedback($"Navigating from {selectedCurrentOffice} to {selectedOffice}", Color.green);
        }

        isNavigationActive = true;
        UpdateNavigationUI();
    }

    private void HandleOfficePicked(string officeName, bool resetDropdown = false)
    {
        if (string.IsNullOrWhiteSpace(officeName)) return;

        // Remember selection
        selectedOffice = officeName;

        // Ensure no path is visible until the user confirms in the popup
        if (navigationSystem && SmartNavigationSystem.IsAnyNavigationActive())
        {
            navigationSystem.StopNavigation();
        }

        // Keep the UI in sync
        UpdateNavigationUI();
        ShowSearchFeedback($"Selected: {officeName}", Color.green);

        // Show details first
        ShowOfficeInfoPopup(officeName);

        // Put dropdown back to placeholder (if called from dropdown)
        if (resetDropdown && officeDropdown)
        {
            officeDropdown.SetValueWithoutNotify(0);
            officeDropdown.RefreshShownValue();
        }
    }
void InitializeOfficeNameMappings()
{
    officeNameMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // EXACT MATCHES (Waypoint officeName → Firebase OfficeName)
        { "City Treasurer", "Treasurer & Assessor's Office" },
        { "EDP", "e-Services / Technical Support (EDP Office)" },
        { "Cash Division", "Cash Division" },
        { "Mayor's Office", "Office of the City Mayor" },
        { "Social Welfare", "Manila Department of Social Welfare (MDSW)" },
        { "Manila Health Department", "Manila Health Department (MHD)" },
        { "OSCA", "Office of Senior Citizens Affairs (OSCA)" },
        { "E-Business", "e-Services / Technical Support (EDP Office)" },
        { "Office of the City Admin", "Office of the City Administrator" }, // ADD TO FIREBASE
        { "License Division", "License Division" }, // ADD TO FIREBASE
        { "Civil Registry", "Civil Registry Office" },
        { "Bureu of Permits", "Bureau of Permits (BPLO)" }, // Note: typo in waypoint
        { "Real Estate Division", "Real Estate Division" }, // ADD TO FIREBASE
        
        // Handle PWD waypoint (has empty officeName, uses waypointName)
        { "SocialWelfare", "Manila Department of Social Welfare (MDSW)" }, // PWD waypoint
    };
}
    [ContextMenu("Debug Firebase Office Match")]
    void DebugFirebaseMatching()
    {
        if (firebaseManager == null)
        {
            Debug.LogError("FirebaseManager is null!");
            return;
        }

        var allFirebaseOffices = firebaseManager.GetAllOfficeNames();
        var allWaypointOffices = cachedOfficeNames;

        Debug.Log("=== FIREBASE OFFICES ===");
        foreach (var office in allFirebaseOffices)
        {
            Debug.Log($"Firebase: '{office}'");
        }

        Debug.Log("=== WAYPOINT OFFICES ===");
        foreach (var office in allWaypointOffices)
        {
            Debug.Log($"Waypoint: '{office}'");
            var match = firebaseManager.GetOfficeByName(office);
            Debug.Log(match != null ? $"  ✓ Matched to: {match.OfficeName}" : "  ✗ NO MATCH");
        }
    }
    [ContextMenu("Debug All Waypoint Names")]
    void DebugActualWaypointNames()
    {
        Debug.Log("=== ALL WAYPOINTS DEBUG ===");

        // Use Resources.FindObjectsOfTypeAll to find inactive objects too
        var allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>();

        Debug.Log($"Total waypoints found: {allWaypoints.Length}");

        var officeWaypoints = new List<NavigationWaypoint>();

        foreach (var wp in allWaypoints)
        {
            // Skip prefabs and assets
            if (wp.gameObject.scene.name == null) continue;

            if (wp.waypointType == WaypointType.Office)
            {
                officeWaypoints.Add(wp);
                string displayName = !string.IsNullOrEmpty(wp.officeName) ? wp.officeName : wp.waypointName;
                bool isActive = wp.gameObject.activeInHierarchy;
                Debug.Log($"Waypoint: '{displayName}' (GameObject: '{wp.gameObject.name}', Active: {isActive})");
            }
        }

        Debug.Log($"\n=== SUMMARY ===");
        Debug.Log($"Office waypoints: {officeWaypoints.Count}");

        Debug.Log("\n=== FIREBASE OFFICE NAMES ===");
        if (firebaseManager != null)
        {
            var fbNames = firebaseManager.GetAllOfficeNames();
            Debug.Log($"Firebase offices: {fbNames.Count}");
            foreach (var name in fbNames)
            {
                Debug.Log($"Firebase: '{name}'");
            }
        }
        else
        {
            Debug.LogError("FirebaseManager is NULL!");
        }
    }



    void OnCurrentOfficeSelected(int index)
    {
        if (suppressCurrentSelection) return;

        var raw = currentOfficeDropdown.options[index].text;

        // Block prompt, headers, and “no data” rows
        if (index == 0 || currentHeaderIndices.Contains(index) || IsDividerOrPlaceholder(raw))
        {
            suppressCurrentSelection = true;
            currentOfficeDropdown.SetValueWithoutNotify(lastValidCurrentIndex);
            currentOfficeDropdown.RefreshShownValue();
            suppressCurrentSelection = false;

            // Keep list open so the user can pick a real office (optional)
            StartCoroutine(ReopenDropdown(currentOfficeDropdown));
            return;
        }

        // Accept normal selection
        lastValidCurrentIndex = index;

        string officeName = StripRichText(raw).Trim();
        // TODO: Use the selected current office (set a field, update UI, etc.)
        // e.g., selectedCurrentOffice = officeName;
    }

    void SetupDropdowns()
    {
        Debug.Log("[UI] === SetupDropdowns ===");

        // Current Office Dropdown ONLY
        if (currentOfficeDropdown != null)
        {
            currentOfficeDropdown.onValueChanged.RemoveAllListeners();
            currentOfficeDropdown.onValueChanged.AddListener(OnCurrentOfficeSelected);
            Debug.Log("[UI] ✅ Current office dropdown listener added");
        }
        else
        {
            Debug.LogError("[UI] ❌ currentOfficeDropdown is NULL! Check Inspector assignment!");
        }

        // ❌ REMOVED destination dropdown code - you don't have it

        Debug.Log("[UI] === SetupDropdowns Complete ===");
    }

    /// <summary>
    /// Finds the waypoint position for an office name
    /// Uses your existing NavigationWaypoint components that were mapped by FirebaseOfficeManager
    /// </summary>
    Vector3 FindWaypointPositionByOfficeName(string officeName)
    {
        if (string.IsNullOrWhiteSpace(officeName))
        {
            Debug.LogWarning("[WAYPOINT] Empty office name provided");
            return Vector3.zero;
        }

        // Find all NavigationWaypoint components in the scene
        NavigationWaypoint[] allWaypoints = FindObjectsOfType<NavigationWaypoint>();

        if (allWaypoints == null || allWaypoints.Length == 0)
        {
            Debug.LogError("[WAYPOINT] No NavigationWaypoint components found in scene!");
            return Vector3.zero;
        }

        Debug.Log($"[WAYPOINT] Searching {allWaypoints.Length} waypoints for office: '{officeName}'");

        // Try to find exact match first
        foreach (var waypoint in allWaypoints)
        {
            if (waypoint.officeName == officeName)
            {
                Debug.Log($"[WAYPOINT] Exact match: '{waypoint.name}' has officeName='{waypoint.officeName}'");
                return waypoint.transform.position;
            }
        }

        // Try normalized match (case-insensitive, trim whitespace)
        string normalizedSearch = NormalizeOfficeName(officeName);

        foreach (var waypoint in allWaypoints)
        {
            string normalizedWaypoint = NormalizeOfficeName(waypoint.officeName);

            if (normalizedWaypoint == normalizedSearch)
            {
                Debug.Log($"[WAYPOINT] Normalized match: '{waypoint.name}' officeName='{waypoint.officeName}' matches '{officeName}'");
                return waypoint.transform.position;
            }
        }

        // Try partial match (contains)
        foreach (var waypoint in allWaypoints)
        {
            string normalizedWaypoint = NormalizeOfficeName(waypoint.officeName);

            if (normalizedWaypoint.Contains(normalizedSearch) || normalizedSearch.Contains(normalizedWaypoint))
            {
                Debug.Log($"[WAYPOINT] Partial match: '{waypoint.name}' officeName='{waypoint.officeName}' partially matches '{officeName}'");
                return waypoint.transform.position;
            }
        }

        // Log all available waypoints for debugging
        Debug.LogWarning($"[WAYPOINT] No match found for '{officeName}'. Available waypoints:");
        foreach (var wp in allWaypoints)
        {
            Debug.Log($"  - GameObject: '{wp.name}', officeName: '{wp.officeName}'");
        }

        return Vector3.zero;
    }

    /// <summary>
    /// Normalizes office name for matching (lowercase, trim, remove extra spaces)
    /// </summary>
    string NormalizeOfficeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        return name.ToLowerInvariant()
                   .Trim()
                   .Replace("  ", " "); // Remove double spaces
    }

    public string GetCurrentOfficeStatus()
    {
        if (!string.IsNullOrEmpty(selectedCurrentOffice))
            return $"Starting from: {selectedCurrentOffice}";
        else
            return "Starting from: Current Location";
    }

    // Add this method to ManilaServeUI to force set the current office
    public void SetCurrentOfficeForTesting(string officeName)
    {
        selectedCurrentOffice = officeName;
        Debug.Log($"Forced current office to: {selectedCurrentOffice}");

        if (currentOfficeDropdown)
        {
            // Find the office in the dropdown and select it
            for (int i = 0; i < currentOfficeDropdown.options.Count; i++)
            {
                if (currentOfficeDropdown.options[i].text == officeName)
                {
                    currentOfficeDropdown.SetValueWithoutNotify(i);
                    currentOfficeDropdown.RefreshShownValue();
                    break;
                }
            }
        }
    }

    Transform FindTargetWaypointAdvanced(string officeName)
    {
        var allWaypoints = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
            .Where(w => w.gameObject.scene.name != null && w.waypointType == WaypointType.Office)
            .ToArray();

        Debug.Log($"FindTargetWaypointAdvanced: Looking for '{officeName}' among {allWaypoints.Length} office waypoints");

        // Special handling for Social Welfare (pick the best one)
        if (officeName.Equals("Social Welfare", StringComparison.OrdinalIgnoreCase))
        {
            var socialWelfareWaypoints = allWaypoints
                .Where(w => w.officeName != null && w.officeName.Contains("Social Welfare"))
                .ToArray();

            if (socialWelfareWaypoints.Length > 0)
            {
                // Prefer SocialWelfare3 (seems to be the main one based on connections)
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
                wp.officeName.Equals(officeName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"Exact match found: {wp.name} for '{officeName}'");
                return wp.transform;
            }
        }

        // Partial match by officeName
        foreach (var wp in allWaypoints)
        {
            if (!string.IsNullOrEmpty(wp.officeName))
            {
                string normalizedWaypoint = Normalize(wp.officeName);
                string normalizedSearch = Normalize(officeName);

                if (normalizedWaypoint.Contains(normalizedSearch) || normalizedSearch.Contains(normalizedWaypoint))
                {
                    Debug.Log($"Partial match found: {wp.name} for '{officeName}'");
                    return wp.transform;
                }
            }
        }

        Debug.LogWarning($"No waypoint found for office: '{officeName}'");
        return null;
    }
    public void CacheAllOffices()
    {
        if (officeLookup == null)
            officeLookup = new Dictionary<string, GameObject>();

        officeLookup.Clear();

        // Find all NavigationWaypoint components in the scene
        NavigationWaypoint[] allWaypoints = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);

        foreach (var wp in allWaypoints)
        {
            // Skip prefabs and non-scene objects
            if (wp.gameObject.scene.name == null) continue;

            // ONLY cache Office type waypoints
            if (wp.waypointType != WaypointType.Office) continue;

            Debug.Log($"Office waypoint found: {wp.gameObject.name}, type={wp.waypointType}, officeName={wp.officeName}");

            // Use officeName if available, fallback to GameObject name
            string name = !string.IsNullOrEmpty(wp.officeName) ? wp.officeName : wp.gameObject.name;

            if (!string.IsNullOrEmpty(name))
            {
                if (!officeLookup.ContainsKey(name))
                {
                    officeLookup[name] = wp.gameObject;
                    Debug.Log($"✅ Cached office: {name}");
                }
                else
                {
                    Debug.LogWarning($"⚠ Duplicate office name skipped: {name}");
                }
            }
        }

        Debug.Log($"✅ Cached {officeLookup.Count} OFFICES ONLY (excluded corridors/stairs).");
    }

    [ContextMenu("Debug Dropdown Contents")]
    void DebugDropdownContents()
    {
        Debug.Log("=== DROPDOWN DEBUG ===");

        if (officeDropdown && officeDropdown.options.Count > 0)
        {
            Debug.Log($"Dropdown has {officeDropdown.options.Count} options:");
            for (int i = 0; i < officeDropdown.options.Count; i++)
            {
                string optionText = officeDropdown.options[i].text;
                Debug.Log($"  [{i}] {optionText}");
            }
        }

        if (currentOfficeDropdown && currentOfficeDropdown.options.Count > 0)
        {
            Debug.Log($"Current office dropdown has {currentOfficeDropdown.options.Count} options:");
            for (int i = 0; i < currentOfficeDropdown.options.Count; i++)
            {
                string optionText = currentOfficeDropdown.options[i].text;
                Debug.Log($"  [{i}] {optionText}");
            }
        }

        // Check what SmartNavigationSystem returns
        if (navigationSystem)
        {
            var officeNames = navigationSystem.GetAllOfficeNames();
            Debug.Log($"SmartNavigationSystem returns {officeNames.Count} offices:");
            foreach (var name in officeNames)
            {
                Debug.Log($"  Office: {name}");
            }
        }
    }
}