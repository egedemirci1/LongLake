using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative co-op dialogue. One global session at a time.
/// Participants must all ready; differing choices are resolved randomly among selected picks.
/// </summary>
public class DialogueManager : NetworkBehaviour
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

    private void RebuildSequenceCache()
    {
        _sequenceById.Clear();

        void Add(DialogueSequence seq)
        {
            if (seq == null || string.IsNullOrEmpty(seq.sequenceId)) return;
            _sequenceById[seq.sequenceId] = seq;
        }

        foreach (var seq in sequences)
            Add(seq);

        // Inspector referansı kaçsa bile Resources/Dialogue altındakiler yüklensin.
        var fromResources = Resources.LoadAll<DialogueSequence>("Dialogue");
        foreach (var seq in fromResources)
            Add(seq);
    }

    /// <summary>Runtime'da ek sequence kaydı (örn. NPC referansı).</summary>
    public void RegisterSequence(DialogueSequence seq)
    {
        if (seq == null || string.IsNullOrEmpty(seq.sequenceId)) return;
        _sequenceById[seq.sequenceId] = seq;
    }

    public DialogueNode GetCurrentNode()
    {
        if (!isActive.Value) return null;
        EnsureNodeCacheForActiveSequence();
        string id = nodeIdNv.Value.ToString();
        if (_nodeById != null && _nodeById.TryGetValue(id, out var node))
            return node;
        return null;
    }

    public DialogueSequence GetActiveSequence()
    {
        if (!isActive.Value) return null;
        string id = sequenceIdNv.Value.ToString();
        return _sequenceById.TryGetValue(id, out var seq) ? seq : null;
    }

    public bool IsLocalParticipant()
    {
        if (NetworkManager.Singleton == null) return false;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < participantClientIds.Count; i++)
        {
            if (participantClientIds[i] == localId)
                return true;
        }
        return false;
    }

    public bool IsLocalReady()
    {
        if (NetworkManager.Singleton == null) return false;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < readyClientIds.Count; i++)
        {
            if (readyClientIds[i] == localId)
                return true;
        }
        return false;
    }

    public int GetLocalChoiceIndex()
    {
        if (NetworkManager.Singleton == null) return -1;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < playerChoices.Count; i++)
        {
            if (playerChoices[i].ClientId == localId)
                return playerChoices[i].ChoiceIndex;
        }
        return -1;
    }

    public int ReadyCount => readyClientIds.Count;
    public int ParticipantCount => participantClientIds.Count;

    public void TryStartDialogue(string sequenceId)
    {
        if (string.IsNullOrEmpty(sequenceId)) return;
        TryStartDialogueServerRpc(new FixedString64Bytes(sequenceId));
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryStartDialogueServerRpc(FixedString64Bytes sequenceIdFs)
    {
        if (!IsServer || !IsSpawned) return;
        if (isActive.Value) return;

        string sequenceId = sequenceIdFs.ToString();
        if (!_sequenceById.TryGetValue(sequenceId, out var seq) || seq == null)
        {
            Debug.LogWarning($"[DialogueManager] Unknown sequenceId '{sequenceId}'.");
            return;
        }

        if (string.IsNullOrEmpty(seq.startNodeId))
        {
            Debug.LogWarning($"[DialogueManager] Sequence '{sequenceId}' has no startNodeId.");
            return;
        }

        _activeSequence = seq;
        _cachedSequenceIdForNodes = sequenceId;
        BuildNodeCache(seq);

        if (!_nodeById.ContainsKey(seq.startNodeId))
        {
            Debug.LogWarning($"[DialogueManager] startNodeId '{seq.startNodeId}' missing in '{sequenceId}'.");
            ClearLocalDialogueCache();
            return;
        }

        _participants.Clear();
        participantClientIds.Clear();
        readyClientIds.Clear();
        playerChoices.Clear();

        if (NetworkManager != null)
        {
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                _participants.Add(clientId);
                participantClientIds.Add(clientId);
            }
        }

        if (_participants.Count == 0)
        {
            // Host edge case
            ulong local = NetworkManager != null ? NetworkManager.LocalClientId : 0;
            _participants.Add(local);
            participantClientIds.Add(local);
        }

        resolvedChoiceIndex.Value = -1;
        sequenceIdNv.Value = sequenceIdFs;
        nodeIdNv.Value = new FixedString64Bytes(seq.startNodeId);
        isActive.Value = true;

        Debug.Log($"[DialogueManager] Started '{sequenceId}' at '{seq.startNodeId}' with {_participants.Count} participant(s).");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetContinueReadyServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer || !isActive.Value) return;
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!_participants.Contains(clientId)) return;

        DialogueNode node = GetCurrentNodeServer();
        if (node == null) return;
        if (node.choices != null && node.choices.Length > 0) return; // choice node — use SelectChoice

        AddReady(clientId);
        TryResolve();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SelectChoiceServerRpc(int choiceIndex, RpcParams rpcParams = default)
    {
        if (!IsServer || !isActive.Value) return;
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!_participants.Contains(clientId)) return;

        DialogueNode node = GetCurrentNodeServer();
        if (node == null || node.choices == null || node.choices.Length == 0) return;
        if (choiceIndex < 0 || choiceIndex >= node.choices.Length) return;

        UpsertChoice(clientId, choiceIndex);
        AddReady(clientId);
        TryResolve();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer || !isActive.Value) return;
        if (!_participants.Contains(clientId)) return;

        _participants.Remove(clientId);
        RemoveFromUlongList(participantClientIds, clientId);
        RemoveFromUlongList(readyClientIds, clientId);
        RemoveChoice(clientId);

        if (_participants.Count == 0)
        {
            ForceCloseAbort();
            return;
        }

        TryResolve();
    }

    private void AddReady(ulong clientId)
    {
        for (int i = 0; i < readyClientIds.Count; i++)
        {
            if (readyClientIds[i] == clientId)
                return;
        }
        readyClientIds.Add(clientId);
    }

    private void UpsertChoice(ulong clientId, int choiceIndex)
    {
        for (int i = 0; i < playerChoices.Count; i++)
        {
            if (playerChoices[i].ClientId == clientId)
            {
                playerChoices[i] = new DialoguePlayerChoice { ClientId = clientId, ChoiceIndex = choiceIndex };
                return;
            }
        }

        playerChoices.Add(new DialoguePlayerChoice { ClientId = clientId, ChoiceIndex = choiceIndex });
    }

    private void RemoveChoice(ulong clientId)
    {
        for (int i = playerChoices.Count - 1; i >= 0; i--)
        {
            if (playerChoices[i].ClientId == clientId)
                playerChoices.RemoveAt(i);
        }
    }

    private static void RemoveFromUlongList(NetworkList<ulong> list, ulong clientId)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == clientId)
                list.RemoveAt(i);
        }
    }

    private bool AllParticipantsReady()
    {
        if (_participants.Count == 0) return false;
        foreach (ulong id in _participants)
        {
            bool found = false;
            for (int i = 0; i < readyClientIds.Count; i++)
            {
                if (readyClientIds[i] == id)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    private void TryResolve()
    {
        if (!IsServer || !isActive.Value) return;
        if (!AllParticipantsReady()) return;

        DialogueNode node = GetCurrentNodeServer();
        if (node == null)
        {
            ForceCloseAbort();
            return;
        }

        string nextId = null;
        int resolved = -1;

        bool hasChoices = node.choices != null && node.choices.Length > 0;
        if (hasChoices)
        {
            var unique = new List<int>();
            for (int i = 0; i < playerChoices.Count; i++)
            {
                int idx = playerChoices[i].ChoiceIndex;
                if (!unique.Contains(idx))
                    unique.Add(idx);
            }

            if (unique.Count == 0)
            {
                Debug.LogWarning("[DialogueManager] Choice node ready but no choices recorded.");
                return;
            }

            if (unique.Count == 1)
                resolved = unique[0];
            else
            {
                resolved = unique[UnityEngine.Random.Range(0, unique.Count)];
                Debug.Log($"[DialogueManager] random resolve picks choice {resolved}");
            }

            resolvedChoiceIndex.Value = resolved;
            if (resolved >= 0 && resolved < node.choices.Length)
                nextId = node.choices[resolved].nextNodeId;
        }
        else
        {
            resolvedChoiceIndex.Value = -1;
            nextId = node.nextNodeId;
        }

        // Clear ready/choice BEFORE next node write (same frame).
        readyClientIds.Clear();
        playerChoices.Clear();

        if (string.IsNullOrEmpty(nextId) || _nodeById == null || !_nodeById.ContainsKey(nextId))
        {
            CloseNormally();
            return;
        }

        nodeIdNv.Value = new FixedString64Bytes(nextId);
    }

    private void CloseNormally()
    {
        string endedSequenceId = _activeSequence != null ? _activeSequence.sequenceId : sequenceIdNv.Value.ToString();
        string completeQuestId = _activeSequence != null ? _activeSequence.completeQuestId : null;
        string startQuestId = _activeSequence != null ? _activeSequence.startQuestId : null;

        if (QuestManager.Instance != null)
        {
            if (!string.IsNullOrEmpty(completeQuestId))
                QuestManager.Instance.CompleteCurrentQuestIfIdServer(completeQuestId, chainToNext: false);

            if (!string.IsNullOrEmpty(startQuestId))
                QuestManager.Instance.StartQuestByIdServer(startQuestId);
        }

        ClearSessionState();
        OnDialogueEnded?.Invoke(endedSequenceId);
    }

    private void ForceCloseAbort()
    {
        ClearSessionState();
        Debug.Log("[DialogueManager] Dialogue aborted (no participants left).");
    }

    private void ClearSessionState()
    {
        readyClientIds.Clear();
        playerChoices.Clear();
        participantClientIds.Clear();
        _participants.Clear();
        resolvedChoiceIndex.Value = -1;
        nodeIdNv.Value = default;
        sequenceIdNv.Value = default;
        isActive.Value = false;
        ClearLocalDialogueCache();
    }

    private void ClearLocalDialogueCache()
    {
        _activeSequence = null;
        _nodeById = null;
        _cachedSequenceIdForNodes = null;
    }

    private DialogueNode GetCurrentNodeServer()
    {
        EnsureNodeCacheForActiveSequence();
        string id = nodeIdNv.Value.ToString();
        if (_nodeById != null && _nodeById.TryGetValue(id, out var node))
            return node;
        return null;
    }

    private void EnsureNodeCacheForActiveSequence()
    {
        if (!isActive.Value)
        {
            ClearLocalDialogueCache();
            return;
        }

        string sequenceId = sequenceIdNv.Value.ToString();
        if (string.IsNullOrEmpty(sequenceId))
        {
            ClearLocalDialogueCache();
            return;
        }

        if (_nodeById != null && _cachedSequenceIdForNodes == sequenceId)
            return;

        if (!_sequenceById.TryGetValue(sequenceId, out var seq) || seq == null)
        {
            // Sequence henüz cache'te yoksa Resources'tan tekrar dene.
            RebuildSequenceCache();
            if (!_sequenceById.TryGetValue(sequenceId, out seq) || seq == null)
            {
                ClearLocalDialogueCache();
                return;
            }
        }

        _activeSequence = seq;
        _cachedSequenceIdForNodes = sequenceId;
        BuildNodeCache(seq);
    }

    private void BuildNodeCache(DialogueSequence seq)
    {
        _nodeById = new Dictionary<string, DialogueNode>(StringComparer.Ordinal);
        if (seq?.nodes == null) return;
        foreach (var node in seq.nodes)
        {
            if (node == null || string.IsNullOrEmpty(node.id)) continue;
            _nodeById[node.id] = node;
        }
    }
}
