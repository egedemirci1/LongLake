using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using Unity.AI.Navigation;

public class DoorController : NetworkBehaviour, IInteractable
{
    [Header("Door State (Initial Values)")]
    [SerializeField] private bool startOpen = false;
    [SerializeField] private bool startLocked = false;
    public string requiredKeyName = "MutfakAnahtari";

    [Header("Settings")]
    public float openRotation = 90.0f;
    public float closeRotation = 0.0f;
    public float smooth = 5.0f;

    [Header("Quest Knock (optional)")]
    [Tooltip("If true, door cannot open/close. Only knockable while the required quest is active.")]
    [SerializeField] private bool knockOnlyForQuest = false;
    /// <summary>Npc/quest tetikleri için: bu kapı quest-knock kapısı mı?</summary>
    public bool IsQuestKnockDoor => knockOnlyForQuest;
    [SerializeField] private string requiredQuestId = "quest_002";
    [SerializeField] private string knockPrompt = "Kapıyı Çal";
    [SerializeField] private string waitingForPartnerPrompt = "Diğer oyuncu da yakında olmalı";
    [SerializeField] private bool completeQuestOnKnock = true;

    [Header("Knock Animation")]
    [Tooltip("Local Z rotation after a successful knock.")]
    [SerializeField] private float knockOpenZRotation = -110f;
    [Tooltip("SmoothDamp time — higher = slower / softer open.")]
    [SerializeField] private float knockAnimSmoothTime = 0.55f;
    [Tooltip("Seconds to wait after knock SFX before the door starts opening.")]
    [SerializeField] private float knockOpenDelaySeconds = 4.5f;

    [Header("NavMesh")]
    [Tooltip("Kapalıyken geçidi keser, açılınca serbest bırakır. Bake'te kapı Navigation Static olmamalı.")]
    [SerializeField] private bool carveNavMeshWhenClosed = true;
    [SerializeField] private NavMeshObstacle navMeshObstacle;
    [Tooltip("Eşikte bake deliği varsa NavMeshLink ile içeri-dışarı bağlar (tüm sahneyi yeniden bake etmez).")]
    [SerializeField] private bool useDoorwayNavMeshLink = true;
    [SerializeField] private NavMeshLink doorwayNavMeshLink;
    [Tooltip("Door_Group local Start — Ismail kapısı için Y üzerinden geçiş.")]
    [SerializeField] private Vector3 doorwayLinkStartPoint = new Vector3(0f, 0.61f, 0f);
    [Tooltip("Door_Group local End.")]
    [SerializeField] private Vector3 doorwayLinkEndPoint = new Vector3(0f, -0.59f, 0f);
    [Tooltip("Link giriş genişliği — düşükse tek çizgi gibi geçer.")]
    [SerializeField] private float doorwayLinkWidth = 1.6f;

    [Header("Co-op Proximity")]
    [Tooltip("Co-op'ta tüm oyuncular kapıya yakın olmalı. Solo'da otomatik sadece 1 kişi yeter.")]
    [SerializeField] private bool requireAllPlayersNearby = true;
    [Tooltip("Co-op'ta gereken minimum oyuncu. Solo'da 1'e düşer.")]
    [SerializeField] private int minimumPlayersRequired = 2;
    [SerializeField] private float nearbyRadius = 6f;

    private NetworkVariable<bool> IsOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> IsLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> hasBeenKnocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>True when the knock door should animate open (after delay).</summary>
    private NetworkVariable<bool> knockDoorOpened = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private float _targetLocalZ;
    private float _currentLocalZ;
    private float _knockZVelocity;

