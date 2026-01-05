using UnityEngine;
using Unity.Netcode;

public class DoorController : NetworkBehaviour, IInteractable
{
    [Header("Kap� Durumu (Ba�lang�� De�erleri)")]
    [SerializeField] private bool startOpen = false;
    [SerializeField] private bool startLocked = false;
    public string requiredKeyName = "MutfakAnahtari";

    [Header("Ayarlar")]
    public float openRotation = 90.0f;
    public float closeRotation = 0.0f;
    public float smooth = 5.0f;

    // Network �zerinden senkronlanan durumlar
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
        // �stersen burada local init b�rakabilirsin; as�l init server'da yap�lacak.
    }

    public override void OnNetworkSpawn()
    {
        // �lk durumlar� sadece server set etmeli
        if (IsServer)
        {
            IsOpen.Value = startOpen;
            IsLocked.Value = startLocked;
        }
    }

    private void Update()
    {
        // Her client kap�n�n g�ncel (network) durumuna g�re animasyonu oynat�r
        float targetAngle = IsOpen.Value ? openRotation : closeRotation;
        Quaternion target = Quaternion.Euler(0, 0, targetAngle);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, Time.deltaTime * smooth);
    }

    // IInteractable
    public void Interact(InventoryManager interactorInventory)
    {
        // Client taraf�nda input geldi�inde server'a istek at
        // (Host ise hem client hem server olaca�� i�in yine g�venli �al���r)
        if (!IsSpawned) return;

        bool hasKey = (interactorInventory != null && interactorInventory.HasKey(requiredKeyName));
        TryToggleDoorServerRpc(hasKey);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryToggleDoorServerRpc(bool clientClaimsHasKey)
    {
        // Kilitliyse, anahtar yoksa a�ma
        if (IsLocked.Value)
        {
            if (clientClaimsHasKey)
            {
                IsLocked.Value = false;
                IsOpen.Value = true;
                Debug.Log($"<b>[KAPI]</b> {requiredKeyName} kullan�ld�, kap� a��ld�! (Server)");
            }
            else
            {
                Debug.Log($"<b>[KAPI]</b> Kilitli! Gereken anahtar: {requiredKeyName} (Server)");
            }
            return;
        }

        // Kilitli de�ilse toggle
        IsOpen.Value = !IsOpen.Value;
    }

    public string GetInteractText()
    {
        if (IsLocked.Value) return $"KAPI K�L�TL� ({requiredKeyName} GEREKL�)";
        return IsOpen.Value ? "KAPIYI KAPAT" : "KAPIYI A�";
    }
}
