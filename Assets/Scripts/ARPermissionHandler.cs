using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class ARPermissionHandler : MonoBehaviour
{
    void Start()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("Requesting camera permission...");
            Permission.RequestUserPermission(Permission.Camera);
        }
        else
        {
            Debug.Log("✓ Camera permission already granted");
        }
#endif
    }
}