using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class CityChecklistServiceAdapter : MonoBehaviour, IChecklistService
{
    const string PP_PREFIX = "checklists_";
    const string FallbackUser = "local";

    [Serializable]
class Wrap { public List<ChecklistDTO> items = new List<ChecklistDTO>(); }

    // IChecklistService
    public Task<List<ChecklistDTO>> GetUserChecklistsAsync(string userId)
    {
        var list = Load(userId);
        Normalize(list);
        return Task.FromResult(list);
    }

    public Task<bool> UpdateChecklistProgressAsync(string checklistId, List<bool> checkedItems)
    {
        string uid = SafeUserId(AuthService.UserId);
        var list = Load(uid);

        var item = list.FirstOrDefault(x => x.Id == checklistId);
        if (item == null) return Task.FromResult(false);

        item.CheckedItems = checkedItems != null ? new List<bool>(checkedItems) : new List<bool>();
        Normalize(item);

        Save(uid, list);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteChecklistAsync(string checklistId)
    {
        string uid = SafeUserId(AuthService.UserId);
        var list = Load(uid);

        int removed = list.RemoveAll(x => x.Id == checklistId);
        Save(uid, list);
        return Task.FromResult(removed > 0);
    }

    // Note: officeId/serviceId may be IDs or names; we resolve names via ChecklistContext if available.
    public Task<(bool ok, string id)> CreateChecklistAsync(string userId, string officeId, string serviceId, List<string> requirements)
    {
        string uid = SafeUserId(userId);
        var list = Load(uid);

        // Resolve display names
        string officeName = !string.IsNullOrEmpty(ChecklistContext.SelectedOfficeName)
            ? ChecklistContext.SelectedOfficeName
            : officeId;

        string serviceName = !string.IsNullOrEmpty(ChecklistContext.SelectedServiceName)
            ? ChecklistContext.SelectedServiceName
            : serviceId;

        // Pick requirements: provided > context > safe default
        List<string> reqs =
            (requirements != null && requirements.Count > 0) ? new List<string>(requirements) :
            (ChecklistContext.SelectedRequirements != null && ChecklistContext.SelectedRequirements.Count > 0) ? new List<string>(ChecklistContext.SelectedRequirements) :
            new List<string> { "Valid ID", "Application Form", "Payment Receipt" };

        var dto = new ChecklistDTO
        {
            Id = Guid.NewGuid().ToString("N"),
            OfficeName = string.IsNullOrEmpty(officeName) ? "Unknown Office" : officeName,
            ServiceName = string.IsNullOrEmpty(serviceName) ? "General Service" : serviceName,
            Requirements = reqs,
            CheckedItems = Enumerable.Repeat(false, reqs.Count).ToList(),
            Progress = 0f
        };

        Normalize(dto);
        list.Add(dto);
        Save(uid, list);

        return Task.FromResult((true, dto.Id));
    }

    // Storage helpers
    string SafeUserId(string userId) => string.IsNullOrEmpty(userId) ? FallbackUser : userId;
    string Key(string userId) => PP_PREFIX + SafeUserId(userId);

    List<ChecklistDTO> Load(string userId)
    {
        string key = Key(userId);
        Debug.Log($"[ChecklistService] Load key={key}");

        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return new List<ChecklistDTO>();

        try
        {
            var wrap = JsonUtility.FromJson<Wrap>(json);
            return wrap?.items ?? new List<ChecklistDTO>();
        }
        catch
        {
            return new List<ChecklistDTO>();
        }
    }

    void Save(string userId, List<ChecklistDTO> list)
    {
        string key = Key(userId);
        Debug.Log($"[ChecklistService] Save key={key} items={list?.Count ?? 0}");

        var wrap = new Wrap { items = list };
        string json = JsonUtility.ToJson(wrap);
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
    }

    // Normalization
    void Normalize(List<ChecklistDTO> list)
    {
        if (list == null) return;
        foreach (var c in list) Normalize(c);
    }

    void Normalize(ChecklistDTO c)
    {
        c.Requirements ??= new List<string>();
        c.CheckedItems ??= new List<bool>();
        while (c.CheckedItems.Count < c.Requirements.Count) c.CheckedItems.Add(false);
        if (c.CheckedItems.Count > c.Requirements.Count)
            c.CheckedItems = c.CheckedItems.Take(c.Requirements.Count).ToList();

        c.Progress = c.CheckedItems.Count == 0
            ? 0f
            : c.CheckedItems.Count(b => b) / (float)c.CheckedItems.Count;
    }

    [ContextMenu("Clear Local Checklists")]
    public void ClearLocalChecklistsForCurrentUser()
    {
        string uid = SafeUserId(AuthService.UserId);
        string key = Key(uid);
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        Debug.Log($"[Checklist] Cleared local checklists for key: {key}");
    }
}