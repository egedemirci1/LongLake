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

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            IsOpen.Value = startOpen;
            IsLocked.Value = startLocked;
            hasBeenKnocked.Value = false;
        }
    }

    private void Update()
    {
        if (knockOnlyForQuest) return;

        float targetAngle = IsOpen.Value ? openRotation : closeRotation;
        Quaternion target = Quaternion.Euler(0, 0, targetAngle);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, Time.deltaTime * smooth);
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
        Debug.Log($"<b>[KAPI]</b> Kapı çalındı (herkes yakında). Quest={requiredQuestId}");

        if (completeQuestOnKnock && QuestManager.Instance != null)
            QuestManager.Instance.CompleteCurrentQuestIfIdServer(requiredQuestId);
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
