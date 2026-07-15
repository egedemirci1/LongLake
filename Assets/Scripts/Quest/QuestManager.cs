using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Shared quest state for all players (server-authoritative NetworkVariables).
/// Place once in the gameplay scene as an in-scene NetworkObject — not on the player prefab.
/// </summary>
public class QuestManager : NetworkBehaviour
{
    public static QuestManager Instance { get; private set; }

    [Header("Quest Data")]
    [SerializeField] private QuestData[] availableQuests;
    [SerializeField] private int openingQuestIndex = 0;

    [Header("Opening Quest Objective")]
    [SerializeField] private string crashBagItemId = "backpack";
    [SerializeField] private int bagsRequired = 2;

    [Header("UI References (optional — auto-created if missing)")]
    [SerializeField] private GameObject questPanelObject;
    [SerializeField] private TextMeshProUGUI currentQuestTitleText;
    [SerializeField] private TextMeshProUGUI currentQuestDescriptionText;
    [SerializeField] private TextMeshProUGUI hudTitleText;
    [SerializeField] private TextMeshProUGUI hudDescriptionText;

    private readonly NetworkVariable<int> currentQuestIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<int> bagsCollected = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkList<int> completedQuestIndices;

    private bool isPanelOpen;
    private GameObject runtimeHudRoot;
    private GameObject runtimePanelRoot;

    /// <summary>Fires on every client when active or completed quests change.</summary>
    public event Action OnQuestStateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[QuestManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        completedQuestIndices = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        currentQuestIndex.OnValueChanged += OnCurrentQuestChanged;
        bagsCollected.OnValueChanged += OnBagsCollectedChanged;
        completedQuestIndices.OnListChanged += OnCompletedQuestsChanged;

        EnsureUiExists();
        CloseQuestPanel();
        RefreshAllUi();

        Debug.Log($"[QuestManager] Spawned. IsServer={IsServer} currentQuest={currentQuestIndex.Value}");
    }

    public override void OnNetworkDespawn()
    {
        currentQuestIndex.OnValueChanged -= OnCurrentQuestChanged;
        bagsCollected.OnValueChanged -= OnBagsCollectedChanged;
        completedQuestIndices.OnListChanged -= OnCompletedQuestsChanged;

        if (Instance == this)
            Instance = null;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (Input.GetKeyDown(KeyCode.M))
            ToggleQuestPanel();

        if (isPanelOpen && Input.GetKeyDown(KeyCode.Escape))
            CloseQuestPanel();
    }

    // ---- Opening quest (shared, once) ----

