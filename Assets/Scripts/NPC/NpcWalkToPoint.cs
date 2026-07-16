using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

/// <summary>
/// NPC için tek seferlik NavMesh yürüyüşü (İsmail).
/// Şimdilik geçici olarak quest-knock kapısı açık açıya ulaşınca başlar;
/// diyalog sonuna geçince <see cref="StartJourney"/>'yi oradan çağırıp
/// <c>autoStartWhenKnockDoorOpens</c>'i kapatman yeterli.
/// </summary>
public class NpcWalkToPoint : NetworkBehaviour
{
    [Header("NPC")]
    [SerializeField] private string npcObjectName = "ismail";
    [SerializeField] private Transform npcOverride;

    [Header("Target")]
    [Tooltip("Boş bırakılırsa Yaman'ın NetworkLocalSetup spawn noktası kullanılır.")]
    [SerializeField] private Transform destination;
    [SerializeField] private bool useYamanSpawnIfNoDestination = true;
    [SerializeField] private Vector3 yamanSpawnFallback = new Vector3(1753.697f, 110f, 523f);

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 1.8f;
    [SerializeField] private float runSpeed = 4.5f;
    [Tooltip("Yürüyüş başladıktan bu kadar saniye sonra koşmaya geçer (bitişe yakın değilse).")]
    [SerializeField] private float walkBeforeRunSeconds = 2.2f;
    [Tooltip("Bitişe bu mesafeden yakınken tekrar yürüyüşe döner.")]
    [SerializeField] private float approachWalkDistance = 7f;
    [SerializeField] private float stoppingDistance = 0.35f;
    [SerializeField] private float walkAnimatorSpeedMultiplier = 2.8f;
    [SerializeField] private float runAnimatorSpeedMultiplier = 2.2f;
    [SerializeField] private float navMeshSampleRadius = 4f;
    [Tooltip("NavMesh'e oturturken yatayda bundan fazla ışınlama yasak (evin dışına atmayı önler).")]
    [SerializeField] private float maxNavMeshSnapDistance = 0.55f;
    [Tooltip("İçerde mesh yoksa kapı eşiğine en fazla bu kadar yaklaşarak oturt.")]
    [SerializeField] private float maxDoorwaySnapDistance = 2.5f;

    [Header("After Arrival Talk")]
    [SerializeField] private bool enableTalkAfterArrival = true;
    [SerializeField] private float talkEnableDelaySeconds = 1.5f;
    [SerializeField] private string arrivalSequenceId = "ismail_arrival";
    [SerializeField] private string talkPrompt = "Konuş";
    [SerializeField] private float talkNearbyRadius = 5f;
    [SerializeField] private int talkMinimumPlayersRequired = 2;

    [Header("Temporary Door Watch (remove when dialogue-driven)")]
    [SerializeField] private bool autoStartWhenKnockDoorOpens = true;
    [SerializeField] private Transform watchDoor;
    [SerializeField] private float watchDoorOpenZ = -110f;
    [SerializeField] private float watchDoorAngleTolerance = 0.5f;

    private readonly NetworkVariable<bool> journeyStarted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> journeyArrived = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Transform _npc;
    private NavMeshAgent _agent;
    private Animator _animator;
    private bool _journeyInitialized;
    private float _journeyElapsed;
    private bool _isRunning;
    private bool _talkEnableScheduled;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public override void OnNetworkSpawn()
    {
        ResolveNpc();
        ResolveWatchDoor();
        journeyStarted.OnValueChanged += OnJourneyStartedChanged;
        journeyArrived.OnValueChanged += OnJourneyArrivedChanged;

        if (journeyStarted.Value)
            StartCoroutine(BeginJourneyLocalRoutine());

        if (journeyArrived.Value)
            ScheduleTalkEnable();
    }

    public override void OnNetworkDespawn()
    {
        journeyStarted.OnValueChanged -= OnJourneyStartedChanged;
        journeyArrived.OnValueChanged -= OnJourneyArrivedChanged;
    }

    private void Update()
    {
        if (IsServer && !journeyStarted.Value && autoStartWhenKnockDoorOpens)
            TryStartFromDoorWatch();

        UpdateGait();
        UpdateAnimation();

        if (IsServer)
            TryMarkArrivedOnServer();
    }

