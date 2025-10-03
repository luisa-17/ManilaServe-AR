using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class CityChecklistServiceAdapter : MonoBehaviour, IChecklistService
{
    const string PP_PREFIX = "checklists_";

    [Serializable] class Wrap { public List<ChecklistDTO> items = new List<ChecklistDTO>(); }

    public Task<List<ChecklistDTO>> GetUserChecklistsAsync(string userId)
    {
        return Task.FromResult(Load(userId));
    }

    public Task<bool> UpdateChecklistProgressAsync(string checklistId, List<bool> checkedItems)
    {
        var all = Load(AuthService.UserId);
        var item = all.FirstOrDefault(x => x.Id == checklistId);
        if (item == null) return Task.FromResult(false);
        item.CheckedItems = new List<bool>(checkedItems);
        item.Progress = item.CheckedItems.Count == 0 ? 0f : item.CheckedItems.Count(b => b) / (float)item.CheckedItems.Count;
        Save(AuthService.UserId, all);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteChecklistAsync(string checklistId)
    {
        var all = Load(AuthService.UserId);
        int removed = all.RemoveAll(x => x.Id == checklistId);
        Save(AuthService.UserId, all);
        return Task.FromResult(removed > 0);
    }

    public Task<(bool ok, string id)> CreateChecklistAsync(string userId, string officeName, string serviceName, List<string> requirements)
    {
        if (requirements == null || requirements.Count == 0)
            requirements = new List<string> { "Valid ID", "Application Form", "Payment Receipt" };

        var dto = new ChecklistDTO
        {
            Id = Guid.NewGuid().ToString("N"),
            OfficeName = string.IsNullOrEmpty(officeName) ? "Unknown Office" : officeName,
            ServiceName = string.IsNullOrEmpty(serviceName) ? "General Service" : serviceName,
            Requirements = new List<string>(requirements),
            CheckedItems = Enumerable.Repeat(false, requirements.Count).ToList(),
            Progress = 0f
        };

        var all = Load(userId);
        all.Add(dto);
        Save(userId, all);
        return Task.FromResult((true, dto.Id));
    }

    List<ChecklistDTO> Load(string userId)
    {
        string key = PP_PREFIX + (string.IsNullOrEmpty(userId) ? "guest" : userId);
        var json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return new List<ChecklistDTO>();
        try
        {
            var wrap = JsonUtility.FromJson<Wrap>(json);
            return wrap?.items ?? new List<ChecklistDTO>();
        }
        catch { return new List<ChecklistDTO>(); }
    }

    void Save(string userId, List<ChecklistDTO> list)
    {
        string key = PP_PREFIX + (string.IsNullOrEmpty(userId) ? "guest" : userId);
        var wrap = new Wrap { items = list };
        var json = JsonUtility.ToJson(wrap);
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
    }

    // Add inside CityChecklistServiceAdapter class
    [ContextMenu("Clear Local Checklists")]
    public void ClearLocalChecklistsForCurrentUser()
    {
        const string PP_PREFIX = "checklists_"; // keep same prefix you used in the adapter
        string uid = string.IsNullOrEmpty(AuthService.UserId) ? "guest" : AuthService.UserId;
        string key = PP_PREFIX + uid;
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        Debug.Log($"[Checklist] Cleared local checklists for key: {key}");
    }

}