using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChecklistUIController : MonoBehaviour
{
    [Header("Tabs")]
    public Button toDoTabButton;
    public Button completedTabButton;
    public GameObject toDoPanel;
    public GameObject completedPanel;

    [Header("Scroll Contents")]
    public Transform toDoContent;
    public Transform completedContent;

    [Header("Prefabs")]
    public ChecklistCardView cardPrefab;
    public ChecklistRequirementRowView requirementRowPrefab;

    [Header("Actions")]
    public Button refreshButton;
    public Button addFromContextButton; // optional: create a checklist from ChecklistContext

    [Header("Feedback")]
    public GameObject loadingOverlay;
    public TextMeshProUGUI snackText;

    [Header("Service")]
    public MonoBehaviour serviceProviderMB; // Assign CityChecklistServiceAdapter here
    IChecklistService service;

    // near the top with other fields
    [Header("Debug")]
    public bool seedSampleIfEmpty = true;

    [SerializeField] private bool allowLocalFallback = false; // keep OFF to force Firebase

    [Header("Priority UX")]
    public bool sortByPriority = true;
    public bool enforcePriorityInTodo = true; // only enforce on To-Do tab
    public bool highlightNextStep = true;
    public bool showPriorityNumbers = false; // e.g., "1) Application Form"

    [Header("Progress UX")]
    public ChecklistCardView.ProgressTextMode progressTextMode = ChecklistCardView.ProgressTextMode.Percentage;
    [Range(0, 2)] public int progressPercentDecimals = 0;

    List<ChecklistDTO> _active = new List<ChecklistDTO>();
    List<ChecklistDTO> _completed = new List<ChecklistDTO>();

    void Awake()
    {
        if (toDoTabButton) toDoTabButton.onClick.AddListener(() => SwitchTab(true));
        if (completedTabButton) completedTabButton.onClick.AddListener(() => SwitchTab(false));
        if (refreshButton) refreshButton.onClick.AddListener(async () => await LoadData());
        if (addFromContextButton) addFromContextButton.onClick.AddListener(async () => await CreateFromContext());

        // Don’t error here; we’ll resolve in OnEnable
        service = serviceProviderMB as IChecklistService;
    }

    [SerializeField] private bool autoCreateLocalServiceIfMissing = false;

    private void ResolveChecklistService()
    {
        if (service != null) return;

        // 1) Inspector reference
        if (serviceProviderMB != null)
            service = serviceProviderMB as IChecklistService;
        if (service != null) return;

        // 2) Prefer Firebase adapter in the scene (incl. DontDestroyOnLoad)
        #if UNITY_2022_2_OR_NEWER
        var fb = FindFirstObjectByType<CityChecklistFirebaseAdapter>(FindObjectsInactive.Include);
        #else
                var fb = FindObjectOfType<CityChecklistFirebaseAdapter>(true);
        #endif
                if (fb != null) { service = fb; return; }

        // 3) Optional local fallback (OFF by default)
        if (allowLocalFallback)
                {
        #if UNITY_2022_2_OR_NEWER
        var local = FindFirstObjectByType<CityChecklistServiceAdapter>(FindObjectsInactive.Include);
        #else
                    var local = FindObjectOfType<CityChecklistServiceAdapter>(true);
        #endif
                    if (local != null) { service = local; return; }
                }
    }
    async void OnEnable()
    {
        SwitchTab(true);

        await AuthService.EnsureInitializedAsync();
        await AuthService.WaitForAuthRestorationAsync(1500);

        // Ensure we have a service
        ResolveChecklistService();

        if (service == null)
        {
            Debug.LogError("ChecklistUIController: checklist service not configured. Assign CityChecklistServiceAdapter.");
            ShowSnack("Checklist service not configured");
            Clear(toDoContent);
            Clear(completedContent);
            return;
        }

        // Gate by auth
        if (!AuthService.IsSignedIn)
        {
            ShowSnack("Please log in to view your checklists.");
            Clear(toDoContent);
            Clear(completedContent);
            return;
        }

        // Load
        await LoadData();

        // Optional seed
        if (seedSampleIfEmpty && _active.Count == 0 && _completed.Count == 0)
        {
            if (string.IsNullOrEmpty(ChecklistContext.SelectedOffice))
                ChecklistContext.SelectedOffice = "City Treasurer";
            if (string.IsNullOrEmpty(ChecklistContext.SelectedServiceName))
                ChecklistContext.SelectedServiceName = "Business Permit";

            await CreateFromContext();
            await LoadData();
        }
    }


    async Task LoadData()
    {
        await AuthService.EnsureInitializedAsync();
        await AuthService.WaitForAuthRestorationAsync(1500);

        if (!AuthService.IsSignedIn)
        {
            ShowSnack("Please log in to view your checklists.");
            Clear(toDoContent); Clear(completedContent);
            return;
        }

        if (service == null)
        {
            ShowSnack("Checklist service not configured");
            Clear(toDoContent); Clear(completedContent);
            return;
        }

        if (!AuthService.IsSignedIn)
        {
            ShowSnack("Please log in to view your checklists.");
            Clear(toDoContent); Clear(completedContent);
            return;
        }

        SetLoading(true);
        try
        {
            var list = await service.GetUserChecklistsAsync(AuthService.UserId);
            list = Normalize(list);

            _active = list.Where(c => !IsComplete(c)).OrderBy(c => c.OfficeName).ToList();
            _completed = list.Where(IsComplete).OrderBy(c => c.OfficeName).ToList();

            BuildList(toDoContent, _active, readOnly: false);
            BuildList(completedContent, _completed, readOnly: true);

            ShowSnack($"Loaded {_active.Count} active, {_completed.Count} completed");
        }
        catch (System.Exception e)
        {
            Debug.LogError("LoadData error: " + e);
            ShowSnack("Failed to load checklists");
        }
        finally
        {
            SetLoading(false);
        }
    }

    List<ChecklistDTO> Normalize(List<ChecklistDTO> src)
    {
        if (src == null) return new List<ChecklistDTO>();
        foreach (var c in src)
        {
            if (c.Requirements == null) c.Requirements = new List<string>();
            if (c.CheckedItems == null || c.CheckedItems.Count != c.Requirements.Count)
                c.CheckedItems = Enumerable.Repeat(false, c.Requirements.Count).ToList();

            c.Progress = c.CheckedItems.Count == 0 ? 0f :
                c.CheckedItems.Count(b => b) / (float)c.CheckedItems.Count;
        }
        return src;
    }

    bool IsComplete(ChecklistDTO c)
    {
        if (c.Requirements.Count == 0) return false;
        return c.CheckedItems.All(x => x);
        // Extend by DecisionTreeService if needed (like your Dart logic)
    }

    void BuildList(Transform content, List<ChecklistDTO> list, bool readOnly)
    {
        if (!content) { Debug.LogError("[ChecklistUI] Content transform is not assigned."); return; }
        if (!cardPrefab) { Debug.LogError("[ChecklistUI] cardPrefab is not assigned."); return; }
        if (!requirementRowPrefab) Debug.LogWarning("[ChecklistUI] requirementRowPrefab not assigned; rows won’t render.");
    
    Clear(content);
        if (list == null || list.Count == 0) return;

        foreach (var item in list)
        {
            var card = Instantiate(cardPrefab, content);

            // Configure UX per tab
            card.titleIsOffice = true;                         // Title = Office, Subtitle = Service
            card.sortByPriority = sortByPriority;
            card.enforcePriorityOrder = false;
            card.highlightNext = true;
            card.showPriorityNumberPrefix = true;

            // Progress display
            card.progressTextMode = progressTextMode;
            card.percentDecimals = progressPercentDecimals;

            // Bind with appropriate interactivity
            card.Bind(
                item,
                requirementRowPrefab,
                onToggleChanged: readOnly
                    ? null // Completed tab is read-only
                    : async (id, newChecked) =>
                    {
                        await service.UpdateChecklistProgressAsync(id, newChecked);
                        item.CheckedItems = newChecked;
                        item.Progress = newChecked.Count == 0 ? 0f :
                            newChecked.Count(b => b) / (float)newChecked.Count;

                        // If it just reached 100%, reload to move it to Completed
                        if (IsComplete(item))
                            await LoadData();
                    },
                onDeleteClicked: async (id) =>
                {
                    bool ok = await service.DeleteChecklistAsync(id);
                    if (ok) await LoadData();
                    else ShowSnack("Delete failed");
                },
                readOnly: readOnly
            );
        }
    }

    async Task CreateFromContext()
    {
        if (service == null) return;

        string officeId = !string.IsNullOrEmpty(ChecklistContext.SelectedOfficeId)
            ? ChecklistContext.SelectedOfficeId
            : ChecklistContext.SelectedOfficeName;

        string serviceId = !string.IsNullOrEmpty(ChecklistContext.SelectedServiceId)
            ? ChecklistContext.SelectedServiceId
            : ChecklistContext.SelectedServiceName;

        var reqs = (ChecklistContext.SelectedRequirements != null && ChecklistContext.SelectedRequirements.Count > 0)
        ? new List<string>(ChecklistContext.SelectedRequirements)
        : null;

        var res = await service.CreateChecklistAsync(AuthService.UserId, officeId, serviceId, reqs);
        if (res.ok) { ShowSnack("Checklist created"); await LoadData(); }
        else ShowSnack("Create failed");
    }

    void Clear(Transform t)
    {
        if (!t) return;
        for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
    }

    void SwitchTab(bool todo)
    {
        if (toDoPanel) toDoPanel.SetActive(todo);
        if (completedPanel) completedPanel.SetActive(!todo);
        if (toDoTabButton) toDoTabButton.interactable = !todo;
        if (completedTabButton) completedTabButton.interactable = todo;
    }

    void SetLoading(bool on) { if (loadingOverlay) loadingOverlay.SetActive(on); }
    void ShowSnack(string msg) { if (snackText) snackText.text = msg; else Debug.Log(msg); }
}