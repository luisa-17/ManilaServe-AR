#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(NavigationWaypoint))]
public class NavigationWaypointInspector : Editor
{
public override void OnInspectorGUI()
{
base.OnInspectorGUI();
    GUILayout.Space(8);
    EditorGUILayout.LabelField("Graph Tools", EditorStyles.boldLabel);

    if (GUILayout.Button("Analyze Waypoint Spacing"))
        MenuAnalyzeSpacing();

    if (GUILayout.Button("Rebuild (Distance Only, Global...)"))
        MenuRebuildDistanceOnly_Global();

    if (GUILayout.Button("Rebuild (K-Nearest + LOS, Global...)"))
        MenuRebuildKNN_LOS_Global();

    if (GUILayout.Button("Rebuild Connections (All Waypoints, Per-Node Dist + LOS)"))
        MenuRebuildAll_PerNode();

    if (GUILayout.Button("Report Blocked Pairs (first 30)"))
        MenuReportBlockedPairs();

    if (GUILayout.Button("Report Waypoints Inside Walls"))
        MenuReportNodesInsideWalls();
}

// 1) Analyze nearest-neighbor spacing to pick a good distance
[MenuItem("Tools/Navigation/Analyze Waypoint Spacing")]
public static void MenuAnalyzeSpacing()
{
    var all = Object.FindObjectsOfType<NavigationWaypoint>();
    if (all.Length < 2) { Debug.LogWarning("Need at least 2 waypoints."); return; }

    var nn = new List<float>(all.Length);
    foreach (var a in all)
    {
        float best = float.PositiveInfinity;
        foreach (var b in all)
        {
            if (a == b) continue;
            float d = Vector3.Distance(a.transform.position, b.transform.position);
            if (d < best) best = d;
        }
        if (best < float.PositiveInfinity) nn.Add(best);
    }

    nn.Sort();
    float min = nn[0];
    float median = nn[nn.Count / 2];
    float p90 = nn[(int)Mathf.Clamp(Mathf.Round(0.9f * (nn.Count - 1)), 0, nn.Count - 1)];
    float max = nn[nn.Count - 1];
    float recommend = Mathf.Ceil(p90 * 1.2f); // 20% margin

    Debug.Log($"[Spacing] count={nn.Count} min={min:F2} med={median:F2} p90={p90:F2} max={max:F2} ? recommended global distance ? {recommend:F1}");
}

// Helper to pick a global distance from a small dialog
static float AskGlobalDistance(float fallback = 30f)
{
    int sel = EditorUtility.DisplayDialogComplex(
        "Global Distance for Rebuild",
        "Choose a global max distance (meters) for linking waypoints.\nTip: run 'Analyze Waypoint Spacing' first.",
        "30 m", "20 m", "50 m");
    switch (sel)
    {
        case 0: return 30f;
        case 1: return 20f;
        default: return 50f;
    }
}

// 2) Rebuild distance-only with a single global distance (ignores walls)
[MenuItem("Tools/Navigation/Rebuild (Distance Only, Global...)")]
public static void MenuRebuildDistanceOnly_Global()
{
    float globalMax = AskGlobalDistance(30f);
    var all = Object.FindObjectsOfType<NavigationWaypoint>();

    foreach (var n in all)
    {
        Undo.RecordObject(n, "Rebuild (Distance Only, Global)");
        n.connectedWaypoints ??= new List<NavigationWaypoint>();
        n.connectedWaypoints.Clear();
    }

    int links = 0;
    for (int i = 0; i < all.Length; i++)
    for (int j = i + 1; j < all.Length; j++)
    {
        var a = all[i];
        var b = all[j];
        float d = Vector3.Distance(a.transform.position, b.transform.position);
        if (d <= globalMax)
        {
            AddLinkBoth(a, b);
            links++;
        }
    }

    foreach (var n in all) EditorUtility.SetDirty(n);
    Debug.Log($"[Waypoint Graph] Distance-only (global {globalMax} m) links: {links} across {all.Length} waypoints");
}

// 3) Rebuild K-nearest neighbors with LOS and a global max (robust & simple)
[MenuItem("Tools/Navigation/Rebuild (K-Nearest + LOS, Global...)")]
public static void MenuRebuildKNN_LOS_Global()
{
    float globalMax = AskGlobalDistance(30f);
    const int K = 3; // connect up to 3 nearest with LOS
    var all = Object.FindObjectsOfType<NavigationWaypoint>();

    // Clear
    foreach (var n in all)
    {
        Undo.RecordObject(n, "Rebuild (KNN + LOS, Global)");
        n.connectedWaypoints ??= new List<NavigationWaypoint>();
        n.connectedWaypoints.Clear();
        if (n.wallLayerMask.value == 0) n.wallLayerMask = LayerMask.GetMask("Walls");
    }

    int links = 0;
    // For each node, find K nearest within globalMax and link if LOS
    foreach (var a in all)
    {
        // Build neighbor list within threshold
        var candidates = new List<(NavigationWaypoint wp, float dist)>();
        foreach (var b in all)
        {
            if (a == b) continue;
            float d = Vector3.Distance(a.transform.position, b.transform.position);
            if (d <= globalMax) candidates.Add((b, d));
        }
        // Sort by distance
        candidates.Sort((x, y) => x.dist.CompareTo(y.dist));
        // Try up to K with LOS
        int connected = 0;
        foreach (var (b, dist) in candidates)
        {
            if (connected >= K) break;
            int mask = a.wallLayerMask.value != 0 ? a.wallLayerMask.value : LayerMask.GetMask("Walls");
            if (HasLOS(a.transform.position, b.transform.position, mask, a.losCheckRadius, a.losSampleSpacing))
            {
                AddLinkBoth(a, b);
                connected++;
                links++;
            }
        }
    }

    foreach (var n in all) EditorUtility.SetDirty(n);
    Debug.Log($"[Waypoint Graph] KNN+LOS (K={K}, global {globalMax} m) links: {links} across {all.Length} waypoints");
}

// Legacy: per-node connection distances + LOS (uses min(a,b))
[MenuItem("Tools/Navigation/Rebuild Connections (All Waypoints, Per-Node Dist + LOS)")]
public static void MenuRebuildAll_PerNode()
{
    var all = Object.FindObjectsOfType<NavigationWaypoint>();

    foreach (var n in all)
    {
        Undo.RecordObject(n, "Rebuild Connections (All, Per-Node)");
        n.connectedWaypoints ??= new List<NavigationWaypoint>();
        n.connectedWaypoints.Clear();
        if (n.wallLayerMask.value == 0) n.wallLayerMask = LayerMask.GetMask("Walls");
    }

    int links = 0;

    for (int i = 0; i < all.Length; i++)
    for (int j = i + 1; j < all.Length; j++)
    {
        var a = all[i];
        var b = all[j];

        float maxD = Mathf.Min(a.connectionDistance, b.connectionDistance);
        if (Vector3.Distance(a.transform.position, b.transform.position) > maxD) continue;

        int mask = a.wallLayerMask.value != 0 ? a.wallLayerMask.value : LayerMask.GetMask("Walls");
        if (HasLOS(a.transform.position, b.transform.position, mask, a.losCheckRadius, a.losSampleSpacing))
        {
            AddLinkBoth(a, b);
            links++;
        }
    }

    foreach (var n in all) EditorUtility.SetDirty(n);
    Debug.Log($"[Waypoint Graph] Links added (per-node + LOS): {links} across {all.Length} waypoints");
}

[MenuItem("Tools/Navigation/Report Blocked Pairs (first 30)")]
public static void MenuReportBlockedPairs()
{
    var all = Object.FindObjectsOfType<NavigationWaypoint>();
    int reported = 0;
    foreach (var a in all)
    foreach (var b in all)
    {
        if (a == b) continue;

        int mask = a.wallLayerMask.value != 0 ? a.wallLayerMask.value : LayerMask.GetMask("Walls");
        Vector3 A = a.transform.position + Vector3.up * 0.6f;
        Vector3 B = b.transform.position + Vector3.up * 0.6f;

        if (Physics.Linecast(A, B, out RaycastHit hit, mask))
        {
            Debug.LogWarning($"BLOCKED {a.name} -> {b.name} by {hit.collider.name} (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)})");
            if (++reported >= 30) { Debug.Log("...truncated"); return; }
        }
    }
    if (reported == 0) Debug.Log("No blocked pairs by linecast (raw LOS).");
}

[MenuItem("Tools/Navigation/Report Waypoints Inside Walls")]
public static void MenuReportNodesInsideWalls()
{
    var all = Object.FindObjectsOfType<NavigationWaypoint>();
    int count = 0;
    foreach (var w in all)
    {
        int mask = w.wallLayerMask.value != 0 ? w.wallLayerMask.value : LayerMask.GetMask("Walls");
        if (Physics.CheckSphere(w.transform.position + Vector3.up * 0.1f, 0.1f, mask))
        {
            Debug.LogWarning($"Waypoint inside wall: {w.name} at {w.transform.position}");
            count++;
        }
    }
    Debug.Log($"Waypoints inside walls: {count}");
}

// Helpers
static void AddLinkBoth(NavigationWaypoint a, NavigationWaypoint b)
{
    a.connectedWaypoints ??= new List<NavigationWaypoint>();
    b.connectedWaypoints ??= new List<NavigationWaypoint>();
    if (!a.connectedWaypoints.Contains(b)) a.connectedWaypoints.Add(b);
    if (!b.connectedWaypoints.Contains(a)) b.connectedWaypoints.Add(a);
}

static bool HasLOS(Vector3 from, Vector3 to, int mask, float radius, float step)
{
    Vector3 a = from + Vector3.up * 0.6f;
    Vector3 b = to   + Vector3.up * 0.6f;
    if (Physics.Linecast(a, b, mask)) return false;

    float dist = Vector3.Distance(a, b);
    int samples = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(0.05f, step)));
    for (int i = 0; i <= samples; i++)
    {
        Vector3 p = Vector3.Lerp(a, b, i / (float)samples);
        if (Physics.CheckSphere(p, radius, mask)) return false;
    }
    return true;
}
}
#endif
