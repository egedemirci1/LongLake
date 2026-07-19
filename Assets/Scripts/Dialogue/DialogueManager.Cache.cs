using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Partial: split for maintainability. Type identity unchanged.
public partial class DialogueManager : NetworkBehaviour
{
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
