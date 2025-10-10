using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;

public class CameraTimeoutFix : MonoBehaviour
{
    private ARCameraManager cameraManager;

    void Awake()
    {
        cameraManager = GetComponent<ARCameraManager>();

        // Force camera manager to wait longer
        if (cameraManager)
        {
            cameraManager.enabled = false;
        }
    }

    IEnumerator Start()
    {
        // Wait for XR to fully initialize
        yield return new WaitForSeconds(2f);

        // Now enable camera manager
        if (cameraManager)
        {
            cameraManager.enabled = true;
            Debug.Log("[TIMEOUT FIX] Camera manager enabled after delay");
        }
    }
}