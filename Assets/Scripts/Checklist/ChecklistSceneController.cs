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
    OnBackClicked();
}
#endif

    void AutoWireButtons()
    {
        if (!backButton && rootCanvas)
        {
            var t = rootCanvas.transform.Find("BackButton");
            if (t) backButton = t.GetComponent<Button>();
        }
        if (!logoutButton && rootCanvas)
        {
            var t = rootCanvas.transform.Find("LogoutButton");
            if (t) logoutButton = t.GetComponent<Button>();
        }

        if (backButton)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackClicked);
        }
        if (logoutButton)
        {
            logoutButton.onClick.RemoveAllListeners();
            logoutButton.onClick.AddListener(OnLogoutClicked);
        }
    }

    public void OnBackClicked()
    {
        SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
    }

    public void OnLogoutClicked()
    {
        AuthService.SignOut();
        SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
    }
}