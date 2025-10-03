using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimpleUIController : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Dropdown officeDropdown;
    public Button navigateButton;
    public Button stopButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI directionText;
    public TextMeshProUGUI distanceText;

    [Header("Navigation System")]
    public SmartNavigationSystem navigationSystem;

    private string selectedOffice = "";

    void Start()
    {
        // Find navigation system if not assigned
        if (navigationSystem == null)
        {
            navigationSystem = FindFirstObjectByType<SmartNavigationSystem>();
        }

        SetupUI();
        PopulateOfficeDropdown();
        UpdateUI();
    }

    void SetupUI()
    {
        // Auto-find UI elements if not assigned
        if (officeDropdown == null)
            officeDropdown = FindFirstObjectByType<TMP_Dropdown>();

        if (navigateButton == null)
        {
            Button[] buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
            foreach (Button btn in buttons)
            {
                if (btn.name.ToLower().Contains("navigate") || btn.name.ToLower().Contains("start"))
                {
                    navigateButton = btn;
                    break;
                }
            }
        }

        if (stopButton == null)
        {
            Button[] buttons = FindObjectsByType<Button>(FindObjectsSortMode.None);
            foreach (Button btn in buttons)
            {
                if (btn.name.ToLower().Contains("stop"))
                {
                    stopButton = btn;
                    break;
                }
            }
        }

        // Setup button listeners
        if (navigateButton != null)
        {
            navigateButton.onClick.RemoveAllListeners();
            navigateButton.onClick.AddListener(StartNavigation);
        }

        if (stopButton != null)
        {
            stopButton.onClick.RemoveAllListeners();
            stopButton.onClick.AddListener(StopNavigation);
            stopButton.gameObject.SetActive(false);
        }

        // Setup dropdown listener
        if (officeDropdown != null)
        {
            officeDropdown.onValueChanged.RemoveAllListeners();
            officeDropdown.onValueChanged.AddListener(OnOfficeSelected);
        }

        Debug.Log("SimpleUIController setup completed");
    }

    void PopulateOfficeDropdown()
    {
        if (officeDropdown == null) return;

        officeDropdown.options.Clear();
        officeDropdown.options.Add(new TMP_Dropdown.OptionData("Select Office"));

        // Add default offices
        string[] defaultOffices = {
            "Reception",
            "Conference Room A",
            "Conference Room B",
            "Manager Office",
            "Break Room",
            "IT Department",
            "HR Department",
            "Accounting",
            "Marketing"
        };

        foreach (string office in defaultOffices)
        {
            officeDropdown.options.Add(new TMP_Dropdown.OptionData(office));
        }

        // If navigation system has departments, use those instead
        if (navigationSystem != null && navigationSystem.departmentNames != null)
        {
            officeDropdown.options.Clear();
            officeDropdown.options.Add(new TMP_Dropdown.OptionData("Select Office"));

            foreach (string dept in navigationSystem.departmentNames)
            {
                if (!string.IsNullOrEmpty(dept))
                {
                    officeDropdown.options.Add(new TMP_Dropdown.OptionData(dept));
                }
            }
        }

        officeDropdown.value = 0;
        officeDropdown.RefreshShownValue();
    }

    void OnOfficeSelected(int index)
    {
        if (officeDropdown != null && index > 0 && index < officeDropdown.options.Count)
        {
            selectedOffice = officeDropdown.options[index].text;
            UpdateUI();

            if (statusText != null)
                statusText.text = $"Selected: {selectedOffice}";

            Debug.Log($"Office selected: {selectedOffice}");
        }
        else
        {
            selectedOffice = "";
            UpdateUI();

            if (statusText != null)
                statusText.text = "Please select an office";
        }
    }

    public void StartNavigation()
    {
        if (string.IsNullOrEmpty(selectedOffice))
        {
            if (statusText != null)
                statusText.text = "Please select an office first";
            return;
        }

        if (navigationSystem == null)
        {
            Debug.LogError("Navigation system not found!");
            if (statusText != null)
                statusText.text = "Navigation system not found";
            return;
        }

        // Check if another navigation is already active
        if (SmartNavigationSystem.IsAnyNavigationActive() && !navigationSystem.CanStartNavigation())
        {
            if (statusText != null)
                statusText.text = "Another navigation is already active";
            Debug.LogWarning("Cannot start navigation: Another session is active");
            return;
        }

        // Start navigation
        navigationSystem.StartNavigationToOffice(selectedOffice);
        UpdateUI();

        Debug.Log($"Started navigation to: {selectedOffice}");
    }

    public void StopNavigation()
    {
        if (navigationSystem != null)
        {
            navigationSystem.StopNavigation();
        }

        UpdateUI();

        if (statusText != null)
            statusText.text = "Navigation stopped";

        Debug.Log("Navigation stopped");
    }

    void UpdateUI()
    {
        bool canNavigate = !string.IsNullOrEmpty(selectedOffice) &&
                          (navigationSystem == null || navigationSystem.CanStartNavigation());

        bool isNavigating = SmartNavigationSystem.IsAnyNavigationActive();

        // Update button states
        if (navigateButton != null)
        {
            navigateButton.gameObject.SetActive(!isNavigating);
            navigateButton.interactable = canNavigate;
        }

        if (stopButton != null)
        {
            stopButton.gameObject.SetActive(isNavigating);
        }

        // Update status text if no specific message is set
        if (statusText != null && !isNavigating)
        {
            if (string.IsNullOrEmpty(selectedOffice))
            {
                statusText.text = "Select an office to navigate";
            }
            else if (canNavigate)
            {
                statusText.text = $"Ready to navigate to {selectedOffice}";
            }
        }
    }

    public string GetSelectedOffice()
    {
        return selectedOffice;
    }

    void Update()
    {
        // Update UI periodically
        if (Time.frameCount % 60 == 0) // Every second
        {
            UpdateUI();
        }
    }

    void OnDestroy()
    {
        // Clean up listeners
        if (navigateButton != null)
            navigateButton.onClick.RemoveAllListeners();
        if (stopButton != null)
            stopButton.onClick.RemoveAllListeners();
        if (officeDropdown != null)
            officeDropdown.onValueChanged.RemoveAllListeners();
    }
}