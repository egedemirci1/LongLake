using UnityEngine;
using Unity.Netcode;

public class ItemPickUp : NetworkBehaviour, IInteractable
{
    public ItemData itemToGive;

    public void Interact(InventoryManager interactorInventory)
    {
        if (interactorInventory == null)
        {
            Debug.LogError("<color=red>[ItemPickUp]</color> interactorInventory NULL (Player'da InventoryManager yok).");
            return;
        }

        if (itemToGive == null)
        {
            Debug.LogError("<color=red>[ItemPickUp]</color> itemToGive NULL (Inspector'da ItemData atanmalý).");
            return;
        }

        interactorInventory.AddItem(itemToGive);
        RequestPickUpServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPickUpServerRpc()
    {
        var no = GetComponent<NetworkObject>();
        if (no != null && no.IsSpawned)
            no.Despawn();

        Destroy(gameObject);
    }

    public string GetInteractText() => itemToGive != null ? itemToGive.itemName : "Eþya";
}