    /// <summary>
    /// Kapı, diyalog veya quest gibi herhangi bir sistemden yürüyüşü başlatır.
    /// </summary>
    public void StartJourney()
    {
        if (!IsSpawned) return;

        if (IsServer)
            TryStartJourneyOnServer();
        else
            StartJourneyServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void StartJourneyServerRpc()
    {
        TryStartJourneyOnServer();
    }

    private void ResolveNpc()
    {
        if (npcOverride != null)
        {
            _npc = npcOverride;
            return;
        }

        GameObject found = GameObject.Find(npcObjectName);
        if (found != null)
            _npc = found.transform;
        else
            Debug.LogError($"[NpcWalkToPoint] NPC '{npcObjectName}' bulunamadı.");
    }

    private void ResolveWatchDoor()
    {
        if (watchDoor != null || !autoStartWhenKnockDoorOpens) return;

        foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!door.IsQuestKnockDoor) continue;
            watchDoor = door.transform;
            break;
        }
    }

    private void TryStartFromDoorWatch()
    {
        if (watchDoor == null)
        {
            ResolveWatchDoor();
            if (watchDoor == null) return;
        }

        float z = watchDoor.localEulerAngles.z;
        if (z > 180f) z -= 360f;

        if (Mathf.Abs(Mathf.DeltaAngle(z, watchDoorOpenZ)) <= watchDoorAngleTolerance)
            TryStartJourneyOnServer();
    }

    private void TryStartJourneyOnServer()
    {
        if (!IsServer || journeyStarted.Value) return;
        journeyStarted.Value = true;
        Debug.Log($"<b>[NPC]</b> {npcObjectName} yürüyüşe başlıyor.");
    }

    private void OnJourneyStartedChanged(bool previous, bool current)
    {
        if (current && !previous)
            StartCoroutine(BeginJourneyLocalRoutine());
    }

    private System.Collections.IEnumerator BeginJourneyLocalRoutine()
    {
        if (_journeyInitialized) yield break;
        _journeyInitialized = true;

        if (_npc == null)
            ResolveNpc();
        if (_npc == null) yield break;

        // Kapı carve'inin NavMesh'e yazılması için kısa bir nefes.
        // (Obstacle kapandıktan sonra Unity carve güncellemesi bir frame sürebilir.)
        for (int i = 0; i < 8; i++)
            yield return null;

        _animator = _npc.GetComponent<Animator>();
        if (_animator != null)
            _animator.applyRootMotion = false;

        _agent = _npc.GetComponent<NavMeshAgent>();
        if (_agent == null)
            _agent = _npc.gameObject.AddComponent<NavMeshAgent>();

        _agent.enabled = false;
        _agent.speed = walkSpeed;
        _agent.acceleration = 5f;
        _agent.angularSpeed = 360f;
        _agent.stoppingDistance = stoppingDistance;
        _agent.autoBraking = true;
        _agent.radius = 0.35f;
        _agent.height = 1.8f;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        _journeyElapsed = 0f;
        _isRunning = false;

        Vector3 target = ResolveDestination();
        if (!TrySampleNear(target, navMeshSampleRadius, float.MaxValue, out NavMeshHit targetHit))
        {
            Debug.LogError($"[NpcWalkToPoint] Hedef yakınında NavMesh bulunamadı: {target}");
            yield break;
        }

        // ÖNCE mevcut konumun HEMEN yanındaki mesh — büyük SamplePosition evi dışına ışınlıyordu.
        if (!TryPlaceOnNavMeshNearNpc())
        {
            Debug.LogError(
                $"[NpcWalkToPoint] {_npc.name} NavMesh üzerinde değil ve güvenli snap yok. " +
                "Ev içi zemin bake'te walkable olmalı; aksi halde agent evi dışına atılırdı (engellendi).");
            yield break;
        }

        _agent.enabled = true;
        if (!_agent.isOnNavMesh)
            _agent.Warp(_npc.position);

        // Carve/path henüz hazır değilse birkaç kez dene.
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
        {
            Debug.LogError(
                $"[NpcWalkToPoint] {_npc.name} için geçerli rota yok. " +
                "NavMesh'i kapı eşiği açık/walkable olacak şekilde yeniden bake et; " +
                "kapı objesinde Navigation Static kapalı olmalı.");
            _agent.enabled = false;
        }
    }

    /// <summary>
    /// NPC'yi NavMesh'e oturtur. Yatayda uzaktaki (evin dışı) noktalara ışınlamaz.
    /// </summary>
    private bool TryPlaceOnNavMeshNearNpc()
    {
        // 1) Mevcut pozisyonun çok yakınında mesh var mı?
        if (TrySampleNear(_npc.position, 0.75f, maxNavMeshSnapDistance, out NavMeshHit localHit))
        {
            _agent.Warp(localHit.position);
            return true;
        }

        // 2) Açık quest kapısı eşiği — kısa mesafe ise oraya oturt (dışarıya rastgele değil).
        Transform door = FindOpenQuestKnockDoor();
        if (door != null)
        {
            Vector3 doorway = door.position + door.forward * 0.6f;
            if (TrySampleNear(doorway, 1.2f, maxDoorwaySnapDistance, out NavMeshHit doorHit))
            {
                float horiz = HorizontalDistance(_npc.position, doorHit.position);
                if (horiz <= maxDoorwaySnapDistance)
                {
                    Debug.LogWarning(
                        $"[NpcWalkToPoint] {_npc.name} içerde mesh bulamadı; kapı eşiğine alındı ({horiz:0.00}m). " +
                        "Kalıcı çözüm: ev içi NavMesh bake.");
                    _agent.Warp(doorHit.position);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TrySampleNear(Vector3 origin, float sampleRadius, float maxHorizontal, out NavMeshHit hit)
    {
        hit = default;
        if (!NavMesh.SamplePosition(origin, out NavMeshHit sampled, sampleRadius, NavMesh.AllAreas))
            return false;

        if (HorizontalDistance(origin, sampled.position) > maxHorizontal)
            return false;

        hit = sampled;
        return true;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static Transform FindOpenQuestKnockDoor()
    {
        foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (door.IsQuestKnockDoor && door.IsNavMeshPassageOpen)
                return door.transform;
        }
        return null;
    }

    private Vector3 ResolveDestination()
    {
        if (destination != null)
            return destination.position;

        if (useYamanSpawnIfNoDestination)
        {
            NetworkLocalSetup setup = FindFirstObjectByType<NetworkLocalSetup>();
            if (setup != null)
                return setup.YamanSpawnPosition;
        }

        return yamanSpawnFallback;
    }

    private void UpdateGait()
    {
        if (!_journeyInitialized || _agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;

        _journeyElapsed += Time.deltaTime;

        // Path henüz hazır değilse yürüyüşte kal
        if (_agent.pathPending || !_agent.hasPath)
        {
            ApplyGait(running: false);
            return;
        }

        float remaining = _agent.remainingDistance;
        if (float.IsInfinity(remaining))
        {
            ApplyGait(running: false);
            return;
        }

        // Bitişe yaklaşınca yürüyüş; ortada süre dolunca koşu
        bool nearEnd = remaining <= Mathf.Max(approachWalkDistance, stoppingDistance + 0.5f);
        bool wantRun = !nearEnd && _journeyElapsed >= walkBeforeRunSeconds;
        ApplyGait(wantRun);
    }

    private void ApplyGait(bool running)
    {
        if (_agent == null) return;

        if (_isRunning == running &&
            Mathf.Approximately(_agent.speed, running ? runSpeed : walkSpeed))
            return;

        _isRunning = running;
        _agent.speed = running ? runSpeed : walkSpeed;
        _agent.acceleration = running ? 10f : 5f;
        _agent.autoBraking = !running || _agent.remainingDistance < approachWalkDistance;
    }

    private void UpdateAnimation()
    {
        if (!_journeyInitialized || _agent == null || _animator == null)
            return;

        float mult = _isRunning ? runAnimatorSpeedMultiplier : walkAnimatorSpeedMultiplier;
        float animationSpeed = _agent.enabled && _agent.isOnNavMesh
            ? _agent.velocity.magnitude * mult
            : 0f;
        _animator.SetFloat(SpeedHash, animationSpeed, 0.12f, Time.deltaTime);
    }

    private void TryMarkArrivedOnServer()
    {
        if (!IsServer || journeyArrived.Value || !_journeyInitialized)
            return;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh)
            return;
        if (_agent.pathPending)
            return;

        float remaining = _agent.remainingDistance;
        bool noPathLeft = !_agent.hasPath || remaining <= Mathf.Max(stoppingDistance + 0.15f, 0.5f);
        bool mostlyStopped = _agent.velocity.sqrMagnitude < 0.05f;

        if (noPathLeft && mostlyStopped)
            journeyArrived.Value = true;
    }

    private void OnJourneyArrivedChanged(bool previous, bool current)
    {
        if (current && !previous)
            ScheduleTalkEnable();
    }

    private void ScheduleTalkEnable()
    {
        if (!enableTalkAfterArrival || _talkEnableScheduled)
            return;
        _talkEnableScheduled = true;
        StartCoroutine(EnableTalkAfterDelayRoutine());
    }

    private System.Collections.IEnumerator EnableTalkAfterDelayRoutine()
    {
        yield return new WaitForSeconds(talkEnableDelaySeconds);

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
        if (!journeyArrived.Value) return;
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
