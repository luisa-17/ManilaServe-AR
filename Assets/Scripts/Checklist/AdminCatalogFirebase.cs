using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Database;
using UnityEngine;

public class AdminCatalogFirebase : MonoBehaviour
{
    DatabaseReference Root => FirebaseDatabase.DefaultInstance.RootReference;

    public async Task<(bool ok, List<(string name, int priority)> items)>
        GetRequirementsAsync(string officeId, string serviceId)
    {
        var items = new List<(string, int)>();
        try
        {
            var snap = await Root.Child("offices").GetValueAsync();
            if (!snap.Exists) return (false, items);

            foreach (var o in snap.Children)
            {
                string oid = o.Child("OfficeId").Value?.ToString() ?? "";
                if (!string.Equals(oid, officeId, StringComparison.OrdinalIgnoreCase)) continue;

                var services = o.Child("Services");
                foreach (var s in services.Children)
                {
                    string sid = s.Child("ServiceId").Value?.ToString() ?? "";
                    if (!string.Equals(sid, serviceId, StringComparison.OrdinalIgnoreCase)) continue;

                    var reqs = s.Child("Requirements");
                    foreach (var r in reqs.Children)
                    {
                        string name = r.Child("Name").Value?.ToString() ?? "";
                        int prio = int.TryParse(r.Child("Priority").Value?.ToString(), out var p) ? p : int.MaxValue;
                        items.Add((name, prio));
                    }
                    items.Sort((a, b) => a.Item2.CompareTo(b.Item2));
                    return (items.Count > 0, items);
                }
            }
            return (items.Count > 0, items);
        }
        catch (Exception e)
        {
            Debug.LogError("[AdminCatalogFirebase] " + e);
            return (false, items);
        }
    }

    // Quick in-editor test
    [ContextMenu("Test Fetch (MHD/MHD1)")]
    async void TestFetch()
    {
        var res = await GetRequirementsAsync("MHD", "MHD1");
        if (!res.ok) { Debug.LogWarning("No requirements found"); return; }
        Debug.Log("REQUIREMENTS:\n - " + string.Join("\n - ", res.items));
    }
}