using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ChecklistSceneController : MonoBehaviour
{
    [Header("UI")]
    public Canvas rootCanvas;
    public GameObject checklistRoot;
    public GameObject authPanelPrefab; // optional if you already have an AuthPanel in-scene

    [Header("Scene Names")]
    public string checklistSceneName = "ChecklistScene";
    public string mainSceneName = "SampleScene";
    public bool loadedAdditively = true;

    [Header("Auth Gate")]
    public bool allowGuests = false;   // false = require login on entry
    public bool forceShowAuth = false; // true = force show auth on entry

    [Header("Canvas Binding")]
    public bool bindToMainCamera = true;
    public int overlaySortingOrder = 5000;
    public float planeDistance = 0.3f;

    [Header("Top Buttons (optional auto-wire)")]
    public Button backButton;
    public Button logoutButton;

    GameObject authPanelInstance;

    void Awake()
    {
        if (!rootCanvas) rootCanvas = FindCanvasInMyScene();
        if (checklistRoot) checklistRoot.SetActive(false);
    }

    async void Start()
    {
        StartCoroutine(BindCanvasToMainCamera());

        bool initOk = await AuthService.EnsureInitializedAsync();
        bool signedIn = initOk && AuthService.IsSignedIn;
        bool isAnon = initOk && AuthService.IsAnonymous;

        Debug.Log($"[Checklist] Start: initOk={initOk}, signedIn={signedIn}, isAnon={isAnon}, allowGuests={allowGuests}, forceShowAuth={forceShowAuth}");

        bool showAuth = forceShowAuth || !signedIn || (!allowGuests && isAnon);
        if (showAuth)
        {
            ShowAuthPanel(false, success =>
            {
                if (success) { ShowChecklistPage(); AutoWireButtons(); BringButtonsToFront(); }
                else UnloadChecklistScene("AuthClosed");
            });
        }
        else
        {
            ShowChecklistPage();
            AutoWireButtons();
            BringButtonsToFront();
        }

        LogLoadedScenes("[Checklist] After Start");
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.backspaceKey.wasPressedThisFrame))
            CloseChecklistButton();
#else
        if (Input.GetKeyDown(KeyCode.Escape))
            CloseChecklistButton();
