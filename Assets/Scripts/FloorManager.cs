
using UnityEngine;
using System.Collections;

public class FloorManager : MonoBehaviour
{
    [Header("Assign Floor Roots")]
    public GameObject groundFloor;
    public GameObject secondFloor;

    [Header("Fade Settings")]
    public float fadeDuration = 1.0f;

    private Material[] groundMats;
    private Material[] secondMats;

    void Start()
    {
        groundMats = GetMaterials(groundFloor);
        secondMats = GetMaterials(secondFloor);

        // Start with ground visible, second hidden
        SetAlpha(secondMats, 0f);
        SetAlpha(groundMats, 1f);
    }

    // Collect all materials under a floor
    Material[] GetMaterials(GameObject floorRoot)
    {
        if (floorRoot == null) return new Material[0];
        var renderers = floorRoot.GetComponentsInChildren<Renderer>(true);
        var mats = new System.Collections.Generic.List<Material>();
        foreach (var r in renderers) mats.AddRange(r.materials);
        return mats.ToArray();
    }

    // Set alpha of a whole floor
    void SetAlpha(Material[] mats, float alpha)
    {
        foreach (var m in mats)
        {
            if (m.HasProperty("_Color"))
            {
                Color c = m.color;
                c.a = alpha;
                m.color = c;
            }
        }
    }

    // Public method to trigger fade
    public void CrossFadeFloors(bool goingUp)
    {
        StopAllCoroutines(); // stop old fades if any

        if (goingUp)
        {
            StartCoroutine(FadeRoutine(groundMats, 1f, 0f)); // fade out ground
            StartCoroutine(FadeRoutine(secondMats, 0f, 1f)); // fade in second
        }
        else
        {
            StartCoroutine(FadeRoutine(groundMats, 0f, 1f)); // fade in ground
            StartCoroutine(FadeRoutine(secondMats, 1f, 0f)); // fade out second
        }
    }

    // Coroutine fade animation
    private IEnumerator FadeRoutine(Material[] mats, float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float alpha = Mathf.Lerp(from, to, t);
            SetAlpha(mats, alpha);
            yield return null;
        }
        SetAlpha(mats, to); // ensure exact final value
    }
}
