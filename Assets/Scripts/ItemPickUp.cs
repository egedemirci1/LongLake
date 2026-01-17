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
        
        // Hide if already picked up (late join case)
        if (isPickedUp.Value)
        {
            SetVisibility(false);
        }

        // Update visibility when state changes
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
        {
            SetVisibility(false);
        }
    }

    private void SetVisibility(bool visible)
    {
        // Disable collider (prevents interaction)
        var collider = GetComponent<Collider>();
        if (collider != null)
            collider.enabled = visible;

        // Hide visually (optional)
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

        // Client-side validation: early exit if already picked up
        if (isPickedUp.Value)
        {
            Debug.LogWarning("<color=yellow>[ItemPickUp]</color> Item already picked up, request denied.");
            return;
        }

        // Özel kontrol: Notebook ve Backpack gibi tek item'lar için envanter kontrolü
        // Eğer envanterde zaten bu item varsa, alma işlemini engelle
        if (itemToGive.itemID == "notebook" && interactorInventory.HasItem("notebook"))
        {
            Debug.LogWarning("<color=yellow>[ItemPickUp]</color> You already have a notebook! You can only carry one notebook.");
            return;
        }
        
        if (itemToGive.itemID == "backpack" && interactorInventory.HasItem("backpack"))
        {
            Debug.LogWarning("<color=yellow>[ItemPickUp]</color> You already have a backpack! You can only carry one backpack.");
            return;
        }

        // Get client ID (interactorInventory's owner)
        ulong clientId = interactorInventory.OwnerClientId;
        
        // Send request to server (DON'T do client-side AddItem, server will handle it)
        RequestPickUpServerRpc(clientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPickUpServerRpc(ulong clientId)
    {
        // Server-side duplicate check
        if (isPickedUp.Value)
        {
            Debug.LogWarning($"[ItemPickUp] Client {clientId} tried to pick up item but it's already taken. Denied.");
            // Notify client of failure (optional, can silently deny)
            return;
        }

        // Server-side validation: Notebook ve Backpack kontrolü
        if (itemToGive != null)
        {
            var playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject != null)
            {
                var inventoryManager = playerObject.GetComponentInChildren<InventoryManager>();
                if (inventoryManager != null)
                {
                    // Notebook kontrolü
                    if (itemToGive.itemID == "notebook" && inventoryManager.HasItem("notebook"))
                    {
                        Debug.LogWarning($"[ItemPickUp] Client {clientId} already has a notebook. Request denied.");
                        return;
                    }
                    
                    // Backpack kontrolü
                    if (itemToGive.itemID == "backpack" && inventoryManager.HasItem("backpack"))
                    {
                        Debug.LogWarning($"[ItemPickUp] Client {clientId} already has a backpack. Request denied.");
                        return;
                    }
                }
            }
        }

        // Mark item (prevents other clients from taking it)
        isPickedUp.Value = true;

        // Tell client to add item
        AddItemToClientClientRpc(clientId);

        // Despawn item
        var no = GetComponent<NetworkObject>();
        if (no != null && no.IsSpawned)
        {
            no.Despawn();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    [ClientRpc]
    private void AddItemToClientClientRpc(ulong targetClientId)
    {
        // Only add item to target client
        if (NetworkManager.Singleton.LocalClientId != targetClientId)
            return;

        // Find InventoryManager (local player's)
        var inventoryManager = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject()
            ?.GetComponentInChildren<InventoryManager>();

        if (inventoryManager == null)
        {
            Debug.LogError("<color=red>[ItemPickUp]</color> InventoryManager not found on local player!");
            return;
        }

        // Add item
        inventoryManager.AddItem(itemToGive);
        Debug.Log($"<color=green>[ItemPickUp]</color> Item '{itemToGive.itemName}' added to inventory.");
    }

    public string GetInteractText()
    {
        if (isPickedUp.Value)
            return ""; // Don't show interaction text if already picked up
        
        return itemToGive != null ? itemToGive.itemName : "Item";
    }
}
