using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using System;

public class InventoryManager : NetworkBehaviour
{
    [Header("Player-specific UI (not assigned in prefab, bound at runtime)")]
    public GameObject hotbarObject;
    public GameObject mainInventoryObject; // Ana envanter paneli
    public TextMeshProUGUI[] nameTexts;
    public Image[] iconImages;
    public Image[] slotFrames; // Slot frame'leri

    public List<ItemData> items = new List<ItemData>();
    
    [Header("Selection")]
    [SerializeField] private int selectedSlotIndex = -1; // Seçili slot (-1 = hiçbiri seçili değil)
    
    // Seçili eşyaya erişim için property
    public ItemData SelectedItem => (selectedSlotIndex >= 0 && selectedSlotIndex < items.Count) ? items[selectedSlotIndex] : null;
    public int SelectedSlotIndex => selectedSlotIndex;
    public bool HasSelectedItem => SelectedItem != null;
    
    // Event: Seçim değiştiğinde çağrılır (ItemData, slotIndex)
    public event Action<ItemData, int> OnItemSelected;
    public event Action OnItemDeselected;

    // UI path in scene (according to your hierarchy)
    private const string HotbarPath = "InteractionUI/Envanter_Sistemi/Hotbar";
    private const string MainInventoryPath = "InteractionUI/Envanter_Sistemi/MainInventory"; // Ana envanter path'i (UI yapınıza göre değiştirin)

    public bool HasKey(string keyID)
    {
        return items.Exists(item => item != null && item.itemID == keyID);
    }

    // Item management methods
    public int CountItem(string itemID)
    {
        if (!IsOwner) return 0;
        return items.Count(item => item != null && item.itemID == itemID);
    }

    public bool HasItem(string itemID)
    {
        if (!IsOwner) return false;
        return items.Exists(item => item != null && item.itemID == itemID);
    }

    public void RemoveItem(string itemID, int quantity = 1)
    {
        if (!IsOwner) return;
        
        for (int i = 0; i < quantity; i++)
        {
            int index = items.FindIndex(item => item != null && item.itemID == itemID);
            if (index >= 0)
            {
                // Seçili item siliniyorsa seçimi kaldır
                if (selectedSlotIndex == index)
                {
                    selectedSlotIndex = -1;
                    OnItemDeselected?.Invoke();
                }
                else if (selectedSlotIndex > index)
                {
                    // Seçili slot'tan önceki bir item silindi, index'i düşür
                    selectedSlotIndex--;
                }
                
                items.RemoveAt(index);
                UpdateUI();
            }
            else
            {
                Debug.LogWarning($"[Inventory] Item {itemID} not found to remove!");
                break;
            }
        }
    }

    public void ReduceItemDurability(string itemID, int amount)
    {
        if (!IsOwner) return;
        
        // Durability sistemi için (ileride eklenebilir)
        // Şimdilik sadece log
        Debug.Log($"[Inventory] Item {itemID} durability reduced by {amount}");
        
        // TODO: ItemData'ya currentDurability field'ı eklenip burada güncellenebilir
        // Eğer durability 0'a düşerse item'ı kaldır veya kırık versiyonuna çevir
    }

    public override void OnNetworkSpawn()
    {
        // Only owner binds and sees their own UI
        if (!IsOwner)
        {
            // Don't open other players' UI (scene UI is single)
            return;
        }

        BindUIRuntime(); // BindUIRuntime already calls UpdateUI() at the end

        if (hotbarObject != null)
            hotbarObject.SetActive(true); // Hotbar her zaman görünür
        
        if (mainInventoryObject != null)
            mainInventoryObject.SetActive(false); // Ana envanter başlangıçta kapalı
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Retry binding if UI not bound (safe for scene timing)
        if (hotbarObject == null || nameTexts == null || nameTexts.Length == 0 || iconImages == null || iconImages.Length == 0)
        {
            BindUIRuntime();
        }

        // I tuşu ile ana envanteri aç/kapat
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (mainInventoryObject != null)
            {
                mainInventoryObject.SetActive(!mainInventoryObject.activeSelf);
            }
        }

