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

    List<ChecklistDTO> _active = new List<ChecklistDTO>();
    List<ChecklistDTO> _completed = new List<ChecklistDTO>();

    void Awake()
    {
        if (toDoTabButton) toDoTabButton.onClick.AddListener(() => SwitchTab(true));
        if (completedTabButton) completedTabButton.onClick.AddListener(() => SwitchTab(false));
        if (refreshButton) refreshButton.onClick.AddListener(async () => await LoadData());
        if (addFromContextButton) addFromContextButton.onClick.AddListener(async () => await CreateFromContext());

        if (serviceProviderMB is IChecklistService sp) service = sp;
        if (service == null)
        {
            Debug.LogError("ChecklistUIController: serviceProviderMB must implement IChecklistService (assign CityChecklistServiceAdapter).");
        }
    }

    async void OnEnable()
    {
        SwitchTab(true);
        await LoadData();

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

            BuildList(toDoContent, _active);
            BuildList(completedContent, _completed);

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

    void BuildList(Transform content, List<ChecklistDTO> list)
    {
        Clear(content);
        if (list.Count == 0) return;

        foreach (var item in list)
        {
            var card = Instantiate(cardPrefab, content);
            card.Bind(item, requirementRowPrefab,
                onToggleChanged: async (id, newChecked) =>
                {
                    await service.UpdateChecklistProgressAsync(id, newChecked);
                    // Recompute and move if completion changed
                    item.CheckedItems = newChecked;
                    item.Progress = newChecked.Count == 0 ? 0f :
                        newChecked.Count(b => b) / (float)newChecked.Count;
                    bool nowComplete = IsComplete(item);
                    bool inActive = _active.Contains(item);
                    if (inActive && nowComplete)
                        await LoadData();
                },
                onDeleteClicked: async (id) =>
                {
                    bool ok = await service.DeleteChecklistAsync(id);
                    if (ok) await LoadData();
                    else ShowSnack("Delete failed");
                }
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

        var res = await service.CreateChecklistAsync(AuthService.UserId, officeId, serviceId, null);
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