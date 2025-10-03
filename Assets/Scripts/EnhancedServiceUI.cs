
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class EnhancedServiceListUI : MonoBehaviour
{
    [Header("Service List Components")]
    public Transform serviceListParent; // Parent for service items
    public ScrollRect serviceScrollRect; // For scrollability

    [Header("Service Item Styling")]
    public Color serviceItemBackgroundColor = new Color(1f, 1f, 1f, 0.9f);
    public Color serviceItemTextColor = new Color(0.2f, 0.2f, 0.2f);
    public Color alternateBackgroundColor = new Color(0.95f, 0.95f, 0.95f, 0.9f);

    [Header("Spacing")]
    public float itemSpacing = 10f;
    public float itemPadding = 15f;
    public float itemMinHeight = 80f;

    private List<GameObject> serviceItems = new List<GameObject>();

    void Start()
    {
        // Auto-find components if not assigned
        if (serviceListParent == null)
        {
            serviceListParent = transform;
        }

        if (serviceScrollRect == null)
        {
            serviceScrollRect = GetComponentInParent<ScrollRect>();
        }
    }

    public void SetupEnhancedServiceDisplay(string servicesText)
    {
        PopulateServiceList(servicesText);
    }

    public void PopulateServiceList(string servicesText)
    {
        // Clear existing items
        ClearServiceList();

        // Parse services from text (remove bullets and split)
        List<string> services = ParseServicesFromText(servicesText);

        Debug.Log($"Creating {services.Count} service items");

        // Create service items
        for (int i = 0; i < services.Count; i++)
        {
            CreateServiceItem(services[i], i);
        }

        // Update layout
        UpdateScrollViewLayout();
    }

    private List<string> ParseServicesFromText(string servicesText)
    {
        List<string> services = new List<string>();

        // Split by newlines and clean up
        string[] lines = servicesText.Split('\n');

        foreach (string line in lines)
        {
            string cleanLine = line.Trim();

            // Remove bullet points (•, -, *, etc.)
            cleanLine = cleanLine.TrimStart('•', '-', '*', '▪', '▫', '◦');
            cleanLine = cleanLine.Trim();

            // Skip empty lines
            if (!string.IsNullOrEmpty(cleanLine))
            {
                services.Add(cleanLine);
            }
        }

        return services;
    }

    private void CreateServiceItem(string serviceText, int index)
    {
        // Create service item container
        GameObject serviceItem = new GameObject($"ServiceItem_{index}");
        serviceItem.transform.SetParent(serviceListParent, false);

        // Set up RectTransform properly
        RectTransform itemRect = serviceItem.AddComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0, 1);
        itemRect.anchorMax = new Vector2(1, 1);
        itemRect.pivot = new Vector2(0.5f, 1);

        // Add background image
        Image background = serviceItem.AddComponent<Image>();
        background.color = (index % 2 == 0) ? serviceItemBackgroundColor : alternateBackgroundColor;

        // Add layout element for proper sizing
        LayoutElement layoutElement = serviceItem.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1f;
        layoutElement.minHeight = itemMinHeight;
        layoutElement.preferredHeight = itemMinHeight;

        // Add content size fitter for dynamic height
        ContentSizeFitter sizeFitter = serviceItem.AddComponent<ContentSizeFitter>();
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Create text component
        GameObject textObject = new GameObject("ServiceText");
        textObject.transform.SetParent(serviceItem.transform, false);

        TextMeshProUGUI serviceTextComponent = textObject.AddComponent<TextMeshProUGUI>();
        serviceTextComponent.text = serviceText;
        serviceTextComponent.fontSize = 16f; // Increased font size
        serviceTextComponent.color = serviceItemTextColor;
        serviceTextComponent.fontStyle = FontStyles.Normal;
        serviceTextComponent.alignment = TextAlignmentOptions.Left;
        serviceTextComponent.enableWordWrapping = true;
        serviceTextComponent.margin = new Vector4(itemPadding, itemPadding, itemPadding, itemPadding);

        // Set text anchoring to fill parent with proper margins
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(itemPadding, itemPadding);
        textRect.offsetMax = new Vector2(-itemPadding, -itemPadding);

        // Add hover effect (optional)
        AddHoverEffect(serviceItem, background);

        // Add to list for management
        serviceItems.Add(serviceItem);

        Debug.Log($"Created service item {index}: {serviceText}");
    }

    private void AddHoverEffect(GameObject serviceItem, Image background)
    {
        // Add button component for hover detection
        Button button = serviceItem.AddComponent<Button>();
        button.targetGraphic = background;

        // Create color block for hover effect
        ColorBlock colors = button.colors;
        colors.normalColor = background.color;
        colors.highlightedColor = new Color(background.color.r * 0.9f, background.color.g * 0.9f, background.color.b * 0.9f, background.color.a);
        colors.pressedColor = new Color(background.color.r * 0.8f, background.color.g * 0.8f, background.color.b * 0.8f, background.color.a);
        colors.fadeDuration = 0.1f;
        button.colors = colors;

        // Optional: Add click functionality
        button.onClick.AddListener(() => OnServiceItemClicked(serviceItem));
    }

    private void OnServiceItemClicked(GameObject serviceItem)
    {
        // Optional: Handle service item clicks (e.g., show more details)
        Debug.Log($"Clicked service: {serviceItem.GetComponentInChildren<TextMeshProUGUI>().text}");
    }

    private void UpdateScrollViewLayout()
    {
        if (serviceListParent == null) return;

        // Add vertical layout group if it doesn't exist
        VerticalLayoutGroup layoutGroup = serviceListParent.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup == null)
        {
            layoutGroup = serviceListParent.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        // Configure layout group with better settings
        layoutGroup.spacing = itemSpacing;
        layoutGroup.padding = new RectOffset(10, 10, 10, 10);
        layoutGroup.childAlignment = TextAnchor.UpperCenter;
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = false;
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = false;
        layoutGroup.childScaleWidth = false;
        layoutGroup.childScaleHeight = false;

        // Add content size fitter to parent
        ContentSizeFitter parentSizeFitter = serviceListParent.GetComponent<ContentSizeFitter>();
        if (parentSizeFitter == null)
        {
            parentSizeFitter = serviceListParent.gameObject.AddComponent<ContentSizeFitter>();
        }

        parentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        parentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Ensure scroll rect is properly configured
        if (serviceScrollRect != null)
        {
            serviceScrollRect.content = serviceListParent.GetComponent<RectTransform>();
            serviceScrollRect.vertical = true;
            serviceScrollRect.horizontal = false;
            serviceScrollRect.scrollSensitivity = 30f;
            serviceScrollRect.movementType = ScrollRect.MovementType.Clamped;
        }

        // Force layout rebuild
        LayoutRebuilder.ForceRebuildLayoutImmediate(serviceListParent.GetComponent<RectTransform>());

        Debug.Log($"Layout updated with {serviceItems.Count} items");
    }

    private void ClearServiceList()
    {
        // Destroy existing service items
        foreach (GameObject item in serviceItems)
        {
            if (item != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(item);
                }
                else
                {
                    DestroyImmediate(item);
                }
            }
        }
        serviceItems.Clear();

        Debug.Log("Service list cleared");
    }
}
