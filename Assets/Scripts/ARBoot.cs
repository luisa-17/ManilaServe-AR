using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARFoundation;

public class XRBootOnce : MonoBehaviour
{
    IEnumerator Start()
    {
        var xr = XRGeneralSettings.Instance?.Manager;
        if (xr == null) { Debug.LogError("[XR] Manager is null"); yield break; }
    
    xr.InitializeLoaderSync();
        Debug.Log("[XR] loader after init: " + (xr.activeLoader ? xr.activeLoader.name : "null"));
        if (xr.activeLoader == null) { Debug.LogError("[XR] No active loader. Enable Google ARCore on Android."); yield break; }

        xr.StartSubsystems();

        yield return ARSession.CheckAvailability();
        if (ARSession.state == ARSessionState.NeedsInstall)
            yield return ARSession.Install();
        Debug.Log("[XR] AR state: " + ARSession.state);
    }
}