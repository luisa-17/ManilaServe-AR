using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using UnityEngine;

public class CityChecklistFirebaseAdapter : MonoBehaviour, IChecklistService
{
    [Header("Mirroring")]
    [Tooltip("Also write records under checklistsByUserKey/<username> for console browsing. Leave OFF unless rules allow it.")]
    public bool mirrorByUsername = false;


    [Header("Catalog (optional)")]
    public AdminCatalogFirebase adminCatalog;

    DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;
    DatabaseReference NodeByUid(string uid) => FirebaseDatabase.DefaultInstance.GetReference("checklistsByUid").Child(uid);
    DatabaseReference NodeByUserKey(string userKey) => FirebaseDatabase.DefaultInstance.GetReference("checklistsByUserKey").Child(userKey);

    // Create a safe key from display name or email's local part (before @)
    static string MakeUserKey()
    {
        var user = AuthService.Auth?.CurrentUser;
        string name = user?.DisplayName;

        if (string.IsNullOrWhiteSpace(name))
        {
            var email = user?.Email;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var at = email.IndexOf('@');
                name = at > 0 ? email.Substring(0, at) : email;
            }
        }
        if (string.IsNullOrWhiteSpace(name)) name = user?.UserId ?? "unknown";
        return SanitizeKey(name);
    }

    static string SanitizeKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unknown";
        var chars = s.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
        var result = new string(chars.ToArray());
        return string.IsNullOrEmpty(result) ? "unknown" : result;
    }

    [ContextMenu("Create Test Checklist (MHD/MHD1)")]
    async void CreateTestChecklist()
    {
        await AuthService.EnsureInitializedAsync();
        await AuthService.WaitForAuthRestorationAsync(1500);
        if (!AuthService.IsSignedIn) await AuthService.SignInAnonymouslyAsync();
        var res = await CreateChecklistAsync(AuthService.UserId, "MHD", "MHD1", null);
        Debug.Log(res.ok ? $"Wrote checklist id: {res.id}" : "Create failed");
    }

    public async Task<List<ChecklistDTO>> GetUserChecklistsAsync(string userId)
    {
        await AuthService.EnsureInitializedAsync();
        var list = new List<ChecklistDTO>();
        if (string.IsNullOrEmpty(userId)) return list;


        // Only read the canonical path
        var snap = await NodeByUid(userId).GetValueAsync();

        if (!snap.Exists)
        {
            Debug.Log($"[FirebaseChecklist] No data under checklistsByUid/{userId} (returning empty).");
            return list;
        }

        foreach (var c in snap.Children)
        {
            try
            {
                var dto = JsonUtility.FromJson<ChecklistDTO>(c.GetRawJsonValue() ?? "{}") ?? new ChecklistDTO();
                dto.Id = c.Key;
                FillDefaults(dto);
                list.Add(dto);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirebaseChecklist] Parse error for key {c.Key}: {e}");
            }
        }

        Debug.Log($"[FirebaseChecklist] Loaded {list.Count} records for uid={userId}");
        return list;
    }

    public async Task<(bool ok, string id)> CreateChecklistAsync(string userId, string officeId, string serviceId, List<string> requirements = null)
    {
        await AuthService.EnsureInitializedAsync();
        if (string.IsNullOrEmpty(userId)) return (false, null);

        // Build requirement items
        List<(string name, int priority)> items;
        if (requirements == null || requirements.Count == 0)
        {
            if (adminCatalog != null)
            {
                var res = await adminCatalog.GetRequirementsAsync(officeId, serviceId);
                if (!res.ok) return (false, null);
                items = res.items;
            }
            else
            {
                items = new List<(string, int)>();
            }
        }
        else
        {
            items = requirements.Select((n, i) => (n, i + 1)).ToList();
        }

        // Prefer display names from context for the UI
        string officeName = string.IsNullOrWhiteSpace(ChecklistContext.SelectedOfficeName) ? officeId : ChecklistContext.SelectedOfficeName;
        string serviceName = string.IsNullOrWhiteSpace(ChecklistContext.SelectedServiceName) ? serviceId : ChecklistContext.SelectedServiceName;

        // Owner metadata for visibility in the DB
        var user = AuthService.Auth?.CurrentUser;
        string ownerEmail = user?.Email ?? "";
        string ownerDisplayName = user?.DisplayName ?? "";

        // DTO for UI usage (you don't have to write this exact shape; we write a dict below)
        var dto = new ChecklistDTO
        {
            OfficeName = officeName,
            ServiceName = serviceName,
            Requirements = items.Select(i => i.name).ToList(),
            CheckedItems = Enumerable.Repeat(false, items.Count).ToList(),
            Priorities = items.Select(i => i.priority).ToList(),
            Progress = 0f
        };

        // Data we will write to RTDB
        var data = new Dictionary<string, object>
        {
            ["OfficeName"] = dto.OfficeName,
            ["ServiceName"] = dto.ServiceName,
            ["Requirements"] = dto.Requirements,
            ["CheckedItems"] = dto.CheckedItems,
            ["Priorities"] = dto.Priorities,
            ["Progress"] = 0.0,
            ["DateAdded"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["OwnerUid"] = userId,
            ["OwnerEmail"] = ownerEmail,
            ["OwnerDisplayName"] = ownerDisplayName,
            ["OfficeId"] = officeId ?? dto.OfficeName,
            ["ServiceId"] = serviceId ?? dto.ServiceName
        };

        // Write under UID
        var push = NodeByUid(userId).Push();
        dto.Id = push.Key;
        data["Id"] = dto.Id;

        Debug.Log($"[FirebaseChecklist] Save UID path: checklistsByUid/{userId}/{dto.Id}");
        await push.SetValueAsync(data);

        // Optional mirror
        if (mirrorByUsername)
        {
            try
            {
                var userKey = MakeUserKey();
                Debug.Log($"[FirebaseChecklist] Mirror username path: checklistsByUserKey/{userKey}/{dto.Id}");
                await NodeByUserKey(userKey).Child(dto.Id).SetValueAsync(data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseChecklist] Mirror create failed: {ex.Message}");
            }
        }

        return (true, dto.Id);
    }

    public async Task<bool> UpdateChecklistProgressAsync(string checklistId, List<bool> checkedItems)
    {
        await AuthService.EnsureInitializedAsync();
        string uid = AuthService.UserId;
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(checklistId)) return false;

        var updates = new Dictionary<string, object>
        {
            ["CheckedItems"] = checkedItems?.Select(b => (object)b).ToList() ?? new List<object>(),
            ["Progress"] = (double)((checkedItems == null || checkedItems.Count == 0)
                ? 0f
                : checkedItems.Count(b => b) / (float)checkedItems.Count)
        };

        Debug.Log($"[FirebaseChecklist] Update UID path: checklistsByUid/{uid}/{checklistId}");
        await NodeByUid(uid).Child(checklistId).UpdateChildrenAsync(updates);

        if (mirrorByUsername)
        {
            try
            {
                var userKey = MakeUserKey();
                Debug.Log($"[FirebaseChecklist] Update username path: checklistsByUserKey/{userKey}/{checklistId}");
                await NodeByUserKey(userKey).Child(checklistId).UpdateChildrenAsync(updates);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseChecklist] Mirror update failed: {ex.Message}");
            }
        }

        return true;
    }

    public async Task<bool> DeleteChecklistAsync(string checklistId)
    {
        await AuthService.EnsureInitializedAsync();
        string uid = AuthService.UserId;
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(checklistId)) return false;

        Debug.Log($"[FirebaseChecklist] Delete UID path: checklistsByUid/{uid}/{checklistId}");
        await NodeByUid(uid).Child(checklistId).RemoveValueAsync();

        if (mirrorByUsername)
        {
            try
            {
                var userKey = MakeUserKey();
                Debug.Log($"[FirebaseChecklist] Delete username path: checklistsByUserKey/{userKey}/{checklistId}");
                await NodeByUserKey(userKey).Child(checklistId).RemoveValueAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirebaseChecklist] Mirror delete failed: {ex.Message}");
            }
        }

        return true;
    }

    // Helpers
    static void FillDefaults(ChecklistDTO dto)
    {
        dto.Requirements ??= new List<string>();
        dto.CheckedItems ??= new List<bool>();
        while (dto.CheckedItems.Count < dto.Requirements.Count) dto.CheckedItems.Add(false);
        if (dto.CheckedItems.Count > dto.Requirements.Count)
            dto.CheckedItems = dto.CheckedItems.Take(dto.Requirements.Count).ToList();

        dto.Priorities ??= Enumerable.Range(1, dto.Requirements.Count).ToList();
        if (dto.Priorities.Count != dto.Requirements.Count)
            dto.Priorities = Enumerable.Range(1, dto.Requirements.Count).ToList();

        dto.Progress = dto.CheckedItems.Count == 0
            ? 0f
            : dto.CheckedItems.Count(b => b) / (float)dto.CheckedItems.Count;
    }
}