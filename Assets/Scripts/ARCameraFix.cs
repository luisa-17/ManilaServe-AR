using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class ARCameraFix : MonoBehaviour
{
    void Start()
    {
        var arCameraManager = GetComponent<ARCameraManager>();
        var arCameraBackground = GetComponent<ARCameraBackground>();
        var camera = GetComponent<Camera>();

        if (camera)
        {
            // Force camera to see everything
            camera.cullingMask = -1; // Everything
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            Debug.Log("[AR FIX] Camera culling mask reset to Everything");
        }

        if (arCameraBackground)
        {
            arCameraBackground.enabled = true;
            Debug.Log("[AR FIX] AR Camera Background enabled");
        }

        if (arCameraManager)
        {
            arCameraManager.enabled = true;
            Debug.Log("[AR FIX] AR Camera Manager enabled");
        }

        // Force update every frame
        StartCoroutine(ForceBackgroundUpdate());
    }

    System.Collections.IEnumerator ForceBackgroundUpdate()
    {
        var arCameraBackground = GetComponent<ARCameraBackground>();

        while (true)
        {
            yield return new WaitForSeconds(0.5f);

            if (arCameraBackground && !arCameraBackground.enabled)
            {
                Debug.LogWarning("[AR FIX] AR Camera Background was disabled! Re-enabling...");
                arCameraBackground.enabled = true;
            }
        }
    }
}