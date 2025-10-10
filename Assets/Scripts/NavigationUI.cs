using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Canvas))]
public class NavigationUI : MonoBehaviour
{
    [Header("UI (assign in Inspector)")]
    public TMP_Dropdown startDropdown;      // Current location dropdown
    public TMP_InputField startSearchInput; // Current location search input (optional)
    public TMP_Dropdown destDropdown;       // Destination dropdown
    public TMP_InputField destSearchInput;  // Destination search input (optional)
    public Button navigateButton;           // Start navigation
    public Button useReticleButton;         // Optional: choose nearest waypoint to reticle

    [Header("Refs")]
    public SmartNavigationSystem nav;       // assign SmartNavigationSystem

    // internal cache
    List<string> officeNames = new List<string>();

    void Start()
    {
        if (nav == null) nav = FindObjectOfType<SmartNavigationSystem>();

        PopulateOfficeList();

        if (startSearchInput != null) startSearchInput.onValueChanged.AddListener(OnStartSearchChanged);
        if (destSearchInput != null) destSearchInput.onValueChanged.AddListener(OnDestSearchChanged);

        if (navigateButton != null)
        {
            navigateButton.onClick.RemoveAllListeners();
            navigateButton.onClick.AddListener(OnNavigateClicked);
        }

        if (useReticleButton != null)
        {
            useReticleButton.onClick.RemoveAllListeners();
            useReticleButton.onClick.AddListener(OnUseReticleClicked);
        }
    }

    void OnDestroy()
    {
        if (startSearchInput != null) startSearchInput.onValueChanged.RemoveListener(OnStartSearchChanged);
        if (destSearchInput != null) destSearchInput.onValueChanged.RemoveListener(OnDestSearchChanged);
        if (navigateButton != null) navigateButton.onClick.RemoveListener(OnNavigateClicked);
        if (useReticleButton != null) useReticleButton.onClick.RemoveListener(OnUseReticleClicked);
    }

    // Collect office names from NavigationWaypoint objects in the scene
    public void PopulateOfficeList()
    {
        officeNames = FindObjectsOfType<NavigationWaypoint>()
            .Where(w => w != null && w.waypointType == WaypointType.Office)
            .Select(w => w.officeName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (startDropdown != null)
        {
            startDropdown.ClearOptions();
            startDropdown.AddOptions(officeNames);
        }
        if (destDropdown != null)
        {
            destDropdown.ClearOptions();
            destDropdown.AddOptions(officeNames);
        }
    }

    // Live filter handlers
    void OnStartSearchChanged(string q) => FilterDropdown(startDropdown, q);
    void OnDestSearchChanged(string q) => FilterDropdown(destDropdown, q);

    void FilterDropdown(TMP_Dropdown dropdown, string query)
    {
        if (dropdown == null) return;
        if (string.IsNullOrWhiteSpace(query))
        {
            // restore full list
            dropdown.ClearOptions();
            dropdown.AddOptions(officeNames);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
            return;
        }

        var filtered = officeNames.Where(n => n.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (filtered.Count == 0)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string> { "<no matches>" });
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }
        else
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(filtered);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }
    }

    public void OnNavigateClicked()
    {
        if (nav == null)
        {
            Debug.LogError("NavigationUI: SmartNavigationSystem not assigned.");
            return;
        }

        string start = GetSelectedName(startDropdown, startSearchInput);
        string dest = GetSelectedName(destDropdown, destSearchInput);

        if (string.IsNullOrWhiteSpace(start))
        {
            Debug.LogWarning("NavigationUI: Please select your current location.");
            if (nav.statusText != null) nav.statusText.text = "Select current location";
            return;
        }
        if (string.IsNullOrWhiteSpace(dest))
        {
            Debug.LogWarning("NavigationUI: Please select a destination.");
            if (nav.statusText != null) nav.statusText.text = "Select destination";
            return;
        }

        // Immediate visible confirmation on device (shows who was chosen)
        if (nav.statusText != null) nav.statusText.text = $"Starting FROM '{start}' TO '{dest}'";

        Debug.Log($"NavigationUI: Starting nav FROM '{start}' TO '{dest}'");
        nav.StartNavigationFromOfficeName(start, dest);
    }
    // Resolve name: prefer exact typed value, then dropdown selection, then best match
    string GetSelectedName(TMP_Dropdown dropdown, TMP_InputField input)
    {
        // typed exact match first
        if (input != null && !string.IsNullOrWhiteSpace(input.text))
        {
            var q = input.text.Trim();
            var exact = officeNames.FirstOrDefault(n => n.Equals(q, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            // fallback to first partial match
            var partial = officeNames.FirstOrDefault(n => n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            if (partial != null) return partial;
        }

        // otherwise use dropdown
        if (dropdown != null && dropdown.options != null && dropdown.options.Count > 0)
        {
            string val = dropdown.options[Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1)].text;
            if (!string.IsNullOrEmpty(val) && val != "<no matches>") return val;
        }

        return null;
    }

    // Optional: pick nearest waypoint to reticle
    public void OnUseReticleClicked()
    {
        var place = FindObjectOfType<PlaceOnFloorARF>();
        if (place == null || place.reticle == null)
        {
            Debug.LogWarning("NavigationUI: PlaceOnFloorARF or reticle not found.");
            return;
        }

        Vector3 pos = place.reticle.transform.position;
        var offices = FindObjectsOfType<NavigationWaypoint>().Where(w => w.waypointType == WaypointType.Office).ToArray();
        if (offices.Length == 0) return;

        NavigationWaypoint best = null;
        float bestD = float.MaxValue;
        foreach (var w in offices)
        {
            float d = Vector3.Distance(w.transform.position, pos);
            if (d < bestD) { bestD = d; best = w; }
        }

        if (best != null)
        {
            // set dropdown and input if present
            if (startDropdown != null)
            {
                int idx = officeNames.IndexOf(best.officeName);
                if (idx >= 0) { startDropdown.value = idx; startDropdown.RefreshShownValue(); }
            }
            if (startSearchInput != null) startSearchInput.text = best.officeName;
            Debug.Log($"NavigationUI: selected nearest office '{best.officeName}' (d={bestD:F2}m)");
        }
    }
}