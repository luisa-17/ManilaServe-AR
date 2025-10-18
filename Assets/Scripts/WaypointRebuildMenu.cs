#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class WaypointGraphTools
{
[MenuItem("Tools/Navigation/Report graph issues")]
public static void Report()
{
NavigationWaypoint.ReportGraphIssues();
SceneView.RepaintAll();
}


[MenuItem("Tools/Navigation/Fix: Make links bidirectional")]
public static void FixBidirectional()
{
    int added = NavigationWaypoint.MakeLinksBidirectional(removeInvalid: true, deduplicate: true);
    Debug.Log($"[Tools] MakeLinksBidirectional done. Reverse links added: {added}");
    SceneView.RepaintAll();
}
}
#endif