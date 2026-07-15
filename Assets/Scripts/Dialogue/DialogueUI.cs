using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds DialoguePanel to DialogueManager. Creates a minimal panel under InteractionUI if refs missing.
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

    private DialogueManager _manager;

    private void Awake()
    {
        EnsurePanel();
        WireButtons();
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
    }

    private void OnEnable()
    {
        TryBindManager();
    }

    private void OnDisable()
    {
        UnbindManager();
    }

    private void Update()
    {
        if (_manager == null)
            TryBindManager();
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
        dialoguePanel.SetActive(active);
        if (!active) return;

        var node = _manager.GetCurrentNode();
        if (node == null)
        {
            if (bodyText != null) bodyText.text = "";
            if (speakerText != null) speakerText.text = "";
            if (statusText != null) statusText.text = "";
            return;
        }

        if (speakerText != null)
            speakerText.text = string.IsNullOrEmpty(node.speaker) ? "" : node.speaker;
        if (bodyText != null)
            bodyText.text = node.text ?? "";

        bool participant = _manager.IsLocalParticipant();
        bool ready = _manager.IsLocalReady();
        int readyCount = _manager.ReadyCount;
        int needed = _manager.ParticipantCount;
        int resolved = _manager.ResolvedChoiceIndex;

        bool hasChoices = node.choices != null && node.choices.Length > 0;

        if (statusText != null)
        {
            if (!participant)
                statusText.text = "Dinliyorsun (konuşmanın parçası değilsin)";
            else if (resolved >= 0)
                statusText.text = $"Seçim: {resolved + 1}  ·  Bekleniyor {readyCount}/{needed}";
            else if (ready)
                statusText.text = $"Hazır · Bekleniyor {readyCount}/{needed}";
            else
                statusText.text = $"Bekleniyor {readyCount}/{needed}";
        }

        if (continueButton != null)
        {
            bool showContinue = !hasChoices && participant;
            continueButton.gameObject.SetActive(showContinue);
            continueButton.interactable = showContinue && !ready;
            if (continueButtonLabel != null)
                continueButtonLabel.text = ready ? "Hazır" : "Devam";
        }

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            Button btn = choiceButtons[i];
            if (btn == null) continue;

            bool show = hasChoices && participant && i < node.choices.Length;
            btn.gameObject.SetActive(show);
            if (!show) continue;

            if (choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null)
                choiceLabels[i].text = node.choices[i].label;

            int localChoice = _manager.GetLocalChoiceIndex();
            // Stay interactable while dialogue open so player can change mind (upsert).
            btn.interactable = true;
            if (localChoice == i && choiceLabels != null && i < choiceLabels.Length && choiceLabels[i] != null)
                choiceLabels[i].text = $"> {node.choices[i].label}";
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void EnsurePanel()
    {
        if (dialoguePanel != null && speakerText != null && bodyText != null && statusText != null && continueButton != null)
            return;

        Transform canvas = transform;
        if (canvas.GetComponent<Canvas>() == null)
        {
            var ui = GameObject.FindWithTag("InteractionUI");
            if (ui != null) canvas = ui.transform;
        }

        if (dialoguePanel == null)
        {
            dialoguePanel = new GameObject("DialoguePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dialoguePanel.transform.SetParent(canvas, false);
            var rt = dialoguePanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var bg = dialoguePanel.GetComponent<Image>();
            bg.color = new Color(0.02f, 0.04f, 0.05f, 0.55f);
            bg.raycastTarget = true;
        }

        Transform root = dialoguePanel.transform;
        var box = CreateChild(root, "Box", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(900f, 280f));
        var boxImg = box.gameObject.AddComponent<Image>();
        boxImg.color = new Color(0.06f, 0.09f, 0.1f, 0.92f);

        speakerText = CreateTmp(box, "Speaker", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -16f), new Vector2(-24f, -48f), 28, FontStyles.Bold);
        bodyText = CreateTmp(box, "Body", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 72f), new Vector2(-24f, -64f), 26, FontStyles.Normal);
        statusText = CreateTmp(box, "Status", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 40f), new Vector2(-24f, 68f), 18, FontStyles.Normal);

        continueButton = CreateButton(box, "ContinueButton", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 16f), new Vector2(180f, 44f), out continueButtonLabel, "Devam");

        choiceButtons = new Button[3];
        choiceLabels = new TextMeshProUGUI[3];
        for (int i = 0; i < 3; i++)
        {
            float y = 16f + i * 50f;
            choiceButtons[i] = CreateButton(box, $"Choice{i}", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, y), new Vector2(420f, 44f), out choiceLabels[i], $"Seçenek {i + 1}");
            choiceButtons[i].gameObject.SetActive(false);
        }
    }

    private static RectTransform CreateChild(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    private static TextMeshProUGUI CreateTmp(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offsetMin, Vector2 offsetMax, float size, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = new Color(0.93f, 0.94f, 0.92f, 1f);
        tmp.raycastTarget = false;
        tmp.alignment = name == "Status" ? TextAlignmentOptions.Left : TextAlignmentOptions.TopLeft;
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 anchoredPos, Vector2 size, out TextMeshProUGUI label, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = aMin;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.color = new Color(0.12f, 0.18f, 0.18f, 0.95f);
        var btn = go.GetComponent<Button>();

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 22;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        return btn;
    }
}
