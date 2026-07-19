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
public partial class QuestManager : NetworkBehaviour
{
    public static QuestManager Instance { get; private set; }

    [Header("Quest Data")]
    [SerializeField] private QuestData[] availableQuests;
    [SerializeField] private int openingQuestIndex = 0;

    [Header("Opening Quest Objective")]
    [SerializeField] private string crashBagItemId = "backpack";
    [Tooltip("Co-op'ta gereken çanta sayısı. Solo'da otomatik 1 olur.")]
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

    // HUD görev kartı elemanları (runtime'da kurulur)
    private TextMeshProUGUI hudEyebrowText;
    private TextMeshProUGUI hudProgressText;
    private Image hudAccentBar;
    private CanvasGroup hudGroup;
    private RectTransform hudRect;
    private Vector2 hudBasePosition;
    private float hudIntroStart = -1f;
    private bool hudFlashActive;

    private static readonly Color AccentColor = new Color(0.95f, 0.77f, 0.32f, 1f);   // kehribar (hotbar seçim rengi)
    private static readonly Color CompleteColor = new Color(0.45f, 0.85f, 0.45f, 1f); // görev tamamlandı yeşili
    private const float HudIntroDuration = 0.25f;
    private const float HudIntroSlide = 24f;
    private const float CompletionFlashSeconds = 1.4f;

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

        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback += OnClientCountChanged;
            NetworkManager.OnClientDisconnectCallback += OnClientCountChanged;
        }

        EnsureUiExists();
        CloseQuestPanel();
        RefreshAllUi();
    }

    public override void OnNetworkDespawn()
    {
        currentQuestIndex.OnValueChanged -= OnCurrentQuestChanged;
        bagsCollected.OnValueChanged -= OnBagsCollectedChanged;
        completedQuestIndices.OnListChanged -= OnCompletedQuestsChanged;

        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientCountChanged;
            NetworkManager.OnClientDisconnectCallback -= OnClientCountChanged;
        }

        if (Instance == this)
            Instance = null;
    }

    private void OnClientCountChanged(ulong _)
    {
        RefreshAllUi();

        // Solo'ya düşünce 1 çanta yetiyorsa görevi tamamla
        if (IsServer &&
            currentQuestIndex.Value == openingQuestIndex &&
            !completedQuestIndices.Contains(openingQuestIndex) &&
            bagsCollected.Value >= GetBagsRequired())
        {
            CompleteOpeningQuestOnServer();
        }
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
        if (GameMenuUI.IsBlockingGameplay) return;

        if (Input.GetKeyDown(KeyCode.M))
            ToggleQuestPanel();

        if (isPanelOpen && Input.GetKeyDown(KeyCode.Escape))
            CloseQuestPanel();

        AnimateHudIntro();
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
    }

    /// <summary>
    /// Solo: 1 çanta. Co-op: bağlı oyuncu sayısı (en fazla bagsRequired).
    /// </summary>
    private int GetBagsRequired()
    {
        int players = 1;
        if (NetworkManager != null)
            players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);

        return Mathf.Clamp(players, 1, Mathf.Max(1, bagsRequired));
    }

    /// <summary>
    /// Server-only. Call from ItemPickUp after a successful world pickup.
    /// Collecting enough crash-site bags (1 solo / 2 co-op) completes the opening quest.
    /// </summary>
    public void NotifyQuestItemCollectedServer(string itemId)
    {
        if (!IsServer || !IsSpawned) return;
        if (string.IsNullOrEmpty(itemId)) return;
        if (itemId != crashBagItemId) return;
        if (currentQuestIndex.Value != openingQuestIndex) return;
        if (completedQuestIndices.Contains(openingQuestIndex)) return;

        int required = GetBagsRequired();
        bagsCollected.Value = Mathf.Min(bagsCollected.Value + 1, required);

        if (bagsCollected.Value >= required)
            CompleteOpeningQuestOnServer();
    }

    private void CompleteOpeningQuestOnServer()
    {
        CompleteCurrentQuestOnServer(chainToNext: true);
    }

    /// <summary>
    /// Aktif görevi tamamlar. chainToNext=true ise listedeki bir sonraki göreve geçer
    /// (çanta → Yardım Ara). Kapı çalma gibi yerlerde false: diyalog startQuestId açar.
    /// </summary>
    private void CompleteCurrentQuestOnServer(bool chainToNext)
    {
        if (!IsServer) return;
        if (currentQuestIndex.Value < 0) return;

        int completedIndex = currentQuestIndex.Value;
        if (!completedQuestIndices.Contains(completedIndex))
            completedQuestIndices.Add(completedIndex);

        if (chainToNext)
        {
            int nextIndex = completedIndex + 1;
            if (availableQuests != null &&
                nextIndex < availableQuests.Length &&
                availableQuests[nextIndex] != null &&
                !completedQuestIndices.Contains(nextIndex))
            {
                currentQuestIndex.Value = nextIndex;
                return;
            }
        }

        currentQuestIndex.Value = -1;
    }

    /// <summary>Server-only. Completes the active quest if it matches questId.</summary>
    public void CompleteCurrentQuestIfIdServer(string questId, bool chainToNext = false)
    {
        if (!IsServer || !IsSpawned) return;
        if (!IsCurrentQuestId(questId)) return;
        CompleteCurrentQuestOnServer(chainToNext);
    }

    /// <summary>Server-only. Starts quest by id (shared for all players).</summary>
    public void StartQuestByIdServer(string questId)
    {
        if (!IsServer || !IsSpawned) return;
        if (string.IsNullOrEmpty(questId) || availableQuests == null) return;

        int index = -1;
        for (int i = 0; i < availableQuests.Length; i++)
        {
            if (availableQuests[i] != null && availableQuests[i].questID == questId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            Debug.LogWarning($"[QuestManager] StartQuestById: unknown '{questId}'.");
            return;
        }

        if (completedQuestIndices.Contains(index))
            return;

        if (currentQuestIndex.Value == index)
            return;

        currentQuestIndex.Value = index;
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

    // ---- UI ----

    // ---- HUD animasyonları ----

}