#endif
    }

    public void ShowChecklistPage()
    {
        Debug.Log("[Checklist] ShowChecklistPage()");
        if (checklistRoot) checklistRoot.SetActive(true);
        CloseAuthPanelIfExists();
    }

    // Back button → return to main scene
    public void CloseChecklistButton()
    {
        Debug.Log("[Checklist] Back pressed → unload checklist");
        UnloadChecklistScene("BackButton");
    }

    // Logout button → sign out and return to main scene (NO auth popup)
    public void SignOutAndReturnButton()
    {
        StartCoroutine(SignOutAndReturnFlow());
    }

    // TEMP wrapper in case any old Inspector reference still calls this
    public void SignOutAndShowAuthButton()
    {
        // Old behavior showed popup; new requirement is to return directly.
        SignOutAndReturnButton();
    }

    IEnumerator SignOutAndReturnFlow()
    {
        Debug.Log("[Checklist] Logout → return to main scene (no auth popup)");
        AuthService.SignOut();

        if (checklistRoot) checklistRoot.SetActive(false);
        CloseAuthPanelIfExists();

        // Give Firebase 1 frame to update local state (defensive)
        yield return null;

        UnloadChecklistScene("Logout");
    }

    void ShowAuthPanel(bool startInSignup, System.Action<bool> onClosed)
    {
        // Try to use prefab; if none, try to find an in-scene AuthPanel
        if (!rootCanvas) rootCanvas = FindCanvasInMyScene();

        if (!authPanelPrefab && rootCanvas)
        {
            var t = rootCanvas.transform.Find("AuthPanel");
            if (t)
            {
                authPanelInstance = t.gameObject;
                authPanelInstance.transform.SetParent(rootCanvas.transform, false);
            }
        }

        if (!authPanelInstance && authPanelPrefab && rootCanvas)
        {
            authPanelInstance = Instantiate(authPanelPrefab, rootCanvas.transform);
        }

        if (!authPanelInstance)
        {
            Debug.LogError("[Checklist] No AuthPanel found and no prefab assigned. Skipping auth.");
            onClosed?.Invoke(false);
            return;
        }

        authPanelInstance.transform.SetAsLastSibling();

        var ctrl = authPanelInstance.GetComponent<AuthPanelController>();
        if (ctrl)
        {
            if (!TryCallModeMethod(ctrl, startInSignup))
                ToggleAuthModeChildren(authPanelInstance, startInSignup);
            ctrl.OnClosed = onClosed;
        }
        else
        {
            ToggleAuthModeChildren(authPanelInstance, startInSignup);
        }

        authPanelInstance.SetActive(true);
        Debug.Log("[Checklist] AuthPanel shown");
    }

    void CloseAuthPanelIfExists()
    {
        if (!authPanelInstance) return;

        // If it’s an in-scene object (no prefab), just hide; otherwise destroy the spawned instance
        bool isSceneObj = authPanelPrefab == null && authPanelInstance.scene == gameObject.scene;
        if (isSceneObj) authPanelInstance.SetActive(false);
        else Destroy(authPanelInstance);

        authPanelInstance = null;
    }

    void UnloadChecklistScene(string reason)
    {
        LogLoadedScenes("[Checklist] Before unload (" + reason + ")");

        if (loadedAdditively)
        {
            var main = SceneManager.GetSceneByName(mainSceneName);
            if (main.IsValid())
            {
                SceneManager.SetActiveScene(main);
                Debug.Log("[Checklist] SetActiveScene → " + main.name);
            }
            else
            {
                Debug.LogWarning("[Checklist] Main scene '" + mainSceneName + "' not found! Check the name and that it’s in Build Settings.");
            }

            Scene current = gameObject.scene;
            Debug.Log("[Checklist] Unloading scene: " + current.name);
            SceneManager.UnloadSceneAsync(current).completed += _ =>
            {
                LogLoadedScenes("[Checklist] After unload");
            };
        }
        else
        {
            Debug.Log("[Checklist] Loading main scene Single: " + mainSceneName);
            SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
        }
    }

    bool TryCallModeMethod(object ctrl, bool startInSignup)
    {
        var type = ctrl.GetType();
        string mname = startInSignup ? "OpenSignupMode" : "OpenLoginMode";
        var mi = type.GetMethod(mname, BindingFlags.Public | BindingFlags.Instance);
        if (mi != null) { mi.Invoke(ctrl, null); return true; }
        return false;
    }

    void ToggleAuthModeChildren(GameObject panel, bool startInSignup)
    {
        var authMode = panel.transform.Find("Card/AuthMode");
        if (!authMode) authMode = panel.transform;
        var login = authMode.Find("LoginMode");
        var signup = authMode.Find("SignupMode");
        if (login) login.gameObject.SetActive(!startInSignup);
        if (signup) signup.gameObject.SetActive(startInSignup);
    }

    Canvas FindCanvasInMyScene()
    {
        var all = FindObjectsOfType<Canvas>(true);
        foreach (var c in all)
            if (c.gameObject.scene == gameObject.scene) return c;
        return null;
    }

    IEnumerator BindCanvasToMainCamera()
    {
        yield return null;

        if (!rootCanvas) yield break;
        Camera cam = Camera.main;
        if (!cam)
        {
            var arCamGO = GameObject.Find("ARCamera");
            if (arCamGO) cam = arCamGO.GetComponent<Camera>();
        }
        if (!cam)
        {
            var cams = FindObjectsOfType<Camera>(true);
            foreach (var c in cams) { if (c && c.isActiveAndEnabled) { cam = c; break; } }
        }

        if (cam && bindToMainCamera)
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            rootCanvas.worldCamera = cam;
            rootCanvas.overrideSorting = true;
            rootCanvas.sortingOrder = overlaySortingOrder;
            rootCanvas.planeDistance = planeDistance;
        }
        else
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.overrideSorting = true;
            rootCanvas.sortingOrder = overlaySortingOrder;
        }
    }

    void AutoWireButtons()
    {
        if (!backButton)
        {
            var t = rootCanvas ? rootCanvas.transform.Find("BackButton") : null;
            if (t) backButton = t.GetComponent<Button>();
        }
        if (!logoutButton)
        {
            var t = rootCanvas ? rootCanvas.transform.Find("LogoutButton") : null;
            if (t) logoutButton = t.GetComponent<Button>();
        }

        if (backButton)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(CloseChecklistButton);
        }
        else
        {
            Debug.LogWarning("[Checklist] BackButton not found. Assign it on the controller or name it 'BackButton' under the Canvas.");
        }

        if (logoutButton)
        {
            logoutButton.onClick.RemoveAllListeners();
            logoutButton.onClick.AddListener(SignOutAndReturnButton); // <- updated
        }
        else
        {
            Debug.LogWarning("[Checklist] LogoutButton not found. Assign it on the controller or name it 'LogoutButton' under the Canvas.");
        }
    }

    void BringButtonsToFront()
    {
        if (backButton) backButton.transform.SetAsLastSibling();
        if (logoutButton) logoutButton.transform.SetAsLastSibling();
    }

    void LogLoadedScenes(string header)
    {
        int count = SceneManager.sceneCount;
        string list = "";
        for (int i = 0; i < count; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            list += (i == 0 ? "" : ", ") + s.name + (s == SceneManager.GetActiveScene() ? " [ACTIVE]" : "");
        }
        Debug.Log(header + " | loaded: " + list);
    }
}