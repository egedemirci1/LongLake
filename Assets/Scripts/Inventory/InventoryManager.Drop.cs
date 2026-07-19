using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using System;

// Partial: split for maintainability. Type identity unchanged.
public partial class InventoryManager : NetworkBehaviour
{
    /// <summary>
    /// Item'ı yere atar (multiplayer için Server RPC)
    /// </summary>
    public void DropItemToGround(ItemData item, bool isHotbarSlot, int slotIndex)
    {
        if (!IsOwner) return;

        // Sırt çantası oyunun ilerleme anahtarı (hotbar/envanter erişimi ona bağlı) — yere atılamaz.
        if (item != null && item.itemID == "backpack")
            return;

        GameAudio.PlayDrop();

        // Server'a istek gönder
        DropItemToGroundServerRpc(item.itemID, isHotbarSlot, slotIndex);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DropItemToGroundServerRpc(string itemID, bool isHotbarSlot, int slotIndex)
    {
        // Sadece owner bu RPC'yi çağırabilir (güvenlik kontrolü)
        if (!IsOwner)
        {
            return;
        }
        
        // ItemData'yı bul
        ItemData item = null;
        int quantityToDrop = 1;
        
        if (isHotbarSlot)
        {
            if (slotIndex >= 0 && slotIndex < hotbarSlots.Length)
            {
                int inventoryIndex = hotbarSlots[slotIndex];
                if (inventoryIndex >= 0 && inventoryIndex < items.Count && 
                    items[inventoryIndex] != null && !items[inventoryIndex].IsEmpty)
                {
                    item = items[inventoryIndex].item;
                    quantityToDrop = items[inventoryIndex].quantity;
                }
            }
        }
        else
        {
            if (slotIndex >= 0 && slotIndex < items.Count && 
                items[slotIndex] != null && !items[slotIndex].IsEmpty)
            {
                item = items[slotIndex].item;
                quantityToDrop = items[slotIndex].quantity;
            }
        }
        
        if (item == null || item.itemID != itemID)
        {
            return;
        }

        // Sırt çantası atılamaz (client tarafı da engelliyor; RPC sınırında ikinci kontrol)
        if (item.itemID == "backpack")
        {
            return;
        }
        
        // Item'ı envanterden kaldır
        if (isHotbarSlot)
        {
            RemoveItemFromHotbar(slotIndex);
        }
        else
        {
            RemoveItemFromInventory(slotIndex);
        }
        
        // Yere spawn et
        SpawnItemPickUpOnGround(item, quantityToDrop, OwnerClientId);
    }

    private void SpawnItemPickUpOnGround(ItemData item, int quantity, ulong ownerClientId)
    {
        // ItemData'da worldPrefab var mı kontrol et
        GameObject prefabToSpawn = item.worldPrefab;
        
        // Eğer yoksa fallback kullan
        if (prefabToSpawn == null)
        {
            prefabToSpawn = fallbackItemPickUpPrefab;
            if (prefabToSpawn == null)
            {
                Debug.LogError($"[Inventory] Cannot drop '{item.itemName}': worldPrefab is not assigned in ItemData and fallbackItemPickUpPrefab is not set!");
                return;
            }
        }
        
        // Player pozisyonunu ve kamera yönünü al
        Vector3 spawnPosition = GetDropPosition(ownerClientId);
        
        // Prefab'ı instantiate et
        GameObject itemObj = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);
        
        // ItemPickUp component'ini bul ve ItemData'yı ata
        ItemPickUp itemPickUp = itemObj.GetComponent<ItemPickUp>();
        if (itemPickUp == null)
        {
            Debug.LogError($"[Inventory] ItemPickUp component not found on prefab '{prefabToSpawn.name}'!");
            Destroy(itemObj);
            return;
        }
        
        itemPickUp.itemToGive = item;
        
        // NetworkObject olarak spawn et
        NetworkObject netObj = itemObj.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
        else
        {
            Debug.LogError($"[Inventory] NetworkObject component not found on ItemPickUp prefab '{prefabToSpawn.name}'!");
            Destroy(itemObj);
            return;
        }
        
    }

    private Vector3 GetDropPosition(ulong clientId)
    {
        // Local player'ı bul
        NetworkObject playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
        if (playerObj == null)
        {
            return Vector3.zero;
        }
        
        // Camera'yı bul
        Camera cam = playerObj.GetComponentInChildren<Camera>();
        if (cam == null)
        {
            cam = Camera.main;
        }
        
        if (cam == null)
        {
            return playerObj.transform.position + playerObj.transform.forward * 2f;
        }
        
        // Camera'dan ileriye raycast yap
        Vector3 rayOrigin = cam.transform.position;
        Vector3 rayDirection = cam.transform.forward;
        float dropDistance = 2f; // 2 metre ileriye at
        
        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, rayDirection, out hit, dropDistance + 1f))
        {
            // Yere çarptıysa, çarptığı noktanın biraz üstüne koy
            return hit.point + Vector3.up * 0.1f;
        }
        else
        {
            // Çarpmadıysa, belirli mesafe ileriye koy
            return rayOrigin + rayDirection * dropDistance;
        }
    }

}
