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

    // Network-synchronized states
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

    private void Awake()
    {
        // Optional local init here; actual init will be done on server
    }

    public override void OnNetworkSpawn()
    {
        // Server sets initial states
        if (IsServer)
        {
            IsOpen.Value = startOpen;
            IsLocked.Value = startLocked;
        }
    }

    private void Update()
    {
        // All clients animate based on network state
        float targetAngle = IsOpen.Value ? openRotation : closeRotation;
        Quaternion target = Quaternion.Euler(0, 0, targetAngle);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, Time.deltaTime * smooth);
    }

    // IInteractable
    public void Interact(InventoryManager interactorInventory)
    {
        // Client sends request to server when input received
        // (If host, it's both client and server, so still safe)
        if (!IsSpawned) return;

        bool hasKey = (interactorInventory != null && interactorInventory.HasKey(requiredKeyName));
        TryToggleDoorServerRpc(hasKey);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryToggleDoorServerRpc(bool clientClaimsHasKey)
    {
        // If locked, check key
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

        // If not locked, toggle
        IsOpen.Value = !IsOpen.Value;
    }

    public string GetInteractText()
    {
        if (IsLocked.Value) return $"Kapi Kilitli ({requiredKeyName} Gerekli)";
        return IsOpen.Value ? "Kapıyı Kapat" : "Kapıyı Aç";
    }
}
