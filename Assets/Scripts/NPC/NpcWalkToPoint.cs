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
    [Tooltip("Boş bırakılırsa carDestination kullanılır.")]
    [SerializeField] private Transform destination;
    [Tooltip("Kapı sonrası araba / inceleme noktası (sabit dünya koordinatı).")]
    [SerializeField] private Vector3 carDestination = new Vector3(1755.214f, 110.6208f, 529.1782f);
    [SerializeField] private bool useYamanSpawnIfNoDestination = false;
    [SerializeField] private Vector3 yamanSpawnFallback = new Vector3(1753.697f, 56f, 523f);

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 6.5f;
    [Tooltip("Yürüyüş başladıktan bu kadar saniye sonra koşmaya geçer (bitişe yakın değilse).")]
    [SerializeField] private float walkBeforeRunSeconds = 2.2f;
    [Tooltip("Bitişe bu mesafeden yakınken tekrar yürüyüşe döner.")]
    [SerializeField] private float approachWalkDistance = 7f;
    [SerializeField] private float stoppingDistance = 0.35f;
    [Tooltip("Araba hedefine bu yatay mesafeden yakınsa 'vardı' sayılır.")]
    [SerializeField] private float arrivalDistanceThreshold = 4f;
    [SerializeField] private float walkAnimatorSpeedMultiplier = 2.8f;
    [SerializeField] private float runAnimatorSpeedMultiplier = 2.2f;
    [SerializeField] private float navMeshSampleRadius = 4f;
    [Tooltip("NavMesh'e oturturken yatayda bundan fazla ışınlama yasak (evin dışına atmayı önler).")]
    [SerializeField] private float maxNavMeshSnapDistance = 0.55f;
    [Tooltip("İçerde mesh yoksa kapı eşiğine en fazla bu kadar yaklaşarak oturt.")]
    [SerializeField] private float maxDoorwaySnapDistance = 2.5f;

    [Header("Run Stamina (no UI — behaviour only)")]
    [SerializeField] private bool useRunStamina = true;
    [SerializeField] private float maxRunStamina = 100f;
    [Tooltip("Koşarken saniyede düşüş.")]
    [SerializeField] private float runStaminaDrainPerSecond = 16f;
    [Tooltip("Yürürken / dinlenirken saniyede doluş.")]
    [SerializeField] private float runStaminaRegenPerSecond = 22f;

    [Header("After Arrival Talk")]
    [SerializeField] private bool enableTalkAfterArrival = true;
    [SerializeField] private float talkEnableDelaySeconds = 1.5f;
    [SerializeField] private string arrivalSequenceId = "ismail_arrival";
    [SerializeField] private string talkPrompt = "Konuş";
    [SerializeField] private float talkNearbyRadius = 5f;
    [SerializeField] private int talkMinimumPlayersRequired = 2;

    [Header("After Car Dialogue → Safiye")]
    [SerializeField] private bool walkToSafiyeAfterArrivalTalk = true;
    [SerializeField] private Vector3 safiyeHouseDestination = new Vector3(1610.107f, 119.9632f, 643.2148f);

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

    private readonly NetworkVariable<bool> safiyeJourneyStarted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Transform _npc;
    private NavMeshAgent _agent;
    private Animator _animator;
    private bool _journeyInitialized;
    private bool _safiyeJourneyInitialized;
    private float _journeyElapsed;
    private bool _isRunning;
    private float _runStamina;
    private bool _runExhausted;
    private bool _talkEnableScheduled;
    private bool _dialogueEndHooked;
    private Vector3 _activeJourneyTarget;
    private bool _hasActiveJourneyTarget;
    private float _destinationRetryCooldown;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public override void OnNetworkSpawn()
    {
        // Eski sahne override'ları (1.8 / 4.5) — oyuncu yürüyüşü 5 ile hizala.
        if (Mathf.Approximately(walkSpeed, 1.8f))
            walkSpeed = 5f;
        if (Mathf.Approximately(runSpeed, 4.5f))
            runSpeed = 6.5f;

        ResolveNpc();
        ResolveWatchDoor();
        journeyStarted.OnValueChanged += OnJourneyStartedChanged;
        journeyArrived.OnValueChanged += OnJourneyArrivedChanged;
        safiyeJourneyStarted.OnValueChanged += OnSafiyeJourneyStartedChanged;
        TryHookDialogueEnd();

        if (journeyStarted.Value)
            StartCoroutine(BeginJourneyLocalRoutine());

        if (journeyArrived.Value && !safiyeJourneyStarted.Value)
            ScheduleTalkEnable();

        if (safiyeJourneyStarted.Value)
            StartCoroutine(BeginSafiyeJourneyLocalRoutine());
    }

    public override void OnNetworkDespawn()
    {
        journeyStarted.OnValueChanged -= OnJourneyStartedChanged;
        journeyArrived.OnValueChanged -= OnJourneyArrivedChanged;
        safiyeJourneyStarted.OnValueChanged -= OnSafiyeJourneyStartedChanged;
        UnhookDialogueEnd();
    }

    private void Update()
    {
        if (IsServer && !journeyStarted.Value && autoStartWhenKnockDoorOpens)
            TryStartFromDoorWatch();

        TryHookDialogueEnd();
        UpdateGait();
        UpdateAnimation();
        TryTraverseDoorLink();

        if (IsServer)
        {
            TryMarkArrivedOnServer();
            TryRetryDestinationOnServer();
        }
    }

    private bool _traversingOffMeshLink;

    /// <summary>
    /// NavMeshLink geçişini düz yürüyüş gibi yap — Unity default'u ince çizgi/teleport hissi verir.
    /// </summary>
    private void TryTraverseDoorLink()
    {
        if (_traversingOffMeshLink) return;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
        if (!_agent.isOnOffMeshLink) return;
        StartCoroutine(TraverseDoorLinkRoutine());
    }

    private System.Collections.IEnumerator TraverseDoorLinkRoutine()
    {
        _traversingOffMeshLink = true;
        OffMeshLinkData data = _agent.currentOffMeshLinkData;
        Vector3 end = data.endPos;

        _agent.updatePosition = false;
        _agent.updateRotation = false;

        float speed = _isRunning ? runSpeed : walkSpeed;
        while (_agent != null && _agent.enabled && _agent.isOnOffMeshLink)
        {
            Vector3 pos = _agent.transform.position;
            Vector3 next = Vector3.MoveTowards(pos, end, speed * Time.deltaTime);
            _agent.transform.position = next;

            Vector3 flat = end - pos;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f)
                _agent.transform.rotation = Quaternion.Slerp(
                    _agent.transform.rotation,
                    Quaternion.LookRotation(flat.normalized),
                    12f * Time.deltaTime);

            if ((next - end).sqrMagnitude <= 0.01f)
                break;

            yield return null;
        }

        if (_agent != null)
        {
            if (_agent.isOnOffMeshLink)
                _agent.CompleteOffMeshLink();
            _agent.Warp(_agent.transform.position);
            _agent.updatePosition = true;
            _agent.updateRotation = true;
        }

        _traversingOffMeshLink = false;

        // Link sonrası rota kopabiliyor — hedefe tekrar yürüt.
        if (IsServer && _hasActiveJourneyTarget && !journeyArrived.Value)
            RequestDestinationRefresh(force: true);
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
        _agent.autoTraverseOffMeshLink = false; // kapı linkini elle yürüterek geç (tek çizgi teleport olmasın)
        _agent.radius = 0.35f;
        _agent.height = 1.8f;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        _journeyElapsed = 0f;
        _isRunning = false;
        ResetRunStamina();

        // Kapı link'ini tazeleyip PathComplete olana kadar dene (Partial = eşikte takılır).
        RefreshOpenQuestDoorLinks();
        for (int i = 0; i < 10; i++)
            yield return null;

        Vector3 target = ResolveDestination();
        // Önce verilen dünya koordinatı; olmazsa terrain snap + geniş yarıçap.
        if (!TrySampleNear(target, navMeshSampleRadius, float.MaxValue, out NavMeshHit targetHit) &&
            !TrySampleNear(target, 40f, float.MaxValue, out targetHit))
        {
            Vector3 grounded = SnapDestinationToGround(target);
            if (!TrySampleNear(grounded, navMeshSampleRadius, float.MaxValue, out targetHit) &&
                !TrySampleNear(grounded, 40f, float.MaxValue, out targetHit))
            {
                Debug.LogError($"[NpcWalkToPoint] Hedef yakınında NavMesh bulunamadı: {target} (grounded: {grounded})");
                yield break;
            }
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

        bool pathOk = false;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (attempt == 5 || attempt == 12)
                RefreshOpenQuestDoorLinks();

            if (_agent.isOnNavMesh && _agent.SetDestination(targetHit.position))
            {
                float wait = 0f;
                while (_agent.pathPending && wait < 1.25f)
                {
                    wait += Time.deltaTime;
                    yield return null;
                }

                if (_agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathComplete)
                {
                    pathOk = true;
                    break;
                }
            }

            yield return new WaitForSeconds(0.2f);
        }

        if (!pathOk)
        {
            Debug.LogError(
                $"[NpcWalkToPoint] {_npc.name} için geçerli rota yok (kapı NavMeshLink / eşik). " +
                "Kapı açıkken Door_Group NavMeshLink uçlarının iki mavi adaya oturduğunu kontrol et.");
            _agent.enabled = false;
            yield break;
        }

        _activeJourneyTarget = targetHit.position;
        _hasActiveJourneyTarget = true;
        if (IsServer)
            RequestDestinationRefresh(force: true);
    }

    private static void RefreshOpenQuestDoorLinks()
    {
        foreach (var door in FindObjectsByType<DoorController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (door == null) continue;
            if (!door.IsNavMeshPassageOpen) continue;
            door.RefreshDoorwayNavMeshLink();
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
            return yamanSpawnFallback;
        }

        return carDestination;
    }

    /// <summary>
    /// Spawn/Inspector Y'si yaklaşık; NavMesh SamplePosition küre yarıçapı kullanır.
    /// Önce terrain yüksekliğine oturt ki 4m sample gerçek zemini kaçırmasın.
    /// </summary>
    private static Vector3 SnapDestinationToGround(Vector3 pos)
    {
        float terrainY = SampleTerrainHeight(pos.x, pos.z);
        if (!float.IsNegativeInfinity(terrainY))
            return new Vector3(pos.x, terrainY + 0.1f, pos.z);

        const float rayHeight = 500f;
        Vector3 origin = new Vector3(pos.x, rayHeight, pos.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayHeight + 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Gökyüzü collider'ı yutmasın — hedefe göre makul yükseklik bandı.
            if (Mathf.Abs(hit.point.y - pos.y) < 80f || pos.y < 1f)
                return new Vector3(pos.x, hit.point.y + 0.1f, pos.z);
        }

        return pos;
    }

    private static float SampleTerrainHeight(float worldX, float worldZ)
    {
        float best = float.NegativeInfinity;
        var terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
            return best;

        var probe = new Vector3(worldX, 0f, worldZ);
        foreach (var terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;

            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float lx = worldX - tp.x;
            float lz = worldZ - tp.z;
            if (lx < 0f || lz < 0f || lx > size.x || lz > size.z)
                continue;

            float h = terrain.SampleHeight(probe) + tp.y;
            if (h > best)
                best = h;
        }

        return best;
    }

    private void UpdateGait()
    {
        if (_traversingOffMeshLink) return;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh || _agent.isStopped)
            return;

        // Araba bacağı (varıştan önce) veya Safiye bacağı.
        bool onCarLeg = _journeyInitialized && !journeyArrived.Value;
        bool onSafiyeLeg = _safiyeJourneyInitialized && safiyeJourneyStarted.Value;
        if (!onCarLeg && !onSafiyeLeg)
            return;

        _journeyElapsed += Time.deltaTime;

        if (_agent.pathPending)
        {
            ApplyGait(running: false);
            return;
        }

        float remaining = _agent.remainingDistance;
        // Unity bazen hasPath iken bile Infinity döner — o zaman düz mesafe kullan.
        if (!_agent.hasPath || float.IsInfinity(remaining) || float.IsNaN(remaining))
        {
            if (_agent.hasPath)
                remaining = Vector3.Distance(_agent.transform.position, _agent.destination);
            else
            {
                ApplyGait(running: false);
                return;
            }
        }

        bool nearEnd = remaining <= Mathf.Max(approachWalkDistance, stoppingDistance + 0.5f);
        bool wantRun = !nearEnd && _journeyElapsed >= walkBeforeRunSeconds;
        bool canRun = wantRun && (!useRunStamina || !_runExhausted);
        ApplyGait(canRun);
        TickRunStamina(Time.deltaTime);
    }

    private void ResetRunStamina()
    {
        _runStamina = maxRunStamina;
        _runExhausted = false;
    }

    private void TickRunStamina(float dt)
    {
        if (!useRunStamina || dt <= 0f) return;

        if (_isRunning)
        {
            _runStamina -= runStaminaDrainPerSecond * dt;
            if (_runStamina <= 0f)
            {
                _runStamina = 0f;
                _runExhausted = true;
                ApplyGait(running: false);
            }
            return;
        }

        // Yürüyüş / duruşta doldur; full olunca tekrar koşabilir.
        _runStamina += runStaminaRegenPerSecond * dt;
        if (_runStamina >= maxRunStamina)
        {
            _runStamina = maxRunStamina;
            _runExhausted = false;
        }
    }

    private void UpdateAnimation()
    {
        if (_agent == null || _animator == null)
            return;

        bool onCarLeg = _journeyInitialized && !journeyArrived.Value;
        bool onSafiyeLeg = _safiyeJourneyInitialized && safiyeJourneyStarted.Value;
        if (!onCarLeg && !onSafiyeLeg)
            return;

        float mult = _isRunning ? runAnimatorSpeedMultiplier : walkAnimatorSpeedMultiplier;
        float animationSpeed = _agent.enabled && _agent.isOnNavMesh && !_agent.isStopped
            ? _agent.velocity.magnitude * mult
            : 0f;
        _animator.SetFloat(SpeedHash, animationSpeed, 0.12f, Time.deltaTime);
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
        float rem = _agent.remainingDistance;
        bool near = !float.IsInfinity(rem) && rem < approachWalkDistance;
        _agent.autoBraking = !running || near;
    }

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
