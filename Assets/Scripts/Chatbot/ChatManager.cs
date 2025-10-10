using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class ChatManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject chatPanel;              // the chat panel container
    public Transform contentParent;           // ScrollView/Viewport/Content
    public TMP_InputField inputField;
    public Button sendButton;
    public Button toggleButton;               // ← toggle button reference

    [Header("Prefabs")]
    public GameObject userMessagePrefab;      // assign UserMessageBubble prefab
    public GameObject botMessagePrefab;       // assign BotMessageBubble prefab

    [Header("Gemini API Key")]
    public string apiKey = "YOUR_API_KEY_HERE";
    private GeminiClient client;

    // helper cached references
    private ScrollRect cachedScrollRect;
    private RectTransform contentRect; // fixed: declared here so all uses compile

    void Start()
    {
        client = new GeminiClient(apiKey);

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendMessage);
        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleChatPanel);

        if (chatPanel != null)
            chatPanel.SetActive(false); // hide by default

        // Ensure the ScrollRect/Viewport/Content are configured correctly at runtime.
        EnsureScrollSetup();
    }

    void OnDestroy()
    {
        if (sendButton != null) sendButton.onClick.RemoveListener(OnSendMessage);
        if (toggleButton != null) toggleButton.onClick.RemoveListener(ToggleChatPanel);
    }

    // Toggle
    public void ToggleChatPanel()
    {
        if (chatPanel != null)
            chatPanel.SetActive(!chatPanel.activeSelf);
    }

    async void OnSendMessage()
    {
        if (inputField == null) return;
        string msg = inputField.text.Trim();
        if (string.IsNullOrEmpty(msg)) return;

        inputField.text = "";

        // Spawn user bubble
        CreateMessageBubble(msg, true);

        // Get Bot response
        string response = "No response (client not configured)";
        if (client != null)
        {
            try { response = await client.GetChatResponseAsync(msg); }
            catch (System.Exception ex)
            {
                Debug.LogWarning("ChatManager: error calling client: " + ex);
                response = "Sorry, something went wrong.";
            }
        }

        // Spawn bot bubble
        CreateMessageBubble(response, false);
    }

    // Ensure ScrollRect/Viewport/Content are configured and viewport won't steal clicks
    void EnsureScrollSetup()
    {
        // try to get contentRect from contentParent
        if (contentParent != null)
            contentRect = contentParent as RectTransform;

        // find a ScrollRect if needed
        if (cachedScrollRect == null && contentParent != null)
            cachedScrollRect = contentParent.GetComponentInParent<ScrollRect>();

        if (cachedScrollRect == null)
            cachedScrollRect = FindObjectOfType<ScrollRect>();

        // if contentParent still null try to grab from found ScrollRect
        if (contentParent == null && cachedScrollRect != null && cachedScrollRect.content != null)
        {
            contentParent = cachedScrollRect.content;
            contentRect = contentParent as RectTransform;
        }

        if (cachedScrollRect != null)
        {
            // ensure viewport assigned
            if (cachedScrollRect.viewport == null)
            {
                var v = cachedScrollRect.transform.Find("Viewport");
                if (v != null) cachedScrollRect.viewport = v.GetComponent<RectTransform>();
            }

            // ensure content assigned
            if (cachedScrollRect.content == null && contentRect != null)
                cachedScrollRect.content = contentRect;

            // sane defaults
            cachedScrollRect.horizontal = false;
            cachedScrollRect.vertical = true;
            cachedScrollRect.movementType = ScrollRect.MovementType.Elastic;
            cachedScrollRect.inertia = true;
            cachedScrollRect.scrollSensitivity = Mathf.Max(1f, cachedScrollRect.scrollSensitivity);

            // viewport must not block clicks: disable raycastTarget on its Image and ensure mask
            if (cachedScrollRect.viewport != null)
            {
                var img = cachedScrollRect.viewport.GetComponent<Image>();
                if (img != null) img.raycastTarget = false;

                if (cachedScrollRect.viewport.GetComponent<RectMask2D>() == null &&
                    cachedScrollRect.viewport.GetComponent<Mask>() == null)
                {
                    cachedScrollRect.viewport.gameObject.AddComponent<RectMask2D>();
                }
            }

            // ensure vertical scrollbar is interactable if present
            if (cachedScrollRect.verticalScrollbar != null)
            {
                cachedScrollRect.verticalScrollbar.interactable = true;
                cachedScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            }
        }

        // ensure EventSystem exists
        if (EventSystem.current == null)
        {
            var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(esGo);
        }
    }

    // Your original CreateMessageBubble with minimal, safe layout rebuild & scroll
    void CreateMessageBubble(string text, bool isUser)
    {
        if (contentParent == null)
        {
            Debug.LogWarning("ChatManager: contentParent is not set.");
            return;
        }

        GameObject prefab = isUser ? userMessagePrefab : botMessagePrefab;
        if (prefab == null)
        {
            Debug.LogWarning("ChatManager: message prefab not assigned.");
            return;
        }

        // Instantiate the message bubble
        GameObject msgObj = Instantiate(prefab, contentParent, false);
        msgObj.transform.SetAsLastSibling();

        // Find the text and bubble components
        TextMeshProUGUI tmp = msgObj.GetComponentInChildren<TextMeshProUGUI>(true);
        Transform bubbleTrans = msgObj.transform.Find("BubbleContainer");

        if (tmp == null || bubbleTrans == null)
        {
            Debug.LogWarning("ChatManager: prefab missing TextMeshPro or BubbleContainer child.");
            Destroy(msgObj);
            return;
        }

        RectTransform bubbleRect = bubbleTrans.GetComponent<RectTransform>();

        // Configure TextMeshPro
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.text = text;

        // Calculate bubble size
        float maxWidth = Screen.width * 0.65f;
        float paddingX = 30f;
        float paddingY = 20f;

        tmp.ForceMeshUpdate();
        Vector2 textSize = tmp.GetPreferredValues(text, maxWidth - paddingX, Mathf.Infinity);

        float bubbleWidth = Mathf.Min(maxWidth, textSize.x + paddingX);
        float bubbleHeight = textSize.y + paddingY;

        bubbleRect.sizeDelta = new Vector2(bubbleWidth, bubbleHeight);

        // Set colors
        Image bubbleImage = bubbleRect.GetComponent<Image>();
        if (bubbleImage != null)
        {
            if (isUser)
            {
                bubbleImage.color = new Color32(0, 122, 255, 255);
                tmp.color = Color.white;
            }
            else
            {
                bubbleImage.color = new Color32(240, 240, 240, 255);
                tmp.color = Color.black;
            }
        }

        // Add Layout Element to the message root if it doesn't exist
        LayoutElement layoutElement = msgObj.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = msgObj.AddComponent<LayoutElement>();
        }
        layoutElement.preferredHeight = bubbleHeight + 20f; // Add some padding
        layoutElement.flexibleWidth = 0;
        layoutElement.flexibleHeight = 0;

        // Force layout rebuild
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
        Canvas.ForceUpdateCanvases();

        // Scroll to bottom after a short delay to ensure layout is complete
        StartCoroutine(ScrollToBottomDelayed());
    }

    private System.Collections.IEnumerator ScrollToBottomDelayed()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame(); // Wait 2 frames to ensure layout is complete

        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        Canvas.ForceUpdateCanvases();

        if (cachedScrollRect != null)
        {
            cachedScrollRect.verticalNormalizedPosition = 0f;
        }
    }
}