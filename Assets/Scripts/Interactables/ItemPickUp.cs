using UnityEngine;
using Unity.Netcode;

public class ItemPickUp : NetworkBehaviour, IInteractable
{
    public ItemData itemToGive;

    // Server-authoritative: tracks if item has been picked up
    private NetworkVariable<bool> isPickedUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (isPickedUp.Value)
            SetVisibility(false);

        isPickedUp.OnValueChanged += OnPickedUpChanged;
    }

    public override void OnNetworkDespawn()
    {
        isPickedUp.OnValueChanged -= OnPickedUpChanged;
        base.OnNetworkDespawn();
    }

    private void OnPickedUpChanged(bool oldValue, bool newValue)
    {
        if (newValue)
            SetVisibility(false);
    }

    private void SetVisibility(bool visible)
    {
        var collider = GetComponent<Collider>();
        if (collider != null)
            collider.enabled = visible;

        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    public void Interact(InventoryManager interactorInventory)
    {
        if (interactorInventory == null)
        {
            Debug.LogError("<color=red>[ItemPickUp]</color> interactorInventory NULL (Player needs InventoryManager).");
            return;
        }

        if (itemToGive == null)
        {
            Debug.LogError("<color=red>[ItemPickUp]</color> itemToGive NULL (ItemData must be assigned in Inspector).");
            return;
        }

        if (isPickedUp.Value)
            return;

        if (IsUniqueItemBlocked(interactorInventory))
            return;

        RequestPickUpServerRpc(interactorInventory.OwnerClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPickUpServerRpc(ulong clientId)
    {
        if (isPickedUp.Value)
        {
            Debug.LogWarning($"[ItemPickUp] Client {clientId} tried to pick up item but it's already taken. Denied.");
            return;
        }

        if (itemToGive != null)
        {
            var playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject != null)
            {
                var inventoryManager = playerObject.GetComponentInChildren<InventoryManager>();
                if (inventoryManager != null && IsUniqueItemBlocked(inventoryManager))
                {
                    Debug.LogWarning($"[ItemPickUp] Client {clientId} already has '{itemToGive.itemID}'. Request denied.");
                    return;
                }
            }
        }

        isPickedUp.Value = true;

        // Shared quest progress (crash-site bags count toward opening quest)
        if (itemToGive != null && QuestManager.Instance != null)
            QuestManager.Instance.NotifyQuestItemCollectedServer(itemToGive.itemID);

        AddItemToClientClientRpc(clientId);

        var no = GetComponent<NetworkObject>();
        if (no != null && no.IsSpawned)
            no.Despawn();
        else
            Destroy(gameObject);
    }

    [ClientRpc]
    private void AddItemToClientClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId)
            return;

        var inventoryManager = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject()
            ?.GetComponentInChildren<InventoryManager>();

        if (inventoryManager == null)
        {
            Debug.LogError("<color=red>[ItemPickUp]</color> InventoryManager not found on local player!");
            return;
        }

        inventoryManager.AddItem(itemToGive);
        GameAudio.PlayPickup();
        Debug.Log($"<color=green>[ItemPickUp]</color> Item '{itemToGive.itemName}' added to inventory.");
    }

    public string GetInteractText()
    {
        if (isPickedUp.Value)
            return string.Empty;

        if (itemToGive == null)
            return "Item";

        // Hide prompt if local player already carries this unique item
        var localInventory = GetLocalInventory();
        if (localInventory != null && IsUniqueItemBlocked(localInventory))
            return string.Empty;

        return itemToGive.itemName;
    }

    /// <summary>
    /// Notebook and backpack are one-per-player. Having one blocks picking another.
    /// </summary>
    private bool IsUniqueItemBlocked(InventoryManager inventory)
    {
        if (inventory == null || itemToGive == null)
            return false;

        string id = itemToGive.itemID;
        if (id == "notebook" && inventory.HasItem("notebook"))
            return true;
        if (id == "backpack" && inventory.HasItem("backpack"))
            return true;

        return false;
    }

    private static InventoryManager GetLocalInventory()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
            return null;

        var localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        return localPlayer != null ? localPlayer.GetComponentInChildren<InventoryManager>(true) : null;
    }
}
