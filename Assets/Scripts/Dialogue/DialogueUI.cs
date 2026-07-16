using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dialogue panel — quest HUD / stamina ile aynı görsel dil:
/// koyu yuvarlak kart, kehribar vurgu, fade+slide, dinamik yükseklik.
/// </summary>
public class DialogueUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TextMeshProUGUI speakerText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button continueButton;
    [SerializeField] private TextMeshProUGUI continueButtonLabel;
    [SerializeField] private Button[] choiceButtons = new Button[3];
    [SerializeField] private TextMeshProUGUI[] choiceLabels = new TextMeshProUGUI[3];

    private static readonly Color AccentColor = new Color(0.95f, 0.77f, 0.32f, 1f);
    private static readonly Color CardColor = new Color(0.05f, 0.06f, 0.08f, 0.94f);
    private static readonly Color ScrimColor = new Color(0.02f, 0.03f, 0.04f, 0.55f);
    private static readonly Color MutedText = new Color(1f, 1f, 1f, 0.5f);
    private static readonly Color BodyTextColor = new Color(0.93f, 0.94f, 0.92f, 1f);
    private static readonly Color ButtonIdle = new Color(0.10f, 0.12f, 0.14f, 0.98f);
    private static readonly Color ButtonHover = new Color(0.16f, 0.18f, 0.20f, 1f);
    private static readonly Color ButtonSelected = new Color(0.95f, 0.77f, 0.32f, 0.28f);
    private static readonly Color ContinueAccent = new Color(0.95f, 0.77f, 0.32f, 0.95f);
    private static readonly Color ContinueReady = new Color(0.18f, 0.20f, 0.18f, 0.9f);

    private const float IntroDuration = 0.22f;
    private const float IntroSlide = 28f;
    private const float CardWidth = 820f;

    private DialogueManager _manager;
    private CanvasGroup _panelGroup;
    private CanvasGroup _cardGroup;
    private RectTransform _cardRect;
    private Vector2 _cardBasePos;
    private Image _continueImage;
    private Image[] _choiceImages = new Image[3];
    private float _introStart = -1f;
    private bool _wasActive;
    private string _lastNodeId;

    private void Awake()
    {
        EnsurePanel();
        WireButtons();
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
        if (_panelGroup != null)
            _panelGroup.alpha = 0f;
    }

    private void OnEnable() => TryBindManager();

    private void OnDisable() => UnbindManager();

    private void Update()
    {
        if (_manager == null)
            TryBindManager();
        AnimateIntro();
    }

    private void TryBindManager()
    {
        if (_manager != null) return;
        _manager = DialogueManager.Instance != null
            ? DialogueManager.Instance
            : FindFirstObjectByType<DialogueManager>();
        if (_manager == null) return;

        _manager.OnDialogueStateChanged += Refresh;
        Refresh();
    }

    private void UnbindManager()
    {
        if (_manager != null)
            _manager.OnDialogueStateChanged -= Refresh;
        _manager = null;
    }

    private void WireButtons()
    {
        if (continueButton != null)
        {
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(OnContinueClicked);
        }

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            if (choiceButtons[i] == null) continue;
            int index = i;
            choiceButtons[i].onClick.RemoveAllListeners();
            choiceButtons[i].onClick.AddListener(() => OnChoiceClicked(index));
        }
    }

    private void OnContinueClicked()
    {
        if (_manager == null || !_manager.IsActive) return;
        if (!_manager.IsLocalParticipant()) return;
        _manager.SetContinueReadyServerRpc();
    }

    private void OnChoiceClicked(int index)
    {
        if (_manager == null || !_manager.IsActive) return;
        if (!_manager.IsLocalParticipant()) return;
        _manager.SelectChoiceServerRpc(index);
    }

    private void Refresh()
    {
        if (_manager == null || dialoguePanel == null) return;

        bool active = _manager.IsActive;

        if (active && !_wasActive)
        {
            dialoguePanel.SetActive(true);
            PlayIntro();
        }
        else if (!active && _wasActive)
        {
            dialoguePanel.SetActive(false);
            _introStart = -1f;
            if (_panelGroup != null) _panelGroup.alpha = 0f;
        }
        _wasActive = active;

        if (!active) return;

        var node = _manager.GetCurrentNode();
        if (node == null)
        {
            if (bodyText != null) bodyText.text = "";
            if (speakerText != null) speakerText.text = "";
            if (statusText != null) statusText.text = "";
            return;
        }

        // Yeni satıra geçince hafif yeniden intro (kart zaten açıksa daha kısa his).
        if (_lastNodeId != node.id)
        {
            _lastNodeId = node.id;
            if (_cardGroup != null && _introStart < 0f)
                PlayNodePulse();
        }

        if (speakerText != null)
            speakerText.text = string.IsNullOrEmpty(node.speaker) ? "" : node.speaker.ToUpperInvariant();
        if (bodyText != null)
            bodyText.text = node.text ?? "";

        bool participant = _manager.IsLocalParticipant();
        bool ready = _manager.IsLocalReady();
        int readyCount = _manager.ReadyCount;
        int needed = Mathf.Max(1, _manager.ParticipantCount);
        int resolved = _manager.ResolvedChoiceIndex;
        bool hasChoices = node.choices != null && node.choices.Length > 0;

        if (statusText != null)
        {
            if (!participant)
                statusText.text = "DİNLİYORSUN";
            else if (resolved >= 0)
                statusText.text = $"SEÇİM {resolved + 1}  ·  {readyCount}/{needed}";
            else if (ready)
                statusText.text = $"HAZIR  ·  {readyCount}/{needed}";
            else
                statusText.text = $"{readyCount}/{needed} HAZIR";
        }

        if (continueButton != null)
        {
            bool showContinue = !hasChoices && participant;
            continueButton.gameObject.SetActive(showContinue);
            continueButton.interactable = showContinue && !ready;
            if (continueButtonLabel != null)
            {
                continueButtonLabel.text = ready ? "Hazır" : "Devam";
                continueButtonLabel.color = ready ? MutedText : (showContinue && !ready ? new Color(0.08f, 0.08f, 0.06f, 1f) : Color.white);
            }
            if (_continueImage != null)
                _continueImage.color = ready ? ContinueReady : ContinueAccent;
        }

        int localChoice = _manager.GetLocalChoiceIndex();
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            Button btn = choiceButtons[i];
            if (btn == null) continue;

            bool show = hasChoices && participant && i < node.choices.Length;
            btn.gameObject.SetActive(show);
            if (!show) continue;

            bool selected = localChoice == i;
            if (choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null)
            {
                choiceLabels[i].text = node.choices[i].label;
                choiceLabels[i].color = selected ? AccentColor : Color.white;
                choiceLabels[i].fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            }

            btn.interactable = true;
            if (_choiceImages != null && i < _choiceImages.Length && _choiceImages[i] != null)
                _choiceImages[i].color = selected ? ButtonSelected : ButtonIdle;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Layout yeniden hesaplansın (dinamik yükseklik).
        if (_cardRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_cardRect);
    }

    private void PlayIntro()
    {
        _introStart = Time.unscaledTime;
        if (_panelGroup != null) _panelGroup.alpha = 0f;
        if (_cardGroup != null) _cardGroup.alpha = 0f;
        if (_cardRect != null)
            _cardRect.anchoredPosition = _cardBasePos + Vector2.down * IntroSlide;
    }

    private void PlayNodePulse()
    {
        // Satır değişiminde kartı hafifçe yeniden belirginleştir.
        if (_cardGroup == null) return;
        _cardGroup.alpha = 0.55f;
        _introStart = Time.unscaledTime - IntroDuration * 0.45f;
    }

    private void AnimateIntro()
    {
        if (_introStart < 0f || _panelGroup == null) return;

        float t = Mathf.Clamp01((Time.unscaledTime - _introStart) / IntroDuration);
        float eased = 1f - (1f - t) * (1f - t);

        _panelGroup.alpha = eased;
        if (_cardGroup != null)
            _cardGroup.alpha = eased;
        if (_cardRect != null)
            _cardRect.anchoredPosition = _cardBasePos + Vector2.down * (IntroSlide * (1f - eased));

        if (t >= 1f)
            _introStart = -1f;
    }

    private void EnsurePanel()
    {
        if (dialoguePanel != null && speakerText != null && bodyText != null && statusText != null && continueButton != null)
            return;

        if (dialoguePanel != null)
            Destroy(dialoguePanel);

        Transform canvas = transform;
        if (canvas.GetComponent<Canvas>() == null)
        {
            var ui = GameObject.FindWithTag("InteractionUI");
            if (ui != null) canvas = ui.transform;
        }

        dialoguePanel = new GameObject("DialoguePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dialoguePanel.transform.SetParent(canvas, false);
        var panelRt = dialoguePanel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        dialoguePanel.GetComponent<Image>().color = ScrimColor;
        dialoguePanel.GetComponent<Image>().raycastTarget = true;

        _panelGroup = dialoguePanel.AddComponent<CanvasGroup>();
        _panelGroup.alpha = 0f;

        // Kart
        var card = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(dialoguePanel.transform, false);
        _cardRect = card.GetComponent<RectTransform>();
        _cardRect.anchorMin = new Vector2(0.5f, 0f);
        _cardRect.anchorMax = new Vector2(0.5f, 0f);
        _cardRect.pivot = new Vector2(0.5f, 0f);
        _cardBasePos = new Vector2(0f, 40f);
        _cardRect.anchoredPosition = _cardBasePos;
        _cardRect.sizeDelta = new Vector2(CardWidth, 100f);

        var cardImg = card.GetComponent<Image>();
        cardImg.sprite = RuntimeUiSprites.GetRoundedSprite(12);
        cardImg.type = Image.Type.Sliced;
        cardImg.color = CardColor;
        cardImg.raycastTarget = true;

        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 24, 18, 18);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = card.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _cardGroup = card.AddComponent<CanvasGroup>();
        _cardGroup.blocksRaycasts = true;

        // Accent (layout dışı)
        var accent = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accent.transform.SetParent(card.transform, false);
        var accentRt = accent.GetComponent<RectTransform>();
        accentRt.anchorMin = new Vector2(0f, 0f);
        accentRt.anchorMax = new Vector2(0f, 1f);
        accentRt.pivot = new Vector2(0f, 0.5f);
        accentRt.anchoredPosition = new Vector2(8f, 0f);
        accentRt.sizeDelta = new Vector2(4f, -24f);
        var accentImg = accent.GetComponent<Image>();
        accentImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        accentImg.type = Image.Type.Sliced;
        accentImg.color = AccentColor;
        accentImg.raycastTarget = false;
        accent.AddComponent<LayoutElement>().ignoreLayout = true;

        // Header: konuşmacı + status
        var header = CreateRow(card.transform, "Header", 28f);
        speakerText = CreateLayoutTmp(header.transform, "Speaker", 15f, FontStyles.Bold, AccentColor);
        speakerText.characterSpacing = 8f;
        speakerText.alignment = TextAlignmentOptions.MidlineLeft;
        var speakerLe = speakerText.gameObject.AddComponent<LayoutElement>();
        speakerLe.flexibleWidth = 1f;
        speakerLe.minHeight = 22f;

        statusText = CreateLayoutTmp(header.transform, "Status", 13f, FontStyles.Bold, MutedText);
        statusText.characterSpacing = 4f;
        statusText.alignment = TextAlignmentOptions.MidlineRight;
        var statusLe = statusText.gameObject.AddComponent<LayoutElement>();
        statusLe.preferredWidth = 200f;
        statusLe.minHeight = 22f;

        // Body
        bodyText = CreateLayoutTmp(card.transform, "Body", 23f, FontStyles.Normal, BodyTextColor);
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.textWrappingMode = TextWrappingModes.Normal;
        bodyText.overflowMode = TextOverflowModes.Overflow;
        bodyText.lineSpacing = -6f;
        var bodyLe = bodyText.gameObject.AddComponent<LayoutElement>();
        bodyLe.minHeight = 40f;
        bodyLe.flexibleWidth = 1f;
        var bodyFit = bodyText.gameObject.AddComponent<ContentSizeFitter>();
        bodyFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        bodyFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Choices
        var choicesRoot = CreateColumn(card.transform, "Choices", 8f);
        choiceButtons = new Button[3];
        choiceLabels = new TextMeshProUGUI[3];
        _choiceImages = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            choiceButtons[i] = CreateLayoutButton(choicesRoot.transform, $"Choice{i}", out choiceLabels[i], out _choiceImages[i], false);
            choiceButtons[i].gameObject.SetActive(false);
        }

        // Continue
        continueButton = CreateLayoutButton(card.transform, "ContinueButton", out continueButtonLabel, out _continueImage, true);
        continueButtonLabel.text = "Devam";
        var contLe = continueButton.gameObject.AddComponent<LayoutElement>();
        contLe.preferredHeight = 46f;
        contLe.minHeight = 46f;
    }

    private static GameObject CreateRow(Transform parent, string name, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.spacing = 12f;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        return go;
    }

    private static GameObject CreateColumn(Transform parent, string name, float spacing)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go;
    }

    private static TextMeshProUGUI CreateLayoutTmp(Transform parent, string name, float fontSize, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateLayoutButton(
        Transform parent, string name, out TextMeshProUGUI label, out Image bg, bool accent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        bg = go.GetComponent<Image>();
        bg.sprite = RuntimeUiSprites.GetRoundedSprite(8);
        bg.type = Image.Type.Sliced;
        bg.color = accent ? ContinueAccent : ButtonIdle;

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.7f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.targetGraphic = bg;

        var le = go.GetComponent<LayoutElement>();
        if (le == null) le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 46f;
        le.minHeight = 46f;
        le.flexibleWidth = 1f;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(16f, 6f);
        lrt.offsetMax = new Vector2(-16f, -6f);
        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 19;
        label.alignment = TextAlignmentOptions.Center;
        label.color = accent ? new Color(0.08f, 0.08f, 0.06f, 1f) : Color.white;
        label.raycastTarget = false;
        return btn;
    }
}
