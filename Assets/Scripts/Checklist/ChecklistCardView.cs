using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChecklistCardView : MonoBehaviour
{

    readonly List<ChecklistRequirementRowView> _rows = new List<ChecklistRequirementRowView>();
    readonly List<int> _rowsDataIndex = new List<int>();

    [Header("Header")]
    public TextMeshProUGUI titleText; // Office
    public TextMeshProUGUI subtitleText; // Service
    [Tooltip("If true → Title=Office, Subtitle=Service")]
    public bool titleIsOffice = true;

    [Header("Progress")]
    public TextMeshProUGUI progressText;   // "66%" or "66% (2/3)"
    public Slider progressSlider;
    public Image progressFill;
    public Gradient progressGradient;

    public enum ProgressTextMode { Fraction, Percentage, Both }
    [Tooltip("How to display progress text")] public ProgressTextMode progressTextMode = ProgressTextMode.Percentage;
    [Range(0, 2)] public int percentDecimals = 0;

    [Header("Priority UX")]
    [Tooltip("Sort requirement rows by admin-defined priority")]
    public bool sortByPriority = true;
    [Tooltip("Disable toggles for items that are not the next-in-order")]
    public bool enforcePriorityOrder = false;
    [Tooltip("Highlight the next required item")]
    public bool highlightNext = true;
    public Color nextHighlightColor = new Color(1f, 0.95f, 0.65f); // pale yellow
    public Color normalLabelColor = Color.white;
    public bool showPriorityNumberPrefix = false; // e.g., "1) Application Form"

    public TextMeshProUGUI nextUpText;

    [Header("Content")]
    public Transform requirementsRoot;     // RequirementsContainer
    public Button deleteButton;

    // Runtime
    ChecklistDTO _dto;
    List<bool> _checked;
    List<int> _priorities; // same length as Requirements
    Func<string, List<bool>, Task> _onToggleChangedAsync;
    Func<string, Task> _onDeleteAsync;
    bool _readOnly;

    // Mapping from displayed row -> original index in DTO
    List<int> _displayToDataIndex = new List<int>();

    void Awake() => AutoWire();
    #if UNITY_EDITOR
    void OnValidate() { if (!Application.isPlaying) AutoWire(); }
    #endif


    void AutoWire()
    {
        if (!requirementsRoot) requirementsRoot = transform.Find("RequirementsContainer");
        if (requirementsRoot == transform)
        {
            var fix = transform.Find("RequirementsContainer");
            if (fix) requirementsRoot = fix;
        }
        if (!progressSlider) progressSlider = transform.Find("ProgressBar")?.GetComponent<Slider>();
        if (!progressFill && progressSlider)
        {
            var t = progressSlider.transform.Find("Background/Fill Area/Fill");
            if (t) progressFill = t.GetComponent<Image>();
        }
        if (progressSlider) progressSlider.interactable = false;
    }

    public void Bind(
    ChecklistDTO dto,
    ChecklistRequirementRowView rowPrefab,
    Func<string, List<bool>, Task> onToggleChanged,
    Func<string, Task> onDeleteClicked,
    bool readOnly = false
    )
    {
        _dto = dto;
        _onToggleChangedAsync = onToggleChanged;
        _onDeleteAsync = onDeleteClicked;
        _readOnly = readOnly;
    
// Header mapping
if (titleIsOffice)
        {
            if (titleText) titleText.text = dto.OfficeName ?? "";
            if (subtitleText) subtitleText.text = dto.ServiceName ?? "";
        }
        else
        {
            if (titleText) titleText.text = dto.ServiceName ?? "";
            if (subtitleText) subtitleText.text = dto.OfficeName ?? "";
        }

        // Ensure lengths
        dto.Requirements ??= new List<string>();
        dto.CheckedItems ??= new List<bool>();
        while (dto.CheckedItems.Count < dto.Requirements.Count) dto.CheckedItems.Add(false);

        _checked = new List<bool>(dto.CheckedItems);
        _priorities = NormalizePriorities(dto.Priorities, dto.Requirements.Count);

        // Build display order
        _displayToDataIndex = BuildDisplayOrder(_priorities, _checked, sortByPriority);

        // Clear rows and lists
        if (!requirementsRoot)
        {
            Debug.LogError("[ChecklistCardView] requirementsRoot not assigned.");
            return;
        }
        for (int i = requirementsRoot.childCount - 1; i >= 0; i--)
            Destroy(requirementsRoot.GetChild(i).gameObject);
        _rows.Clear();
        _rowsDataIndex.Clear();

        // Compute next-step data index (smallest priority not yet checked)
        int nextDataIndex = FindNextDataIndex(_priorities, _checked);

        // Build rows in display order
        for (int disp = 0; disp < _displayToDataIndex.Count; disp++)
        {
            int dataIndex = _displayToDataIndex[disp];
            string rawText = _dto.Requirements[dataIndex];
            int prio = _priorities[dataIndex];
            bool isOn = _checked[dataIndex];

            string displayText = showPriorityNumberPrefix && prio < int.MaxValue
                ? $"{prio}) {rawText}"
                : rawText;

            var row = Instantiate(rowPrefab, requirementsRoot);

            // All toggles clickable unless read-only
            row.Bind(displayText, isOn, (val) =>
            {
                if (_readOnly) return;

                _checked[dataIndex] = val;
                UpdateProgressUI();

                // Refresh suggested "next" visuals immediately
                RefreshPriorityVisuals();

                _ = _onToggleChangedAsync?.Invoke(_dto.Id, new List<bool>(_checked));
            });

            // Interactability: suggestion only (no enforcement)
            row.SetInteractable(!_readOnly);

            // Initial visuals for priority + next
            bool isSuggestedNext = highlightNext && dataIndex == nextDataIndex && !isOn;
            row.SetPriorityChip(prio, showPriorityNumberPrefix);                           // optional chip
            row.SetNextIcon(isSuggestedNext);                                               // optional icon
            row.SetBackgroundColor(isSuggestedNext ? nextHighlightColor : new Color(0, 0, 0, 0));
            row.SetLabelColor(isSuggestedNext ? Color.white : normalLabelColor);

            // Track row for later refresh
            _rows.Add(row);
            _rowsDataIndex.Add(dataIndex);
        }

        // Header "Next:" chip
        var nextIdx = FindNextDataIndex(_priorities, _checked);
        if (nextUpText)
        {
            if (nextIdx >= 0)
            {
                nextUpText.text = "Next: " + _dto.Requirements[nextIdx];
                nextUpText.gameObject.SetActive(true);
            }
            else
            {
                nextUpText.gameObject.SetActive(false);
            }
        }

        UpdateProgressUI();

        if (deleteButton)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(async () =>
            {
                if (_onDeleteAsync != null) await _onDeleteAsync.Invoke(_dto.Id);
            });
        }
    }


    void RefreshPriorityVisuals()
    {
        // Recompute suggested next
        int nextDataIndex = FindNextDataIndex(_priorities, _checked);

// Update "Next:" in header
if (nextUpText)
        {
            if (!_readOnly && nextDataIndex >= 0)
            {
                nextUpText.text = "Next: " + _dto.Requirements[nextDataIndex];
                nextUpText.gameObject.SetActive(true);
            }
            else
            {
                nextUpText.gameObject.SetActive(false);
            }
        }

        // Update each row visuals
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            int dataIndex = _rowsDataIndex[i];
            bool isOn = _checked[dataIndex];

            bool isSuggestedNext = !_readOnly && highlightNext && (dataIndex == nextDataIndex) && !isOn;

            row.SetPriorityChip(_priorities[dataIndex], showPriorityNumberPrefix);          // chip stays visible
            row.SetNextIcon(isSuggestedNext);
            row.SetBackgroundColor(isSuggestedNext ? nextHighlightColor : new Color(0, 0, 0, 0));
            row.SetLabelColor(isSuggestedNext ? Color.white : normalLabelColor);
            row.SetInteractable(!_readOnly); // all rows remain clickable in To‑Do
        }
    }

    List<int> NormalizePriorities(List<int> src, int count)
    {
        var prios = src != null ? new List<int>(src) : new List<int>();
        while (prios.Count < count) prios.Add(int.MaxValue);
        if (prios.Count > count) prios = prios.Take(count).ToList();

        // If all are int.MaxValue (no admin priority), fall back to 1..N
        if (prios.All(p => p == int.MaxValue))
        {
            prios = Enumerable.Range(1, count).ToList();
        }
        return prios;
    }

    List<int> BuildDisplayOrder(List<int> prios, List<bool> checkedItems, bool sort)
    {
        int n = prios.Count;
        var indices = Enumerable.Range(0, n).ToList();

        if (!sort) return indices;

        // Sort by priority ascending, then by checked status (unchecked first), then by name
        return indices
            .OrderBy(i => prios[i])
            .ThenBy(i => checkedItems[i] ? 1 : 0)
            .ThenBy(i => _dto.Requirements[i], StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    int FindNextDataIndex(List<int> prios, List<bool> checkedItems)
    {
        int next = -1;
        int bestPrio = int.MaxValue;
        for (int i = 0; i < prios.Count; i++)
        {
            if (!checkedItems[i] && prios[i] < bestPrio)
            {
                bestPrio = prios[i];
                next = i;
            }
        }
        return next;
    }

    void UpdateProgressUI()
    {
        int total = _checked?.Count ?? 0;
        int done = 0; if (_checked != null) foreach (var b in _checked) if (b) done++;
        float ratio = total > 0 ? (float)done / total : 0f;

        if (progressText)
        {
            string pct = (ratio * 100f).ToString(percentDecimals == 0 ? "0" : $"0.{new string('0', percentDecimals)}") + "%";
            progressText.text = progressTextMode switch
            {
                ProgressTextMode.Percentage => pct,
                ProgressTextMode.Both => $"{pct} ({done}/{total})",
                _ => $"{done}/{total}"
            };
        }

        if (progressSlider)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            progressSlider.SetValueWithoutNotify(ratio);
        }

        if (progressFill && progressGradient != null)
            progressFill.color = progressGradient.Evaluate(ratio);
    }
}