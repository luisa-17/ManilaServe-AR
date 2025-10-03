using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChecklistRequirementRowView : MonoBehaviour
{
    public Toggle toggle;
    public TextMeshProUGUI label;

    public void Bind(string text, bool isOn, Action<bool> onChanged)
    {
        if (label) label.text = text;

        if (toggle)
        {
            toggle.onValueChanged.RemoveAllListeners();
            toggle.isOn = isOn;
            toggle.onValueChanged.AddListener(v => onChanged?.Invoke(v));
        }
    }

    public void SetLabel(string text)
    {
        if (label) label.text = text;
    }
    public void SetLabelColor(Color c)
    {
        if (label) label.color = c;
    }
    public void SetInteractable(bool on)
    {
        if (toggle) toggle.interactable = on;
    }

}