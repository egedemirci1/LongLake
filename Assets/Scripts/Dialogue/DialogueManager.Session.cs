using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Partial: split for maintainability. Type identity unchanged.
public partial class DialogueManager : NetworkBehaviour
{
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

}
