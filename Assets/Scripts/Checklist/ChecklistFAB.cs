using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class ChecklistFAB : MonoBehaviour
{
    [Header("UI")]
    public Button button;
    public CanvasGroup canvasGroup;

    [Header("Navigation")]
    [Tooltip("The scene to load for the checklist page")]
    public string checklistSceneName = "ChecklistScene";

    [Tooltip("Additive keeps your AR scene alive. Switch to Single for a hard scene change.")]
    public LoadSceneMode loadMode = LoadSceneMode.Additive;

    [Tooltip("If additive, make the Checklist scene the active one after load")]
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

    void OnClick()
    {
        if (isOpening) return;
        StartCoroutine(OpenChecklistScene());
    }

    IEnumerator OpenChecklistScene()
    {
        isOpening = true;

        // 🔹 Completely disable FAB's raycasts to allow clicks behind (signup/login)
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 0.8f; // 🔹 optional visual dim (can remove)

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

    // 🔹 Called when login/signup closes or checklist scene exits
    public void EnableChecklistButtonAgain()
    {
        isOpening = false;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f; // 🔹 restore full visibility
    }
}
