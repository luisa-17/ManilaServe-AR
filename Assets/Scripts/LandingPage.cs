

using UnityEngine;
using UnityEngine.SceneManagement;

public class LandingPage : MonoBehaviour
{
    public float delay = 5f; // how long to show before switching

    void Start()
    {
        Invoke(nameof(LoadSampleScene), delay);
    }

    void LoadSampleScene()
    {
        SceneManager.LoadScene("SampleScene");
        // replace "MainScene" with the exact name of your navigation scene
    }
}

