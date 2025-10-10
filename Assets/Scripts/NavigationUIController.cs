using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NavigationUIController : MonoBehaviour
{
    public SmartNavigationSystem nav;

    // Hide this whole root while navigating (drag a parent panel here)
    public GameObject panelRootToHide;           // e.g., SearchSelectController OR a new parent that contains all search/select UI

    // Optional: extra things to hide (e.g., OfficeInfoPopup)
    public GameObject[] alsoHide;

    // Only this shows while navigating
    public GameObject cancelButtonRoot;          // e.g., Canvas/CancelNavigate (set inactive by default)

    // Inputs to lock
    public TMP_Dropdown officeDropdown;
    public Button searchButton;

    // Optional: your popup root so we can close it on confirm
    public GameObject popupRoot;

    bool lastState;

    void Start()
    {
        if (!nav) nav = FindFirstObjectByType<SmartNavigationSystem>();
        if (cancelButtonRoot) cancelButtonRoot.SetActive(false);
        ApplyUI(nav && nav.IsNavigating);
    }

    void Update()
    {
        if (!nav) return;
        bool s = nav.IsNavigating;
        if (s != lastState) ApplyUI(s); // auto-restores UI on cancel/arrival
    }

    public void OnConfirmNavigate()
    {
        if (!nav || nav.IsNavigating) return;

        // Get selected office text
        string office = null;
        if (officeDropdown && officeDropdown.options.Count > 0)
            office = officeDropdown.options[officeDropdown.value].text;

        if (string.IsNullOrWhiteSpace(office))
        {
            Debug.LogWarning("No office selected.");
            return;
        }

        nav.StartNavigationConfirmedByUI(office);
        if (popupRoot) popupRoot.SetActive(false);
        ApplyUI(true);
    }

    public void OnCancelNavigation()
    {
        // Try to recover a reference if nav not set in Inspector
        if (nav == null) nav = FindObjectOfType<SmartNavigationSystem>();
        if (nav == null)
        {
            Debug.LogWarning("[UI] OnCancelNavigation: SmartNavigationSystem not found.");
            ApplyUI(false); // still restore UI
            return;
        }

        Debug.Log("[UI] Cancel pressed. isNavigating before stop = " + nav.IsNavigating);
        nav.StopNavigation();              // call the nav stop
        Debug.Log("[UI] Cancel processed. isNavigating after stop = " + nav.IsNavigating);

        ApplyUI(false); // restore search/select UI
    }

    void ApplyUI(bool navigating)
    {
        lastState = navigating;

        if (panelRootToHide) panelRootToHide.SetActive(!navigating);

        if (alsoHide != null)
            foreach (var go in alsoHide)
                if (go) go.SetActive(!navigating);

        if (cancelButtonRoot) cancelButtonRoot.SetActive(navigating);

        if (officeDropdown) officeDropdown.interactable = !navigating;
        if (searchButton) searchButton.interactable = !navigating;
    }
}