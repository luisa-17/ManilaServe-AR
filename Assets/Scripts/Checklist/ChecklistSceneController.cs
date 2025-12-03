using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ChecklistSceneController : MonoBehaviour
{
    [Header("UI Root (optional)")]
    public Canvas rootCanvas;

    [Header("Buttons")]
    public Button backButton;
    public Button logoutButton;

    [Header("Logout Confirmation")]
    public GameObject logoutConfirmationPanel;
    public Button confirmLogoutButton;
    public Button cancelLogoutButton;

    [Header("Navigation")]
    public string mainSceneName = "SampleScene";

#if ENABLE_INPUT_SYSTEM
    [Header("Input System")]
    [Tooltip("Drag UI/Cancel from your EventSystem's Actions Asset here")]
    public InputActionReference uiCancelAction;
#endif

    void Awake()
    {
        if (!rootCanvas)
        {
            var canvases = FindObjectsOfType<Canvas>(true);
            if (canvases.Length > 0) rootCanvas = canvases[0];
        }

        // Make sure confirmation panel is hidden at start
        if (logoutConfirmationPanel != null)
        {
            logoutConfirmationPanel.SetActive(false);
        }
    }

    void Start()
    {
        AutoWireButtons();
    }

#if ENABLE_INPUT_SYSTEM
    void OnEnable()
    {
        if (uiCancelAction != null)
        {
            uiCancelAction.action.performed += OnUICancel;
            uiCancelAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (uiCancelAction != null)
            uiCancelAction.action.performed -= OnUICancel;
    }

    void OnUICancel(InputAction.CallbackContext _)
    {
        // If confirmation panel is open, close it instead of going back
        if (logoutConfirmationPanel != null && logoutConfirmationPanel.activeSelf)
        {
            HideLogoutConfirmation();
        }
        else
        {
            OnBackClicked();
        }
    }
#endif

    void AutoWireButtons()
    {
        // Wire back button
        if (!backButton && rootCanvas)
        {
            var t = rootCanvas.transform.Find("BackButton");
            if (t) backButton = t.GetComponent<Button>();
        }

        // Wire logout button
        if (!logoutButton && rootCanvas)
        {
            var t = rootCanvas.transform.Find("LogoutButton");
            if (t) logoutButton = t.GetComponent<Button>();
        }

        // Wire confirmation panel and its buttons
        if (!logoutConfirmationPanel && rootCanvas)
        {
            var t = rootCanvas.transform.Find("LogoutConfirmationPanel");
            if (t)
            {
                logoutConfirmationPanel = t.gameObject;

                // Try to find confirm and cancel buttons within the panel
                if (!confirmLogoutButton)
                {
                    var confirmBtn = t.Find("ConfirmButton");
                    if (confirmBtn) confirmLogoutButton = confirmBtn.GetComponent<Button>();
                }
                if (!cancelLogoutButton)
                {
                    var cancelBtn = t.Find("CancelButton");
                    if (cancelBtn) cancelLogoutButton = cancelBtn.GetComponent<Button>();
                }
            }
        }

        // Set up button listeners
        if (backButton)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackClicked);
        }

        if (logoutButton)
        {
            logoutButton.onClick.RemoveAllListeners();
            logoutButton.onClick.AddListener(ShowLogoutConfirmation);
        }

        if (confirmLogoutButton)
        {
            confirmLogoutButton.onClick.RemoveAllListeners();
            confirmLogoutButton.onClick.AddListener(OnConfirmLogout);
        }

        if (cancelLogoutButton)
        {
            cancelLogoutButton.onClick.RemoveAllListeners();
            cancelLogoutButton.onClick.AddListener(HideLogoutConfirmation);
        }
    }

    public void OnBackClicked()
    {
        SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
    }

    public void ShowLogoutConfirmation()
    {
        if (logoutConfirmationPanel != null)
        {
            logoutConfirmationPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("LogoutConfirmationPanel not assigned! Logging out directly.");
            OnConfirmLogout();
        }
    }

    public void HideLogoutConfirmation()
    {
        if (logoutConfirmationPanel != null)
        {
            logoutConfirmationPanel.SetActive(false);
        }
    }

    public void OnConfirmLogout()
    {
        HideLogoutConfirmation();
        AuthService.SignOut();
        SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
    }

    // Keep this for backward compatibility if called from UI directly
    public void OnLogoutClicked()
    {
        ShowLogoutConfirmation();
    }
}