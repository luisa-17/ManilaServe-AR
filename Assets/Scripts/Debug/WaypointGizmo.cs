using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(NavigationWaypoint))]
public class WaypointGizmo : MonoBehaviour
{
    public float radius = 0.25f;
    public bool drawLinks = true;

    public Color office = new Color(0.0f, 0.8f, 0.2f, 0.9f);
    public Color corridor = new Color(0.1f, 0.7f, 1.0f, 0.9f);
    public Color junction = new Color(1.0f, 0.9f, 0.2f, 0.9f);
    public Color stairs = new Color(1.0f, 0.2f, 0.8f, 0.9f);

    void OnDrawGizmos()
    {
        var wp = GetComponent<NavigationWaypoint>();
        if (!wp) return;

        Color c = corridor;
        switch (wp.waypointType)
        {
            case WaypointType.Office: c = office; break;
            case WaypointType.Junction: c = junction; break;
            case WaypointType.Stairs: c = stairs; break;
            case WaypointType.Corridor: c = corridor; break;
        }

        var p = transform.position + Vector3.up * 0.05f;
        Gizmos.color = c;
        Gizmos.DrawSphere(p, radius);

        if (drawLinks && wp.connectedWaypoints != null)
        {
            Gizmos.color = new Color(c.r, c.g, c.b, 0.6f);
            foreach (var n in wp.connectedWaypoints)
                if (n) Gizmos.DrawLine(p, n.transform.position + Vector3.up * 0.05f);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        var wp = GetComponent<NavigationWaypoint>();
        if (!wp) return;
        string label = !string.IsNullOrEmpty(wp.officeName) ? wp.officeName : wp.waypointName;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.7f, label);
    }
#endif
}