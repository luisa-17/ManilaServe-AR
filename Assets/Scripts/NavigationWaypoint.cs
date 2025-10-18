
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using System.Linq;


public class NavigationWaypoint : MonoBehaviour
{
    [Header("Waypoint Info")]
    public string waypointName;
    public string officeName;
    public WaypointType waypointType = WaypointType.Office;

    [TextArea] public string[] services;

    [Header("Connections")]
    public List<NavigationWaypoint> connectedWaypoints = new List<NavigationWaypoint>();
    [Tooltip("Max distance to consider when auto-connecting")]
    public float connectionDistance = 5f;

    [Header("Wall Awareness")]
    [Tooltip("Only this layer will be treated as 'walls' for LOS checks")]
    public LayerMask wallLayerMask;          // set to Walls in Inspector
    [Tooltip("Small radius used when sampling along the LOS to catch thin walls")]
    public float losCheckRadius = 0.2f;      // 0.15–0.3 typically
    [Tooltip("Spacing (m) between LOS samples")]
    public float losSampleSpacing = 0.3f;    // denser = more accurate

    [Header("Visual")]
    public Color waypointColor = Color.cyan;
    public bool showInEditor = true;


    void Reset()
    {
        waypointName = gameObject.name;
        officeName = ExtractOfficeNameFromGameObjectName();

        int wallsLayer = LayerMask.NameToLayer("Walls");
        if (wallsLayer >= 0) wallLayerMask = 1 << wallsLayer;
    }

    void Awake()
    {
        if (string.IsNullOrEmpty(waypointName)) waypointName = gameObject.name;
        if (string.IsNullOrEmpty(officeName)) officeName = ExtractOfficeNameFromGameObjectName();
    }

    string ExtractOfficeNameFromGameObjectName()
    {
        string n = gameObject.name;
        n = n.Replace("Waypoint_", "").Replace("ImageTarget_", "").Replace("Target_", "");
        return n;
    }

    // --------- LOS + link helpers ---------
    public bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 a = from + Vector3.up * 0.6f;
        Vector3 b = to + Vector3.up * 0.6f;

        if (Physics.Linecast(a, b, wallLayerMask))
            return false;

