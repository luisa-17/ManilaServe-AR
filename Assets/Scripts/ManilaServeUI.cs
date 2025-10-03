using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

public class ManilaServeUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown officeDropdown;
    public Button navigateButton;
    public Button stopButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI directionText;
    public TextMeshProUGUI distanceText;

    [Header("Firebase Integration")]
    public FirebaseOfficeManager firebaseManager;
    private Dictionary<string, FirebaseOfficeManager.Office> firebaseOffices;

    [Header("Filters (Optional)")]
    public TMP_InputField officeSearchInput;
    public Toggle currentFloorOnlyToggle;

    [Header("Arrival Notification")]
    public GameObject arrivalPanel;
    public TextMeshProUGUI arrivalText;
    public Button arrivalOKButton;
    public float arrivalDisplayTime = 3f;
    public CanvasGroup arrivalCanvasGroup;

    [Header("Navigation Status")]
    public GameObject navigationStatusPanel;
    public TextMeshProUGUI navigationStatusText;

    SmartNavigationSystem navigationSystem;
    string selectedOffice = "";
    bool isNavigationActive = false;


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
    public int customFontSize = 14;

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
    public float searchResultFontSize = 14f; // Add this field


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



    // Dropdown helpers
    readonly HashSet<int> nonSelectableIndices = new HashSet<int>();
    readonly Dictionary<string, NavigationWaypoint> waypointCache = new Dictionary<string, NavigationWaypoint>();
    const float FloorThresholdY = 5f;

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
    }

    void Start()
    {
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
            Debug.Log("Setting up dropdown listener in ManilaServeUI");
            officeDropdown.onValueChanged = new TMP_Dropdown.DropdownEvent(); // wipes persistent + runtime
            officeDropdown.onValueChanged.AddListener(OnOfficeSelected);
        }

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

        Debug.Log("ManilaServeUI initialized");
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

    void ShowLoadingPlaceholder()
    {
        if (!officeDropdown) return;
        officeDropdown.ClearOptions();
        officeDropdown.AddOptions(new List<string> { "Loading offices..." });
        officeDropdown.value = 0;
        officeDropdown.RefreshShownValue();
        officeDropdown.interactable = false;
    }

    // REPLACE your existing PopulateOfficeDropdown method with this version that adds caching:
    public void PopulateOfficeDropdown()
    {
        if (!officeDropdown) return;

        // Build or refresh a quick waypoint cache (for floor grouping)
        BuildWaypointCache();

        // Gather office names (prefer from SmartNavigationSystem)
        List<string> names = navigationSystem ? navigationSystem.GetAllOfficeNames() : null;

        if (names == null || names.Count == 0)
        {
            // Fallback: read names straight from waypoints
            names = waypointCache.Keys.ToList();
        }

        // Normalize, de-dup and sort
        names = names.Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct()
                     .OrderBy(n => n)
                     .ToList();

        // CACHE FOR SEARCH - This is the only new line
        cachedOfficeNames = new List<string>(names);

        // Apply filters
        string filter = Normalize(officeSearchInput ? officeSearchInput.text : null);
        bool currentFloorOnly = currentFloorOnlyToggle && currentFloorOnlyToggle.isOn;

        string currentFloor = GetCurrentFloorLabel();

        List<string> ground = new List<string>();
        List<string> second = new List<string>();
        List<string> unknown = new List<string>();

        foreach (var name in names)
        {
            // Filter by text
            if (!string.IsNullOrEmpty(filter) && !Normalize(name).Contains(filter))
                continue;

            string floor = GetOfficeFloorLabel(name); // "Ground", "Second", "Unknown"

            // Filter by current floor toggle
            if (currentFloorOnly && floor != currentFloor)
                continue;

            if (floor == "Ground") ground.Add(name);
            else if (floor == "Second") second.Add(name);
            else unknown.Add(name);
        }

        // Rebuild dropdown
        officeDropdown.ClearOptions();
        nonSelectableIndices.Clear();

        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("Select an Office...")); // placeholder

        int idx = 1; // running index after placeholder

        // Helper to add a section
        void AddSection(string header, List<string> items)
        {
            if (items.Count == 0) return;
            options.Add(new TMP_Dropdown.OptionData($"— {header} —"));
            nonSelectableIndices.Add(idx);
            idx++;

            foreach (var nm in items)
            {
                options.Add(new TMP_Dropdown.OptionData(nm));
                idx++;
            }
        }

        AddSection("Ground Floor", ground);
        AddSection("Second Floor", second);
        AddSection("Unknown Floor", unknown);

        if (options.Count == 1)
        {
            // Nothing matched filter
            options.Add(new TMP_Dropdown.OptionData("No offices found"));
            nonSelectableIndices.Add(1);
        }

        officeDropdown.AddOptions(options);
        officeDropdown.SetValueWithoutNotify(0);   // select placeholder without firing event
        officeDropdown.RefreshShownValue();
        officeDropdown.interactable = options.Count > 1 && !options[1].text.StartsWith("No offices");

        Debug.Log($"Dropdown: {ground.Count} ground, {second.Count} second, {unknown.Count} unknown. Cached {cachedOfficeNames.Count} offices for search.");
    }

    void OnOfficeSelected(int index)
    {
        Debug.Log($"===== OnOfficeSelected CALLED with index: {index} =====");
        if (!officeDropdown) return;

        if (index <= 0 || nonSelectableIndices.Contains(index))
        {
            selectedOffice = "";
            UpdateNavigationUI();
            return;
        }

        string text = officeDropdown.options[index].text;
        if (text == "No offices found" || text == "Loading offices...")
        {
            selectedOffice = "";
            UpdateNavigationUI();
            return;
        }

        // Mirror search result behavior: prepare, stop active path, show popup first
        HandleOfficePicked(text, resetDropdown: true);
    }


    void OnNavigateClicked()
    {
        if (string.IsNullOrEmpty(selectedOffice))
        {
            ShowSearchFeedback("Please select an office first", Color.yellow);
            return;
        }
        if (!navigationSystem)
        {
            ShowSearchFeedback("Navigation system not found", Color.red);
            return;
        }

        Debug.Log($"Starting navigation to: {selectedOffice}");
        navigationSystem.StartNavigationToOffice(selectedOffice);
        isNavigationActive = true;
        UpdateNavigationUI();
        ShowSearchFeedback($"Navigating to {selectedOffice}...", Color.green);
    }

    void OnStopClicked()
    {
        if (navigationSystem) navigationSystem.StopNavigation();
        isNavigationActive = false;
        UpdateNavigationUI();
    }


    public void ShowArrivalConfirmation(string officeName)
    {
        if (!arrivalPanel || !arrivalText) return;

        arrivalText.text = $"You have arrived at {officeName}!";
        arrivalPanel.SetActive(true);

        if (arrivalCanvasGroup) arrivalCanvasGroup.alpha = 1f;
        StartCoroutine(AutoHideArrivalPanel());
    }

    IEnumerator AutoHideArrivalPanel()
    {
        yield return new WaitForSeconds(arrivalDisplayTime);

        if (arrivalPanel && arrivalPanel.activeInHierarchy && arrivalCanvasGroup)
        {
            float fade = 0.5f, t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                arrivalCanvasGroup.alpha = 1f - Mathf.Clamp01(t / fade);
                yield return null;
            }
            arrivalPanel.SetActive(false);
        }
    }

    void OnArrivalOKClicked()
    {
        if (arrivalPanel) arrivalPanel.SetActive(false);
        StopAllCoroutines();
    }

    void UpdateNavigationUI()
    {
        bool canNavigate = !string.IsNullOrEmpty(selectedOffice) &&
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
        if (Time.frameCount % 30 == 0)
        {
            bool was = isNavigationActive;
            isNavigationActive = SmartNavigationSystem.IsAnyNavigationActive();
            if (was != isNavigationActive) UpdateNavigationUI();
            UpdateNavigationStatusDisplay();
        }

        // Replace the old input code with this:
        if (searchResultsPanel && searchResultsPanel.activeInHierarchy)
        {
            // Check if mouse/touch is clicked anywhere
            if (UnityEngine.InputSystem.Mouse.current != null &&
                UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                RectTransform panelRect = searchResultsPanel.GetComponent<RectTransform>();

                if (!RectTransformUtility.RectangleContainsScreenPoint(panelRect, mousePos))
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

    static string Normalize(string s) =>
        string.IsNullOrWhiteSpace(s) ? "" :
        s.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

    void CustomizeDropdownAppearance()
    {
        if (!officeDropdown) return;

        var bgImage = officeDropdown.GetComponent<Image>();
        if (bgImage)
        {
            if (customDropdownSprite) bgImage.sprite = customDropdownSprite;
            bgImage.color = customBackgroundColor;
        }

        if (officeDropdown.captionText)
        {
            officeDropdown.captionText.color = customTextColor;
            officeDropdown.captionText.fontSize = customFontSize;
            if (customFont) officeDropdown.captionText.font = customFont;
        }

        var arrow = officeDropdown.transform.Find("Arrow");
        if (arrow)
        {
            var arrowImage = arrow.GetComponent<Image>();
            if (arrowImage)
            {
                if (customArrowSprite) arrowImage.sprite = customArrowSprite;
                arrowImage.color = customArrowColor;
            }
        }

        if (officeDropdown.template)
        {
            var templateBg = officeDropdown.template.GetComponent<Image>();
            if (templateBg) templateBg.color = customDropdownBgColor;

            var templateText = officeDropdown.template.GetComponentInChildren<TextMeshProUGUI>();
            if (templateText)
            {
                templateText.color = customItemTextColor;
                templateText.fontSize = customFontSize;
                if (customFont) templateText.font = customFont;
            }

            var scrollbar = officeDropdown.template.GetComponentInChildren<Scrollbar>();
            if (scrollbar)
            {
                var scrollbarImage = scrollbar.GetComponent<Image>();
                if (scrollbarImage) scrollbarImage.color = customDropdownBgColor;

                var handleImage = scrollbar.handleRect?.GetComponent<Image>();
                if (handleImage) handleImage.color = customArrowColor;
            }

            var viewport = officeDropdown.template.Find("Viewport");
            if (viewport)
            {
                var viewportImage = viewport.GetComponent<Image>();
                if (viewportImage) viewportImage.color = customDropdownBgColor;
            }

            var itemBackground = officeDropdown.template.Find("Viewport/Content/Item");
            if (itemBackground)
            {
                var itemBgImage = itemBackground.GetComponent<Image>();
                if (itemBgImage) itemBgImage.color = customItemColor;
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
        itemText.fontSize = 14;
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

    void StartNavigationFromPopup(string officeName)
    {
        selectedOffice = officeName;

        if (navigationSystem)
        {
            navigationSystem.StartNavigationConfirmedByUI(officeName); // <-- use this
            isNavigationActive = true;
            UpdateNavigationUI();
            ShowSearchFeedback($"Navigating to {officeName}...", Color.green);
        }

        CloseOfficeInfoPopup();
        Debug.Log($"Started navigation from popup to: {officeName}");
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
        // EXACT waypoint names from your scene → Firebase names
        { "City Treasurer", "Treasurer & Assessor’s Office" },
        { "Cash Division", "Cash Division" },
        { "Mayor's Office", "Office of the City Mayor" },
        { "Social Welfare", "Manila Department of Social Welfare (MDSW)" },
        { "Real Estate Division", "Real Estate Division" },
        { "Manila Health Department", "Manila Health Department (MHD)" },
        { "OSCA", "Office of the Senior Citizens Affairs (OSCA)" },
        { "E-Business", "E-Services / Technical Support (EDP Office)" },
        { "Office of the City Admin", "Office of the City Administrator" },
        { "License Division", "License Division" },
        { "Civil Registry", "Civil Registry Office" },
        { "Bureu of Permits", "Bureau of Permits (BPLO)" }, // Note: typo in waypoint name
        { "EDP", "E-Services / Technical Support (EDP Office)" },
        { "Waypoint_SocialWelfare", "Manila Department of Social Welfare (MDSW)" }
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
}