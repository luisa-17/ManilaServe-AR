using System; // added for Environment
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Text.RegularExpressions;


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
    public string apiKey = "AIzaSyA2w9PcimvY1Z3wEikQYyF0O3wSsRBP17Q";
    private GeminiClient client;

    [Header("Bubble Sizing")]
    [Range(0.4f, 0.95f)] public float bubbleMaxWidthPercent = 0.72f;
    public float paddingX = 28f;   // total left+right padding inside bubble
    public float paddingY = 18f;   // total top+bottom padding inside bubble
    public Color32 userBubbleColor = new Color32(0, 122, 255, 255);
    public Color32 userTextColor = new Color32(255, 255, 255, 255);
    public Color32 botBubbleColor = new Color32(240, 240, 240, 255);
    public Color32 botTextColor = new Color32(20, 20, 20, 255);

    [Header("Avatar/Alignment")]
    public float sideMargin = 12f;           // gap from screen edge to icon/bubble
    public float avatarSize = 150f;           // size of the Icon child
    public float iconBubbleSpacing = 6f;     // gap between icon and bubble
    public string iconChildName = "Icon";    // change if your prefab uses a different name

    [Header("Bubble Alignment")]
    public float botSideMargin = 6f;     // smaller to push bot bubble closer to the left edge
    public float userSideMargin = 16f;   // keep a bit of space on the right for user

    // helper cached references
    private ScrollRect cachedScrollRect;
    private RectTransform contentRect; // fixed: declared here so all uses compile

    void Start()
    {
        client = new GeminiClient("AIzaSyA2w9PcimvY1Z3wEikQYyF0O3wSsRBP17Q");

        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendMessage);
        if (toggleButton != null)
            toggleButton.onClick.AddListener(ToggleChatPanel);

        if (chatPanel != null)
            chatPanel.SetActive(false);

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

        // Instantiate
        GameObject msgObj = Instantiate(prefab, contentParent, false);
        msgObj.name = (isUser ? "User" : "Bot") + "Message_" + Time.frameCount;
        msgObj.transform.SetAsLastSibling();

        // Find bubble and text
        Transform bubbleTrans = msgObj.transform.Find("BubbleContainer");
        if (bubbleTrans == null)
        {
            Debug.LogWarning("ChatManager: prefab missing 'BubbleContainer' child.");
            Destroy(msgObj);
            return;
        }
        RectTransform bubbleRect = bubbleTrans as RectTransform;

        TextMeshProUGUI tmp = bubbleTrans.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp == null) tmp = msgObj.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp == null)
        {
            Debug.LogWarning("ChatManager: no TextMeshProUGUI found.");
            Destroy(msgObj);
            return;
        }

        // Sanitize bot text (keep this so ** is gone)
        if (!isUser)
            text = SanitizeBotMarkdown(text);

        // Configure text
        tmp.enableWordWrapping = true;
        tmp.richText = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = isUser ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        tmp.text = text;

        // Colors
        Image bubbleImage = bubbleRect.GetComponent<Image>();
        if (bubbleImage != null)
        {
            bubbleImage.color = isUser ? userBubbleColor : botBubbleColor;
            bubbleImage.type = Image.Type.Sliced;
            bubbleImage.raycastTarget = false;
        }
        tmp.color = isUser ? userTextColor : botTextColor;

        // Compute width based on viewport
        float containerWidth = 0f;
        if (cachedScrollRect != null && cachedScrollRect.viewport != null)
            containerWidth = cachedScrollRect.viewport.rect.width;
        if (containerWidth <= 0f && contentParent is RectTransform cr)
            containerWidth = cr.rect.width;
        if (containerWidth <= 0f)
            containerWidth = Screen.width; // fallback

        float maxBubbleWidth = Mathf.Max(80f, containerWidth * bubbleMaxWidthPercent);

        // Measure and size bubble
        tmp.ForceMeshUpdate();
        Vector2 pref = tmp.GetPreferredValues(text, maxBubbleWidth - paddingX, Mathf.Infinity);

        float bubbleWidth = Mathf.Min(maxBubbleWidth, pref.x + paddingX);
        float bubbleHeight = Mathf.Max(36f, pref.y + paddingY);

        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleWidth);
        bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);

        // Alignment using your prefab’s HorizontalLayoutGroup (preferred)
        var hlg = msgObj.GetComponent<HorizontalLayoutGroup>();
        if (hlg != null)
        {
            // Different margins for bot vs user
            int padLeft = Mathf.RoundToInt(isUser ? botSideMargin : botSideMargin);  // both sides keep small general pad
            int padRight = Mathf.RoundToInt(isUser ? userSideMargin : userSideMargin); // give some breathing room opposite side

            // If you want true asymmetry (less margin on the bubble’s side):
            // padLeft  = Mathf.RoundToInt(isUser ? userSideMargin : botSideMargin);  // user row has more left, bot row has less left
            // padRight = Mathf.RoundToInt(isUser ? botSideMargin  : userSideMargin);  // user row has less right, bot row has more right

            hlg.padding = new RectOffset(padLeft, padRight, 2, 2);
            hlg.spacing = iconBubbleSpacing;

            // Let each child use its own size (we’ll set sizes via LayoutElement)
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            // Left for bot, right for user
            hlg.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

            // Icon: enforce 80x80 and preserve order
            Transform iconTrans = msgObj.transform.Find(iconChildName);
            if (iconTrans != null)
            {
                var iconRect = iconTrans as RectTransform;
                iconRect.localScale = Vector3.one; // avoid hidden scaling
                iconRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, avatarSize);
                iconRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, avatarSize);

                var iconLE = iconRect.GetComponent<LayoutElement>() ?? iconRect.gameObject.AddComponent<LayoutElement>();
                iconLE.minWidth = iconLE.preferredWidth = avatarSize;
                iconLE.minHeight = iconLE.preferredHeight = avatarSize;
                iconLE.flexibleWidth = 0; iconLE.flexibleHeight = 0;

                var iconImg = iconRect.GetComponent<Image>();
                if (iconImg != null) iconImg.preserveAspect = true;

                // Order: Bot = [Icon, Bubble], User = [Bubble, Icon]
                if (isUser)
                {
                    bubbleTrans.SetSiblingIndex(0);
                    iconTrans.SetSiblingIndex(1);
                }
                else
                {
                    iconTrans.SetSiblingIndex(0);
                    bubbleTrans.SetSiblingIndex(1);
                }
            }

            // Let HLG keep the exact bubble size we computed
            var bubbleLE = bubbleRect.GetComponent<LayoutElement>() ?? bubbleRect.gameObject.AddComponent<LayoutElement>();
            bubbleLE.preferredWidth = bubbleRect.sizeDelta.x;
            bubbleLE.preferredHeight = bubbleRect.sizeDelta.y;
            bubbleLE.flexibleWidth = 0; bubbleLE.flexibleHeight = 0;
        }
        else
        {
            // Fallback (no HLG on the row): pin bubble to left/right
            tmp.alignment = isUser ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;

            float yMin = bubbleRect.anchorMin.y;
            float yMax = bubbleRect.anchorMax.y;
            bubbleRect.anchorMin = new Vector2(isUser ? 1f : 0f, yMin);
            bubbleRect.anchorMax = new Vector2(isUser ? 1f : 0f, yMax);
            bubbleRect.pivot = new Vector2(isUser ? 1f : 0f, bubbleRect.pivot.y);
            bubbleRect.anchoredPosition = new Vector2(isUser ? -userSideMargin : botSideMargin, bubbleRect.anchoredPosition.y);
        }

        // Make the row span width; set height to fit the tallest child (icon vs bubble)
        RectTransform rowRect = msgObj.GetComponent<RectTransform>();
        if (rowRect != null)
        {
            rowRect.anchorMin = new Vector2(0f, rowRect.anchorMin.y);
            rowRect.anchorMax = new Vector2(1f, rowRect.anchorMax.y);
            rowRect.offsetMin = new Vector2(0f, rowRect.offsetMin.y);
            rowRect.offsetMax = new Vector2(0f, rowRect.offsetMax.y);
        }

        var rowLE = msgObj.GetComponent<LayoutElement>() ?? msgObj.AddComponent<LayoutElement>();
        // If there is an icon, row height should consider it; otherwise bubble height
        float iconH = 0f;
        var iconCheck = msgObj.transform.Find(iconChildName) as RectTransform;
        if (iconCheck != null) iconH = avatarSize;
        rowLE.preferredHeight = Mathf.Max(bubbleHeight, iconH) + 4f;
        rowLE.flexibleWidth = 1;
        rowLE.flexibleHeight = 0;

        // Rebuild and scroll
        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRect);
        Canvas.ForceUpdateCanvases();

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

    // Remove common Markdown artifacts the model returns (bold **, bullets with -/*, inline code `text`)
    private static readonly Regex RE_BOLD = new Regex(@"\*\*(.*?)\*\*", RegexOptions.Singleline);
    private static readonly Regex RE_BULLET1 = new Regex(@"(?m)^\s*-\s+");   // lines starting with "- "
    private static readonly Regex RE_BULLET2 = new Regex(@"(?m)^\s*\*\s+");  // lines starting with "* "
    private static readonly Regex RE_BACKTICKS = new Regex(@"`([^`]+)`", RegexOptions.Singleline);

    private string SanitizeBotMarkdown(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        s = RE_BOLD.Replace(s, "$1");       // remove **bold** markers
        s = RE_BACKTICKS.Replace(s, "$1");  // remove inline code ticks
        s = RE_BULLET1.Replace(s, "• ");    // - item -> • item
        s = RE_BULLET2.Replace(s, "• ");    // * item -> • item
        return s;
    }
}