using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChecklistRequirementRowView : MonoBehaviour
{
    [Header("Core")]
    public Toggle toggle;
    public TextMeshProUGUI label;

    [Header("Optional visuals")]
    public Image background;                 // Background highlight panel (optional)
    public Image priorityChipBG;             // Chip circle (optional)
    public TextMeshProUGUI priorityChipText; // Chip number text (optional)
    public Image nextIcon;                   // Star/arrow icon (optional)

    void Awake()
    {
        // Safety: make sure the decorative images don't block clicks
        if (priorityChipBG) priorityChipBG.raycastTarget = false;
        if (priorityChipText) priorityChipText.raycastTarget = false;
        if (nextIcon) nextIcon.raycastTarget = false;
        if (label) label.raycastTarget = false; // optional; Toggle handles click
    }

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

    // Basic helpers (existing)
    public void SetLabel(string text) { if (label) label.text = text; }
    public void SetLabelColor(Color c) { if (label) label.color = c; }
    public void SetInteractable(bool on) { if (toggle) toggle.interactable = on; }

    // New helpers for visuals (safe if refs are not assigned)
    public void SetBackgroundColor(Color c) 
    { 
        if (background) {
            background.color = c; 
            background.gameObject.SetActive(c.a > 0.01f); 
        } 
    }

    public void SetPriorityChip(int priority, bool showNumber)
    {
        if (priorityChipBG) priorityChipBG.gameObject.SetActive(true);
        if (priorityChipText)
        {
            priorityChipText.gameObject.SetActive(showNumber);
            if (showNumber) priorityChipText.text = priority.ToString();
        }
    }

    public void SetNextIcon(bool on) 
    { 
        if (nextIcon) nextIcon.gameObject.SetActive(on); 
    }

}