        float dist = Vector3.Distance(a, b);
        int samples = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(0.05f, losSampleSpacing)));
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 p = Vector3.Lerp(a, b, t);
            if (Physics.CheckSphere(p, losCheckRadius, wallLayerMask))
                return false;
        }
        return true;
    }

    public void AddLinkBidirectional(NavigationWaypoint other)
    {
        if (!other || other == this) return;

        connectedWaypoints ??= new List<NavigationWaypoint>();
        if (!connectedWaypoints.Contains(other)) connectedWaypoints.Add(other);

        other.connectedWaypoints ??= new List<NavigationWaypoint>();
        if (!other.connectedWaypoints.Contains(this)) other.connectedWaypoints.Add(this);
    }

    public void RemoveLinkBidirectional(NavigationWaypoint other)
    {
        if (!other) return;
        connectedWaypoints?.Remove(other);
        other.connectedWaypoints?.Remove(this);
    }

    public void RebuildConnectionsThisOnly(NavigationWaypoint[] all)
    {
        connectedWaypoints ??= new List<NavigationWaypoint>();
        connectedWaypoints.Clear();

        foreach (var wp in all)
        {
            if (wp == this) continue;
            float d = Vector3.Distance(transform.position, wp.transform.position);
            if (d <= connectionDistance && HasLineOfSight(transform.position, wp.transform.position))
            {
                AddLinkBidirectional(wp);
            }
        }

        if (waypointType == WaypointType.Stairs) LinkStairPairs();
    }

    public static int RebuildConnectionsAll()
    {
        var all = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
    
foreach (var wp in all)
        {
            wp.connectedWaypoints ??= new List<NavigationWaypoint>();
            wp.connectedWaypoints.Clear();

            if (wp.wallLayerMask.value == 0)
                wp.wallLayerMask = LayerMask.GetMask("Walls");
        }

        int links = 0;

        for (int i = 0; i < all.Length; i++)
        {
            for (int j = i + 1; j < all.Length; j++)
            {
                var a = all[i];
                var b = all[j];

                float maxD = Mathf.Min(a.connectionDistance, b.connectionDistance);
                float d = Vector3.Distance(a.transform.position, b.transform.position);

                if (d <= maxD && a.HasLineOfSight(a.transform.position, b.transform.position))
                {
                    a.AddLinkBidirectional(b);
                    links++;
                }
            }
        }

        foreach (var wp in all)
            if (wp.waypointType == WaypointType.Stairs)
                wp.LinkStairPairs();

        return links;
    }

    public void ValidateLinksThisOnly()
    {
        if (connectedWaypoints == null) return;
        for (int i = connectedWaypoints.Count - 1; i >= 0; i--)
        {
            var other = connectedWaypoints[i];
            if (!other || !HasLineOfSight(transform.position, other.transform.position))
                RemoveLinkBidirectional(other);
        }
    }

    public static int MakeLinksBidirectional(bool removeInvalid = true, bool deduplicate = true)
    {
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(wp => wp && wp.gameObject.scene.IsValid())
        .ToArray();

int added = 0, removed = 0, deduped = 0;

        foreach (var a in all)
        {
            a.connectedWaypoints ??= new List<NavigationWaypoint>();

            // Remove invalid/self links
            if (removeInvalid)
            {
                for (int i = a.connectedWaypoints.Count - 1; i >= 0; i--)
                {
                    var b = a.connectedWaypoints[i];
                    if (b == null || !b.gameObject.scene.IsValid() || b == a)
                    {
                        a.connectedWaypoints.RemoveAt(i);
                        removed++;
                    }
                }
            }

            // De-duplicate
            if (deduplicate && a.connectedWaypoints.Count > 1)
            {
                int before = a.connectedWaypoints.Count;
                a.connectedWaypoints = a.connectedWaypoints.Distinct().ToList();
                deduped += before - a.connectedWaypoints.Count;
            }
        }

        // Ensure reverse links
        foreach (var a in all)
        {
            foreach (var b in a.connectedWaypoints)
            {
                if (b == null) continue;
                b.connectedWaypoints ??= new List<NavigationWaypoint>();
                if (!b.connectedWaypoints.Contains(a))
                {
                    b.connectedWaypoints.Add(a);
                    added++;
                }
            }
        }

        Debug.Log($"[Waypoints] MakeLinksBidirectional: added reverse={added}, removed invalid={removed}, deduped={deduped}.");
        return added;
    }

    public static void ReportGraphIssues()
    {
        var all = Resources.FindObjectsOfTypeAll<NavigationWaypoint>()
        .Where(wp => wp && wp.gameObject.scene.IsValid())
        .ToArray();
    
int oneSided = 0, selfRefs = 0, nullRefs = 0, dups = 0, noConn = 0;

        foreach (var a in all)
        {
            if (a.connectedWaypoints == null || a.connectedWaypoints.Count == 0)
            {
                noConn++;
                Debug.LogWarning($"⚠ {a.name} has NO connections!");
                continue;
            }

            var seen = new HashSet<NavigationWaypoint>();
            foreach (var b in a.connectedWaypoints)
            {
                if (b == null) { nullRefs++; continue; }
                if (b == a) { selfRefs++; continue; }
                if (!seen.Add(b)) dups++;

                if (b.connectedWaypoints == null || !b.connectedWaypoints.Contains(a))
                {
                    oneSided++;
                    Debug.LogWarning($"↔ One-sided: {a.name} → {b.name} (missing reverse).");
                }
            }
        }

        Debug.Log($"[Waypoints] Report: oneSided={oneSided}, self={selfRefs}, null={nullRefs}, dups={dups}, noConn={noConn}, nodes={all.Length}");
    }

    public void RemoveAllLinksThisOnly()
    {
        if (connectedWaypoints == null) return;
        foreach (var other in new List<NavigationWaypoint>(connectedWaypoints))
            RemoveLinkBidirectional(other);
        connectedWaypoints.Clear();
    }

    // Pair stair nodes across floors (robust)
    public void LinkStairPairs()
    {
        // Tolerances
        float maxHorizontal = 2.0f;  // meters
        float minVertical = 1.2f;  // meters

        var all = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        Vector2 myXZ = new Vector2(transform.position.x, transform.position.z);
        float myY = transform.position.y;

        // Prefer exact id if you added a 'stairId' field
        string myId = null;
        var t = GetType();
        var fi = t.GetField("stairId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (fi != null) myId = fi.GetValue(this) as string;

        NavigationWaypoint best = null;
        float bestScore = float.MaxValue;

        foreach (var wp in all)
        {
            if (wp == null || wp == this) continue;
            if (wp.waypointType != WaypointType.Stairs) continue;

            float v = Mathf.Abs(wp.transform.position.y - myY);
            if (v < minVertical) continue; // must be another floor

            float h = Vector2.Distance(new Vector2(wp.transform.position.x, wp.transform.position.z), myXZ);
            if (h > maxHorizontal) continue;

            // If stairId exists, require same id
            if (!string.IsNullOrEmpty(myId))
            {
                string otherId = null;
                var t2 = wp.GetType();
                var fi2 = t2.GetField("stairId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (fi2 != null) otherId = fi2.GetValue(wp) as string;
                if (!string.Equals(myId, otherId, System.StringComparison.OrdinalIgnoreCase)) continue;
            }

            float score = h + v * 0.1f; // prefer close horizontally
            if (score < bestScore)
            {
                bestScore = score;
                best = wp;
            }
        }

        if (best != null)
        {
            AddLinkBidirectional(best);
            // Also ensure each stairs node has links to nearby corridor nodes on its floor
            LinkLocalCorridorNeighbors(this);
            LinkLocalCorridorNeighbors(best);
            // Debug.Log($"[STAIRS] Paired {name} ↔ {best.name}");
        }
    }

    void LinkLocalCorridorNeighbors(NavigationWaypoint stair)
    {
        var all = FindObjectsByType<NavigationWaypoint>(FindObjectsSortMode.None);
        float radius = 3.0f; // meters
        float yTol = 0.75f;
        Vector2 sxz = new Vector2(stair.transform.position.x, stair.transform.position.z);
        float sy = stair.transform.position.y;

        foreach (var wp in all)
        {
            if (wp == null || wp == stair) continue;
            if (wp.waypointType == WaypointType.Stairs) continue;

            float v = Mathf.Abs(wp.transform.position.y - sy);
            if (v > yTol) continue;

            float h = Vector2.Distance(new Vector2(wp.transform.position.x, wp.transform.position.z), sxz);
            if (h <= radius)
                stair.AddLinkBidirectional(wp);
        }
    }


    // --------- Gizmos ---------
    void OnDrawGizmos()
    {
        if (!showInEditor) return;

        Gizmos.color = waypointType == WaypointType.Office ? Color.green :
                       waypointType == WaypointType.Stairs ? Color.magenta :
                       waypointColor;
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.05f, 0.15f);

        if (connectedWaypoints != null)
        {
            foreach (var n in connectedWaypoints)
            {
                if (!n) continue;
                bool clear = HasLineOfSight(transform.position, n.transform.position);
                Gizmos.color = clear ? Color.yellow : Color.red;
                Gizmos.DrawLine(transform.position + Vector3.up * 0.05f,
                                n.transform.position + Vector3.up * 0.05f);
            }
        }

        Gizmos.color = new Color(waypointColor.r, waypointColor.g, waypointColor.b, 0.15f);
        Gizmos.DrawWireSphere(transform.position, connectionDistance);

#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(waypointName))
        Handles.Label(transform.position + Vector3.up * 0.25f, waypointName);
#endif
    }
}