    /// <summary>Called after character select. Server starts quest 0 once for everyone.</summary>
    public void RequestStartOpeningQuest()
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("[QuestManager] Not spawned yet — cannot start opening quest.");
            return;
        }

        StartOpeningQuestServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartOpeningQuestServerRpc()
    {
        if (availableQuests == null || availableQuests.Length == 0)
        {
            Debug.LogWarning("[QuestManager] No quests assigned.");
            return;
        }

        if (openingQuestIndex < 0 || openingQuestIndex >= availableQuests.Length)
        {
            Debug.LogWarning($"[QuestManager] Invalid openingQuestIndex: {openingQuestIndex}");
            return;
        }

        // Already active or already completed — shared progress, do not restart
        if (currentQuestIndex.Value == openingQuestIndex)
            return;

        if (completedQuestIndices.Contains(openingQuestIndex))
            return;

        if (currentQuestIndex.Value >= 0)
            return;

        bagsCollected.Value = 0;
        currentQuestIndex.Value = openingQuestIndex;
        Debug.Log($"[QuestManager] Opening quest started for all players: {availableQuests[openingQuestIndex].questTitle}");
    }

    /// <summary>
    /// Server-only. Call from ItemPickUp after a successful world pickup.
    /// Collecting both crash-site bags completes the opening quest for everyone.
    /// </summary>
    public void NotifyQuestItemCollectedServer(string itemId)
    {
        if (!IsServer || !IsSpawned) return;
        if (string.IsNullOrEmpty(itemId)) return;
        if (itemId != crashBagItemId) return;
        if (currentQuestIndex.Value != openingQuestIndex) return;
        if (completedQuestIndices.Contains(openingQuestIndex)) return;

        bagsCollected.Value = Mathf.Min(bagsCollected.Value + 1, bagsRequired);
        Debug.Log($"[QuestManager] Crash bag collected: {bagsCollected.Value}/{bagsRequired}");

        if (bagsCollected.Value >= bagsRequired)
            CompleteOpeningQuestOnServer();
    }

    private void CompleteOpeningQuestOnServer()
    {
        if (!IsServer) return;
        if (currentQuestIndex.Value < 0) return;

        int completedIndex = currentQuestIndex.Value;
        if (!completedQuestIndices.Contains(completedIndex))
            completedQuestIndices.Add(completedIndex);

        // Chain to next shared quest (e.g. Yardım Ara) if one exists
        int nextIndex = completedIndex + 1;
        if (availableQuests != null &&
            nextIndex < availableQuests.Length &&
            availableQuests[nextIndex] != null &&
            !completedQuestIndices.Contains(nextIndex))
        {
            currentQuestIndex.Value = nextIndex;
            Debug.Log($"[QuestManager] Quest {completedIndex} completed. Next quest started: {availableQuests[nextIndex].questTitle}");
        }
        else
        {
            currentQuestIndex.Value = -1;
            Debug.Log($"[QuestManager] Quest {completedIndex} completed for all players. No next quest.");
        }
    }

    // ---- Shared quest API ----

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetCurrentQuestServerRpc(int questIndex)
    {
        if (questIndex < -1 || questIndex >= availableQuests.Length)
        {
            Debug.LogWarning($"[QuestManager] Invalid quest index: {questIndex}");
            return;
        }

        if (questIndex < 0)
        {
            currentQuestIndex.Value = -1;
            return;
        }

        if (currentQuestIndex.Value >= 0 &&
            currentQuestIndex.Value < availableQuests.Length &&
            !completedQuestIndices.Contains(currentQuestIndex.Value))
        {
            completedQuestIndices.Add(currentQuestIndex.Value);
        }

        currentQuestIndex.Value = questIndex;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CompleteCurrentQuestServerRpc()
    {
        CompleteOpeningQuestOnServer();
    }

    public void SetCurrentQuest(int questIndex) => SetCurrentQuestServerRpc(questIndex);
    public void CompleteCurrentQuest() => CompleteCurrentQuestServerRpc();

    // ---- Getters ----

    public QuestData GetCurrentQuest()
    {
        if (currentQuestIndex.Value < 0 || currentQuestIndex.Value >= availableQuests.Length)
            return null;
        return availableQuests[currentQuestIndex.Value];
    }

    public int CurrentQuestIndex => currentQuestIndex.Value;

    public List<int> GetCompletedQuestIndices()
    {
        var result = new List<int>();
        if (completedQuestIndices == null) return result;
        foreach (int index in completedQuestIndices)
            result.Add(index);
        return result;
    }

    public QuestData[] GetAvailableQuests() => availableQuests;

    public bool IsPanelOpen => isPanelOpen;

    public bool HasActiveQuest => currentQuestIndex.Value >= 0;

    public bool IsCurrentQuestId(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return false;
        QuestData quest = GetCurrentQuest();
        return quest != null && quest.questID == questId;
    }

    /// <summary>Server-only. Completes the active quest if it matches questId.</summary>
    public void CompleteCurrentQuestIfIdServer(string questId)
    {
        if (!IsServer || !IsSpawned) return;
        if (!IsCurrentQuestId(questId)) return;
        CompleteOpeningQuestOnServer();
    }

    // ---- UI ----

    private void ToggleQuestPanel()
    {
        EnsureUiExists();
        if (questPanelObject == null) return;

        if (isPanelOpen) CloseQuestPanel();
        else OpenQuestPanel();
    }

    private void OpenQuestPanel()
    {
        EnsureUiExists();
        if (questPanelObject == null) return;

        isPanelOpen = true;
        questPanelObject.SetActive(true);
        RefreshAllUi();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseQuestPanel()
    {
        isPanelOpen = false;
        if (questPanelObject != null)
            questPanelObject.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnCurrentQuestChanged(int oldValue, int newValue)
    {
        RefreshAllUi();
        OnQuestStateChanged?.Invoke();
    }

    private void OnBagsCollectedChanged(int oldValue, int newValue)
    {
        RefreshAllUi();
        OnQuestStateChanged?.Invoke();
    }

    private void OnCompletedQuestsChanged(NetworkListEvent<int> changeEvent)
    {
        RefreshAllUi();
        OnQuestStateChanged?.Invoke();
    }

    private void RefreshAllUi()
    {
        EnsureUiExists();
        UpdateCurrentQuestDisplay();
        UpdateHud();
    }

    private string BuildQuestDescription(QuestData quest)
    {
        if (quest == null) return string.Empty;

        if (currentQuestIndex.Value == openingQuestIndex)
            return $"{quest.questDescription}\nSırt çantası: {bagsCollected.Value}/{bagsRequired}";

        return quest.questDescription;
    }

    private void UpdateCurrentQuestDisplay()
    {
        QuestData quest = GetCurrentQuest();

        if (quest == null)
        {
            if (currentQuestTitleText != null)
                currentQuestTitleText.text = "Güncel Görev Yok";
            if (currentQuestDescriptionText != null)
                currentQuestDescriptionText.text = "Aktif bir görev bulunmuyor.";
            return;
        }

        if (currentQuestTitleText != null)
            currentQuestTitleText.text = quest.questTitle;
        if (currentQuestDescriptionText != null)
            currentQuestDescriptionText.text = BuildQuestDescription(quest);
    }

    private void UpdateHud()
    {
        QuestData quest = GetCurrentQuest();
        bool show = quest != null;

        if (runtimeHudRoot != null)
            runtimeHudRoot.SetActive(show);

        if (!show) return;

        if (hudTitleText != null)
            hudTitleText.text = quest.questTitle;
        if (hudDescriptionText != null)
            hudDescriptionText.text = BuildQuestDescription(quest);
    }

    private void EnsureUiExists()
    {
        if (hudTitleText != null && questPanelObject != null && currentQuestTitleText != null)
            return;

        Canvas canvas = FindInteractionCanvas();
        if (canvas == null) return;

        if (hudTitleText == null)
            CreateRuntimeHud(canvas);

        if (questPanelObject == null)
            CreateRuntimePanel(canvas);
    }

    private static Canvas FindInteractionCanvas()
    {
        var tagged = GameObject.FindGameObjectWithTag("InteractionUI");
        if (tagged != null)
        {
            var c = tagged.GetComponent<Canvas>();
            if (c != null) return c;
        }

        return FindFirstObjectByType<Canvas>();
    }

    private void CreateRuntimeHud(Canvas canvas)
    {
        if (runtimeHudRoot != null) return;

        EnsureCanvasScalesWithScreen(canvas);

        runtimeHudRoot = new GameObject("ActiveQuestHud", typeof(RectTransform));
        runtimeHudRoot.transform.SetParent(canvas.transform, false);

        var root = runtimeHudRoot.GetComponent<RectTransform>();
        // Left strip (~32% width): stays on-screen when aspect ratio changes
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(0.32f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.offsetMin = new Vector2(16f, -120f);
        root.offsetMax = new Vector2(-16f, -16f);

        var bg = runtimeHudRoot.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;

        hudTitleText = CreateTmp(runtimeHudRoot.transform, "HudTitle", new Vector2(12f, -8f), 22, FontStyles.Bold, 36f);
        hudDescriptionText = CreateTmp(runtimeHudRoot.transform, "HudDesc", new Vector2(12f, -44f), 16, FontStyles.Normal, 70f);

        runtimeHudRoot.SetActive(false);
    }

    private static void EnsureCanvasScalesWithScreen(Canvas canvas)
    {
        if (canvas == null) return;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void CreateRuntimePanel(Canvas canvas)
    {
        if (runtimePanelRoot != null) return;

        EnsureCanvasScalesWithScreen(canvas);

        runtimePanelRoot = new GameObject("QuestPanel", typeof(RectTransform));
        runtimePanelRoot.transform.SetParent(canvas.transform, false);

        var root = runtimePanelRoot.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(520f, 220f);

        var bg = runtimePanelRoot.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

        currentQuestTitleText = CreateTmp(runtimePanelRoot.transform, "PanelTitle", new Vector2(24f, -24f), 28, FontStyles.Bold);
        currentQuestDescriptionText = CreateTmp(runtimePanelRoot.transform, "PanelDesc", new Vector2(24f, -70f), 18, FontStyles.Normal);
        var hint = CreateTmp(runtimePanelRoot.transform, "Hint", new Vector2(24f, -170f), 14, FontStyles.Italic);
        hint.text = "Kapatmak için M veya ESC";

        questPanelObject = runtimePanelRoot;
        questPanelObject.SetActive(false);
    }

    private static TextMeshProUGUI CreateTmp(Transform parent, string name, Vector2 anchoredPos, float fontSize, FontStyles style, float height = 40f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(-24f, height);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }
}
