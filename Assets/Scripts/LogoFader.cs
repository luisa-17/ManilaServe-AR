using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class LogoFader : MonoBehaviour
{
    public CanvasGroup logoGroup;
    public float fadeDuration = 1.5f;
    public float displayTime = 2f; // how long logo stays fully visible
    public string nextScene = "MainScene"; // change to your main scene name

    void Start()
    {
        StartCoroutine(FadeSequence());
    }

    IEnumerator FadeSequence()
    {
        // Fade In
        float t = 0;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            logoGroup.alpha = Mathf.Clamp01(t / fadeDuration);
            yield return null;
        }

        // Hold
        yield return new WaitForSeconds(displayTime);

        // Fade Out
        t = 0;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            logoGroup.alpha = 1 - Mathf.Clamp01(t / fadeDuration);
            yield return null;
        }

        // Load Main Scene
        SceneManager.LoadScene(nextScene);
    }
}