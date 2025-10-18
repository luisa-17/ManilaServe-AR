
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Threading.Tasks; // add

public class ChecklistFAB : MonoBehaviour
{
    [Header("UI")]
    public Button button;
    public CanvasGroup canvasGroup;

    [Header("Auth Panel")]
    public AuthPanelController authPanelPrefab;
    public Transform uiParent; // drag your overlay Canvas here

    [Header("Navigation")]
    public string checklistSceneName = "ChecklistScene";
    public LoadSceneMode loadMode = LoadSceneMode.Additive;
    public bool makeChecklistSceneActive = true;

    [Header("Animation")]
    public float showDuration = 0.18f;
    public float hideDuration = 0.15f;
    public Vector2 safeAreaOffset = new Vector2(20, 20);

    RectTransform rt;
    private bool isOpening = false;

    void Reset() { AutoWire(); }

    void Awake()
    {
        AutoWire();
        HideImmediate();
        if (button) button.onClick.AddListener(OnClick);
        ApplySafeArea();
    }

    void Start() => Show();

    void AutoWire()
    {
        if (!button) button = GetComponent<Button>();
        if (!canvasGroup)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (!canvasGroup) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        rt = GetComponent<RectTransform>();
    }

    void ApplySafeArea()
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = new Vector2(-safeAreaOffset.x, safeAreaOffset.y);
    }

    public void Show() { StopAllCoroutines(); StartCoroutine(FadeTo(1f, showDuration)); }
    public void Hide() { StopAllCoroutines(); StartCoroutine(FadeTo(0f, hideDuration)); }

    void HideImmediate()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        transform.localScale = Vector3.one * 0.9f;
    }

    IEnumerator FadeTo(float targetAlpha, float dur)
    {
        float start = canvasGroup.alpha;
        float t = 0f;
        Vector3 startScale = transform.localScale;
        Vector3 endScale = Vector3.one;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, k);
            transform.localScale = Vector3.Lerp(startScale, endScale, k);
            yield return null;
        }

        canvasGroup.alpha = targetAlpha;
        transform.localScale = endScale;
        bool visible = targetAlpha > 0.99f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }

    async void OnClick()
    {
        if (isOpening) return;
        isOpening = true;

canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 0.8f;

        var initOk = await AuthService.EnsureInitializedAsync();
        if (!initOk)
        {
            Debug.LogError("Auth init failed");
            EnableChecklistButtonAgain();
            return;
        }

        await AuthService.WaitForAuthRestorationAsync(1500);
        Debug.Log($"Auth IsSignedIn = {AuthService.IsSignedIn}");

        if (AuthService.IsSignedIn)
        {
            // Already signed in -> go straight to checklist (Single load)
            StartCoroutine(OpenChecklistScene());
            return;
        }

        // Not signed in -> open auth popup
        var panel = SpawnAuthPanel();
        if (panel == null)
        {
            Debug.LogError("AuthPanel prefab not assigned on ChecklistFAB.");
            EnableChecklistButtonAgain();
            isOpening = false;
            return;
        }

        panel.OnClosed += success =>
        {
            if (success)
            {
                StartCoroutine(OpenChecklistScene());
            }
            else
            {
                EnableChecklistButtonAgain();
                isOpening = false;
            }
        };
    }

    AuthPanelController SpawnAuthPanel()
    {
        if (!authPanelPrefab) return null;
        Transform parent = uiParent;
        if (parent == null)
        {
            // Fallback: find a Canvas, but this may pick a world-space canvas.
            // Prefer explicitly assigning uiParent to your overlay Canvas in the Inspector.
            var canvas = FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas != null) parent = canvas.transform;
        }


        var panel = Instantiate(authPanelPrefab, parent, false);
        panel.gameObject.SetActive(true);
        panel.OpenLoginMode();
        return panel;
    }

    IEnumerator OpenChecklistScene()
    {
        // leave FAB disabled while transitioning
        var ui = FindFirstObjectByType<ManilaServeUI>();
        if (ui != null) ChecklistContext.SelectedOffice = ui.GetSelectedOffice();

        if (loadMode == LoadSceneMode.Single)
        {
            SceneManager.LoadScene(checklistSceneName, LoadSceneMode.Single);
            yield break;
        }

        AsyncOperation op = SceneManager.LoadSceneAsync(checklistSceneName, LoadSceneMode.Additive);
        while (!op.isDone) yield return null;

        if (makeChecklistSceneActive)
        {
            var checklist = SceneManager.GetSceneByName(checklistSceneName);
            if (checklist.IsValid())
                SceneManager.SetActiveScene(checklist);
        }
    }

    public void EnableChecklistButtonAgain()
    {
        isOpening = false;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;
    }
}