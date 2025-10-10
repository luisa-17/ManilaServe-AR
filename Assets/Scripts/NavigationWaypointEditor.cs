#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(NavigationWaypoint))]
public class NavigationWaypointEditor : Editor
{
    SerializedProperty waypointNameProp;
    SerializedProperty officeNameProp;
    SerializedProperty waypointTypeProp;
    SerializedProperty servicesProp;

    private List<string> officeOptions = new List<string>();
    private string[] officeOptionsArr = new string[0];
    private int popupIndex = 0;
    private NavigationWaypoint wpTarget;

    // Editor toggles (persist between selections)
    private static bool includeNonOfficeItems = false;

    void OnEnable()
    {
        waypointNameProp = serializedObject.FindProperty("waypointName");
        officeNameProp = serializedObject.FindProperty("officeName");
        waypointTypeProp = serializedObject.FindProperty("waypointType");
        servicesProp = serializedObject.FindProperty("services");

        wpTarget = (NavigationWaypoint)target;

        // Initial populate (prefer Firebase if available)
        RefreshOfficeOptions(preferFirebase: true, forceFromScene: false);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw all properties except officeName (we'll render it with a custom dropdown)
        DrawPropertiesExcluding(serializedObject, "officeName");

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh Office List (Firebase)"))
        {
            RefreshOfficeOptions(preferFirebase: true, forceFromScene: false);
        }
        if (GUILayout.Button("Refresh From Scene (Office waypoints only)"))
        {
            RefreshOfficeOptions(preferFirebase: false, forceFromScene: true);
        }
        EditorGUILayout.EndHorizontal();

        includeNonOfficeItems = EditorGUILayout.ToggleLeft("Include non-office items (corridor, stairs, etc.)", includeNonOfficeItems);

        EditorGUILayout.Space();

        // Build popup array with "<None>" as first element
        var popupOptions = new List<string> { "<None>" };
        popupOptions.AddRange(officeOptions);
        officeOptionsArr = popupOptions.ToArray();

        // Determine current index from serialized officeName or target value
        string currentOffice = officeNameProp.stringValue ?? wpTarget.officeName ?? "";
        popupIndex = 0;
        if (!string.IsNullOrEmpty(currentOffice))
        {
            int idx = officeOptions.IndexOf(currentOffice);
            if (idx >= 0) popupIndex = idx + 1;
            else popupIndex = 0;
        }

        EditorGUI.BeginChangeCheck();
        popupIndex = EditorGUILayout.Popup("Office (dropdown)", popupIndex, officeOptionsArr);
        if (EditorGUI.EndChangeCheck())
        {
            string newOffice = popupIndex == 0 ? "" : officeOptionsArr[popupIndex];

            // Undo + apply change
            Undo.RecordObject(wpTarget, "Set Office Name");
            officeNameProp.stringValue = newOffice;
            wpTarget.officeName = newOffice;

            // If we have a FirebaseOfficeManager with detailed Services, fill services[] automatically
            var manager = Object.FindObjectOfType<FirebaseOfficeManager>();
            if (manager != null && !string.IsNullOrEmpty(newOffice))
            {
                var matched = manager.GetOfficeByName(newOffice);
                if (matched != null && matched.Services != null)
                {
                    servicesProp.arraySize = matched.Services.Count;
                    for (int i = 0; i < matched.Services.Count; i++)
                    {
                        var sName = matched.Services[i].ServiceName ?? matched.Services[i].ServiceId ?? "";
                        servicesProp.GetArrayElementAtIndex(i).stringValue = sName;
                    }
                }
                else
                {
                    servicesProp.arraySize = 0;
                }
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(wpTarget);
        }

        if (officeOptions.Count == 0)
        {
            EditorGUILayout.HelpBox("No offices found for the dropdown. Either run the scene to load Firebase data (then click 'Refresh Office List (Firebase)') or click 'Refresh From Scene' (this will gather only waypoints with WaypointType = Office).", MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// Populate officeOptions. preferFirebase tries FirebaseOfficeManager first;
    /// forceFromScene ignores Firebase and only uses scene waypoints (only WaypointType.Office unless includeNonOfficeItems is true).
    /// </summary>
    private void RefreshOfficeOptions(bool preferFirebase, bool forceFromScene)
    {
        officeOptions.Clear();

        if (preferFirebase && !forceFromScene)
        {
            var manager = Object.FindObjectOfType<FirebaseOfficeManager>();
            if (manager != null)
            {
                var list = manager.GetAllOfficeNames();
                if (list != null && list.Count > 0)
                {
                    foreach (var name in list)
                    {
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!includeNonOfficeItems && IsLikelyNonOffice(name)) continue;
                        officeOptions.Add(name.Trim());
                    }
                }
            }
        }

        // fallback or forced: build from scene, but only include waypoints whose waypointType == Office (unless includeNonOfficeItems)
        if (officeOptions.Count == 0 || forceFromScene)
        {
            var all = Object.FindObjectsOfType<NavigationWaypoint>();
            var set = new HashSet<string>();
            foreach (var w in all)
            {
                if (!includeNonOfficeItems && w.waypointType != WaypointType.Office) continue;

                string candidate = !string.IsNullOrWhiteSpace(w.officeName)
                    ? w.officeName
                    : (!string.IsNullOrWhiteSpace(w.waypointName) ? w.waypointName : StripPrefixes(w.gameObject.name));

                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (!includeNonOfficeItems && IsLikelyNonOffice(candidate)) continue;

                set.Add(candidate.Trim());
            }

            officeOptions.AddRange(set.OrderBy(x => x));
        }

        officeOptions = officeOptions.Distinct().OrderBy(x => x).ToList();
    }

    private static string StripPrefixes(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        string s = name;
        s = s.Replace("Waypoint_", "").Replace("Waypoint-", "").Replace("Waypoint", "");
        s = s.Replace("ImageTarget_", "").Replace("Target_", "");
        return s.Trim();
    }

    // Heuristic — returns true for likely corridor/entrance/stairs/waypoint etc.
    private static bool IsLikelyNonOffice(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        string s = name.ToLowerInvariant();

        string[] blacklist = new string[] {
            "corridor", "corridors", "entrance", "entrances",
            "stairs", "stair", "staircase", "staircases",
            "openspace", "open space", "open_space", "open-space",
            "waypoint", "proxy", "mainfloor", "floor", "hall", "lobby",
            "elevator", "exit", "junction", "left", "right", "parking",
            "restroom", "toilet", "service", "corridor"
        };

        foreach (var b in blacklist)
            if (s.Contains(b)) return true;

        return false;
    }
}
#endif