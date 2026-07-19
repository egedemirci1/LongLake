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
    private void BindUIRuntime()
    {
        // 1) Find hotbar root
        GameObject hotbarGO = GameObject.Find(HotbarPath);

        if (hotbarGO == null)
        {
            // Alternative: find by "Hotbar" name only (worst case)
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var tr in all)
            {
                if (tr != null && tr.name == "Hotbar")
                {
                    hotbarGO = tr.gameObject;
                    break;
                }
            }
        }

        if (hotbarGO == null)
        {
            // UI might not be loaded yet
            return;
        }

        hotbarObject = hotbarGO;

        // 2) Find main inventory (optional - eğer yoksa null kalır)
        GameObject mainInvGO = GameObject.Find(MainInventoryPath);
        if (mainInvGO == null)
        {
            // Alternative: find by "MainInventory" or "InventoryPanel" name
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var tr in all)
            {
                if (tr != null && (tr.name == "MainInventory" || tr.name == "InventoryPanel"))
                {
                    mainInvGO = tr.gameObject;
                    break;
                }
            }
        }
        mainInventoryObject = mainInvGO;

        // 3) Find slots in order: Slot_1..Slot_5
        nameTexts = new TextMeshProUGUI[HOTBAR_SIZE];
        iconImages = new Image[HOTBAR_SIZE];
        slotFrames = new Image[HOTBAR_SIZE]; // Slot frame'leri için

        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            string slotName = $"Slot_{i + 1}";
            
            // Önce direkt child olarak ara
            Transform slot = hotbarObject.transform.Find(slotName);
            
            // Bulunamazsa nested Hotbar içinde ara
            if (slot == null && hotbarObject.transform.childCount > 0)
            {
                Transform nestedHotbar = hotbarObject.transform.GetChild(0);
                if (nestedHotbar != null && nestedHotbar.name == "Hotbar")
                {
                    slot = nestedHotbar.Find(slotName);
                }
            }

            if (slot == null)
            {
                Debug.LogError($"[Inventory] '{slotName}' not found in Hotbar!");
                continue;
            }

            // Find child objects - önce ItemName_Text dene (hotbar'da bu isim kullanılıyor), sonra ItemName_Tex, sonra ItemName
            Transform nameT = slot.Find("ItemName_Text");
            if (nameT == null)
            {
                nameT = slot.Find("ItemName_Tex");
            }
            if (nameT == null)
            {
                nameT = slot.Find("ItemName");
            }
            
            // Eğer hala bulunamazsa, tüm child'ları kontrol et (TextMeshProUGUI component'i olan)
            if (nameT == null)
            {
                for (int j = 0; j < slot.childCount; j++)
                {
                    Transform child = slot.GetChild(j);
                    if (child.GetComponent<TextMeshProUGUI>() != null)
                    {
                        nameT = child;
                        break;
                    }
                }
            }
            
            Transform iconT = slot.Find("ItemIcon");
            
            // Slot itself has Image component (frame/background - always visible)
            // ItemIcon child has Image component (item icon - only visible when item exists)
            // ItemName should be positioned inside ItemIcon (overlay text)

            if (nameT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemName not found!");

            if (iconT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemIcon not found!");

            nameTexts[i] = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
            iconImages[i] = iconT != null ? iconT.GetComponent<Image>() : null;

            if (iconT != null && iconImages[i] == null)
            {
                iconImages[i] = iconT.gameObject.AddComponent<Image>();
            }

            // Slot frame'ini al
            slotFrames[i] = slot.GetComponent<Image>();
            // Initialize text and icon (will be updated by UpdateUI)
            if (nameTexts[i] != null) nameTexts[i].text = "";
            if (iconImages[i] != null)
            {
                iconImages[i].sprite = null;
                iconImages[i].color = new Color(1, 1, 1, 0); // icon hidden (transparent)
            }
            
            // Ensure Slot's Image component (frame/background) is always enabled and visible
            if (slotFrames[i] != null)
            {
                slotFrames[i].enabled = true; // Always keep frame/background visible
                slotFrames[i].color = FrameEmptyColor;
            }
        }

        // Görünürlük kuralı: hotbar sadece çanta alındıysa görünür.
        // (Bind ne zaman gerçekleşirse gerçekleşsin sahnedeki başlangıç durumuna güvenme.)
        hotbarObject.SetActive(HasItem("backpack"));

        // 4) Find main inventory grid slots
        if (mainInventoryObject != null)
        {
            BindMainInventoryUI();
            BindCraftingUI();
        }
        
        // Update UI after binding to show current items
        UpdateUI();
    }

    private void BindMainInventoryUI()
    {
        // Ana envanter için UI binding (InventoryGrid içindeki slot'lar)
        inventorySlotIcons = new Image[MAX_INVENTORY_SIZE];
        inventorySlotNames = new TextMeshProUGUI[MAX_INVENTORY_SIZE];
        inventorySlotFrames = new Image[MAX_INVENTORY_SIZE];

        // InventoryGrid'i bul - Path: MainInventory > InventoryPanel > InventoryGrid
        Transform inventoryGrid = mainInventoryObject.transform.Find("InventoryPanel/InventoryGrid");
        if (inventoryGrid == null)
        {
            inventoryGrid = mainInventoryObject.transform.Find("Inventory/InventoryGrid");
        }
        if (inventoryGrid == null)
        {
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var tr in all)
            {
                if (tr != null && tr.name == "InventoryGrid")
                {
                    inventoryGrid = tr;
                    break;
                }
            }
        }

        if (inventoryGrid == null)
        {
            return;
        }

        // InventoryGrid içindeki slot'ları bul
        for (int i = 0; i < MAX_INVENTORY_SIZE; i++)
        {
            string slotName = $"InvSlot_{i + 1}";
            Transform slot = inventoryGrid.Find(slotName);

            // Eğer InvSlot_X bulunamazsa, child index ile dene (Image (0), Image (1), ...)
            if (slot == null && i < inventoryGrid.childCount)
            {
                slot = inventoryGrid.GetChild(i);
            }

            if (slot != null)
            {
                // ItemIcon child'ını bul
                Transform iconT = slot.Find("ItemIcon");
                if (iconT == null)
                {
                    // Tüm child'ları kontrol et - Image component'i olan ilk child'ı kullan (ItemName değilse)
                    for (int j = 0; j < slot.childCount; j++)
                    {
                        Transform child = slot.GetChild(j);
                        Image childImage = child.GetComponent<Image>();
                        if (childImage != null && 
                            !child.name.Contains("ItemName") && 
                            !child.name.Contains("Text") &&
                            !child.name.Contains("Frame") &&
                            !child.name.Contains("Border"))
                        {
                            iconT = child;
                            break;
                        }
                    }
                }
                
                // Eğer hala bulunamazsa, slot içinde Image component'i olan bir child oluştur
                if (iconT == null)
                {
                    GameObject iconGO = new GameObject("ItemIcon");
                    iconGO.transform.SetParent(slot, false);
                    RectTransform iconRect = iconGO.AddComponent<RectTransform>();
                    iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                    iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                    iconRect.pivot = new Vector2(0.5f, 0.5f);
                    iconRect.sizeDelta = new Vector2(50, 50);
                    iconRect.anchoredPosition = Vector2.zero;
                    iconT = iconGO.transform;
                    Image iconImage = iconGO.AddComponent<Image>();
                    iconImage.raycastTarget = false; // Drag-drop için raycast'i kapat
                }

                // ItemName child'ını bul (önce ItemName_Text, sonra ItemName_Tex, sonra ItemName)
                Transform nameT = slot.Find("ItemName_Text");
                if (nameT == null)
                {
                    nameT = slot.Find("ItemName_Tex");
                }
                if (nameT == null)
                {
                    nameT = slot.Find("ItemName");
                }
                
                if (nameT == null)
                {
                    for (int j = 0; j < slot.childCount; j++)
                    {
                        Transform child = slot.GetChild(j);
                        if (child.GetComponent<TextMeshProUGUI>() != null)
                        {
                            nameT = child;
                            break;
                        }
                    }
                }

                inventorySlotIcons[i] = iconT != null ? iconT.GetComponent<Image>() : null;
                inventorySlotNames[i] = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;

                if (inventorySlotIcons[i] != null)
                {
                    // Icon'u başlangıçta hazırla
                    inventorySlotIcons[i].enabled = true;
                    if (inventorySlotIcons[i].gameObject != null)
                    {
                        inventorySlotIcons[i].gameObject.SetActive(true);
                    }
                }
                
                // Slot frame'ini al (boş slot görseli için)
                // Önce slot'un kendisinde Image ara
                inventorySlotFrames[i] = slot.GetComponent<Image>();
                
                // Bulunamazsa child'larda "Frame" veya "Background" ara
                if (inventorySlotFrames[i] == null)
                {
                    Transform frameT = slot.Find("Frame") ?? slot.Find("Background") ?? slot.Find("SlotFrame");
                    if (frameT != null)
                    {
                        inventorySlotFrames[i] = frameT.GetComponent<Image>();
                    }
                }
                
                // Hala bulunamazsa, slot'un kendisinde Image oluştur
                if (inventorySlotFrames[i] == null)
                {
                    Image frameImage = slot.gameObject.GetComponent<Image>();
                    if (frameImage == null)
                    {
                        frameImage = slot.gameObject.AddComponent<Image>();
                        frameImage.color = FrameEmptyColor;
                    }
                    inventorySlotFrames[i] = frameImage;
                }
                
                // Frame'i her zaman görünür tut (hotbar'daki gibi)
                if (inventorySlotFrames[i] != null)
                {
                    inventorySlotFrames[i].enabled = true;
                    inventorySlotFrames[i].color = FrameEmptyColor;
                    if (inventorySlotFrames[i].gameObject != null)
                    {
                        inventorySlotFrames[i].gameObject.SetActive(true);
                    }
                }
                
                // Drag & Drop handler'ları ekle (eğer yoksa)
                InventorySlotDragHandler dragHandler = slot.GetComponent<InventorySlotDragHandler>();
                if (dragHandler == null)
                {
                    dragHandler = slot.gameObject.AddComponent<InventorySlotDragHandler>();
                }
                dragHandler.isHotbarSlot = false;
                dragHandler.slotIndex = i;
                
                InventorySlotDropHandler dropHandler = slot.GetComponent<InventorySlotDropHandler>();
                if (dropHandler == null)
                {
                    dropHandler = slot.gameObject.AddComponent<InventorySlotDropHandler>();
                }
                dropHandler.isHotbarSlot = false;
                dropHandler.slotIndex = i;
                
            }
        }

    }

}
