using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

public class FirebaseOfficeManager : MonoBehaviour
{
    private DatabaseReference databaseRef;
    private bool isInitialized = false;

    [Serializable] public class Requirement { public string Name; public int Priority; }
    [Serializable] public class Service { public string ServiceId; public string ServiceName; public List<Requirement> Requirements; }
    [Serializable] public class Office { public string OfficeId; public string OfficeName; public string Location; public string Head; public string Phone; public List<Service> Services; }
    [Serializable] public class OfficeData { public List<Office> offices; }

    public static event Action<Dictionary<string, Office>> OnOfficeDataLoaded;
    private Dictionary<string, Office> officeDatabase = new Dictionary<string, Office>(); // keyed by original OfficeName
    private Dictionary<string, Office> officeDatabaseById = new Dictionary<string, Office>();
    private Dictionary<string, Office> officeDatabaseByNameNormalized = new Dictionary<string, Office>(); // normalized -> Office

    void Start()
    {
        Debug.Log("FirebaseOfficeManager Start called");
        InitializeFirebase();
    }

    void InitializeFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                databaseRef = FirebaseDatabase.DefaultInstance.RootReference;
                isInitialized = true;
                Debug.Log("Firebase initialized successfully");
                LoadOfficeData();
            }
            else
            {
                Debug.LogError($"Could not resolve Firebase dependencies: {dependencyStatus}");
            }
        });
    }

    public void LoadOfficeData()
    {
        Debug.Log("=== LoadOfficeData called ===");

        if (!isInitialized)
        {
            Debug.LogError("Firebase not initialized - cannot load data");
            return;
        }

        Debug.Log("Firebase initialized - fetching from database...");

        databaseRef.Child("offices").GetValueAsync().ContinueWithOnMainThread(task => {
            if (task.IsFaulted)
            {
                Debug.LogError("Failed to load office data: " + task.Exception);
                return;
            }

            if (task.IsCompleted)
            {
                var snapshot = task.Result;

                officeDatabase.Clear();
                officeDatabaseById.Clear();
                officeDatabaseByNameNormalized.Clear();

                if (!snapshot.Exists)
                {
                    Debug.LogWarning("No 'offices' node found in database. Check your Firebase path. Available keys at this node: " +
                                     string.Join(", ", snapshot.Children.Select(c => c.Key)));
                    OnOfficeDataLoaded?.Invoke(officeDatabase);
                    return;
                }

                foreach (var officeSnapshot in snapshot.Children)
                {
                    try
                    {
                        var office = new Office
                        {
                            OfficeId = officeSnapshot.Child("OfficeId").Value?.ToString() ?? officeSnapshot.Key,
                            OfficeName = officeSnapshot.Child("OfficeName").Value?.ToString() ?? officeSnapshot.Key,
                            Location = officeSnapshot.Child("Location").Value?.ToString(),
                            Head = officeSnapshot.Child("Head").Value?.ToString(),
                            Phone = officeSnapshot.Child("Phone").Value?.ToString(),
                            Services = new List<Service>()
                        };

                        // Parse services (if any)
                        var servicesSnapshot = officeSnapshot.Child("Services");
                        if (servicesSnapshot.Exists)
                        {
                            foreach (var serviceSnapshot in servicesSnapshot.Children)
                            {
                                var service = new Service
                                {
                                    ServiceId = serviceSnapshot.Child("ServiceId").Value?.ToString() ?? serviceSnapshot.Key,
                                    ServiceName = serviceSnapshot.Child("ServiceName").Value?.ToString() ?? serviceSnapshot.Key,
                                    Requirements = new List<Requirement>()
                                };

                                var reqSnapshot = serviceSnapshot.Child("Requirements");
                                if (reqSnapshot.Exists)
                                {
                                    foreach (var req in reqSnapshot.Children)
                                    {
                                        var reqName = req.Child("Name").Value?.ToString() ?? req.Key;
                                        var prioStr = req.Child("Priority").Value?.ToString();
                                        int prio = 0;
                                        int.TryParse(prioStr, out prio);
                                        service.Requirements.Add(new Requirement { Name = reqName, Priority = prio });
                                    }
                                }

                                office.Services.Add(service);
                            }
                        }

                        // Add to dictionaries
                        officeDatabase[office.OfficeName] = office;
                        officeDatabaseById[office.OfficeId] = office;

                        var normalized = Normalize(office.OfficeName);
                        if (!officeDatabaseByNameNormalized.ContainsKey(normalized))
                            officeDatabaseByNameNormalized[normalized] = office;
                        else
                            Debug.LogWarning($"Duplicate normalized office name skipped: {normalized} (existing: '{officeDatabaseByNameNormalized[normalized].OfficeName}', new: '{office.OfficeName}')");

                        Debug.Log($"Loaded office: {office.OfficeName} (id:{office.OfficeId}) services:{office.Services.Count}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error parsing office: {e}");
                    }
                }

                Debug.Log($"Total offices loaded: {officeDatabase.Count}");
                Debug.Log("Normalized keys: " + string.Join(", ", officeDatabaseByNameNormalized.Keys));

                OnOfficeDataLoaded?.Invoke(officeDatabase);

                // Optional: map offices into NavigationWaypoint components in the scene
                MapOfficesToWaypoints();
            }
        });
    }

    // Normalizes names for matching (lowercase, remove spaces/underscores/hyphens, remove non-alphanumeric)
    private string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant().Trim();
        s = Regex.Replace(s, @"[\s_\-]+", "");
        s = Regex.Replace(s, @"[^a-z0-9]", "");
        return s;
    }

    // Robust name lookup using several heuristics
    public Office GetOfficeByName(string officeName)
    {
        if (string.IsNullOrWhiteSpace(officeName))
        {
            Debug.LogWarning("Empty office name provided");
            return null;
        }

        // Exact match
        if (officeDatabase.TryGetValue(officeName, out Office office))
        {
            Debug.Log($"Exact match found: {officeName}");
            return office;
        }

        // Normalized exact match
        var normalized = Normalize(officeName);
        if (officeDatabaseByNameNormalized.TryGetValue(normalized, out office))
        {
            Debug.Log($"Normalized match found: '{officeName}' -> '{office.OfficeName}'");
            return office;
        }

        // Partial normalized match (contains)
        foreach (var kvp in officeDatabase)
        {
            string dbNorm = Normalize(kvp.Key);
            if (dbNorm.Contains(normalized) || normalized.Contains(dbNorm))
            {
                Debug.Log($"Partial match: '{officeName}' matched with '{kvp.Key}'");
                return kvp.Value;
            }
        }

        Debug.LogWarning($"No match found for: '{officeName}'. Available: {string.Join(", ", officeDatabase.Keys)}");
        return null;
    }

    public List<string> GetAllOfficeNames()
    {
        return new List<string>(officeDatabase.Keys);
    }

    private string StripWaypointPrefixes(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        string s = name;
        s = s.Replace("Waypoint_", "").Replace("Waypoint-", "").Replace("Waypoint", "");
        s = s.Replace("ImageTarget_", "").Replace("Target_", "");
        return s.Trim();
    }
    // Attempts to find your NavigationWaypoint objects and fill their OfficeName and Services if possible.
    // It uses reflection so it will work whether the fields are public or [SerializeField] private fields.
    public void MapOfficesToWaypoints()
    {
        Debug.Log("MapOfficesToWaypoints: scanning scene for NavigationWaypoint components...");

        var waypoints = FindObjectsOfType<NavigationWaypoint>();
        if (waypoints == null || waypoints.Length == 0)
        {
            Debug.LogWarning("No NavigationWaypoint components found in the scene.");
            return;
        }

        int mapped = 0;

        foreach (var wp in waypoints)
        {
            try
            {
                // Candidates to try (in order)
                string objName = wp.gameObject.name;
                string[] candidates = new string[]
                {
                wp.officeName,                              // explicit inspector value
                wp.waypointName,                            // waypointName (set from Reset/Awake)
                StripWaypointPrefixes(objName),             // object name with "Waypoint_" etc. removed
                StripWaypointPrefixes(objName).Replace("_", " ").Replace("-", " "), // spaced variant
                objName                                     // raw object name
                };

                Office matched = null;

                foreach (var cand in candidates)
                {
                    if (string.IsNullOrWhiteSpace(cand)) continue;
                    matched = GetOfficeByName(cand);
                    if (matched != null)
                    {
                        Debug.Log($"Matched Waypoint '{wp.name}' candidate '{cand}' -> Office '{matched.OfficeName}'");
                        break;
                    }
                }

                // final fallback: normalized token match (remove non-alphanumerics)
                if (matched == null)
                {
                    string alt = StripWaypointPrefixes(objName).Replace("_", "").Replace("-", "").Replace(" ", "");
                    if (!string.IsNullOrEmpty(alt))
                        matched = GetOfficeByName(alt);
                }

                if (matched != null)
                {
                    // assign OfficeName and services[] (doesn't change other NavigationWaypoint logic)
                    wp.officeName = matched.OfficeName ?? matched.OfficeId ?? wp.officeName;

                    if (matched.Services != null && matched.Services.Count > 0)
                        wp.services = matched.Services.Select(s => s.ServiceName ?? s.ServiceId ?? "").ToArray();
                    else
                        wp.services = new string[0];

                    mapped++;

                    // If you want these changes persisted in the Editor (not required at runtime), mark object dirty:
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.Undo.RecordObject(wp, "Map Office to Waypoint");
                    UnityEditor.EditorUtility.SetDirty(wp);
                }
#endif
                }
                else
                {
                    Debug.LogWarning($"No office match for Waypoint '{wp.name}' (officeName='{wp.officeName}', waypointName='{wp.waypointName}').");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"MapOfficesToWaypoints error for {wp.name}: {ex}");
            }
        }

        Debug.Log($"MapOfficesToWaypoints done. Matched {mapped}/{waypoints.Length} waypoints.");
    }
}