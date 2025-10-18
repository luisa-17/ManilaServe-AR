using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

public class ServiceArrivalManager : MonoBehaviour
{
    [Header("Popup Panels")]
    public GameObject servicePopupPanel;
    public GameObject serviceOfferPanel; // View 1: Service selection
    public GameObject requirementView; // View 2: Requirements

 
    [Header("Service Selection (View 1)")]
    public TextMeshProUGUI officeTitleText;  // "Welcome to <Office>"
    public Transform servicesContainer;      // Container for service buttons
    public GameObject serviceButtonPrefab;

    [Header("Requirements Display (View 2)")]
    public Button backButton;                 // ← Back arrow
    public TextMeshProUGUI serviceNameHeader; // "<Service> Requirements"
    public Transform requirementsContent;     // Container for requirement items
    public GameObject requirementItemPrefab;  // prefab that has a TMP_Text
    public Button addToChecklistButton;

    [Header("References")]
    public FirebaseOfficeManager firebaseManager;

    [Header("Checklist Service (IChecklistService)")]
    public MonoBehaviour checklistServiceMB; // assign CityChecklistServiceAdapter here
    private IChecklistService checklistService;

    [Header("Auth Integration")]
    public GameObject authPanelPrefab;       // assign your AuthPanelController prefab
    public Transform authPanelParent;        // assign an Overlay/ScreenSpace Canvas
    private AuthPanelController activeAuthPanel;

    [Header("Optional Navigation")]
    public bool openChecklistSceneAfterAdd = true;
    public string checklistSceneName = "ChecklistScene";

    [Header("Local Fallback (NavigationWaypoint)")]
    public bool enableWaypointFallback = true;

    [Header("Close")]
    public Button closeButton;

    // Runtime state
    private string currentOfficeId;
    private string currentOfficeName;
    private FirebaseOfficeManager.Office currentOfficeData;
    private FirebaseOfficeManager.Service currentService;
    private bool addInProgress = false;

    void Start()
    {
        if (closeButton) closeButton.onClick.AddListener(ClosePopup);
        if (addToChecklistButton) addToChecklistButton.onClick.AddListener(() => _ = AddCurrentServiceToChecklist());
        if (backButton) backButton.onClick.AddListener(ShowServiceSelectionView);

        ResolveDependencies();

        if (servicePopupPanel) servicePopupPanel.SetActive(false);
        Debug.Log("[ARRIVAL] ServiceArrivalManager initialized");
    }

    [SerializeField] bool allowLocalFallback = false; // keep OFF

    void ResolveDependencies()
    {
        if (!firebaseManager)
            firebaseManager = FindFirstObjectByType<FirebaseOfficeManager>(FindObjectsInactive.Include);

    
        // Prefer adapter assigned in Inspector
        if (checklistServiceMB is IChecklistService svc)
            checklistService = svc;

        // Prefer Firebase adapter in scene
        if (checklistService == null)
        {
            var fb = FindFirstObjectByType<CityChecklistFirebaseAdapter>(FindObjectsInactive.Include);
            if (fb != null) checklistService = fb;
        }

        // Optional local fallback (OFF by default)
        if (checklistService == null && allowLocalFallback)
        {
            var local = FindFirstObjectByType<CityChecklistServiceAdapter>(FindObjectsInactive.Include);
            if (local != null) checklistService = local;
        }

        if (checklistService == null)
            Debug.LogError("[ARRIVAL] IChecklistService not found. Add CityChecklistFirebaseAdapter and assign it.");
    }
    // Call this from navigation when you know the officeId (best)
    public void ShowArrivalPopupById(string officeId, string officeNameFallback = null)
    {
        currentOfficeId = officeId;
        currentOfficeName = officeNameFallback;
        OpenPopup();
    }

    // Backward-compatible call (name only)
    public void ShowArrivalPopup(string officeName)
    {
        currentOfficeId = null;
        currentOfficeName = officeName;
        OpenPopup();
    }

