using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Text;

public class PointerDebugger : MonoBehaviour
{
    void Start()
    {
        Debug.Log($"PointerDebugger started. AppFocused={Application.isFocused}, EventSystem={(EventSystem.current != null)}");
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
            DoRaycast(Input.mousePosition, "Mouse");
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            DoRaycast(Input.GetTouch(0).position, "Touch");
    }

    void DoRaycast(Vector2 screenPos, string source)
    {
        if (EventSystem.current == null) { Debug.LogWarning("No EventSystem present"); return; }

        var pointer = new PointerEventData(EventSystem.current) { position = screenPos };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, results);

        var sb = new StringBuilder();
        sb.AppendLine($"PointerDebugger ({source}) at {screenPos}: hits={results.Count}, top-first:");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var g = r.gameObject;
            var graphic = g.GetComponent<Graphic>();
            string ray = graphic != null ? graphic.raycastTarget.ToString() : "noGraphic";
            sb.AppendLine($"{i}: path='{GetFullPath(g.transform)}' name='{g.name}' depth={r.depth} raycastTarget={ray}");
        }
        Debug.Log(sb.ToString());
    }

    string GetFullPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}