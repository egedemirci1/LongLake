using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative co-op dialogue. One global session at a time.
/// Participants must all ready; differing choices are resolved randomly among selected picks.
/// </summary>
public partial class DialogueManager : NetworkBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [SerializeField] private DialogueSequence[] sequences = Array.Empty<DialogueSequence>();

    private readonly NetworkVariable<bool> isActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString64Bytes> sequenceIdNv = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString64Bytes> nodeIdNv = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> resolvedChoiceIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkList<ulong> readyClientIds;
    private NetworkList<ulong> participantClientIds;
    private NetworkList<DialoguePlayerChoice> playerChoices;

    private readonly HashSet<ulong> _participants = new();
    private readonly Dictionary<string, DialogueSequence> _sequenceById = new(StringComparer.Ordinal);
    private Dictionary<string, DialogueNode> _nodeById;

    private DialogueSequence _activeSequence;
    private string _cachedSequenceIdForNodes;
    private bool _disconnectHooked;

    public static bool IsDialogueOpen => Instance != null && Instance.isActive.Value;

    public bool IsActive => isActive.Value;
    public string CurrentSequenceId => sequenceIdNv.Value.ToString();
    public string CurrentNodeId => nodeIdNv.Value.ToString();
    public int ResolvedChoiceIndex => resolvedChoiceIndex.Value;

    public event Action OnDialogueStateChanged;

    /// <summary>Diyalog normal bittiğinde (abort değil). Argüman: sequenceId.</summary>
    public event Action<string> OnDialogueEnded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DialogueManager] Duplicate instance destroyed.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        readyClientIds = new NetworkList<ulong>();
        participantClientIds = new NetworkList<ulong>();
        playerChoices = new NetworkList<DialoguePlayerChoice>();

        RebuildSequenceCache();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        isActive.OnValueChanged += OnAnyChanged;
        sequenceIdNv.OnValueChanged += OnAnyChangedFs;
        nodeIdNv.OnValueChanged += OnAnyChangedFs;
        resolvedChoiceIndex.OnValueChanged += OnAnyChangedInt;
        readyClientIds.OnListChanged += OnListChanged;
        participantClientIds.OnListChanged += OnListChanged;
        playerChoices.OnListChanged += OnChoicesChanged;

        if (IsServer && !_disconnectHooked && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            _disconnectHooked = true;
        }

        OnDialogueStateChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        isActive.OnValueChanged -= OnAnyChanged;
        sequenceIdNv.OnValueChanged -= OnAnyChangedFs;
        nodeIdNv.OnValueChanged -= OnAnyChangedFs;
        resolvedChoiceIndex.OnValueChanged -= OnAnyChangedInt;
        readyClientIds.OnListChanged -= OnListChanged;
        participantClientIds.OnListChanged -= OnListChanged;
        playerChoices.OnListChanged -= OnChoicesChanged;

        if (_disconnectHooked && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            _disconnectHooked = false;
        }
    }

    private void OnAnyChanged(bool _, bool __)
    {
        if (!isActive.Value)
            ClearLocalDialogueCache();
        OnDialogueStateChanged?.Invoke();
    }

    private void OnAnyChangedFs(FixedString64Bytes _, FixedString64Bytes __)
    {
        // Sequence değişince eski node cache (aynı n1/n2 id'leri) client'ta yanlış metin gösterir.
        ClearLocalDialogueCache();
        OnDialogueStateChanged?.Invoke();
    }
    private void OnAnyChangedInt(int _, int __) => OnDialogueStateChanged?.Invoke();
    private void OnListChanged(NetworkListEvent<ulong> _) => OnDialogueStateChanged?.Invoke();
    private void OnChoicesChanged(NetworkListEvent<DialoguePlayerChoice> _) => OnDialogueStateChanged?.Invoke();

    public int ReadyCount => readyClientIds.Count;
    public int ParticipantCount => participantClientIds.Count;

}
