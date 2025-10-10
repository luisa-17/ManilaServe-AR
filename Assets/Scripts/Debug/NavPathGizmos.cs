using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
public class NavPathGizmos : MonoBehaviour
{
    public SmartNavigationSystem nav;
    public Color pathColor = new Color(1f, 0.4f, 0f, 1f);
    public Color nextColor = Color.magenta;
    public float yOffset = 0.06f;

    void OnDrawGizmos()
    {
        if (!nav) nav = FindFirstObjectByType<SmartNavigationSystem>();
        if (!nav) return;

        var pts = nav.GetCurrentPathWorld();   // add this accessor (next step)
        if (pts == null || pts.Count < 2) return;

        Gizmos.color = pathColor;
        for (int i = 0; i < pts.Count - 1; i++)
            Gizmos.DrawLine(pts[i] + Vector3.up * yOffset, pts[i + 1] + Vector3.up * yOffset);

        // Show the next point marker
        if (nav.TryGetNextPoint(out var next, 0.5f))
        {
            Gizmos.color = nextColor;
            Gizmos.DrawSphere(next + Vector3.up * yOffset, 0.25f);
        }
    }
}