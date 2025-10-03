using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using System.Collections.Generic;
using System;
using System.Linq;

public class FirebaseOfficeManager : MonoBehaviour
{
    private DatabaseReference databaseRef;
    private bool isInitialized = false;

    [System.Serializable]
    public class Requirement
    {
        public string Name;
        public int Priority;
    }

    [System.Serializable]
    public class Service
    {
        public string ServiceId;
        public string ServiceName;
        public List<Requirement> Requirements;
    }

    [System.Serializable]
    public class Office
    {
        public string OfficeId;
        public string OfficeName;
        public string Location;
        public string Head;
        public string Phone;
        public List<Service> Services;
    }

    [System.Serializable]
    public class OfficeData
    {
        public List<Office> offices;
    }

    public static event Action<Dictionary<string, Office>> OnOfficeDataLoaded;
    private Dictionary<string, Office> officeDatabase = new Dictionary<string, Office>();

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
                Debug.Log("Firebase data retrieved successfully");
                DataSnapshot snapshot = task.Result;

                foreach (var officeSnapshot in snapshot.Children)
                {
                    try
                    {
                        var office = new Office
                        {
                            OfficeId = officeSnapshot.Child("OfficeId").Value?.ToString(),
                            OfficeName = officeSnapshot.Child("OfficeName").Value?.ToString(),
                            Location = officeSnapshot.Child("Location").Value?.ToString(),
                            Head = officeSnapshot.Child("Head").Value?.ToString(),
                            Phone = officeSnapshot.Child("Phone").Value?.ToString(),
                            Services = new List<Service>()
                        };

                        // Parse services
                        var servicesSnapshot = officeSnapshot.Child("Services");
                        foreach (var serviceSnapshot in servicesSnapshot.Children)
                        {
                            var service = new Service
                            {
                                ServiceId = serviceSnapshot.Child("ServiceId").Value?.ToString(),
                                ServiceName = serviceSnapshot.Child("ServiceName").Value?.ToString(),
                                Requirements = new List<Requirement>()
                            };

                            // Parse requirements
                            var reqSnapshot = serviceSnapshot.Child("Requirements");
                            foreach (var req in reqSnapshot.Children)
                            {
                                service.Requirements.Add(new Requirement
                                {
                                    Name = req.Child("Name").Value?.ToString(),
                                    Priority = int.Parse(req.Child("Priority").Value?.ToString() ?? "0")
                                });
                            }

                            office.Services.Add(service);
                        }

                        officeDatabase[office.OfficeName] = office;
                        Debug.Log($"Loaded office: {office.OfficeName} with {office.Services.Count} services");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error parsing office: {e.Message}");
                    }
                }

                OnOfficeDataLoaded?.Invoke(officeDatabase);
                Debug.Log($"Total offices loaded: {officeDatabase.Count}");
            }
        });
    }

    public Office GetOfficeByName(string officeName)
    {
        if (string.IsNullOrWhiteSpace(officeName))
        {
            Debug.LogWarning("Empty office name provided");
            return null;
        }

        // Try exact match first
        if (officeDatabase.TryGetValue(officeName, out Office office))
        {
            Debug.Log($"Exact match found: {officeName}");
            return office;
        }

        // Normalize search term
        string normalized = officeName.ToLower().Trim()
            .Replace(" ", "").Replace("-", "").Replace("_", "");

        // Try partial match
        foreach (var kvp in officeDatabase)
        {
            string dbName = kvp.Key.ToLower().Trim()
                .Replace(" ", "").Replace("-", "").Replace("_", "");

            // Check if either contains the other
            if (dbName.Contains(normalized) || normalized.Contains(dbName))
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
}