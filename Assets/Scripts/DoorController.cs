using UnityEngine;
using Unity.Netcode;

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

    [Header("Co-op Proximity")]
    [Tooltip("All connected players must stand near the door to knock.")]
    [SerializeField] private bool requireAllPlayersNearby = true;
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
        Debug.Log($"<b>[KAPI]</b> Kapı çalındı (herkes yakında). Quest={requiredQuestId}. Açılış {knockOpenDelaySeconds:0.#}s sonra.");

        if (completeQuestOnKnock && QuestManager.Instance != null)
            QuestManager.Instance.CompleteCurrentQuestIfIdServer(requiredQuestId);

        StartCoroutine(OpenKnockDoorAfterDelayServer());
    }

    private System.Collections.IEnumerator OpenKnockDoorAfterDelayServer()
    {
        yield return new WaitForSeconds(knockOpenDelaySeconds);
        if (!IsSpawned || !IsServer) yield break;
        knockDoorOpened.Value = true;
        Debug.Log("<b>[KAPI]</b> Kapı açılıyor (gecikme bitti).");
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
                Debug.Log($"<b>[KAPI]</b> {requiredKeyName} kullanildi, kapi acildi! (Server)");
            }
            else
            {
                Debug.Log($"<b>[KAPI]</b> Kilitli! Gereken anahtar: {requiredKeyName} (Server)");
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
    /// True when enough players are connected and every connected player is within nearbyRadius.
    /// Client-safe: does not use GetPlayerNetworkObject (server-only for remote clients).
    /// </summary>
    private bool AreAllPlayersNearby()
    {
        if (!requireAllPlayersNearby)
            return true;

        int playerCount = 0;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!pc.IsSpawned) continue;

            if (!IsWithinNearbyRadius(pc.transform.position))
                return false;

            playerCount++;
        }

        return playerCount >= minimumPlayersRequired;
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