    void OpenPopup()
    {
        currentService = null;

        // Resolve office via Firebase (id or name)
        currentOfficeData = TryResolveOffice(currentOfficeId, currentOfficeName);

        // Decide the display name
        string officeDisplay = currentOfficeData?.OfficeName ?? currentOfficeName ?? "Office";
        if (officeTitleText) officeTitleText.text = $"Welcome to {officeDisplay}";

        // Primary services = Firebase
        var services = currentOfficeData?.Services ?? new List<FirebaseOfficeManager.Service>();
        services = NormalizeAndSortServices(services);

        // Fallback to NavigationWaypoint if needed
        if (enableWaypointFallback && (services == null || services.Count == 0))
        {
            var wp = FindWaypointByOfficeName(officeDisplay) ?? FindWaypointByOfficeName(currentOfficeName);
            if (wp != null)
            {
                var local = BuildServicesFromWaypoint(wp);
                if (local.Count > 0)
                {
                    services = NormalizeAndSortServices(local);
                    Debug.Log($"[ARRIVAL] Using local waypoint services for '{officeDisplay}' ({services.Count}).");
                }
            }
        }

        PopulateServices(services);
        ShowServiceSelectionView();
        if (servicePopupPanel) servicePopupPanel.SetActive(true);
    }

    FirebaseOfficeManager.Office TryResolveOffice(string officeId, string officeName)
    {
        if (!firebaseManager) return null;

        FirebaseOfficeManager.Office byId = null;
        if (!string.IsNullOrWhiteSpace(officeId))
            byId = firebaseManager.GetOfficeById(officeId);

        if (byId != null) return byId;

        // Fallback to name (normalized)
        string norm = Norm(officeName);
        if (string.IsNullOrEmpty(norm)) return null;

        var all = firebaseManager.GetAllOffices();
        if (all != null)
        {
            var exact = all.FirstOrDefault(o => Norm(o.OfficeName) == norm);
            if (exact != null) return exact;

            // Loose contains fallback
            var loose = all.FirstOrDefault(o => Norm(o.OfficeName).Contains(norm) || norm.Contains(Norm(o.OfficeName)));
            if (loose != null) return loose;
        }

        // Last chance: legacy lookup
        return firebaseManager.GetOfficeByName(officeName);
    }

    static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

