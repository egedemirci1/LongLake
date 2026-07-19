using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

// Partial: split for maintainability. Type identity unchanged.
public partial class NpcWalkToPoint : NetworkBehaviour
{
    private void TryMarkArrivedOnServer()
    {
        if (!IsServer || journeyArrived.Value || !_journeyInitialized || !_hasActiveJourneyTarget)
            return;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;
        if (_agent.pathPending || _traversingOffMeshLink)
            return;

        float distToTarget = HorizontalDistance(_agent.transform.position, _activeJourneyTarget);
        if (distToTarget > arrivalDistanceThreshold)
            return;

        float remaining = _agent.remainingDistance;
        bool nearEndOfPath = _agent.hasPath &&
            !float.IsInfinity(remaining) &&
            !float.IsNaN(remaining) &&
            remaining <= Mathf.Max(stoppingDistance + 0.15f, 0.5f);
        bool mostlyStopped = _agent.velocity.sqrMagnitude < 0.08f;

        if ((nearEndOfPath || distToTarget <= stoppingDistance + 0.5f) && mostlyStopped)
            journeyArrived.Value = true;
    }

    private void TryRetryDestinationOnServer()
    {
        if (!IsServer || journeyArrived.Value || !_journeyInitialized || !_hasActiveJourneyTarget)
            return;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;
        if (_traversingOffMeshLink)
            return;

        _destinationRetryCooldown -= Time.deltaTime;
        if (_destinationRetryCooldown > 0f)
            return;

        float distToTarget = HorizontalDistance(_agent.transform.position, _activeJourneyTarget);
        if (distToTarget <= arrivalDistanceThreshold)
            return;

        bool stuck = !_agent.hasPath ||
            _agent.pathStatus == NavMeshPathStatus.PathPartial ||
            _agent.pathStatus == NavMeshPathStatus.PathInvalid;

        float rem = _agent.remainingDistance;
        bool farOnPath = _agent.hasPath &&
            !float.IsInfinity(rem) &&
            !float.IsNaN(rem) &&
            rem > arrivalDistanceThreshold + 2f &&
            _agent.velocity.sqrMagnitude < 0.04f;

        if (stuck || farOnPath)
            RequestDestinationRefresh(force: false);
    }

    private void RequestDestinationRefresh(bool force)
    {
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh || !_hasActiveJourneyTarget)
            return;

        if (!force)
            _destinationRetryCooldown = 1.25f;

