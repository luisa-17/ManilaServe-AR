// ============================================================================
// FILE: ChecklistManager.cs (NEW FILE - Create this)
// Manages user checklists and syncs with Firebase
// ============================================================================

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Firebase.Database;
using Firebase.Extensions;

public class ChecklistManager : MonoBehaviour
{
    private DatabaseReference databaseRef;
    private Dictionary<string, List<ChecklistItem>> userChecklists = new Dictionary<string, List<ChecklistItem>>();

    // Events
    public delegate void ChecklistUpdated(string userId);
    public static event ChecklistUpdated OnChecklistUpdated;

    void Start()
    {
        InitializeFirebase();
    }

    void InitializeFirebase()
    {
        if (Firebase.FirebaseApp.DefaultInstance == null)
        {
            Debug.LogWarning("[CHECKLIST] Firebase not initialized yet");
            return;
        }

        databaseRef = FirebaseDatabase.DefaultInstance.RootReference;
        Debug.Log("[CHECKLIST] Firebase initialized");
    }

    // ============================================================================
    // Add Item to Checklist
    // ============================================================================
    public void AddChecklistItem(string userId, ChecklistItem item)
    {
        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogError("[CHECKLIST] User ID is empty!");
            return;
        }

        Debug.Log($"[CHECKLIST] Adding item for user {userId}: {item.serviceName} at {item.officeName}");

        // Initialize requirement checkboxes
        if (item.requirementChecked == null || item.requirementChecked.Count == 0)
        {
            item.requirementChecked = new List<bool>();
            for (int i = 0; i < item.requirements.Count; i++)
            {
                item.requirementChecked.Add(false);
            }
        }

        // Add to local cache
        if (!userChecklists.ContainsKey(userId))
        {
            userChecklists[userId] = new List<ChecklistItem>();
        }

        userChecklists[userId].Add(item);

        // Save to Firebase
        SaveChecklistToFirebase(userId);

