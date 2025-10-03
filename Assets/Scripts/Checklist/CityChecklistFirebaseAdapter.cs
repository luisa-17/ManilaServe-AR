using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Database;
using UnityEngine;

public class CityChecklistFirebaseAdapter : MonoBehaviour, IChecklistService
{
    [ContextMenu("Create Test Checklist (MHD/MHD1)")]
    async void CreateTestChecklist()
    {
        if (!AuthService.IsSignedIn)
            await AuthService.SignInAnonymouslyAsync();
        var res = await CreateChecklistAsync(AuthService.UserId, "MHD", "MHD1", null);
        Debug.Log(res.ok ? $"Wrote checklist id: {res.id}" : "Create failed");
    }

    public AdminCatalogFirebase adminCatalog; 

    DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;
    string U(string uid) => $"checklists/{uid}";

    public async Task<List<ChecklistDTO>> GetUserChecklistsAsync(string userId)
    {
        var list = new List<ChecklistDTO>();
        if (string.IsNullOrEmpty(userId)) return list;

        var snap = await Root.Child(U(userId)).GetValueAsync();
        if (!snap.Exists) return list;

        foreach (var c in snap.Children)
        {
            var dto = JsonUtility.FromJson<ChecklistDTO>(c.GetRawJsonValue() ?? "{}") ?? new ChecklistDTO();
            dto.Id = c.Key;

            dto.Requirements ??= new List<string>();
            dto.CheckedItems ??= Enumerable.Repeat(false, dto.Requirements.Count).ToList();
            if (dto.CheckedItems.Count != dto.Requirements.Count)
                dto.CheckedItems = Enumerable.Repeat(false, dto.Requirements.Count).ToList();

            dto.Priorities ??= Enumerable.Range(1, dto.Requirements.Count).ToList();
            if (dto.Priorities.Count != dto.Requirements.Count)
                dto.Priorities = Enumerable.Range(1, dto.Requirements.Count).ToList();

            // simple progress (or use weighted if you prefer)
            dto.Progress = dto.CheckedItems.Count == 0 ? 0f :
                dto.CheckedItems.Count(b => b) / (float)dto.CheckedItems.Count;

            list.Add(dto);
        }
        return list;
    }

    public async Task<(bool ok, string id)> CreateChecklistAsync(string userId, string officeId, string serviceId, List<string> requirements = null)
    {
        if (string.IsNullOrEmpty(userId)) return (false, null);

        List<(string name, int priority)> items = new();
        if (requirements == null || requirements.Count == 0)
        {
            var res = await adminCatalog.GetRequirementsAsync(officeId, serviceId);
            if (!res.ok) return (false, null);
            items = res.items;
        }
        else
        {
            items = requirements.Select((n, i) => (n, i + 1)).ToList();
        }

        var dto = new ChecklistDTO
        {
            OfficeName = officeId,    // storing IDs; you can also add display names
            ServiceName = serviceId,
            Requirements = items.Select(i => i.name).ToList(),
            CheckedItems = Enumerable.Repeat(false, items.Count).ToList(),
            Priorities = items.Select(i => i.priority).ToList(),
            Progress = 0f
        };

        var push = Root.Child(U(userId)).Push();
        dto.Id = push.Key;
        await push.SetRawJsonValueAsync(JsonUtility.ToJson(dto));
        return (true, dto.Id);
    }

    public async Task<bool> UpdateChecklistProgressAsync(string checklistId, List<bool> checkedItems)
    {
        string uid = AuthService.UserId;
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(checklistId)) return false;

        var updates = new Dictionary<string, object>
        {
            ["CheckedItems"] = checkedItems.Select(b => (object)b).ToList(),
            ["Progress"] = (double)(checkedItems.Count == 0 ? 0f :
                checkedItems.Count(b => b) / (float)checkedItems.Count)
        };
        await Root.Child(U(uid)).Child(checklistId).UpdateChildrenAsync(updates);
        return true;
    }

    public async Task<bool> DeleteChecklistAsync(string checklistId)
    {
        string uid = AuthService.UserId;
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(checklistId)) return false;
        await Root.Child(U(uid)).Child(checklistId).RemoveValueAsync();
        return true;
    }
}