    /// <summary>NavMesh açısından kapı şu an geçite izin veriyor mu?</summary>
    public bool IsNavMeshPassageOpen =>
        knockOnlyForQuest ? knockDoorOpened.Value : IsOpen.Value;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            IsOpen.Value = startOpen;
            IsLocked.Value = startLocked;
            hasBeenKnocked.Value = false;
            knockDoorOpened.Value = false;
        }

        hasBeenKnocked.OnValueChanged += OnHasBeenKnockedChanged;
        knockDoorOpened.OnValueChanged += OnKnockDoorOpenedChanged;
        IsOpen.OnValueChanged += OnIsOpenChanged;

        EnsureNavMeshObstacle();
        EnsureDoorwayNavMeshLink();
        ApplyNavMeshCarveState();

        _currentLocalZ = transform.localEulerAngles.z;
        // Normalize to signed range for SmoothDampAngle
        if (_currentLocalZ > 180f) _currentLocalZ -= 360f;

        if (knockOnlyForQuest)
        {
            _targetLocalZ = knockDoorOpened.Value ? knockOpenZRotation : closeRotation;
            if (knockDoorOpened.Value)
            {
                _currentLocalZ = knockOpenZRotation;
                ApplyLocalZ(_currentLocalZ);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        hasBeenKnocked.OnValueChanged -= OnHasBeenKnockedChanged;
        knockDoorOpened.OnValueChanged -= OnKnockDoorOpenedChanged;
        IsOpen.OnValueChanged -= OnIsOpenChanged;
    }

    private void OnHasBeenKnockedChanged(bool previous, bool current)
    {
        if (!knockOnlyForQuest) return;

        // Knock immediately plays SFX; door open waits for knockDoorOpened (delay).
        if (current && !previous)
            GameAudio.PlayKnock();
    }

    private void OnKnockDoorOpenedChanged(bool previous, bool current)
    {
        if (!knockOnlyForQuest) return;
        _targetLocalZ = current ? knockOpenZRotation : closeRotation;
        ApplyNavMeshCarveState();

        if (current)
        {
            GameAudio.PlayOpen();
            StartCoroutine(RefreshLinkAfterOpenRoutine());
        }
        else
        {
            GameAudio.PlayClose();
        }
    }

    private void OnIsOpenChanged(bool previous, bool current)
    {
        if (knockOnlyForQuest) return;
        ApplyNavMeshCarveState();

        if (current)
        {
            GameAudio.PlayOpen();
            StartCoroutine(RefreshLinkAfterOpenRoutine());
        }
        else
        {
            GameAudio.PlayClose();
        }
    }

    private System.Collections.IEnumerator RefreshLinkAfterOpenRoutine()
    {
        // Obstacle kapanınca / carve güncellenince birkaç frame bekle, sonra link uçlarını oturt.
        for (int i = 0; i < 6; i++)
            yield return null;
        RefreshDoorwayNavMeshLink();
        yield return null;
        RefreshDoorwayNavMeshLink();
    }

    private void Update()
    {
        if (knockOnlyForQuest)
        {
            _currentLocalZ = Mathf.SmoothDampAngle(
                _currentLocalZ,
                _targetLocalZ,
                ref _knockZVelocity,
                knockAnimSmoothTime
            );
            ApplyLocalZ(_currentLocalZ);
            return;
        }

        float targetAngle = IsOpen.Value ? openRotation : closeRotation;
        Quaternion target = Quaternion.Euler(0, 0, targetAngle);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, Time.deltaTime * smooth);
    }

    private void ApplyLocalZ(float zDegrees)
    {
        transform.localRotation = Quaternion.Euler(0f, 0f, zDegrees);
    }

    /// <summary>
    /// Kapalıyken NavMesh geçidini keser; açıkken carve'i kapatır.
    /// Bake'te kapı eşiği walkable olmalıdır — aksi halde carve kapatmak yol açmaz.
    /// </summary>
    private void EnsureNavMeshObstacle()
    {
        if (!carveNavMeshWhenClosed) return;

        if (navMeshObstacle == null)
            navMeshObstacle = GetComponent<NavMeshObstacle>();
        if (navMeshObstacle == null)
            navMeshObstacle = gameObject.AddComponent<NavMeshObstacle>();

        navMeshObstacle.shape = NavMeshObstacleShape.Box;
        navMeshObstacle.carveOnlyStationary = false;
        navMeshObstacle.carvingMoveThreshold = 0.05f;
        navMeshObstacle.carvingTimeToStationary = 0.15f;

        // Sadece ince Size yaz; Center'a dokunma (kapı hinge / mesh offset bozulmasın).
        ApplyThinDoorObstacleSize(navMeshObstacle);
    }

    /// <summary>Tüm kapılar için ortak ince carve boyutu (Center korunur).</summary>
    private static readonly Vector3 ThinDoorObstacleSize = new Vector3(0.01f, 0.001f, 0.03f);

    public static void ApplyThinDoorObstacleSize(NavMeshObstacle obstacle)
    {
        if (obstacle == null) return;
        obstacle.size = ThinDoorObstacleSize;
    }

    public static void ClampIfOversized(NavMeshObstacle obstacle)
    {
        ApplyThinDoorObstacleSize(obstacle);
    }

    private void ApplyNavMeshCarveState()
    {
        if (carveNavMeshWhenClosed)
        {
            EnsureNavMeshObstacle();
            if (navMeshObstacle != null)
            {
                bool passageOpen = IsNavMeshPassageOpen;
                navMeshObstacle.carving = !passageOpen;
                navMeshObstacle.enabled = !passageOpen;
            }
        }

        if (useDoorwayNavMeshLink)
        {
            EnsureDoorwayNavMeshLink();
            if (doorwayNavMeshLink != null)
                doorwayNavMeshLink.activated = IsNavMeshPassageOpen;
        }
    }

    /// <summary>Kapı açılınca / yolculuk öncesi link uçlarını NavMesh adalarına oturt.</summary>
    public void RefreshDoorwayNavMeshLink()
    {
        EnsureDoorwayNavMeshLink();
        if (doorwayNavMeshLink != null)
            doorwayNavMeshLink.activated = IsNavMeshPassageOpen;
    }

    /// <summary>
    /// Link Door_Group üzerinde. Uçlar Inspector/kod local noktaları (varsayılan Y: 0.61 / -0.59).
    /// </summary>
    private void EnsureDoorwayNavMeshLink()
    {
        if (!useDoorwayNavMeshLink) return;

        Transform host = FindDoorGroupHost();
        if (host == null)
            host = transform.parent != null ? transform.parent : transform;

        if (doorwayNavMeshLink == null)
            doorwayNavMeshLink = host.GetComponent<NavMeshLink>();
        if (doorwayNavMeshLink == null)
            doorwayNavMeshLink = host.gameObject.AddComponent<NavMeshLink>();

        doorwayNavMeshLink.agentTypeID = 0;
        doorwayNavMeshLink.startPoint = doorwayLinkStartPoint;
        doorwayNavMeshLink.endPoint = doorwayLinkEndPoint;
        doorwayNavMeshLink.width = doorwayLinkWidth;
        doorwayNavMeshLink.bidirectional = true;
        doorwayNavMeshLink.autoUpdate = false;
        doorwayNavMeshLink.area = 0;
    }

    private Transform FindDoorGroupHost()
    {
        Transform t = transform;
        while (t != null)
        {
            if (t.name != null && t.name.StartsWith("Door_Group", System.StringComparison.Ordinal))
                return t;
            t = t.parent;
        }
        return null;
    }

    public void Interact(InventoryManager interactorInventory)
    {
        if (!IsSpawned) return;

        if (knockOnlyForQuest)
        {
            if (!CanKnock()) return;
            KnockServerRpc();
            return;
        }

        bool hasKey = (interactorInventory != null && interactorInventory.HasKey(requiredKeyName));
        TryToggleDoorServerRpc(hasKey);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void KnockServerRpc()
    {
        if (!CanKnock()) return;

        hasBeenKnocked.Value = true;

        if (completeQuestOnKnock && QuestManager.Instance != null)
            QuestManager.Instance.CompleteCurrentQuestIfIdServer(requiredQuestId);

        StartCoroutine(OpenKnockDoorAfterDelayServer());
    }

    private System.Collections.IEnumerator OpenKnockDoorAfterDelayServer()
    {
        yield return new WaitForSeconds(knockOpenDelaySeconds);
        if (!IsSpawned || !IsServer) yield break;
        knockDoorOpened.Value = true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryToggleDoorServerRpc(bool clientClaimsHasKey)
    {
        if (knockOnlyForQuest) return;

        if (IsLocked.Value)
        {
            if (clientClaimsHasKey)
            {
                IsLocked.Value = false;
                IsOpen.Value = true;
            }
            else
            {
                return;
            }
            return;
        }

        IsOpen.Value = !IsOpen.Value;
    }

    public string GetInteractText()
    {
        if (knockOnlyForQuest)
        {
            if (hasBeenKnocked.Value) return string.Empty;
            if (QuestManager.Instance == null || !QuestManager.Instance.IsCurrentQuestId(requiredQuestId))
                return string.Empty;

            if (!AreAllPlayersNearby())
                return waitingForPartnerPrompt;

            return knockPrompt;
        }

        if (IsLocked.Value) return $"Kapi Kilitli ({requiredKeyName} Gerekli)";
        return IsOpen.Value ? "Kapıyı Kapat" : "Kapıyı Aç";
    }

    private bool CanKnock()
    {
        if (!knockOnlyForQuest) return false;
        if (hasBeenKnocked.Value) return false;
        if (QuestManager.Instance == null) return false;
        if (!QuestManager.Instance.IsCurrentQuestId(requiredQuestId)) return false;
        return AreAllPlayersNearby();
    }

    /// <summary>
    /// True when every connected player is within nearbyRadius.
    /// Solo: 1 oyuncu yeter. Co-op: minimumPlayersRequired (genelde 2).
    /// Client-safe: does not use GetPlayerNetworkObject (server-only for remote clients).
    /// </summary>
    private bool AreAllPlayersNearby()
    {
        if (!requireAllPlayersNearby)
            return true;

        int required = GetMinimumPlayersRequired();
        int playerCount = 0;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!pc.IsSpawned) continue;

            if (!IsWithinNearbyRadius(pc.transform.position))
                return false;

            playerCount++;
        }

        return playerCount >= required;
    }

    private int GetMinimumPlayersRequired()
    {
        int connected = 1;
        if (NetworkManager.Singleton != null)
            connected = Mathf.Max(1, NetworkManager.Singleton.ConnectedClientsIds.Count);

        return Mathf.Clamp(connected, 1, Mathf.Max(1, minimumPlayersRequired));
    }

    private bool IsWithinNearbyRadius(Vector3 worldPos)
    {
        Vector3 doorPos = transform.position;
        doorPos.y = 0f;
        worldPos.y = 0f;
        return Vector3.Distance(doorPos, worldPos) <= nearbyRadius;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!knockOnlyForQuest || !requireAllPlayersNearby) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, nearbyRadius);
    }
#endif
}
