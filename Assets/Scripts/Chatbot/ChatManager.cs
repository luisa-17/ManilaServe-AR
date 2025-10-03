using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    void Start()
    {
        client = new GeminiClient(apiKey);

        sendButton.onClick.AddListener(OnSendMessage);
        toggleButton.onClick.AddListener(ToggleChatPanel);   // ← re‑added

        if (chatPanel != null)
            chatPanel.SetActive(false); // hide by default
    }

    // ✅ Toggle button logic
    public void ToggleChatPanel()
    {
        if (chatPanel != null)
            chatPanel.SetActive(!chatPanel.activeSelf);
    }

    async void OnSendMessage()
    {
        string msg = inputField.text.Trim();
        if (string.IsNullOrEmpty(msg)) return;

        inputField.text = "";

        // ➤ Spawn User bubble (blue, right‑aligned)
        CreateMessageBubble(msg, true);

        // ➤ Get Bot response
        string response = await client.GetChatResponseAsync(msg);

        // ➤ Spawn Bot bubble (grey, left‑aligned)
        CreateMessageBubble(response, false);
    }


    //-------------------------------------------------------
    // Create bubble for User (isUser=true) or Bot (isUser=false)
    //-------------------------------------------------------
    void CreateMessageBubble(string text, bool isUser)
    {
        GameObject prefab = isUser ? userMessagePrefab : botMessagePrefab;
        GameObject msgObj = Instantiate(prefab, contentParent);

        // Grab TMP and Bubble
        TextMeshProUGUI tmp = msgObj.GetComponentInChildren<TextMeshProUGUI>();
        RectTransform bubbleRect = msgObj.transform.Find("BubbleContainer").GetComponent<RectTransform>();

        if (tmp == null || bubbleRect == null) return;

        // Assign text
        tmp.text = text;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;

        // Force TMP to calculate values
        tmp.ForceMeshUpdate();

        // Max bubble width = 65% of screen
        float maxWidth = Screen.width * 0.65f;
        float paddingX = 30f; // internal left+right
        float paddingY = 20f; // top+bottom

        // Ask TMP its preferred size at this width
        Vector2 preferredSize = tmp.GetPreferredValues(text, maxWidth - paddingX, Mathf.Infinity);

        float bubbleWidth = Mathf.Min(maxWidth, preferredSize.x + paddingX);
        float bubbleHeight = preferredSize.y + paddingY;

        // ✅ Apply bubble size
        bubbleRect.sizeDelta = new Vector2(bubbleWidth, bubbleHeight);

        // ✅ Colors styling
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

        // ✅ Auto-scroll
        Canvas.ForceUpdateCanvases();
        var scroll = contentParent.GetComponentInParent<ScrollRect>();
        if (scroll) scroll.verticalNormalizedPosition = 0f;
    }
}