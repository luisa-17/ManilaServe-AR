using UnityEngine;

public class CameraCullingProfile : MonoBehaviour
{
    [Tooltip("Layers to hide from this camera at runtime")]
    public string[] layersToHide = { "Walls", "BuildingVisuals" };

    [Tooltip("Apply also when playing in Editor (handy to test)")]
    public bool forceInEditor = false;

    void Awake()
    {

#if UNITY_ANDROID || UNITY_IOS
Apply();
#else
        if (forceInEditor) Apply();
#endif
    }
    void Apply()
    {
        var cam = GetComponent<Camera>();
        if (!cam) return;

        int mask = cam.cullingMask;
        foreach (var name in layersToHide)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0) mask &= ~(1 << layer); // remove layer from culling
        }
        cam.cullingMask = mask;
    }
}