    List<FirebaseOfficeManager.Service> NormalizeAndSortServices(List<FirebaseOfficeManager.Service> src)
    {
        if (src == null) return new List<FirebaseOfficeManager.Service>();
        // Remove nulls/duplicates by normalized name
        var unique = src
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.ServiceName))
            .GroupBy(s => Norm(s.ServiceName))
            .Select(g => g.First())
            .ToList();

        // Consistent order: by name (or by a SortIndex if you have one)
        return unique.OrderBy(s => s.ServiceName, System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    void ShowServiceSelectionView()
    {
        if (serviceOfferPanel) serviceOfferPanel.SetActive(true);
        if (requirementView) requirementView.SetActive(false);

     SetAddButtonEnabled(false);
        if (serviceNameHeader) serviceNameHeader.text = "Select a service";
    }

    void ShowRequirementsView()
    {
        if (serviceOfferPanel) serviceOfferPanel.SetActive(false);
        if (requirementView) requirementView.SetActive(true);
    }
    void PopulateServices(List<FirebaseOfficeManager.Service> services)
    {
        if (servicesContainer)
        {
            for (int i = servicesContainer.childCount - 1; i >= 0; i--)
                Destroy(servicesContainer.GetChild(i).gameObject);
        }

    if (services == null || services.Count == 0)
        {
            CreateNoServicesMessage();
            SetAddButtonEnabled(false); // disable Add when no services available
            return;
        }

        // We still keep Add disabled here; it becomes enabled after a service is selected
        SetAddButtonEnabled(false);

        foreach (var s in services)
            CreateServiceButton(s);
    }
    void CreateServiceButton(FirebaseOfficeManager.Service service)
    {
        if (!serviceButtonPrefab || !servicesContainer)
        {
            Debug.LogError("[ARRIVAL] Missing serviceButtonPrefab or servicesContainer");
            return;
        }

        var btnObj = Instantiate(serviceButtonPrefab, servicesContainer);
        btnObj.name = $"Btn_{service.ServiceName}";

        var btnText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText) btnText.text = service.ServiceName;

        var button = btnObj.GetComponent<Button>();
        if (button) button.onClick.AddListener(() => OnServiceButtonClicked(service));
    }

    void CreateNoServicesMessage()
    {
        if (!servicesContainer) return;

        var msgObj = new GameObject("NoServices");
        msgObj.transform.SetParent(servicesContainer, false);

        var text = msgObj.AddComponent<TextMeshProUGUI>();
        text.text = "No services available for this office";
        text.fontSize = 24;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.gray;

        var rect = msgObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500, 100);
    }

    void OnServiceButtonClicked(FirebaseOfficeManager.Service service)
    {
        currentService = service;

        if (serviceNameHeader)
            serviceNameHeader.text = $"{service.ServiceName} Requirements";

        PopulateRequirements(service.Requirements);
        ShowRequirementsView();
    }

    void PopulateRequirements(List<FirebaseOfficeManager.Requirement> requirements)
    {
        if (requirementsContent)
        {
            for (int i = requirementsContent.childCount - 1; i >= 0; i--)
                Destroy(requirementsContent.GetChild(i).gameObject);
        }

  

        var list = requirements ?? new List<FirebaseOfficeManager.Requirement>();
        if (list.Count == 0)
        {
            CreateNoRequirementsMessage();
            SetAddButtonEnabled(true); // enable Add even if no specific requirements
            return;
        }

        for (int i = 0; i < list.Count; i++)
            CreateRequirementItem(list[i].Name, i + 1);

        SetAddButtonEnabled(true); // requirements present → enable Add
    }
    void CreateRequirementItem(string requirement, int number)
    {
        if (!requirementsContent) return;

        if (requirementItemPrefab != null)
        {
            var row = Instantiate(requirementItemPrefab, requirementsContent.transform);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label) label.text = $"{number}. {requirement}";
            row.name = $"Requirement_{number}";
        }
        else
        {
            var reqObj = new GameObject($"Requirement_{number}");
            reqObj.transform.SetParent(requirementsContent, false);

            var text = reqObj.AddComponent<TextMeshProUGUI>();
            text.text = $"{number}. {requirement}";
            text.fontSize = 20;
            text.color = Color.black;
            text.alignment = TextAlignmentOptions.Left;

            var fitter = reqObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = reqObj.AddComponent<LayoutElement>();
            layout.minHeight = 40;
            layout.preferredWidth = 550;

            var rt = reqObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(550, 50);
        }
    }

    void CreateNoRequirementsMessage()
    {
        if (!requirementsContent) return;
        var msgObj = new GameObject("NoRequirements");
        msgObj.transform.SetParent(requirementsContent, false);

        var text = msgObj.AddComponent<TextMeshProUGUI>();
        text.text = "No specific requirements for this service";
        text.fontSize = 20;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.gray;

        var rect = msgObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500, 100);
    }

    async Task AddCurrentServiceToChecklist()
    {
        if (addInProgress) return;
        addInProgress = true;

        if (currentService == null)
        {
            Debug.LogError("[ARRIVAL] No service selected");
            addInProgress = false;
            return;
        }

        await AuthService.EnsureInitializedAsync();
        await AuthService.WaitForAuthRestorationAsync(1500);

        if (!AuthService.IsSignedIn)
        {
            ShowAuthPanel();
            addInProgress = false;
            return;
        }

        if (checklistService == null)
        {
            Debug.LogError("[ARRIVAL] IChecklistService not available");
            addInProgress = false;
            return;
        }

        // Resolve names/IDs and requirements
        var office = currentOfficeData ?? TryResolveOffice(currentOfficeId, currentOfficeName);
        string officeId = office?.OfficeId ?? currentOfficeId ?? currentOfficeName;
        string officeName = office?.OfficeName ?? currentOfficeName ?? "Office";

        string serviceId = currentService.ServiceId ?? currentService.ServiceName;
        string serviceName = currentService.ServiceName;

        var reqs = currentService.Requirements != null
            ? currentService.Requirements.Select(r => r.Name).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
            : new List<string>();

        // Fill context (used by Checklist scene or dedupe logic)
        ChecklistContext.SetSelection(officeId, officeName, serviceId, serviceName, reqs);

        if (addToChecklistButton) addToChecklistButton.interactable = false;
        if (serviceNameHeader)
        {
            serviceNameHeader.text = "Saving to checklist...";
            serviceNameHeader.color = new Color(1f, 0.8f, 0f);
        }

        try
        {
            var (ok, id) = await checklistService.CreateChecklistAsync(AuthService.UserId, officeId, serviceId, reqs);
            if (ok)
            {
                if (serviceNameHeader)
                {
                    serviceNameHeader.text = "✅ Added to your checklist!";
                    serviceNameHeader.color = Color.green;
                }

                if (openChecklistSceneAfterAdd && !string.IsNullOrEmpty(checklistSceneName))
                {
                    SceneManager.LoadScene(checklistSceneName, LoadSceneMode.Single);
                }
                else
                {
                    Invoke(nameof(ClosePopup), 1.0f);
                }
            }
            else
            {
                if (serviceNameHeader)
                {
                    serviceNameHeader.text = "❌ Failed to save";
                    serviceNameHeader.color = Color.red;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ARRIVAL] Save failed: {e}");
            if (serviceNameHeader)
            {
                serviceNameHeader.text = "❌ Failed to save";
                serviceNameHeader.color = Color.red;
            }
        }
        finally
        {
            if (addToChecklistButton) addToChecklistButton.interactable = true;
            addInProgress = false;
        }
    }

    private void SetAddButtonEnabled(bool enabled)
    {
        if (addToChecklistButton) addToChecklistButton.interactable = enabled;
    }

    void ShowAuthPanel()
    {
        if (!authPanelPrefab)
        {
            Debug.LogError("[ARRIVAL] AuthPanel prefab not assigned");
            if (serviceNameHeader)
            {
                serviceNameHeader.text = "⚠️ Login required";
                serviceNameHeader.color = Color.yellow;
            }
            return;
        }   

        Transform parent = authPanelParent;
        if (parent == null)
        {
            foreach (var c in FindObjectsOfType<Canvas>(true))
                if (c.renderMode != RenderMode.WorldSpace) { parent = c.transform; break; }
        }

        var obj = Instantiate(authPanelPrefab, parent, false);

        // Look for controller on root or any child (active or inactive)
        activeAuthPanel = obj.GetComponent<AuthPanelController>() ?? obj.GetComponentInChildren<AuthPanelController>(true);
        if (!activeAuthPanel)
        {
            Debug.LogError("[ARRIVAL] AuthPanelController missing on prefab (root or children). Assign the correct prefab.");
            return;
        }

        activeAuthPanel.OnClosed = success =>
        {
            if (success && AuthService.IsSignedIn)
            {
                _ = AddCurrentServiceToChecklist();
            }
            else
            {
                if (serviceNameHeader)
                {
                    serviceNameHeader.text = "Login required to save";
                    serviceNameHeader.color = Color.yellow;
                }
            }
        };
    }

    private NavigationWaypoint FindWaypointByOfficeName(string officeName)
    {
        if (string.IsNullOrWhiteSpace(officeName)) return null;
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(wp => wp && wp.gameObject.scene.IsValid())
        .ToArray();

       string n = Norm(officeName);
        return all.FirstOrDefault(wp =>
            Norm(wp.officeName) == n ||
            Norm(wp.waypointName) == n ||
            Norm(wp.name) == n);
    }

    private List<FirebaseOfficeManager.Service> BuildServicesFromWaypoint(NavigationWaypoint wp)
    {
        var list = new List<FirebaseOfficeManager.Service>();
        if (wp == null || wp.services == null) return list;

        foreach (var raw in wp.services)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var s = new FirebaseOfficeManager.Service
            {
                ServiceName = raw.Trim(),
                Requirements = new List<FirebaseOfficeManager.Requirement>() // no detailed reqs locally
            };
            list.Add(s);
        }
        return list;
    }

    void ClosePopup()
    {
        if (servicePopupPanel) servicePopupPanel.SetActive(false);
        currentService = null;
        currentOfficeId = null;
        currentOfficeName = null;
        currentOfficeData = null;
    }
}