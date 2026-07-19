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
    // Crafting Methods
    public void AddItemToCraftingInput(int inputSlotIndex, ItemData item, bool fromHotbar, int sourceSlotIndex)
    {
        if (!IsOwner) return;
        if (inputSlotIndex < 0 || inputSlotIndex >= 3) return;
        if (item == null || !item.isMaterial) return;

        // Aynı item zaten bu craft slot'unda varsa, hiçbir şey yapma (duplicate önleme)
        if (craftingInputItems[inputSlotIndex] != null && 
            craftingInputItems[inputSlotIndex].itemID == item.itemID)
        {
            UpdateUI();
            return;
        }

        // Eğer input slot doluysa, eski item'ı source'a geri koy
        if (craftingInputItems[inputSlotIndex] != null)
        {
            ItemData oldItem = craftingInputItems[inputSlotIndex];
            
            // Eski item'ı source'a geri koy
            if (fromHotbar)
            {
                if (sourceSlotIndex >= 0 && sourceSlotIndex < HOTBAR_SIZE)
                {
                    int inventoryIndex = hotbarSlots[sourceSlotIndex];
                    if (inventoryIndex >= 0 && inventoryIndex < items.Count)
                    {
                        if (inventoryIndex != -1 && !items[inventoryIndex].IsEmpty)
                        {
                            if (items[inventoryIndex].CanStack(oldItem))
                            {
                                items[inventoryIndex].quantity++;
                            }
                            else
                            {
                                int emptySlot = items.FindIndex(s => s.IsEmpty);
                                if (emptySlot == -1 && items.Count < MAX_INVENTORY_SIZE)
                                {
                                    emptySlot = items.Count;
                                    items.Add(new InventorySlot(oldItem, 1));
                                }
                                else if (emptySlot != -1)
                                {
                                    items[emptySlot] = new InventorySlot(oldItem, 1);
                                }
                                UpdateHotbarReferencesForNewItem(emptySlot);
                            }
                        }
                        else
                        {
                            items[inventoryIndex] = new InventorySlot(oldItem, 1);
                        }
                    }
                }
            }
            else
            {
                if (sourceSlotIndex >= 0 && sourceSlotIndex < items.Count)
                {
                    if (items[sourceSlotIndex].CanStack(oldItem))
                    {
                        items[sourceSlotIndex].quantity++;
                    }
                    else
                    {
                        int emptySlot = items.FindIndex(s => s.IsEmpty);
                        if (emptySlot == -1 && items.Count < MAX_INVENTORY_SIZE)
                        {
                            emptySlot = items.Count;
                            items.Add(new InventorySlot(oldItem, 1));
                        }
                        else if (emptySlot != -1)
                        {
                            items[emptySlot] = new InventorySlot(oldItem, 1);
                        }
                        UpdateHotbarReferencesForNewItem(emptySlot);
                    }
                }
            }
        }
        
        // Yeni item'ı crafting input'a ekle (sadece referans)
        craftingInputItems[inputSlotIndex] = item;
        
        // ÖNEMLİ: Item'ı envanterden kaldır (1 adet)
        if (fromHotbar)
        {
            if (sourceSlotIndex >= 0 && sourceSlotIndex < HOTBAR_SIZE)
            {
                int inventoryIndex = hotbarSlots[sourceSlotIndex];
                if (inventoryIndex >= 0 && inventoryIndex < items.Count && 
                    items[inventoryIndex] != null && !items[inventoryIndex].IsEmpty)
                {
                    if (items[inventoryIndex].quantity > 1)
                    {
                        // Quantity'den 1 azalt
                        items[inventoryIndex].quantity--;
                    }
                    else
                    {
                        // Son item, slot'u tamamen temizle
                        items.RemoveAt(inventoryIndex);
                        
                        // Hotbar referanslarını güncelle
                        for (int j = 0; j < HOTBAR_SIZE; j++)
                        {
                            if (hotbarSlots[j] == inventoryIndex)
                            {
                                hotbarSlots[j] = -1;
                            }
                            else if (hotbarSlots[j] > inventoryIndex)
                            {
                                hotbarSlots[j]--;
                            }
                        }
                        
                    }
                }
            }
        }
        else
        {
            // Ana envanterden kaldır
            if (sourceSlotIndex >= 0 && sourceSlotIndex < items.Count && 
                items[sourceSlotIndex] != null && !items[sourceSlotIndex].IsEmpty)
            {
                if (items[sourceSlotIndex].quantity > 1)
                {
                    // Quantity'den 1 azalt
                    items[sourceSlotIndex].quantity--;
                }
                else
                {
                    // Son item, slot'u tamamen temizle
                    items.RemoveAt(sourceSlotIndex);
                    
                    // Hotbar referanslarını güncelle
                    for (int j = 0; j < HOTBAR_SIZE; j++)
                    {
                        if (hotbarSlots[j] == sourceSlotIndex)
                        {
                            hotbarSlots[j] = -1;
                        }
                        else if (hotbarSlots[j] > sourceSlotIndex)
                        {
                            hotbarSlots[j]--;
                        }
                    }
                    
                }
            }
        }
        
        UpdateUI();
        UpdateCraftingUI();
    }

    private void UpdateCraftingUI()
    {
        // Update crafting input slots
        if (craftingInputIcons != null && craftingInputNames != null && craftingInputIcons.Length == 3)
        {
            for (int i = 0; i < 3; i++)
            {
                bool hasInput = craftingInputItems[i] != null;

                // Çerçeve her zaman görünür; rengi dolu/boş durumuna göre
                if (craftingInputFrames != null && craftingInputFrames.Length > i && craftingInputFrames[i] != null)
                {
                    craftingInputFrames[i].enabled = true;
                    craftingInputFrames[i].color = hasInput ? FrameFilledColor : FrameEmptyColor;
                    if (craftingInputFrames[i].gameObject != null)
                    {
                        craftingInputFrames[i].gameObject.SetActive(true);
                    }
                }
                
                if (hasInput)
                {
                    ItemData item = craftingInputItems[i];
                    if (craftingInputNames[i] != null)
                        craftingInputNames[i].text = item.itemName;
                    if (craftingInputIcons[i] != null && item.itemIcon != null)
                    {
                        craftingInputIcons[i].sprite = item.itemIcon;
                        craftingInputIcons[i].color = Color.white;
                        craftingInputIcons[i].enabled = true;
                        if (craftingInputIcons[i].gameObject != null)
                        {
                            craftingInputIcons[i].gameObject.SetActive(true);
                        }
                    }
                }
                else
                {
                    // Boş crafting input slot
                    if (craftingInputNames[i] != null) craftingInputNames[i].text = "";
                    if (craftingInputIcons[i] != null)
                    {
                        craftingInputIcons[i].sprite = null;
                        craftingInputIcons[i].color = new Color(1, 1, 1, 0);
                        craftingInputIcons[i].enabled = false;
                        if (craftingInputIcons[i].gameObject != null)
                        {
                            craftingInputIcons[i].gameObject.SetActive(false);
                        }
                    }
                }
            }
        }
        
        // Update crafting output slot
        if (craftingOutputIcon != null && craftingOutputName != null)
        {
            bool hasOutput = craftingOutputItem != null;

            // Çerçeve her zaman görünür; rengi dolu/boş durumuna göre
            if (craftingOutputFrame != null)
            {
                craftingOutputFrame.enabled = true;
                craftingOutputFrame.color = hasOutput ? CraftOutputFilledColor : CraftOutputEmptyColor;
                if (craftingOutputFrame.gameObject != null)
                {
                    craftingOutputFrame.gameObject.SetActive(true);
                }

                Outline outline = craftingOutputFrame.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = hasOutput
                        ? CraftAccentColor
                        : new Color(CraftAccentColor.r, CraftAccentColor.g, CraftAccentColor.b, 0.72f);
                    outline.effectDistance = hasOutput ? new Vector2(3f, -3f) : new Vector2(2f, -2f);
                }
            }
            
            if (hasOutput)
            {
                if (craftingOutputName != null)
                    craftingOutputName.text = craftingOutputItem.itemName;
                if (craftingOutputIcon != null && craftingOutputItem.itemIcon != null)
                {
                    craftingOutputIcon.sprite = craftingOutputItem.itemIcon;
                    craftingOutputIcon.color = Color.white;
                    craftingOutputIcon.enabled = true;
                    if (craftingOutputIcon.gameObject != null)
                    {
                        craftingOutputIcon.gameObject.SetActive(true);
                    }
                }
            }
            else
            {
                // Boş crafting output slot
                if (craftingOutputName != null) craftingOutputName.text = "";
                if (craftingOutputIcon != null)
                {
                    craftingOutputIcon.sprite = null;
                    craftingOutputIcon.color = new Color(1, 1, 1, 0);
                    craftingOutputIcon.enabled = false;
                    if (craftingOutputIcon.gameObject != null)
                    {
                        craftingOutputIcon.gameObject.SetActive(false);
                    }
                }
            }
        }
    }

    // Craft iptal fonksiyonu - craft alanındaki tüm item'ları envantere geri ekler
    public void CancelCrafting()
    {
        if (!IsOwner) return;

        // Craft alanındaki tüm input item'ları envantere geri ekle
        for (int i = 0; i < 3; i++)
        {
            if (craftingInputItems[i] != null)
            {
                ItemData item = craftingInputItems[i];
                
                // Envanterde aynı item var mı kontrol et (stack edilebilir mi?)
                bool stacked = false;
                for (int j = 0; j < items.Count; j++)
                {
                    if (items[j] != null && !items[j].IsEmpty && items[j].item.itemID == item.itemID)
                    {
                        if (items[j].CanStack(item))
                        {
                            items[j].quantity++;
                            stacked = true;
                            break;
                        }
                    }
                }
                
                // Stack edilemediyse yeni slot'a ekle
                if (!stacked)
                {
                    if (items.Count < MAX_INVENTORY_SIZE)
                    {
                        items.Add(new InventorySlot(item, 1));
                        int addedIndex = items.Count - 1;
                        // Eğer hotbar'da boş slot varsa, yeni eklenen item'ı ilk boş hotbar slot'una ata
                        for (int k = 0; k < HOTBAR_SIZE; k++)
                        {
                            if (hotbarSlots[k] == -1)
                            {
                                hotbarSlots[k] = addedIndex;
                                break;
                            }
                        }
                    }
                }
                
                // Craft slot'unu temizle
                craftingInputItems[i] = null;
            }
        }
        
        // Output item'ı da temizle (eğer varsa)
        if (craftingOutputItem != null)
        {
            craftingOutputItem = null;
        }
        
        // UI'ı güncelle
        UpdateUI();
        UpdateCraftingUI();
    }

}