        _agent.isStopped = false;
        _agent.SetDestination(_activeJourneyTarget);
    }

    private void OnJourneyArrivedChanged(bool previous, bool current)
    {
        if (current && !previous)
            ScheduleTalkEnable();
    }

    private void ScheduleTalkEnable()
    {
        if (!enableTalkAfterArrival || _talkEnableScheduled || safiyeJourneyStarted.Value)
            return;
        _talkEnableScheduled = true;
        StartCoroutine(EnableTalkAfterDelayRoutine());
    }

    private System.Collections.IEnumerator EnableTalkAfterDelayRoutine()
    {
        yield return new WaitForSeconds(talkEnableDelaySeconds);

        if (safiyeJourneyStarted.Value)
            yield break;

        if (_npc == null)
            ResolveNpc();
        if (_npc == null)
            yield break;

        EnsureNpcHasCollider();

        var talk = _npc.GetComponent<NpcDialogueTalk>();
        if (talk == null)
            talk = _npc.gameObject.AddComponent<NpcDialogueTalk>();

        talk.Configure(arrivalSequenceId, talkPrompt, this);
        talk.SetInteractEnabled(true);

        if (_agent != null && _agent.enabled)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
        }

        if (_animator != null)
            _animator.SetFloat(SpeedHash, 0f);
    }

    /// <summary>Client E ile çağırır; server yakınlığı doğrulayıp herkese diyalog açar.</summary>
    public void RequestStartArrivalDialogue()
    {
        if (!IsSpawned) return;
        StartArrivalDialogueServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartArrivalDialogueServerRpc()
    {
        if (!journeyArrived.Value || safiyeJourneyStarted.Value) return;
        if (DialogueManager.IsDialogueOpen) return;
        if (DialogueManager.Instance == null) return;

        if (_npc == null)
            ResolveNpc();
        if (_npc == null) return;

        int required = GetTalkMinimumPlayersRequired();
        if (!PlayersNearbyUtility.AreEnoughPlayersNear(_npc.position, talkNearbyRadius, required))
            return;

        DialogueManager.Instance.TryStartDialogue(arrivalSequenceId);
    }

    private void TryHookDialogueEnd()
    {
        if (_dialogueEndHooked || DialogueManager.Instance == null) return;
        DialogueManager.Instance.OnDialogueEnded += OnArrivalDialogueEnded;
        _dialogueEndHooked = true;
    }

    private void UnhookDialogueEnd()
    {
        if (!_dialogueEndHooked || DialogueManager.Instance == null) return;
        DialogueManager.Instance.OnDialogueEnded -= OnArrivalDialogueEnded;
        _dialogueEndHooked = false;
    }

    private void OnArrivalDialogueEnded(string endedSequenceId)
    {
        if (!walkToSafiyeAfterArrivalTalk) return;
        if (endedSequenceId != arrivalSequenceId) return;
        StartSafiyeJourney();
    }

    /// <summary>Araba diyaloğu bitince Safiye evine yürüyüş.</summary>
    public void StartSafiyeJourney()
    {
        if (!IsSpawned) return;
        if (IsServer)
            TryStartSafiyeJourneyOnServer();
        else
            StartSafiyeJourneyServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartSafiyeJourneyServerRpc()
    {
        TryStartSafiyeJourneyOnServer();
    }

    private void TryStartSafiyeJourneyOnServer()
    {
        if (!IsServer || safiyeJourneyStarted.Value) return;
        if (!journeyArrived.Value) return;
        safiyeJourneyStarted.Value = true;
    }

    private void OnSafiyeJourneyStartedChanged(bool previous, bool current)
    {
        if (current && !previous)
            StartCoroutine(BeginSafiyeJourneyLocalRoutine());
    }

    private System.Collections.IEnumerator BeginSafiyeJourneyLocalRoutine()
    {
        if (_safiyeJourneyInitialized) yield break;
        _safiyeJourneyInitialized = true;

        if (_npc == null)
            ResolveNpc();
        if (_npc == null) yield break;

        var talk = _npc.GetComponent<NpcDialogueTalk>();
        if (talk != null)
            talk.SetInteractEnabled(false);

        // İlk yolculuk agent'ı yoksa kur.
        if (!_journeyInitialized)
            yield return BeginJourneyLocalRoutine();

        if (_agent == null)
            yield break;

        _journeyElapsed = 0f;
        _isRunning = false;
        ResetRunStamina();
        ApplyGait(running: false);

        _agent.isStopped = false;
        _agent.enabled = true;

        Vector3 safiyeTarget = SnapDestinationToGround(safiyeHouseDestination);
        // Önce dar, olmazsa geniş yarıçap — Safiye evi önü yeni bake edilene kadar yakın mesh'e snap.
        if (!TrySampleNear(safiyeTarget, navMeshSampleRadius, float.MaxValue, out NavMeshHit targetHit) &&
            !TrySampleNear(safiyeTarget, 40f, float.MaxValue, out targetHit))
        {
            Debug.LogError(
                $"[NpcWalkToPoint] Safiye hedefi NavMesh'te değil: {safiyeTarget}. " +
                "LongLake → Bake CrashSite NavMesh (Doors Open) çalıştır (yol proxy'leri ekler).");
            yield break;
        }

        bool pathOk = false;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (_agent.isOnNavMesh && _agent.SetDestination(targetHit.position))
            {
                float wait = 0f;
                while (_agent.pathPending && wait < 1f)
                {
                    wait += Time.deltaTime;
                    yield return null;
                }

                if (_agent.hasPath && _agent.pathStatus != NavMeshPathStatus.PathInvalid)
                {
                    pathOk = true;
                    break;
                }
            }

            yield return new WaitForSeconds(0.15f);
        }

        if (!pathOk)
            Debug.LogError("[NpcWalkToPoint] Safiye evine geçerli rota yok.");
    }

    private int GetTalkMinimumPlayersRequired()
    {
        int connected = 1;
        if (NetworkManager != null)
            connected = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);

        return Mathf.Clamp(connected, 1, Mathf.Max(1, talkMinimumPlayersRequired));
    }

    private void EnsureNpcHasCollider()
    {
        if (_npc.GetComponentInChildren<Collider>() != null)
            return;

        var capsule = _npc.gameObject.AddComponent<CapsuleCollider>();
        capsule.height = 1.8f;
        capsule.radius = 0.4f;
        capsule.center = new Vector3(0f, 0.9f, 0f);
    }

}
