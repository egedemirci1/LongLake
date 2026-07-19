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
    // Drag & Drop Methods
    public void SwapHotbarSlots(int slot1, int slot2)
    {
        if (!IsOwner) return;
        if (slot1 < 0 || slot1 >= HOTBAR_SIZE || slot2 < 0 || slot2 >= HOTBAR_SIZE) return;
        
        int temp = hotbarSlots[slot1];
        hotbarSlots[slot1] = hotbarSlots[slot2];
        hotbarSlots[slot2] = temp;
        
        UpdateUI();
    }

    public void SwapInventorySlots(int slot1, int slot2)
    {
        if (!IsOwner) return;
        if (slot1 < 0 || slot1 >= items.Count || slot2 < 0 || slot2 >= items.Count) return;
        
        InventorySlot temp = items[slot1];
        items[slot1] = items[slot2];
        items[slot2] = temp;
        
        // Hotbar referanslarını güncelle
        UpdateHotbarReferences(slot1, slot2);
        
        UpdateUI();
    }

    public void MoveItemFromInventoryToHotbar(int inventoryIndex, int hotbarIndex)
    {
        if (!IsOwner) return;
        if (inventoryIndex < 0 || inventoryIndex >= items.Count || items[inventoryIndex] == null || items[inventoryIndex].IsEmpty) 
        {
            return;
        }
        if (hotbarIndex < 0 || hotbarIndex >= HOTBAR_SIZE) 
        {
            return;
        }
        
        // Önce bu item'ın zaten hotbar'da başka bir slot'ta olup olmadığını kontrol et
        int existingHotbarSlot = -1;
        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            if (hotbarSlots[i] == inventoryIndex)
            {
                existingHotbarSlot = i;
                break;
            }
        }
        
        // Eğer item zaten hotbar'da bir slot'taysa, o slot'u temizle
        if (existingHotbarSlot >= 0)
        {
            hotbarSlots[existingHotbarSlot] = -1;
        }
        
        // Eğer hedef hotbar slot'u doluysa, swap yap
        if (hotbarSlots[hotbarIndex] >= 0 && hotbarSlots[hotbarIndex] != inventoryIndex)
        {
            int oldInventoryIndex = hotbarSlots[hotbarIndex];
            hotbarSlots[hotbarIndex] = inventoryIndex;
        }
        else
        {
            hotbarSlots[hotbarIndex] = inventoryIndex;
        }
        
        UpdateUI();
    }

    public void MoveItemFromHotbarToInventory(int hotbarIndex, int inventoryIndex)
    {
        if (!IsOwner) return;
        if (hotbarIndex < 0 || hotbarIndex >= HOTBAR_SIZE || hotbarSlots[hotbarIndex] < 0) 
        {
            return;
        }
        if (inventoryIndex < 0 || inventoryIndex >= MAX_INVENTORY_SIZE) 
        {
            return;
        }
        
        int itemInventoryIndex = hotbarSlots[hotbarIndex];
        
        // Eğer inventory slot mevcut ve doluysa, swap yap
        if (inventoryIndex < items.Count && items[inventoryIndex] != null && !items[inventoryIndex].IsEmpty)
        {
            int otherHotbarIndex = -1;
            for (int i = 0; i < HOTBAR_SIZE; i++)
            {
                if (hotbarSlots[i] == inventoryIndex)
                {
                    otherHotbarIndex = i;
                    break;
                }
            }
            
            if (otherHotbarIndex >= 0)
            {
                hotbarSlots[hotbarIndex] = inventoryIndex;
                hotbarSlots[otherHotbarIndex] = itemInventoryIndex;
            }
            else
            {
                hotbarSlots[hotbarIndex] = inventoryIndex;
            }
        }
        else
        {
            hotbarSlots[hotbarIndex] = -1;
        }
        
        UpdateUI();
    }

    private void UpdateHotbarReferences(int oldIndex, int newIndex)
    {
        // Hotbar referanslarını güncelle (inventory slot index değiştiğinde)
        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            if (hotbarSlots[i] == oldIndex)
            {
                hotbarSlots[i] = newIndex;
            }
            else if (hotbarSlots[i] == newIndex)
            {
                hotbarSlots[i] = oldIndex;
            }
        }
    }

    private void UpdateHotbarReferencesForNewItem(int newItemIndex)
    {
        // Yeni item eklendiğinde hotbar referanslarını güncelle (index kayması yok, sadece yeni item için)
        // Bu metod şimdilik boş, gerekirse eklenebilir
    }

    private void RemoveItemFromHotbar(int hotbarIndex)
    {
        if (hotbarIndex < 0 || hotbarIndex >= hotbarSlots.Length) return;
        
        int inventoryIndex = hotbarSlots[hotbarIndex];
        if (inventoryIndex < 0 || inventoryIndex >= items.Count) return;
        
        // Item'ı envanterden kaldır
        items[inventoryIndex] = new InventorySlot(null, 0);
        
        // Hotbar referansını temizle
        hotbarSlots[hotbarIndex] = -1;
        
        UpdateUI();
    }

    private void RemoveItemFromInventory(int inventoryIndex)
    {
        if (inventoryIndex < 0 || inventoryIndex >= items.Count) return;
        
        // Item'ı kaldır
        items[inventoryIndex] = new InventorySlot(null, 0);
        
        // Hotbar'dan bu referansı kaldır
        for (int i = 0; i < hotbarSlots.Length; i++)
        {
            if (hotbarSlots[i] == inventoryIndex)
            {
                hotbarSlots[i] = -1;
            }
        }
        
        UpdateUI();
    }

}
