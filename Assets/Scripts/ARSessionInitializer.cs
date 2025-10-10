using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

public class ARSessionInitializer : MonoBehaviour
{
    ARSession arSession;
    ARCameraManager arCameraManager;
    Camera arCamera;


    IEnumerator Start()
    {
        Debug.Log("=== AR SESSION INITIALIZATION ===");

        // Initialize XR loader
        yield return InitializeXRLoaderCoroutine();

        // Find AR components
        arSession = FindObjectOfType<ARSession>();
        if (!arSession) { Debug.LogError("❌ AR Session component not found!"); yield break; }
        arCameraManager = FindObjectOfType<ARCameraManager>();
        if (!arCameraManager) { Debug.LogError("❌ AR Camera Manager not found!"); yield break; }
        arCamera = arCameraManager.GetComponent<Camera>();
        if (!arCamera) { Debug.LogError("❌ Camera component not found!"); yield break; }

        // Enable everything
        arSession.enabled = true;
        arCameraManager.enabled = true;
        arCamera.enabled = true;

        CheckCameraPermission();

        // Check availability/install before expecting tracking
        yield return ARSession.CheckAvailability();
        if (ARSession.state == ARSessionState.NeedsInstall)
            yield return ARSession.Install();

        StartCoroutine(CheckARSessionState());
    }

    IEnumerator InitializeXRLoaderCoroutine()
    {
        Debug.Log("[XR] Initializing XR Loader...");
        var xr = XRGeneralSettings.Instance?.Manager;
        if (xr == null) { Debug.LogError("[XR] ❌ XRGeneralSettings.Instance is null!"); yield break; }

        if (xr.activeLoader != null)
        {
            Debug.Log($"[XR] ✓ XR Loader already active: {xr.activeLoader.name}");
            yield break;
        }

        Debug.Log("[XR] Starting XR Loader initialization...");
        yield return xr.InitializeLoader(); // <- coroutine, not await

        if (xr.activeLoader == null)
        {
            Debug.LogError("[XR] ❌ Failed to initialize XR Loader!");
            yield break;
        }

        Debug.Log($"[XR] ✓ XR Loader initialized: {xr.activeLoader.name}");
        xr.StartSubsystems();
        Debug.Log("[XR] ✓ XR Subsystems started");
    }

    void CheckCameraPermission()
    {
#if UNITY_ANDROID
    if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
#endif
    }

    IEnumerator CheckARSessionState()
    {
        float timeout = 10f, elapsed = 0f;
        while (elapsed < timeout)
        {
            if (!arSession) yield break;

            var state = ARSession.state;
            Debug.Log($"[AR] Session State: {state} (elapsed: {elapsed:F1}s)");

            if (state == ARSessionState.SessionTracking)
            {
                var bg = arCamera?.GetComponent<ARCameraBackground>();
                if (arCamera && !arCamera.enabled) arCamera.enabled = true;
                if (bg && !bg.enabled) bg.enabled = true;
                Debug.Log("✅ AR Session is tracking - camera should be working!");
                yield break;
            }
            if (state == ARSessionState.Unsupported) { Debug.LogError("❌ ARCore not supported"); yield break; }
            if (state == ARSessionState.NeedsInstall) { Debug.LogWarning("⚠️ ARCore needs to be installed"); yield break; }

            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
        }
        Debug.LogError($"❌ AR Session failed to initialize after {timeout}s! Final state: {ARSession.state}");
    }

    void Update()
    {
        if (arCamera && !arCamera.enabled) arCamera.enabled = true;
    }
}