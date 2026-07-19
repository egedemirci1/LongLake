using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

/// <summary>
/// NPC için tek seferlik NavMesh yürüyüşü (İsmail).
/// Şimdilik geçici olarak quest-knock kapısı açık açıya ulaşınca başlar;
/// diyalog sonuna geçince <see cref="StartJourney"/>'yi oradan çağırıp
/// <c>autoStartWhenKnockDoorOpens</c>'i kapatman yeterli.
/// </summary>
public partial class NpcWalkToPoint : NetworkBehaviour
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

}
