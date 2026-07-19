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
    private void SetMainInventoryOpen(bool open)
    {
        if (mainInventoryObject == null)
            return;

        mainInventoryObject.SetActive(open);
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        if (open)
            GameAudio.PlayInvOpen();
        else
            GameAudio.PlayInvClose();
    }

    private void HandleSlotSelection()
    {
        // Alpha1 = 1, Alpha2 = 2, vb.
        for (int i = 0; i < 5; i++)
        {
            KeyCode key = KeyCode.Alpha1 + i;
            if (Input.GetKeyDown(key))
            {
                SelectSlot(i);
                break;
            }
        }
    }

    public void SelectSlot(int slotIndex)
    {
        // Slot geçerli mi kontrol et (hotbar slot index)
        if (slotIndex < 0 || slotIndex >= HOTBAR_SIZE)
        {
            return;
        }
        
        int oldSelected = selectedSlotIndex;
        
        // Hotbar slot'unda item var mı kontrol et
        int inventoryIndex = hotbarSlots[slotIndex];
        bool hasItem = inventoryIndex >= 0 && inventoryIndex < items.Count && items[inventoryIndex] != null && !items[inventoryIndex].IsEmpty;
        
        if (hasItem)
        {
            ItemData item = items[inventoryIndex].item;
            
            // Aynı slot'a tekrar basılırsa seçimi kaldır (toggle)
            if (selectedSlotIndex == slotIndex)
            {
                selectedSlotIndex = -1;
                OnItemDeselected?.Invoke();
            }
            else
            {
                // Farklı bir slot seçildi - her zaman seç (toggle değil, direkt seç)
                // Önceki seçimi kaldır (eğer varsa)
                if (oldSelected >= 0 && oldSelected != slotIndex)
                {
                    OnItemDeselected?.Invoke();
                }
                
                selectedSlotIndex = slotIndex;
                OnItemSelected?.Invoke(item, slotIndex);
            }
        }
        else
        {
            // Boş slot seçildi, seçimi kaldır
            if (selectedSlotIndex >= 0)
            {
                selectedSlotIndex = -1;
                OnItemDeselected?.Invoke();
            }
        }
        
        // UI'ı her zaman güncelle
        UpdateUI();
    }

    private void UpdateUI()
    {
        // Don't update if UI not bound
        if (nameTexts == null || iconImages == null || nameTexts.Length == 0 || iconImages.Length == 0)
        {
            return;
        }

        if (hotbarObject == null)
        {
            Debug.LogError("[Inventory] UpdateUI: hotbarObject is NULL!");
            return;
        }

        // Update Hotbar UI (5 slots)
        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            int inventoryIndex = hotbarSlots[i]; // Ana envanterdeki item index'i
            bool isSelected = selectedSlotIndex == i;

            // Hotbar slot'unda item var mı?
            if (inventoryIndex >= 0 && inventoryIndex < items.Count && items[inventoryIndex] != null && !items[inventoryIndex].IsEmpty)
            {
                InventorySlot slot = items[inventoryIndex];
                ItemData item = slot.item;
                
                if (nameTexts[i] != null)
                {
                    // Miktarı sadece 1'den fazlaysa göster
                    nameTexts[i].text = slot.quantity > 1 ? $"{item.itemName} x{slot.quantity}" : item.itemName;
                }

                if (iconImages[i] != null && item.itemIcon != null)
                {
                    iconImages[i].sprite = item.itemIcon;
                    iconImages[i].color = Color.white; // visible
                    iconImages[i].enabled = true;

                    if (iconImages[i].gameObject != null)
                    {
                        iconImages[i].gameObject.SetActive(true);
                    }
                }

                if (slotFrames[i] != null)
                    slotFrames[i].color = isSelected ? FrameSelectedColor : FrameFilledColor;
            }
            else
            {
                // Boş hotbar slot: yazı yok, ikon gizli, çerçeve soluk
                if (nameTexts[i] != null) nameTexts[i].text = "";
                if (iconImages[i] != null)
                {
                    iconImages[i].sprite = null;
                    iconImages[i].color = new Color(1, 1, 1, 0);
                }

                if (slotFrames[i] != null)
                    slotFrames[i].color = FrameEmptyColor;
            }
        }

        // Update Main Inventory UI (12 slots) if bound
        if (inventorySlotIcons != null && inventorySlotNames != null && inventorySlotFrames != null &&
            inventorySlotIcons.Length == MAX_INVENTORY_SIZE && inventorySlotNames.Length == MAX_INVENTORY_SIZE && inventorySlotFrames.Length == MAX_INVENTORY_SIZE)
        {
            for (int i = 0; i < MAX_INVENTORY_SIZE; i++)
            {
                if (i < items.Count && items[i] != null && !items[i].IsEmpty)
                {
                    InventorySlot slot = items[i];
                    ItemData item = slot.item;
                    
                    if (inventorySlotNames[i] != null)
                    {
                        // Miktarı sadece 1'den fazlaysa göster
                        inventorySlotNames[i].text = slot.quantity > 1 ? $"{item.itemName} x{slot.quantity}" : item.itemName;
                    }
                    
                    if (inventorySlotIcons[i] != null)
                    {
                        if (item.itemIcon != null)
                        {
                            inventorySlotIcons[i].sprite = item.itemIcon;
                            inventorySlotIcons[i].color = Color.white;
                            inventorySlotIcons[i].enabled = true;
                            
                            // GameObject'i aktif et (parent'ları aktif etme - envanter açılmasın)
                            if (inventorySlotIcons[i].gameObject != null)
                            {
                                inventorySlotIcons[i].gameObject.SetActive(true);
                            }
                        }
                    }
                    if (inventorySlotFrames[i] != null)
                    {
                        inventorySlotFrames[i].enabled = true;
                        inventorySlotFrames[i].color = FrameFilledColor;
                    }
                }
                else
                {
                    // Boş inventory slot: yazı yok, ikon gizli, çerçeve soluk
                    if (inventorySlotNames[i] != null) inventorySlotNames[i].text = "";
                    if (inventorySlotIcons[i] != null)
                    {
                        inventorySlotIcons[i].sprite = null;
                        inventorySlotIcons[i].color = new Color(1, 1, 1, 0); // Icon gizli
                        inventorySlotIcons[i].enabled = false;
                        if (inventorySlotIcons[i].gameObject != null)
                        {
                            inventorySlotIcons[i].gameObject.SetActive(false);
                        }
                    }
                    
                    if (inventorySlotFrames[i] != null)
                    {
                        inventorySlotFrames[i].enabled = true;
                        inventorySlotFrames[i].color = FrameEmptyColor;
                        if (inventorySlotFrames[i].gameObject != null)
                        {
                            inventorySlotFrames[i].gameObject.SetActive(true);
                        }
                    }
                }
            }
        }
        // Update Crafting UI
        UpdateCraftingUI();
    }

}
