

using UnityEngine;
using Vuforia;
public class VuforiaTargetRelay : MonoBehaviour
{
    public SmartNavigationSystem nav;
    ObserverBehaviour _observer;
    bool _wasTracked;

    void Awake()
    {
        _observer = GetComponent<ObserverBehaviour>();
        if (nav == null) nav = FindFirstObjectByType<SmartNavigationSystem>();
    }

    void OnEnable()
    {
        if (_observer == null) _observer = GetComponent<ObserverBehaviour>();
        if (_observer != null) _observer.OnTargetStatusChanged += OnStatusChanged;
    }

    void OnDisable()
    {
        if (_observer != null) _observer.OnTargetStatusChanged -= OnStatusChanged;
    }

    void OnStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        bool isTracked = status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED;

        if (isTracked)
        {
            Camera cam = (nav != null && nav.arCamera != null) ? nav.arCamera : Camera.main;
            Vector3 userPos = cam != null ? cam.transform.position : behaviour.transform.position;
            nav?.OnImageTargetDetected(behaviour.TargetName, userPos);
        }
        else if (_wasTracked)
        {
            nav?.OnImageTargetLost();
        }

        _wasTracked = isTracked;
    }
}
