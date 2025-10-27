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

    // CORRECTED: Only Name and Priority are present in the provided JSON structure
    [Serializable] public class Requirement { public string Name; public int Priority; }
    [Serializable] public class Service { public string ServiceId; public string ServiceName; public List<Requirement> Requirements; }
    [Serializable] public class Office { public string OfficeId; public string OfficeName; public string Location; public string Head; public string Phone; public List<Service> Services; }
    [Serializable] public class OfficeData { public List<Office> offices; }

    public static event Action<Dictionary<string, Office>> OnOfficeDataLoaded;

    // In-memory DB
    private Dictionary<string, Office> officeDatabase = new Dictionary<string, Office>(); // keyed by original OfficeName
    private Dictionary<string, Office> officeDatabaseById = new Dictionary<string, Office>();
    private Dictionary<string, Office> officeDatabaseByNameNormalized = new Dictionary<string, Office>(); // normalized -> Office

    // Optional helper if you want to check readiness
    public bool IsReady => isInitialized && (officeDatabaseById.Count > 0 || officeDatabase.Count > 0);

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

        // Define the aggregation map: subordinate IDs map to the parent's ID (Manila Health Department)
        // This ensures all services from these divisions are grouped under MHD.
        var OfficesToAggregate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"SAN", "MHD"},      // Division of Sanitation -> Manila Health Department
            {"CEM", "MHD"},      // Office of Public Cemeteries -> Manila Health Department
            {"PHL", "MHD"},      // Public Health Laboratory -> Manila Health Department
            {"DPD", "MHD"},      // Division of Preventable Diseases -> Manila Health Department
            {"HDC", "MHD"},      // Health District/Centers -> Manila Health Department
            {"CGEC", "MHD"}      // City Government Employees Clinic -> Manila Health Department
        };

        // Dictionary to hold all parsed offices, keyed by OfficeId
        var tempOfficeMap = new Dictionary<string, Office>();

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
                tempOfficeMap.Clear();

                if (!snapshot.Exists)
                {
                    Debug.LogWarning("No 'offices' node found in database. Check your Firebase path. Available keys at this node: " +
                                     string.Join(", ", snapshot.Children.Select(c => c.Key)));
                    OnOfficeDataLoaded?.Invoke(officeDatabase);
                    return;
                }

                // --- 1. First Pass: Parse all offices and store them in the temporary map ---
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
                                        // CORRECTED PARSING: Only Name and Priority are used
                                        var prioStr = req.Child("Priority").Value?.ToString();

                                        int prio = 0;
                                        int.TryParse(prioStr, out prio);

                                        service.Requirements.Add(new Requirement
                                        {
                                            Name = reqName,
                                            Priority = prio
                                        });
                                    }
                                }

                                office.Services.Add(service);
                            }
                        }

                        // Store office in the temporary map
                        if (!tempOfficeMap.ContainsKey(office.OfficeId))
                        {
                            tempOfficeMap.Add(office.OfficeId, office);
                        }
                        else
                        {
                            Debug.LogWarning($"Duplicate OfficeId found and skipped: {office.OfficeId}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error parsing office: {e}");
                    }
                }

                // --- 2. Second Pass: Aggregate subordinate offices into the parent MHD office ---

                // Get the parent MHD office
                if (!tempOfficeMap.TryGetValue("MHD", out var parentOffice))
                {
                    Debug.LogError("Aggregation failed: MHD (Manila Health Department) parent office not found.");
                }
                else
                {
                    // Offices that have been successfully aggregated should be removed from the final list
                    var officesToRemove = new List<string>();

                    foreach (var kvp in OfficesToAggregate)
                    {
                        string subordinateId = kvp.Key;
                        string parentId = kvp.Value;

                        if (parentId.Equals("MHD", StringComparison.OrdinalIgnoreCase) &&
                            tempOfficeMap.TryGetValue(subordinateId, out var subordinateOffice))
                        {
                            if (subordinateOffice.Services != null && subordinateOffice.Services.Count > 0)
                            {
                                // Append services from the subordinate office to the parent MHD office's service list
                                parentOffice.Services.AddRange(subordinateOffice.Services);
                                Debug.Log($"Aggregated {subordinateOffice.Services.Count} services from '{subordinateId}' into '{parentId}'.");
                            }
                            // Mark the subordinate office for removal from the final output
                            officesToRemove.Add(subordinateId);
                        }
                    }

                    // Remove aggregated offices from the temporary map
                    foreach (var id in officesToRemove)
                    {
                        tempOfficeMap.Remove(id);
                    }
                }

                // --- 3. Final Pass: Populate permanent dictionaries from the processed map ---
                foreach (var office in tempOfficeMap.Values)
                {
                    // Add to permanent dictionaries
                    officeDatabase[office.OfficeName] = office;
                    officeDatabaseById[office.OfficeId] = office;

                    var normalized = Normalize(office.OfficeName);
                    if (!officeDatabaseByNameNormalized.ContainsKey(normalized))
                        officeDatabaseByNameNormalized[normalized] = office;
                    else
                        Debug.LogWarning($"Duplicate normalized office name skipped: {normalized} (existing: '{officeDatabaseByNameNormalized[normalized].OfficeName}', new: '{office.OfficeName}')");

                    Debug.Log($"Final Loaded office: {office.OfficeName} (id:{office.OfficeId}) services:{office.Services.Count}");
                }


                Debug.Log($"Total offices loaded after aggregation: {officeDatabase.Count}");
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

    // NEW: ID lookup
    public Office GetOfficeById(string officeId)
    {
        if (string.IsNullOrWhiteSpace(officeId)) return null;

        if (officeDatabaseById.TryGetValue(officeId, out var byId))
            return byId;

        // Fallbacks: sometimes you might accidentally pass a name in here
        if (officeDatabase.TryGetValue(officeId, out var byNameExact))
            return byNameExact;

        var norm = Normalize(officeId);
        if (officeDatabaseByNameNormalized.TryGetValue(norm, out var byNameNorm))
            return byNameNorm;

        Debug.LogWarning($"GetOfficeById: no match for '{officeId}'");
        return null;
    }

    // NEW: return list of all offices currently loaded
    public List<Office> GetAllOffices()
    {
        // Prefer the ID dictionary (keys are stable)
        if (officeDatabaseById != null && officeDatabaseById.Count > 0)
            return officeDatabaseById.Values.ToList();

        return officeDatabase.Values.ToList();
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

        // Ensure NavigationWaypoint type exists before trying to use FindObjectsOfType
        // Note: I cannot reliably check if this type exists in the current environment context,
        // so I rely on Unity's behavior to handle the type lookup if it's available in the project.
        // Assuming NavigationWaypoint is available in the scene.
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
                    {
                        // Note: NavigationWaypoint expects string array, so we only pass the Name
                        wp.services = matched.Services.Select(s => s.ServiceName ?? s.ServiceId ?? "").ToArray();
                    }
                    else
                        wp.services = new string[0];

                    mapped++;
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
