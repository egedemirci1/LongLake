using UnityEngine;
using Unity.Netcode;

public class DoorController : NetworkBehaviour, IInteractable
{
    [Header("Kapý Durumu (Baþlangýç Deðerleri)")]
    [SerializeField] private bool startOpen = false;
    [SerializeField] private bool startLocked = false;
    public string requiredKeyName = "MutfakAnahtari";

    [Header("Ayarlar")]
    public float openRotation = 90.0f;
    public float closeRotation = 0.0f;
    public float smooth = 5.0f;

    // Network üzerinden senkronlanan durumlar
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
        // Ýstersen burada local init býrakabilirsin; asýl init server'da yapýlacak.
    }

    public override void OnNetworkSpawn()
    {
        // Ýlk durumlarý sadece server set etmeli
        if (IsServer)
        {
            IsOpen.Value = startOpen;
            IsLocked.Value = startLocked;
        }
    }

    private void Update()
    {
        // Her client kapýnýn güncel (network) durumuna göre animasyonu oynatýr
        float targetAngle = IsOpen.Value ? openRotation : closeRotation;
        Quaternion target = Quaternion.Euler(0, 0, targetAngle);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, Time.deltaTime * smooth);
    }

    // IInteractable
    public void Interact(InventoryManager interactorInventory)
    {
        // Client tarafýnda input geldiðinde server'a istek at
        // (Host ise hem client hem server olacaðý için yine güvenli çalýþýr)
        if (!IsSpawned) return;

        bool hasKey = (interactorInventory != null && interactorInventory.HasKey(requiredKeyName));
        TryToggleDoorServerRpc(hasKey);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TryToggleDoorServerRpc(bool clientClaimsHasKey)
    {
        // Kilitliyse, anahtar yoksa açma
        if (IsLocked.Value)
        {
            if (clientClaimsHasKey)
            {
                IsLocked.Value = false;
                IsOpen.Value = true;
                Debug.Log($"<b>[KAPI]</b> {requiredKeyName} kullanýldý, kapý açýldý! (Server)");
            }
            else
            {
                Debug.Log($"<b>[KAPI]</b> Kilitli! Gereken anahtar: {requiredKeyName} (Server)");
            }
            return;
        }

        // Kilitli deðilse toggle
        IsOpen.Value = !IsOpen.Value;
    }

    public string GetInteractText()
    {
        if (IsLocked.Value) return $"KAPI KÝLÝTLÝ ({requiredKeyName} GEREKLÝ)";
        return IsOpen.Value ? "KAPIYI KAPAT" : "KAPIYI AÇ";
    }
}
