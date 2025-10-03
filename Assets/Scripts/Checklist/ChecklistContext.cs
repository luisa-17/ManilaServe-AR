using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ChecklistContext
{
    // IDs (used to fetch admin requirements)
    public static string SelectedOfficeId;
    public static string SelectedServiceId;

    // Display names (for UI)
    public static string SelectedOfficeName;
    public static string SelectedServiceName;

    // Back-compat aliases for old code
    public static string SelectedOffice   // legacy: treat as name
    {
        get => SelectedOfficeName;
        set => SelectedOfficeName = value;
    }
    public static string SelectedService  // legacy: treat as name
    {
        get => SelectedServiceName;
        set => SelectedServiceName = value;
    }
}