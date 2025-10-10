using System.Collections.Generic;

[System.Serializable]
public class ChecklistItem
{
    public string officeName;
    public string serviceName;
    public List<string> requirements;
    public string dateAdded;
    public bool isCompleted;
    public List<bool> requirementChecked; // tracks which requirements are checked off

    public ChecklistItem()
    {
        requirements = new List<string>();
        requirementChecked = new List<bool>();
        isCompleted = false;
    }
}