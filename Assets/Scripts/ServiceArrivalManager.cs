using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class ServiceArrivalManager : MonoBehaviour
{
    [Header("Popup Panels")]
    public GameObject servicePopupPanel;
    public GameObject serviceOfferPanel;     // View 1: Service selection
    public GameObject requirementView;       // View 2: Requirements

    [Header("Service Selection (View 1)")]
    public TextMeshProUGUI officeTitleText;  // "Welcome to <Office>"
    public Transform servicesContainer;       // Container for service buttons
    public GameObject serviceButtonPrefab;

    [Header("Requirements Display (View 2)")]
    public Button backButton;                 // ← Back arrow
    public TextMeshProUGUI serviceNameHeader; // Service name + "Requirements"
    public Transform requirementsContent;     // Container for requirement items
    public GameObject requirementItemPrefab;
    public Button addToChecklistButton;

    [Header("References")]
    public FirebaseOfficeManager firebaseManager;
    public ChecklistManager checklistManager;
    public Button closeButton;

    [Header("Auth Integration")]
    public GameObject authPanelPrefab;
    private AuthPanelController activeAuthPanel;

    private string currentOfficeName;
    private FirebaseOfficeManager.Service currentService;

    void Start()
    {
        // Wire button listeners
        if (closeButton)
            closeButton.onClick.AddListener(ClosePopup);

        if (addToChecklistButton)
            addToChecklistButton.onClick.AddListener(AddCurrentServiceToChecklist);

        if (backButton)
            backButton.onClick.AddListener(ShowServiceSelectionView);

        // Hide popup initially
        if (servicePopupPanel)
            servicePopupPanel.SetActive(false);

        Debug.Log("[ARRIVAL] ServiceArrivalManager initialized");
    }

    public void ShowArrivalPopup(string officeName)
    {
        Debug.Log($"[ARRIVAL] ========================================");
        Debug.Log($"[ARRIVAL] ShowArrivalPopup called for: {officeName}");
        Debug.Log($"[ARRIVAL] ========================================");

        currentOfficeName = officeName;
        currentService = null; // Reset current service

        // Find Firebase manager if not assigned
        if (firebaseManager == null)
        {
            firebaseManager = FindFirstObjectByType<FirebaseOfficeManager>();
            Debug.Log($"[ARRIVAL] FirebaseManager: {(firebaseManager != null ? "Found" : "NOT FOUND")}");
        }

        // Get office data
        var office = firebaseManager?.GetOfficeByName(officeName);

        if (office == null)
        {
            Debug.LogWarning($"[ARRIVAL] ❌ No office data found for: {officeName}");
            if (officeTitleText)
                officeTitleText.text = $"Welcome to {officeName}";
            PopulateServices(new List<FirebaseOfficeManager.Service>());
        }
        else
        {
            Debug.Log($"[ARRIVAL] ✅ Office found: {office.OfficeName}");
            Debug.Log($"[ARRIVAL] ✅ Services count: {office.Services?.Count ?? 0}");

            if (officeTitleText)
                officeTitleText.text = $"Welcome to {office.OfficeName}";

            PopulateServices(office.Services);
        }

        // CRITICAL: Show service selection view first (not requirements)
        ShowServiceSelectionView();

        // Show the popup
        if (servicePopupPanel)
        {
            servicePopupPanel.SetActive(true);
            Debug.Log("[ARRIVAL] ✅ Popup activated");
        }
    }

    void ShowServiceSelectionView()
    {
        Debug.Log("[ARRIVAL] Switching to Service Selection View");

        if (serviceOfferPanel)
        {
            serviceOfferPanel.SetActive(true);
            Debug.Log("[ARRIVAL] ✅ Service Offer Panel: VISIBLE");
        }

        if (requirementView)
        {
            requirementView.SetActive(false);
            Debug.Log("[ARRIVAL] ✅ Requirement View: HIDDEN");
        }
    }

    void ShowRequirementsView()
    {
        Debug.Log("[ARRIVAL] Switching to Requirements View");

        if (serviceOfferPanel)
        {
            serviceOfferPanel.SetActive(false);
            Debug.Log("[ARRIVAL] ✅ Service Offer Panel: HIDDEN");
        }

        if (requirementView)
        {
            requirementView.SetActive(true);
            Debug.Log("[ARRIVAL] ✅ Requirement View: VISIBLE");
        }
    }

    void PopulateServices(List<FirebaseOfficeManager.Service> services)
    {
        Debug.Log($"[ARRIVAL] PopulateServices called with {services?.Count ?? 0} services");

        // Clear existing buttons
        if (servicesContainer)
        {
            int childCount = servicesContainer.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                Destroy(servicesContainer.GetChild(i).gameObject);
            }
            Debug.Log($"[ARRIVAL] Cleared {childCount} old service buttons");
        }

        if (services == null || services.Count == 0)
        {
            Debug.LogWarning("[ARRIVAL] No services to display");
            CreateNoServicesMessage();
            return;
        }

        // Create button for each service
        foreach (var service in services)
        {
            CreateServiceButton(service);
        }

        Debug.Log($"[ARRIVAL] ✅ Created {services.Count} service buttons");
    }

    void CreateServiceButton(FirebaseOfficeManager.Service service)
    {
        if (!serviceButtonPrefab || !servicesContainer)
        {
            Debug.LogError("[ARRIVAL] ❌ Missing serviceButtonPrefab or servicesContainer!");
            return;
        }

        GameObject btnObj = Instantiate(serviceButtonPrefab, servicesContainer);
        btnObj.name = $"Btn_{service.ServiceName}";

        // Set button text
        var btnText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText)
        {
            btnText.text = service.ServiceName;
        }

        // Add click listener
        var button = btnObj.GetComponent<Button>();
        if (button)
        {
            button.onClick.AddListener(() => OnServiceButtonClicked(service));
        }

        Debug.Log($"[ARRIVAL] Created button: {service.ServiceName}");
    }

    void CreateNoServicesMessage()
    {
        if (!servicesContainer) return;

        GameObject msgObj = new GameObject("NoServices");
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
        Debug.Log($"[ARRIVAL] ========================================");
        Debug.Log($"[ARRIVAL] Service button clicked: {service.ServiceName}");
        Debug.Log($"[ARRIVAL] ========================================");

        currentService = service;

        // Update header
        if (serviceNameHeader)
        {
            serviceNameHeader.text = $"{service.ServiceName} Requirements";
            Debug.Log($"[ARRIVAL] Header updated to: {service.ServiceName} Requirements");
        }

        // Populate requirements
        PopulateRequirements(service.Requirements);

        // Switch to requirements view
        ShowRequirementsView();
    }

    void PopulateRequirements(List<FirebaseOfficeManager.Requirement> requirements)
    {
        Debug.Log($"[ARRIVAL] PopulateRequirements called with {requirements?.Count ?? 0} items");

        // Clear existing requirements
        if (requirementsContent)
        {
            int childCount = requirementsContent.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                Destroy(requirementsContent.GetChild(i).gameObject);
            }
            Debug.Log($"[ARRIVAL] Cleared {childCount} old requirements");
        }

        if (requirements == null || requirements.Count == 0)
        {
            Debug.LogWarning("[ARRIVAL] No requirements to display");
            CreateNoRequirementsMessage();
            if (addToChecklistButton) addToChecklistButton.interactable = false;
            return;
        }

        // Create item for each requirement
        for (int i = 0; i < requirements.Count; i++)
        {
            CreateRequirementItem(requirements[i].Name, i + 1);
        }

        if (addToChecklistButton)
            addToChecklistButton.interactable = true;

        Debug.Log($"[ARRIVAL] ✅ Created {requirements.Count} requirement items");
    }

    void CreateRequirementItem(string requirement, int number)
    {
        if (!requirementsContent) return;

        GameObject reqObj = new GameObject($"Requirement_{number}");
        reqObj.transform.SetParent(requirementsContent, false);

        var text = reqObj.AddComponent<TextMeshProUGUI>();
        text.text = $"{number}. {requirement}";
        text.fontSize = 20;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.Left;

        var contentSizeFitter = reqObj.AddComponent<ContentSizeFitter>();
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var layoutElement = reqObj.AddComponent<LayoutElement>();
        layoutElement.minHeight = 40;
        layoutElement.preferredWidth = 550;

        var rectTransform = reqObj.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(550, 50);

        Debug.Log($"[ARRIVAL] Created requirement: {number}. {requirement}");
    }

    void CreateNoRequirementsMessage()
    {
        if (!requirementsContent) return;

        GameObject msgObj = new GameObject("NoRequirements");
        msgObj.transform.SetParent(requirementsContent, false);

        var text = msgObj.AddComponent<TextMeshProUGUI>();
        text.text = "No specific requirements for this service";
        text.fontSize = 20;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.gray;

        var rect = msgObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500, 100);
    }

    void AddCurrentServiceToChecklist()
    {
        Debug.Log($"[ARRIVAL] ========================================");
        Debug.Log($"[ARRIVAL] AddCurrentServiceToChecklist called");
        Debug.Log($"[ARRIVAL] Current service: {currentService?.ServiceName ?? "NULL"}");
        Debug.Log($"[ARRIVAL] User signed in: {AuthService.IsSignedIn}");
        Debug.Log($"[ARRIVAL] ========================================");

        if (currentService == null)
        {
            Debug.LogError("[ARRIVAL] ❌ No service selected!");
            return;
        }

        // CRITICAL: Check if user is logged in
        if (!AuthService.IsSignedIn)
        {
            Debug.Log("[ARRIVAL] ⚠️ User not logged in - showing auth panel");
            ShowAuthPanel();
            return;
        }

        // User is logged in - proceed with adding to checklist
        string userId = AuthService.UserId;
        Debug.Log($"[ARRIVAL] ✅ User logged in: {userId}");

        if (!checklistManager)
        {
            Debug.LogError("[ARRIVAL] ❌ ChecklistManager not assigned!");
            if (serviceNameHeader)
            {
                serviceNameHeader.text = "❌ Checklist system error";
                serviceNameHeader.color = Color.red;
            }
            return;
        }

        if (!firebaseManager)
        {
            Debug.LogError("[ARRIVAL] ❌ FirebaseManager not assigned!");
            return;
        }

        // Disable button while saving
        if (addToChecklistButton) addToChecklistButton.interactable = false;

        if (serviceNameHeader)
        {
            serviceNameHeader.text = "Saving to checklist...";
            serviceNameHeader.color = new Color(1f, 0.8f, 0f); // Orange
        }

        // Get office and service IDs
        var office = firebaseManager.GetOfficeByName(currentOfficeName);
        string officeId = office?.OfficeId ?? currentOfficeName;
        string serviceId = currentService.ServiceId ?? currentService.ServiceName;

        Debug.Log($"[ARRIVAL] Office ID: {officeId}");
        Debug.Log($"[ARRIVAL] Service ID: {serviceId}");

        // Create checklist item
        var checklistItem = new ChecklistItem
        {
            officeName = officeId,
            serviceName = serviceId,
            requirements = currentService.Requirements?.Select(r => r.Name).ToList() ?? new List<string>(),
            dateAdded = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            isCompleted = false,
            requirementChecked = new List<bool>()
        };

        // Initialize all requirements as unchecked
        for (int i = 0; i < checklistItem.requirements.Count; i++)
        {
            checklistItem.requirementChecked.Add(false);
        }

        Debug.Log($"[ARRIVAL] ChecklistItem created with {checklistItem.requirements.Count} requirements");

        try
        {
            // Add to checklist
            checklistManager.AddChecklistItem(userId, checklistItem);

            // Success!
            Debug.Log($"[ARRIVAL] ========================================");
            Debug.Log($"[ARRIVAL] ✅✅✅ SUCCESS! Added to checklist!");
            Debug.Log($"[ARRIVAL] Service: {currentService.ServiceName}");
            Debug.Log($"[ARRIVAL] Office: {currentOfficeName}");
            Debug.Log($"[ARRIVAL] User: {userId}");
            Debug.Log($"[ARRIVAL] ========================================");

            if (serviceNameHeader)
            {
                serviceNameHeader.text = $"✅ Added to your checklist!";
                serviceNameHeader.color = Color.green;
            }

            // Close popup after 2 seconds
            Invoke(nameof(ClosePopup), 2f);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ARRIVAL] ❌ Failed to add to checklist: {e.Message}");
            Debug.LogError($"[ARRIVAL] Stack trace: {e.StackTrace}");

            if (serviceNameHeader)
            {
                serviceNameHeader.text = $"❌ Failed to save";
                serviceNameHeader.color = Color.red;
            }
        }
        finally
        {
            // Re-enable button
            if (addToChecklistButton)
                addToChecklistButton.interactable = true;
        }
    }

    void ShowAuthPanel()
    {
        Debug.Log("[ARRIVAL] ShowAuthPanel called");

        if (authPanelPrefab == null)
        {
            Debug.LogError("[ARRIVAL] ❌ AuthPanel prefab not assigned in Inspector!");

            if (serviceNameHeader)
            {
                serviceNameHeader.text = "⚠️ Login system not configured";
                serviceNameHeader.color = Color.yellow;
            }
            return;
        }

        Debug.Log("[ARRIVAL] Instantiating auth panel...");

        // Instantiate auth panel
        GameObject authPanelObj = Instantiate(authPanelPrefab);
        activeAuthPanel = authPanelObj.GetComponent<AuthPanelController>();

        if (activeAuthPanel)
        {
            Debug.Log("[ARRIVAL] ✅ Auth panel created successfully");

            // Set callback for when auth completes
            activeAuthPanel.OnClosed = (bool success) =>
            {
                Debug.Log($"[ARRIVAL] Auth panel closed. Success: {success}, Is signed in: {AuthService.IsSignedIn}");

                if (success && AuthService.IsSignedIn)
                {
                    Debug.Log("[ARRIVAL] ✅ User logged in! Retrying add to checklist...");
                    // Retry adding to checklist
                    AddCurrentServiceToChecklist();
                }
                else
                {
                    Debug.Log("[ARRIVAL] ⚠️ Login cancelled or failed");

                    if (serviceNameHeader)
                    {
                        serviceNameHeader.text = "Login required to save to checklist";
                        serviceNameHeader.color = Color.yellow;
                    }
                }
            };
        }
        else
        {
            Debug.LogError("[ARRIVAL] ❌ AuthPanelController component not found on prefab!");
        }
    }

    void ClosePopup()
    {
        Debug.Log("[ARRIVAL] ClosePopup called");

        if (servicePopupPanel)
            servicePopupPanel.SetActive(false);

        currentService = null;
        currentOfficeName = null;

        Debug.Log("[ARRIVAL] ✅ Popup closed and reset");
    }
}