        // Notify listeners
        OnChecklistUpdated?.Invoke(userId);
    }

    // ============================================================================
    // Get User's Checklist
    // ============================================================================
    public List<ChecklistItem> GetUserChecklist(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogWarning("[CHECKLIST] User ID is empty!");
            return new List<ChecklistItem>();
        }

        if (userChecklists.ContainsKey(userId))
        {
            return userChecklists[userId];
        }

        // If not in cache, load from Firebase
        LoadChecklistFromFirebase(userId);
        return new List<ChecklistItem>();
    }

    // ============================================================================
    // Update Requirement Status
    // ============================================================================
    public void ToggleRequirement(string userId, int itemIndex, int requirementIndex)
    {
        if (!userChecklists.ContainsKey(userId)) return;
        if (itemIndex >= userChecklists[userId].Count) return;

        var item = userChecklists[userId][itemIndex];
        if (requirementIndex >= item.requirementChecked.Count) return;

        // Toggle the requirement
        item.requirementChecked[requirementIndex] = !item.requirementChecked[requirementIndex];

        // Check if all requirements are done
        item.isCompleted = item.requirementChecked.All(isChecked => isChecked);

        Debug.Log($"[CHECKLIST] Toggled requirement {requirementIndex} for item {itemIndex}. Completed: {item.isCompleted}");

        // Save to Firebase
        SaveChecklistToFirebase(userId);

        // Notify listeners
        OnChecklistUpdated?.Invoke(userId);
    }
    // ============================================================================
    // Remove Item from Checklist
    // ============================================================================
    public void RemoveChecklistItem(string userId, int itemIndex)
    {
        if (!userChecklists.ContainsKey(userId)) return;
        if (itemIndex >= userChecklists[userId].Count) return;

        Debug.Log($"[CHECKLIST] Removing item {itemIndex} for user {userId}");

        userChecklists[userId].RemoveAt(itemIndex);

        // Save to Firebase
        SaveChecklistToFirebase(userId);

        // Notify listeners
        OnChecklistUpdated?.Invoke(userId);
    }

    // ============================================================================
    // Clear Completed Items
    // ============================================================================
    public void ClearCompletedItems(string userId)
    {
        if (!userChecklists.ContainsKey(userId)) return;

        int beforeCount = userChecklists[userId].Count;
        userChecklists[userId].RemoveAll(item => item.isCompleted);
        int afterCount = userChecklists[userId].Count;

        Debug.Log($"[CHECKLIST] Cleared {beforeCount - afterCount} completed items for user {userId}");

        if (beforeCount != afterCount)
        {
            SaveChecklistToFirebase(userId);
            OnChecklistUpdated?.Invoke(userId);
        }
    }

    // ============================================================================
    // Firebase Operations
    // ============================================================================
    void SaveChecklistToFirebase(string userId)
    {
        if (databaseRef == null)
        {
            Debug.LogWarning("[CHECKLIST] Firebase not initialized, saving to PlayerPrefs instead");
            SaveToPlayerPrefs(userId);
            return;
        }

        if (!userChecklists.ContainsKey(userId)) return;

        Debug.Log($"[CHECKLIST] Saving checklist to Firebase for user {userId}");

        // Convert checklist to Firebase format
        var checklistData = new Dictionary<string, object>();

        foreach (var item in userChecklists[userId])
        {
            // Use auto-generated key for each item
            var pushKey = databaseRef.Child("checklists").Child(userId).Push().Key;

            var itemData = new Dictionary<string, object>
        {
            { "Id", pushKey },
            { "OfficeName", item.officeName },
            { "ServiceName", item.serviceName },
            { "Requirements", item.requirements ?? new List<string>() },
            { "CheckedItems", item.requirementChecked ?? new List<bool>() },
            { "DateAdded", item.dateAdded },
            { "Progress", item.isCompleted ? 100 : 0 },
            { "Priorities", new List<int>() } // Can be populated later if needed
        };

            checklistData[pushKey] = itemData;
        }

        // Save to Firebase
        databaseRef.Child("checklists").Child(userId).SetValueAsync(checklistData)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted && !task.IsFaulted)
                {
                    Debug.Log("[CHECKLIST] ✓ Saved to Firebase successfully");
                }
                else
                {
                    Debug.LogError($"[CHECKLIST] ✗ Failed to save: {task.Exception}");
                    SaveToPlayerPrefs(userId); // Fallback
                }
            });
    }

    void LoadChecklistFromFirebase(string userId)
    {
        if (databaseRef == null)
        {
            Debug.LogWarning("[CHECKLIST] Firebase not initialized, loading from PlayerPrefs");
            LoadFromPlayerPrefs(userId);
            return;
        }

        Debug.Log($"[CHECKLIST] Loading checklist from Firebase for user {userId}");

        databaseRef.Child("checklists").Child(userId).GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompleted && !task.IsFaulted && task.Result.Exists)
                {
                    Debug.Log("[CHECKLIST] ✓ Loaded from Firebase");
                    ParseFirebaseData(userId, task.Result);
                }
                else
                {
                    Debug.LogWarning("[CHECKLIST] No data in Firebase, trying PlayerPrefs");
                    LoadFromPlayerPrefs(userId);
                }
            });
    }

    void ParseFirebaseData(string userId, DataSnapshot snapshot)
    {
        var checklist = new List<ChecklistItem>();

        foreach (var child in snapshot.Children)
        {
            try
            {
                var item = new ChecklistItem
                {
                    officeName = child.Child("OfficeName").Value?.ToString() ?? "",
                    serviceName = child.Child("ServiceName").Value?.ToString() ?? "",
                    dateAdded = child.Child("DateAdded").Value?.ToString() ?? System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    isCompleted = int.Parse(child.Child("Progress").Value?.ToString() ?? "0") >= 100,
                    requirements = new List<string>(),
                    requirementChecked = new List<bool>()
                };

                // Parse requirements array
                if (child.Child("Requirements").Exists)
                {
                    foreach (var req in child.Child("Requirements").Children)
                    {
                        item.requirements.Add(req.Value?.ToString() ?? "");
                    }
                }

                // Parse checked items array
                if (child.Child("CheckedItems").Exists)
                {
                    foreach (var check in child.Child("CheckedItems").Children)
                    {
                        item.requirementChecked.Add(bool.Parse(check.Value?.ToString() ?? "false"));
                    }
                }

                // If CheckedItems is shorter than Requirements, fill with false
                while (item.requirementChecked.Count < item.requirements.Count)
                {
                    item.requirementChecked.Add(false);
                }

                checklist.Add(item);
                Debug.Log($"[CHECKLIST] Parsed item: {item.serviceName} at {item.officeName}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[CHECKLIST] Error parsing item: {e.Message}");
            }
        }

        userChecklists[userId] = checklist;
        Debug.Log($"[CHECKLIST] Loaded {checklist.Count} items for user {userId}");

        OnChecklistUpdated?.Invoke(userId);
    }

    // ============================================================================
    // PlayerPrefs Fallback (for offline or testing)
    // ============================================================================
    void SaveToPlayerPrefs(string userId)
    {
        if (!userChecklists.ContainsKey(userId)) return;

        string json = JsonUtility.ToJson(new ChecklistWrapper { items = userChecklists[userId] });
        PlayerPrefs.SetString($"checklist_{userId}", json);
        PlayerPrefs.Save();

        Debug.Log($"[CHECKLIST] Saved to PlayerPrefs for user {userId}");
    }

    void LoadFromPlayerPrefs(string userId)
    {
        string key = $"checklist_{userId}";

        if (PlayerPrefs.HasKey(key))
        {
            string json = PlayerPrefs.GetString(key);
            var wrapper = JsonUtility.FromJson<ChecklistWrapper>(json);

            if (wrapper != null && wrapper.items != null)
            {
                userChecklists[userId] = wrapper.items;
                Debug.Log($"[CHECKLIST] Loaded {wrapper.items.Count} items from PlayerPrefs");
                OnChecklistUpdated?.Invoke(userId);
            }
        }
        else
        {
            Debug.Log($"[CHECKLIST] No saved checklist found for user {userId}");
            userChecklists[userId] = new List<ChecklistItem>();
        }
    }

    [System.Serializable]
    class ChecklistWrapper
    {
        public List<ChecklistItem> items;
    }
}