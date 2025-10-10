using UnityEngine;
using UnityEngine.XR.Management;
using System.Collections;

public class ForceXRInit : MonoBehaviour
{
    IEnumerator Start()
    {
        Debug.Log("[XR FORCE] Starting XR initialization...");

        var xrManager = XRGeneralSettings.Instance?.Manager;
        if (xrManager == null)
        {
            Debug.LogError("[XR FORCE] XRGeneralSettings.Instance is null!");
            yield break;
        }

        if (xrManager.activeLoader == null)
        {
            Debug.Log("[XR FORCE] No active loader, initializing...");
            yield return xrManager.InitializeLoader();
        }

        if (xrManager.activeLoader == null)
        {
            Debug.LogError("[XR FORCE] Failed to initialize XR Loader!");
            yield break;
        }

        Debug.Log($"[XR FORCE] ✓ Active Loader: {xrManager.activeLoader.name}");

        xrManager.StartSubsystems();
        Debug.Log("[XR FORCE] ✓ Subsystems started");

        // Wait a bit for subsystems to fully initialize
        yield return new WaitForSeconds(1f);

        // Enable AR components
        var arSession = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARSession>();
        if (arSession) arSession.enabled = true;

        var arCameraManager = FindFirstObjectByType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        if (arCameraManager) arCameraManager.enabled = true;

        Debug.Log("[XR FORCE] ✓ Initialization complete");
    }
}