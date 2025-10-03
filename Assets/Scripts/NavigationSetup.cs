using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NavigationSetup : MonoBehaviour
{
    void Start()
    {
        SetupNavigation();
    }

    void SetupNavigation()
    {
        SmartNavigationSystem navSystem = FindFirstObjectByType<SmartNavigationSystem>();
        if (navSystem == null)
        {
            GameObject navObj = new GameObject("NavigationSystem");
            navSystem = navObj.AddComponent<SmartNavigationSystem>();
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            Button[] buttons = canvas.GetComponentsInChildren<Button>();
            TextMeshProUGUI[] texts = canvas.GetComponentsInChildren<TextMeshProUGUI>();

            foreach (Button btn in buttons)
            {
                if (btn.name.Contains("Navigate")) navSystem.navigateButton = btn;
                if (btn.name.Contains("Stop")) navSystem.stopButton = btn;
            }

            foreach (TextMeshProUGUI text in texts)
            {
                if (text.name.Contains("Status")) navSystem.statusText = text;
            }
        }

        Debug.Log("Enhanced 3D Navigation system setup complete!");
    }
}
