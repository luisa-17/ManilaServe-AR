using System.Collections.Generic;
using UnityEngine;

public static class ChecklistContext
{
    // IDs (use if you fetch from DB)
    public static string SelectedOfficeId;
    public static string SelectedServiceId;

    // Display names (for UI)
    public static string SelectedOfficeName;
    public static string SelectedServiceName;

    // The requirements for the selected office/service (filled before opening ChecklistScene)
    public static List<string> SelectedRequirements = new List<string>();

    // Back-compat aliases (names)
    public static string SelectedOffice
    {
        get => SelectedOfficeName;
        set => SelectedOfficeName = value;
    }
    public static string SelectedService
    {
        get => SelectedServiceName;
        set => SelectedServiceName = value;
    }

    public static void SetSelection(string officeId, string officeName, string serviceId, string serviceName, List<string> requirements)
    {
        SelectedOfficeId = officeId;
        SelectedOfficeName = officeName;
        SelectedServiceId = serviceId;
        SelectedServiceName = serviceName;

        SelectedRequirements = requirements != null
            ? new List<string>(requirements)
            : new List<string>();
    }

    public static bool HasSelectionWithRequirements()
    {
        return !string.IsNullOrEmpty(SelectedOfficeName)
            && !string.IsNullOrEmpty(SelectedServiceName)
            && SelectedRequirements != null
            && SelectedRequirements.Count > 0;
    }

    public static void ClearSelection()
    {
        SelectedOfficeId = null;
        SelectedServiceId = null;
        SelectedOfficeName = null;
        SelectedServiceName = null;
        SelectedRequirements?.Clear();
    }
}