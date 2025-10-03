using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ChecklistLauncher : MonoBehaviour
{
    public Button button;
    public string checklistSceneName = "ChecklistScene";
    public LoadSceneMode loadMode = LoadSceneMode.Additive;
    public bool makeChecklistSceneActive = true;

    static bool isOpening;
    static bool isOpen;

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (button) button.onClick.AddListener(OnClick);

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    void OnClick()
    {
        if (isOpening || isOpen) return; // prevent spam
        StartCoroutine(OpenChecklistScene());
    }

    IEnumerator OpenChecklistScene()
    {
        isOpening = true;

        var op = SceneManager.LoadSceneAsync(checklistSceneName, loadMode);
        while (!op.isDone) yield return null;

        if (makeChecklistSceneActive)
        {
            var scn = SceneManager.GetSceneByName(checklistSceneName);
            if (scn.IsValid()) SceneManager.SetActiveScene(scn);
        }

        isOpening = false;
    }

    void OnSceneLoaded(Scene scn, LoadSceneMode mode)
    {
        if (scn.name == checklistSceneName) isOpen = true;
    }

    void OnSceneUnloaded(Scene scn)
    {
        if (scn.name == checklistSceneName) isOpen = false;
    }
}