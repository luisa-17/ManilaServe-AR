using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChecklistCardView : MonoBehaviour
{
    public TextMeshProUGUI officeNameText;
    public TextMeshProUGUI serviceNameText;
    public GameObject completedChip;

    public Slider progressBar;
    public TextMeshProUGUI progressLabel;

    public Transform requirementsContainer;
    public Button deleteButton;

    string _id;
    List<bool> _checked;
    Action<string, List<bool>> _onToggleChanged;
    Action<string> _onDelete;

    public void Bind(
        ChecklistDTO data,
        ChecklistRequirementRowView rowPrefab,
        Action<string, List<bool>> onToggleChanged,
        Action<string> onDeleteClicked)
    {
        _id = data.Id;
        _onToggleChanged = onToggleChanged;
        _onDelete = onDeleteClicked;
        _checked = new List<bool>(data.CheckedItems);

        if (officeNameText) officeNameText.text = data.OfficeName;
        if (serviceNameText) serviceNameText.text = $"Requesting For: {data.ServiceName}";

        UpdateProgressUI(data.Requirements.Count, data.CheckedItems);

        foreach (Transform c in requirementsContainer) Destroy(c.gameObject);

        for (int i = 0; i < data.Requirements.Count; i++)
        {
            var row = Instantiate(rowPrefab, requirementsContainer);
            int idx = i;
            row.Bind(data.Requirements[i], data.CheckedItems[i], (val) =>
            {
                _checked[idx] = val;
                UpdateProgressUI(data.Requirements.Count, _checked);
                _onToggleChanged?.Invoke(_id, new List<bool>(_checked));
            });
        }

        if (deleteButton)
        {
            deleteButton.onClick.RemoveAllListeners();
            deleteButton.onClick.AddListener(() => _onDelete?.Invoke(_id));
        }
    }

    void UpdateProgressUI(int total, List<bool> states)
    {
        float p = total == 0 ? 0f : (states.Count(b => b) / (float)total);
        if (progressBar) progressBar.value = p;
        if (progressLabel) progressLabel.text = $"{Mathf.RoundToInt(p * 100)}%";
        if (completedChip) completedChip.SetActive(p >= 1f);
    }
}