
using UnityEngine;

public class MarkerDetection : MonoBehaviour
{
    [Header("Location Info")]
    public string locationName;
    public string departmentDescription;

    void Start()
    {
        Debug.Log($"MarkerDetection script loaded for: {locationName}");
    }

    // This will be called when marker is detected (we'll add Vuforia integration later)
    public void OnMarkerDetected()
    {
        Debug.Log($"Detected marker: {locationName}");
        Debug.Log($"Location: {departmentDescription}");

        // Show some visual feedback
        ShowLocationInfo();
    }

    public void OnMarkerLost()
    {
        Debug.Log($"Lost marker: {locationName}");
        HideLocationInfo();
    }

    void ShowLocationInfo()
    {
        // For now, just log to console
        Debug.Log($"Now at: {departmentDescription}");

        // Later we can add UI updates here
        // UIManager.Instance?.ShowLocationInfo(departmentDescription);
    }

    void HideLocationInfo()
    {
        Debug.Log("Marker tracking lost");

        // Later we can add UI updates here
        // UIManager.Instance?.HideLocationInfo();
    }
}
