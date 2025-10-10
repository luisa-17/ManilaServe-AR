using UnityEngine;

public class HideWorldVisuals : MonoBehaviour
{
    public Transform contentAnchor; // center_anchor/ContentAnchor
    public bool hudOnly = true;

    void Start() { Apply(); }
    void LateUpdate() { if (hudOnly) Apply(); } // keep enforcing during Play

    [ContextMenu("Apply")]
    public void Apply()
    {
        if (!contentAnchor) return;

        // Hide world visuals but keep waypoints visible/active
        foreach (var r in contentAnchor.GetComponentsInChildren<Renderer>(true))
            if (!r.GetComponentInParent<NavigationWaypoint>())
                r.enabled = !hudOnly;

        foreach (var c in contentAnchor.GetComponentsInChildren<Collider>(true))
            if (!c.GetComponentInParent<NavigationWaypoint>())
                c.enabled = !hudOnly;

        // Hide PathRoot completely (world arrows/lines) in HUD
        var pathRoot = contentAnchor.Find("PathRoot");
        if (pathRoot) pathRoot.gameObject.SetActive(!hudOnly);
    }
}