        // 1-2-3-4-5 tuşlarıyla slot seçme
        HandleSlotSelection();
    }

    private void HandleSlotSelection()
    {
        // Alpha1 = 1, Alpha2 = 2, vb.
        for (int i = 0; i < 5; i++)
        {
            KeyCode key = KeyCode.Alpha1 + i;
            if (Input.GetKeyDown(key))
            {
                Debug.Log($"<color=cyan>[Inventory]</color> Key {i + 1} (Alpha{i + 1}) pressed! Calling SelectSlot({i})...");
                SelectSlot(i);
                break;
            }
        }
    }

    private void SelectSlot(int slotIndex)
    {
        // Slot geçerli mi kontrol et
        if (slotIndex < 0 || slotIndex >= 5)
        {
            Debug.LogWarning($"[Inventory] Invalid slot index: {slotIndex}");
            return;
        }
        
        int oldSelected = selectedSlotIndex;
        Debug.Log($"<color=blue>[Inventory]</color> SelectSlot CALLED: slotIndex={slotIndex + 1}, oldSelected={(oldSelected >= 0 ? (oldSelected + 1).ToString() : "NONE")}, items.Count={items.Count}, selectedSlotIndex BEFORE={selectedSlotIndex}");
        
        // Eşya var mı kontrol et
        bool hasItem = slotIndex < items.Count;
        bool itemNotNull = hasItem && items[slotIndex] != null;
        Debug.Log($"[Inventory] Check: slotIndex < items.Count? {hasItem}, items[{slotIndex}] != null? {itemNotNull}");
        
        if (hasItem && itemNotNull)
        {
            Debug.Log($"[Inventory] Item exists: {items[slotIndex].itemName}");
            Debug.Log($"[Inventory] Comparison: selectedSlotIndex ({selectedSlotIndex}) == slotIndex ({slotIndex})? {selectedSlotIndex == slotIndex}");
            
            // Aynı slot'a tekrar basılırsa seçimi kaldır (toggle)
            if (selectedSlotIndex == slotIndex)
            {
                Debug.Log($"[Inventory] ⚠️ Same slot pressed, DESELECTING...");
                selectedSlotIndex = -1;
                OnItemDeselected?.Invoke();
                Debug.Log($"<color=yellow>[Inventory]</color> ❌ Slot {slotIndex + 1} DESELECTED (toggle off). selectedSlotIndex is now: {selectedSlotIndex}");
            }
            else
            {
                Debug.Log($"[Inventory] ➡️ Different slot, SELECTING...");
                // Farklı bir slot seçildi - her zaman seç (toggle değil, direkt seç)
                // Önceki seçimi kaldır (eğer varsa)
                if (oldSelected >= 0 && oldSelected != slotIndex)
                {
                    Debug.Log($"[Inventory] Previous selection (slot {oldSelected + 1}) will be deselected.");
                    OnItemDeselected?.Invoke();
                }
                
                selectedSlotIndex = slotIndex;
                OnItemSelected?.Invoke(items[slotIndex], slotIndex);
                Debug.Log($"<color=green>[Inventory]</color> ✅ Slot {slotIndex + 1} SELECTED: {items[slotIndex].itemName}. selectedSlotIndex changed from {oldSelected} to {selectedSlotIndex}");
            }
        }
        else
        {
            // Boş slot seçildi, seçimi kaldır
            Debug.Log($"<color=red>[Inventory]</color> ⚠️ Slot {slotIndex + 1} is empty or out of range! hasItem={hasItem}, itemNotNull={itemNotNull}");
            if (selectedSlotIndex >= 0)
            {
                selectedSlotIndex = -1;
                OnItemDeselected?.Invoke();
                Debug.Log($"<color=yellow>[Inventory]</color> Selection cleared. selectedSlotIndex is now: {selectedSlotIndex}");
            }
        }
        
        // UI'ı her zaman güncelle (highlight border'ları güncellemek için)
        Debug.Log($"[Inventory] Calling UpdateUI(). Current selectedSlotIndex: {selectedSlotIndex}");
        UpdateUI();
    }

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
        nameTexts = new TextMeshProUGUI[5];
        iconImages = new Image[5];
        slotFrames = new Image[5]; // Slot frame'leri için

        for (int i = 0; i < 5; i++)
        {
            string slotName = $"Slot_{i + 1}";
            Transform slot = hotbarObject.transform.Find(slotName);

            if (slot == null)
            {
                Debug.LogError($"[Inventory] '{slotName}' not found! Hotbar should have Slot_1..Slot_5 as children.");
                continue;
            }

            // Find child objects
            Transform nameT = slot.Find("ItemName");
            Transform iconT = slot.Find("ItemIcon");
            
            // Slot itself has Image component (frame/background - always visible)
            // ItemIcon child has Image component (item icon - only visible when item exists)
            // ItemName should be positioned inside ItemIcon (overlay text)
            
            if (nameT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemName not found!");

            if (iconT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemIcon not found!");

            nameTexts[i] = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
            
            // Get Image component from ItemIcon child (not from Slot itself)
            iconImages[i] = iconT != null ? iconT.GetComponent<Image>() : null;
            
            if (iconImages[i] == null && iconT != null)
            {
                Debug.LogWarning($"[Inventory] {slotName}/ItemIcon found but Image component is missing. Adding Image component...");
                iconImages[i] = iconT.gameObject.AddComponent<Image>();
                if (iconImages[i] != null)
                {
                    Debug.Log($"<color=green>[Inventory]</color> Image component added to {slotName}/ItemIcon");
                }
            }

            // Slot frame'ini al
            slotFrames[i] = slot.GetComponent<Image>();
            if (slotFrames[i] == null)
            {
                Debug.LogWarning($"[Inventory] {slotName} has no Image component for frame!");
            }

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
                slotFrames[i].color = Color.white; // Normal renk (highlight border kullanacağız)
            }
        }

        Debug.Log("<color=green>[Inventory]</color> UI runtime bound.");
        
        // Update UI after binding to show current items
        UpdateUI();
    }

    public void AddItem(ItemData newItem)
    {
        if (!IsOwner) return; // only our own inventory
        if (newItem == null) return;

        if (items.Count < 5)
        {
            items.Add(newItem);
            
            // Bind UI if not bound
            if (nameTexts == null || iconImages == null || nameTexts.Length == 0 || iconImages.Length == 0)
            {
                BindUIRuntime();
            }
            
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        // Don't update if UI not bound
        if (nameTexts == null || iconImages == null || nameTexts.Length == 0 || iconImages.Length == 0)
        {
            Debug.LogWarning("[Inventory] UpdateUI called but UI not bound. BindUIRuntime() should be called.");
            return;
        }

        for (int i = 0; i < 5; i++)
        {
            // Slot seçim durumunu log'la (highlight'ı siz Unity Editor'da yöneteceksiniz)
            if (i == selectedSlotIndex && i < items.Count && items[i] != null)
            {
                Debug.Log($"<color=cyan>[Inventory]</color> UpdateUI: Slot {i + 1} is SELECTED (selectedSlotIndex={selectedSlotIndex})");
            }

            if (i < items.Count && items[i] != null)
            {
                if (nameTexts[i] != null) nameTexts[i].text = items[i].itemName;

                if (iconImages[i] != null)
                {
                    if (items[i].itemIcon == null)
                    {
                        Debug.LogWarning($"[Inventory] Item '{items[i].itemName}' has null itemIcon!");
                    }
                    else
                    {
                        iconImages[i].sprite = items[i].itemIcon;
                        iconImages[i].color = Color.white; // visible
                        iconImages[i].enabled = true; // ensure enabled
                        
                        // Ensure ItemIcon GameObject is active
                        if (iconImages[i].gameObject != null)
                        {
                            iconImages[i].gameObject.SetActive(true);
                        }
                        
                        // Fix RectTransform size if it's 0
                        RectTransform iconRect = iconImages[i].GetComponent<RectTransform>();
                        if (iconRect != null)
                        {
                            if (iconRect.sizeDelta.x == 0 || iconRect.sizeDelta.y == 0)
                            {
                                // Try to get size from LayoutElement first
                                LayoutElement layoutElement = iconImages[i].GetComponent<LayoutElement>();
                                if (layoutElement != null && layoutElement.preferredWidth > 0 && layoutElement.preferredHeight > 0)
                                {
                                    iconRect.sizeDelta = new Vector2(layoutElement.preferredWidth, layoutElement.preferredHeight);
                                    Debug.Log($"[Inventory] Slot {i}: ItemIcon size set from LayoutElement: {iconRect.sizeDelta}");
                                }
                                else
                                {
                                    // Default size if LayoutElement doesn't have preferred size
                                    iconRect.sizeDelta = new Vector2(50, 50);
                                    Debug.Log($"[Inventory] Slot {i}: ItemIcon size set to default: {iconRect.sizeDelta}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[Inventory] Slot {i}: iconImages[{i}] is null! ItemIcon Image component missing?");
                }
            }
            else
            {
                // show empty slot
                if (nameTexts[i] != null) nameTexts[i].text = "Boş";
                if (iconImages[i] != null)
                {
                    iconImages[i].sprite = null;
                    iconImages[i].color = new Color(1, 1, 1, 0);
                }
            }
        }
    }
}
