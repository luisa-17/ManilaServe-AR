using UnityEngine;
using UnityEngine.UI;

public class OfficeSelectionGate : MonoBehaviour
{
    public Selectable[] selectables;
    public CanvasGroup[] groups;
    public GameObject[] showWhenReady;

        bool _lastApplied;
    float _pollTimer;

    void OnEnable()
    {
        Apply(PlaceOnFloorARF.FloorReadyGlobal);
        PlaceOnFloorARF.OnFloorReadyChanged += Apply;
    }

    void OnDisable()
    {
        PlaceOnFloorARF.OnFloorReadyChanged -= Apply;
    }

    void Update()
    {
        // Poll every 0.25s as a safety net
        _pollTimer += Time.deltaTime;
        if (_pollTimer >= 0.25f)
        {
            _pollTimer = 0f;
            Apply(PlaceOnFloorARF.FloorReadyGlobal);
        }
    }

    void Apply(bool enable)
    {
        if (enable == _lastApplied) return;
        _lastApplied = enable;

        if (selectables != null)
            foreach (var s in selectables) if (s) s.interactable = enable;

        if (groups != null)
            foreach (var g in groups) if (g) { g.interactable = enable; g.blocksRaycasts = enable; }

        if (showWhenReady != null)
            foreach (var go in showWhenReady) if (go) go.SetActive(enable);

        // Close any stray TMP dropdown popups when disabling
        if (!enable && selectables != null)
        {
            foreach (var s in selectables)
            {
                var dd = s as TMPro.TMP_Dropdown;
                if (dd)
                {
                    dd.Hide();
                    var root = dd.transform.root;
                    foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
                        if (rt.name == "TMP Dropdown List" || rt.name == "Dropdown List")
                            Destroy(rt.gameObject);
                }
            }
        }
    }
}