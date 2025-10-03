using System;
using System.Collections.Generic;

[Serializable]
public class ChecklistDTO
{
    public string Id;
    public string OfficeName;
    public string ServiceName;

    public List<string> Requirements = new List<string>();
    public List<bool> CheckedItems = new List<bool>();

    // NEW: admin-driven ordering
    public List<int> Priorities = new List<int>();      // aligns 1:1 with Requirements
    public List<float> Fees = new List<float>();        // optional, can stay empty

    public float Progress; // 0..1
}

public interface IChecklistService
{
    System.Threading.Tasks.Task<List<ChecklistDTO>> GetUserChecklistsAsync(string userId);
    System.Threading.Tasks.Task<bool> UpdateChecklistProgressAsync(string checklistId, List<bool> checkedItems);
    System.Threading.Tasks.Task<bool> DeleteChecklistAsync(string checklistId);
    System.Threading.Tasks.Task<(bool ok, string id)> CreateChecklistAsync(string userId, string officeId, string serviceId, List<string> requirements);
}