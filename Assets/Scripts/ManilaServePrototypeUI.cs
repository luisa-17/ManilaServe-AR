

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;   // ✅ Required for List<>

public class ManilaServePrototypeUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown officeDropdown;
    public Button stopButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI navigationStatusText;

    private SmartNavigationSystem navigationSystem;
    private string selectedOffice = "";
    private bool isNavigationActive = false;

    void Start()
    {
        navigationSystem = FindFirstObjectByType<SmartNavigationSystem>();

        if (officeDropdown == null)
            officeDropdown = GameObject.Find("OfficeDropdown")?.GetComponent<TMP_Dropdown>();
        if (stopButton == null)
            stopButton = GameObject.Find("StopButton")?.GetComponent<Button>();
        if (statusText == null)
            statusText = GameObject.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        if (navigationStatusText == null)
            navigationStatusText = GameObject.Find("NavigationStatusText")?.GetComponent<TextMeshProUGUI>();

        // Setup stop button
        if (stopButton != null)
        {
            stopButton.onClick.RemoveAllListeners();
            stopButton.onClick.AddListener(OnStopClicked);
            stopButton.gameObject.SetActive(false);
        }

        // Setup dropdown
        if (officeDropdown != null)
        {
            officeDropdown.onValueChanged.RemoveAllListeners();
            officeDropdown.onValueChanged.AddListener(OnOfficeSelected);
        }

        // ❌ No PopulateOfficeDropdown() here
        UpdateNavigationUI();
    }

    // ✅ Now public, so SmartNavigationSystem can call it
    public void PopulateOfficeDropdown()
    {
        if (officeDropdown == null)
        {
            Debug.LogError("Office dropdown reference is missing!");
            return;
        }

        officeDropdown.ClearOptions();

        if (navigationSystem != null)
        {
            List<string> offices = navigationSystem.GetAllOfficeNames();

            if (offices.Count == 0)
            {
                Debug.LogWarning("⚠ No offices returned from ARNavigationSystem.GetAllOfficeNames()");
            }
            else
            {
                // ✅ Insert a default "Select an Office..." placeholder
                List<string> dropdownOptions = new List<string>();
                dropdownOptions.Add("Select an Office...");
                dropdownOptions.AddRange(offices);

                officeDropdown.AddOptions(dropdownOptions);
                officeDropdown.value = 0; // ✅ start at placeholder
                officeDropdown.RefreshShownValue();

                Debug.Log($"✅ Dropdown populated with {offices.Count} offices (+ placeholder)");
            }
        }
        else
        {
            Debug.LogError("❌ navigationSystem reference is NULL in ManilaServePrototypeUI!");
        }
    }

    void OnOfficeSelected(int index)
    {
        if (officeDropdown != null && index > 0 && index < officeDropdown.options.Count)
        {
            selectedOffice = officeDropdown.options[index].text;
            Debug.Log($"Office selected: {selectedOffice}");

            if (navigationSystem != null)
            {
                navigationSystem.StartNavigationToOffice(selectedOffice);
                isNavigationActive = true;

                if (statusText != null)
                    statusText.text = $"➡ Navigating to {selectedOffice}...";

                UpdateNavigationUI();
            }
        }
        else
        {
            selectedOffice = "";
            if (statusText != null)
                statusText.text = "⚠ Please select a valid office.";
        }
    }


    void OnStopClicked()
    {
        if (navigationSystem != null)
        {
            navigationSystem.StopNavigation();
        }

        isNavigationActive = false;

        if (statusText != null)
            statusText.text = "⏹ Navigation stopped.";

        UpdateNavigationUI();
    }

    void UpdateNavigationUI()
    {
        if (stopButton != null)
            stopButton.gameObject.SetActive(isNavigationActive);

        if (navigationStatusText != null)
        {
            navigationStatusText.text = isNavigationActive
                ? $"Navigating to {selectedOffice}..."
                : "Ready to navigate";
        }
    }

    void OnDestroy()
    {
        if (stopButton != null) stopButton.onClick.RemoveAllListeners();
        if (officeDropdown != null) officeDropdown.onValueChanged.RemoveAllListeners();
    }

    public string GetSelectedOffice()
    {
        return selectedOffice;
    }

    public void ShowArrivalConfirmation(string officeName)
    {
        if (statusText != null)
            statusText.text = $"🎉 Arrived at {officeName}!";

        Debug.Log($"[UI] Arrival confirmed at {officeName}");
    }

}
