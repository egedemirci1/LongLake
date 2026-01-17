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
    public TextMeshProUGUI[] nameTexts; // Hotbar text'leri (5 slot)
    public Image[] iconImages; // Hotbar icon'ları (5 slot)
    public Image[] slotFrames; // Hotbar slot frame'leri (5 slot)

    [Header("Inventory System")]
    [SerializeField] private const int MAX_INVENTORY_SIZE = 12; // Ana envanter boyutu
    [SerializeField] private const int HOTBAR_SIZE = 5; // Hotbar boyutu
    
    [System.Serializable]
    public class InventorySlot
    {
        public ItemData item;
        public int quantity;
        
        public InventorySlot(ItemData item, int quantity = 1)
        {
            this.item = item;
            this.quantity = quantity;
        }
        
        public bool IsEmpty => item == null || quantity <= 0;
        public bool CanStack(ItemData otherItem)
        {
            return item != null && otherItem != null && 
                   item.itemID == otherItem.itemID && 
                   quantity < item.maxStackSize;
        }
    }
    
    public List<InventorySlot> items = new List<InventorySlot>(); // Ana envanter (12 slot) - Stack desteği ile
    
    // Hotbar slot'ları ana envanterdeki item index'lerini tutar (-1 = boş)
    [SerializeField] public int[] hotbarSlots = new int[HOTBAR_SIZE]; // Hotbar slot referansları (public for drag-drop)
    
    [Header("Main Inventory UI")]
    public Image[] inventorySlotIcons; // Ana envanter slot icon'ları (12 slot)
    public TextMeshProUGUI[] inventorySlotNames; // Ana envanter slot text'leri (12 slot)
    public Image[] inventorySlotFrames; // Ana envanter slot frame'leri (12 slot) - boş slot görseli için
    
    [Header("Crafting UI")]
    public Image[] craftingInputIcons; // Crafting input slot icon'ları (3 slot)
    public TextMeshProUGUI[] craftingInputNames; // Crafting input slot text'leri (3 slot)
    public Image[] craftingInputFrames; // Crafting input slot frame'leri (3 slot) - boş slot görseli için
    public Image craftingOutputIcon; // Crafting output slot icon
    public TextMeshProUGUI craftingOutputName; // Crafting output slot text
    public Image craftingOutputFrame; // Crafting output slot frame - boş slot görseli için
    private Button cancelCraftButton; // Craft iptal butonu
    
    [Header("Crafting System")]
    public ItemData[] craftingInputItems = new ItemData[3]; // Crafting input slot'larındaki item'lar
    public ItemData craftingOutputItem; // Crafting output item
    
    [Header("Item Drop")]
    [Tooltip("Eğer ItemData'da worldPrefab yoksa, bu generic prefab kullanılır (fallback)")]
    [SerializeField] private GameObject fallbackItemPickUpPrefab;
    
    [Header("Selection")]
    [SerializeField] private int selectedSlotIndex = -1; // Seçili hotbar slot (-1 = hiçbiri seçili değil)
    
    // Seçili eşyaya erişim için property (hotbar slot'undan)
    public ItemData SelectedItem
    {
        get
        {
            if (selectedSlotIndex >= 0 && selectedSlotIndex < HOTBAR_SIZE && hotbarSlots[selectedSlotIndex] >= 0)
            {
                int inventoryIndex = hotbarSlots[selectedSlotIndex];
                if (inventoryIndex >= 0 && inventoryIndex < items.Count && items[inventoryIndex] != null && !items[inventoryIndex].IsEmpty)
                {
                    return items[inventoryIndex].item;
                }
            }
            return null;
        }
    }
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
        return items.Exists(slot => slot != null && !slot.IsEmpty && slot.item.itemID == keyID);
    }

    // Item management methods
    public int CountItem(string itemID)
    {
        if (!IsOwner) return 0;
        int total = 0;
        foreach (var slot in items)
        {
            if (slot != null && !slot.IsEmpty && slot.item.itemID == itemID)
            {
                total += slot.quantity;
            }
        }
        return total;
    }

    public bool HasItem(string itemID)
    {
        if (!IsOwner) return false;
        return items.Exists(slot => slot != null && !slot.IsEmpty && slot.item.itemID == itemID);
    }

    public void RemoveItem(string itemID, int quantity = 1)
    {
        if (!IsOwner) return;
        
        int remainingToRemove = quantity;
        
        for (int i = items.Count - 1; i >= 0 && remainingToRemove > 0; i--)
        {
            if (items[i] != null && !items[i].IsEmpty && items[i].item.itemID == itemID)
            {
                if (items[i].quantity <= remainingToRemove)
                {
                    // Slot'taki tüm item'ları kaldır
                    remainingToRemove -= items[i].quantity;
                    
                    // Hotbar'dan bu item'ı kaldır
                    for (int j = 0; j < HOTBAR_SIZE; j++)
                    {
                        if (hotbarSlots[j] == i)
                        {
                            hotbarSlots[j] = -1;
                        }
                        else if (hotbarSlots[j] > i)
                        {
                            hotbarSlots[j]--;
                        }
                    }
                    
                    // Seçili item siliniyorsa seçimi kaldır
                    if (selectedSlotIndex >= 0 && hotbarSlots[selectedSlotIndex] == i)
                    {
                        selectedSlotIndex = -1;
                        OnItemDeselected?.Invoke();
                    }
                    
                    items.RemoveAt(i);
                }
                else
                {
                    // Slot'taki item'lardan sadece bir kısmını kaldır
                    items[i].quantity -= remainingToRemove;
                    remainingToRemove = 0;
                }
            }
        }
        
        if (remainingToRemove > 0)
        {
            Debug.LogWarning($"[Inventory] Could not remove {remainingToRemove} items of {itemID}! Not enough items.");
        }
        
        UpdateUI();
    }

    public void ReduceItemDurability(string itemID, int amount)
    {
        if (!IsOwner) return;
        
        // Durability sistemi için (ileride eklenebilir)
        Debug.Log($"[Inventory] Item {itemID} durability reduced by {amount}");
    }

    public override void OnNetworkSpawn()
    {
        // Only owner binds and sees their own UI
        if (!IsOwner)
        {
            // Don't open other players' UI (scene UI is single)
            return;
        }

        // Initialize hotbar slots (-1 = empty)
        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            hotbarSlots[i] = -1;
        }

        BindUIRuntime(); // BindUIRuntime already calls UpdateUI() at the end

        // Hotbar başlangıçta kapalı - backpack alınca açılacak
        if (hotbarObject != null)
            hotbarObject.SetActive(false);
        
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

        // I tuşu ile ana envanteri aç/kapat (sadece backpack varsa)
        if (Input.GetKeyDown(KeyCode.I))
        {
            // Backpack kontrolü - backpack yoksa envanter açılmaz
            if (!HasItem("backpack"))
            {
                Debug.LogWarning("[Inventory] You need a backpack to open inventory!");
                return;
            }
            
            if (mainInventoryObject != null)
            {
                bool isOpening = !mainInventoryObject.activeSelf;
                mainInventoryObject.SetActive(isOpening);
                
                // Cursor kontrolü
                if (isOpening)
                {
                    // Envanter açılıyor - cursor'u göster ve unlock et
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    // Envanter kapanıyor - cursor'u kilitle ve gizle
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
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
                SelectSlot(i);
                break;
            }
        }
    }

    private void SelectSlot(int slotIndex)
    {
        // Slot geçerli mi kontrol et (hotbar slot index)
        if (slotIndex < 0 || slotIndex >= HOTBAR_SIZE)
        {
            Debug.LogWarning($"[Inventory] Invalid hotbar slot index: {slotIndex}");
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
                Debug.Log($"<color=yellow>[Inventory]</color> Hotbar Slot {slotIndex + 1} DESELECTED (toggle off)");
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
                Debug.Log($"<color=green>[Inventory]</color> Hotbar Slot {slotIndex + 1} SELECTED: {item.itemName} (inventory index: {inventoryIndex})");
            }
        }
        else
        {
            // Boş slot seçildi, seçimi kaldır
            if (selectedSlotIndex >= 0)
            {
                selectedSlotIndex = -1;
                OnItemDeselected?.Invoke();
                Debug.Log($"<color=yellow>[Inventory]</color> Hotbar Slot {slotIndex + 1} is empty, selection cleared");
            }
        }
        
        // UI'ı her zaman güncelle
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
            if (nameT != null && nameTexts[i] == null)
            {
                Debug.LogWarning($"[Inventory] {slotName}/ItemName GameObject found but TextMeshProUGUI component is missing!");
            }
            
            iconImages[i] = iconT != null ? iconT.GetComponent<Image>() : null;

            if (iconT != null && iconImages[i] == null)
            {
                Debug.LogWarning($"[Inventory] {slotName}/ItemIcon GameObject found but Image component is missing. Adding Image component...");
                iconImages[i] = iconT.gameObject.AddComponent<Image>();
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
                slotFrames[i].color = Color.white; // Normal renk
            }
        }

        Debug.Log("<color=green>[Inventory]</color> Hotbar UI runtime bound.");
        Debug.Log($"[Inventory] Hotbar binding summary: nameTexts={nameTexts.Count(t => t != null)}/{HOTBAR_SIZE}, iconImages={iconImages.Count(i => i != null)}/{HOTBAR_SIZE}");
        
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
            Debug.LogWarning("[Inventory] InventoryGrid not found! Main inventory UI will not update.");
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
                            Debug.Log($"[Inventory] Main inventory slot {i + 1}: Using child '{child.name}' as ItemIcon (ItemIcon child not found)");
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
                    Debug.Log($"[Inventory] Main inventory slot {i + 1}: Created ItemIcon GameObject (not found in slot structure)");
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
                
                // Icon bulunamazsa uyar
                if (inventorySlotIcons[i] == null)
                {
                    Debug.LogWarning($"[Inventory] Main inventory slot {i + 1} ({slot.name}): ItemIcon Image component not found! Slot children: {GetChildrenNames(slot)}");
                }
                else
                {
                    // Icon'u başlangıçta hazırla
                    inventorySlotIcons[i].enabled = true;
                    if (inventorySlotIcons[i].gameObject != null)
                    {
                        inventorySlotIcons[i].gameObject.SetActive(true);
                    }
                    Debug.Log($"[Inventory] Main inventory slot {i + 1} ({slot.name}): ItemIcon bound successfully");
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
                        frameImage.color = Color.white;
                        Debug.Log($"[Inventory] Main inventory slot {i + 1}: Created Image component for frame");
                    }
                    inventorySlotFrames[i] = frameImage;
                }
                
                // Frame'i her zaman görünür tut (hotbar'daki gibi)
                if (inventorySlotFrames[i] != null)
                {
                    inventorySlotFrames[i].enabled = true;
                    inventorySlotFrames[i].color = Color.white;
                    if (inventorySlotFrames[i].gameObject != null)
                    {
                        inventorySlotFrames[i].gameObject.SetActive(true);
                    }
                    Debug.Log($"[Inventory] Main inventory slot {i + 1}: Frame bound successfully");
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
                
                Debug.Log($"[Inventory] Main inventory slot {i + 1} ({slot.name}) bound with drag-drop handlers");
            }
            else
            {
                Debug.LogWarning($"[Inventory] Main inventory slot {i + 1} (InvSlot_{i + 1}) not found in InventoryGrid!");
            }
        }

        Debug.Log($"<color=green>[Inventory]</color> Main Inventory UI bound. Icons: {inventorySlotIcons.Count(i => i != null)}/{MAX_INVENTORY_SIZE}, Names: {inventorySlotNames.Count(t => t != null)}/{MAX_INVENTORY_SIZE}, Frames: {inventorySlotFrames.Count(f => f != null)}/{MAX_INVENTORY_SIZE}");
    }

    private void BindCraftingUI()
    {
        // Crafting input slot'ları için UI binding (3 slot)
        craftingInputIcons = new Image[3];
        craftingInputNames = new TextMeshProUGUI[3];
        craftingInputFrames = new Image[3];

        // CraftSite'i bul (MainInventory > CraftPanel > CraftSite)
        Transform craftSite = mainInventoryObject.transform.Find("CraftPanel/CraftSite");
        if (craftSite == null)
        {
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var tr in all)
            {
                if (tr != null && tr.name == "CraftSite")
                {
                    craftSite = tr;
                    break;
                }
            }
        }

        if (craftSite == null)
        {
            Debug.LogWarning("[Inventory] CraftSite not found! Crafting UI will not update.");
            return;
        }

        // Input slot'ları bul
        for (int i = 0; i < 3; i++)
        {
            string slotName = $"InputSlot_{i + 1}";
            Transform slot = craftSite.Find(slotName);

            if (slot == null && i < craftSite.childCount)
            {
                slot = craftSite.GetChild(i);
                if (slot.name.Contains("Output")) continue;
            }

            if (slot != null && !slot.name.Contains("Output"))
            {
                Transform iconT = slot.Find("ItemIcon");
                if (iconT == null)
                {
                    // Fallback: Slot'un kendisinde Image varsa onu kullan
                    if (slot.GetComponent<Image>() != null)
                    {
                        iconT = slot;
                    }
                    else
                    {
                        // Tüm child'ları kontrol et
                        for (int j = 0; j < slot.childCount; j++)
                        {
                            Transform child = slot.GetChild(j);
                            if (child.GetComponent<Image>() != null && !child.name.Contains("ItemName"))
                            {
                                iconT = child;
                                break;
                            }
                        }
                    }
                }

                Transform nameT = slot.Find("ItemName_Text");
                if (nameT == null) nameT = slot.Find("ItemName_Tex");
                if (nameT == null) nameT = slot.Find("ItemName");
                
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

                craftingInputIcons[i] = iconT != null ? iconT.GetComponent<Image>() : null;
                craftingInputNames[i] = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
                
                // Slot frame'ini al (boş slot görseli için)
                craftingInputFrames[i] = slot.GetComponent<Image>();
                
                // Bulunamazsa child'larda "Frame" veya "Background" ara
                if (craftingInputFrames[i] == null)
                {
                    Transform frameT = slot.Find("Frame") ?? slot.Find("Background") ?? slot.Find("SlotFrame");
                    if (frameT != null)
                    {
                        craftingInputFrames[i] = frameT.GetComponent<Image>();
                    }
                }
                
                // Hala bulunamazsa, slot'un kendisinde Image oluştur
                if (craftingInputFrames[i] == null)
                {
                    Image frameImage = slot.gameObject.GetComponent<Image>();
                    if (frameImage == null)
                    {
                        frameImage = slot.gameObject.AddComponent<Image>();
                        frameImage.color = Color.white;
                        Debug.Log($"[Inventory] Crafting input slot {i + 1}: Created Image component for frame");
                    }
                    craftingInputFrames[i] = frameImage;
                }
                
                // Frame'i her zaman görünür tut (envanterdeki gibi)
                if (craftingInputFrames[i] != null)
                {
                    craftingInputFrames[i].enabled = true;
                    craftingInputFrames[i].color = Color.white;
                    if (craftingInputFrames[i].gameObject != null)
                    {
                        craftingInputFrames[i].gameObject.SetActive(true);
                    }
                    Debug.Log($"[Inventory] Crafting input slot {i + 1}: Frame bound successfully");
                }
            }
        }

        // Output slot'u bul
        Transform outputSlot = craftSite.Find("OutputSlot");
        if (outputSlot == null && craftSite.childCount > 0)
        {
            outputSlot = craftSite.GetChild(craftSite.childCount - 1);
            if (!outputSlot.name.Contains("Output"))
            {
                outputSlot = null;
            }
        }

        if (outputSlot != null)
        {
            Transform iconT = outputSlot.Find("ItemIcon");
            if (iconT == null)
            {
                // Fallback: OutputSlot'un kendisinde Image varsa onu kullan
                if (outputSlot.GetComponent<Image>() != null)
                {
                    iconT = outputSlot;
                }
                else
                {
                    for (int j = 0; j < outputSlot.childCount; j++)
                    {
                        Transform child = outputSlot.GetChild(j);
                        if (child.GetComponent<Image>() != null && !child.name.Contains("ItemName"))
                        {
                            iconT = child;
                            break;
                        }
                    }
                }
            }

            Transform nameT = outputSlot.Find("ItemName_Text");
            if (nameT == null) nameT = outputSlot.Find("ItemName_Tex");
            if (nameT == null) nameT = outputSlot.Find("ItemName");
            
            if (nameT == null)
            {
                for (int j = 0; j < outputSlot.childCount; j++)
                {
                    Transform child = outputSlot.GetChild(j);
                    if (child.GetComponent<TextMeshProUGUI>() != null)
                    {
                        nameT = child;
                        break;
                    }
                }
            }

            craftingOutputIcon = iconT != null ? iconT.GetComponent<Image>() : null;
            craftingOutputName = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
            
            // Output slot frame'ini al (boş slot görseli için)
            craftingOutputFrame = outputSlot.GetComponent<Image>();
            
            // Bulunamazsa child'larda "Frame" veya "Background" ara
            if (craftingOutputFrame == null)
            {
                Transform frameT = outputSlot.Find("Frame") ?? outputSlot.Find("Background") ?? outputSlot.Find("SlotFrame");
                if (frameT != null)
                {
                    craftingOutputFrame = frameT.GetComponent<Image>();
                }
            }
            
            // Hala bulunamazsa, slot'un kendisinde Image oluştur
            if (craftingOutputFrame == null)
            {
                Image frameImage = outputSlot.gameObject.GetComponent<Image>();
                if (frameImage == null)
                {
                    frameImage = outputSlot.gameObject.AddComponent<Image>();
                    frameImage.color = Color.white;
                    Debug.Log("[Inventory] Crafting output slot: Created Image component for frame");
                }
                craftingOutputFrame = frameImage;
            }
            
            // Frame'i her zaman görünür tut (envanterdeki gibi)
            if (craftingOutputFrame != null)
            {
                craftingOutputFrame.enabled = true;
                craftingOutputFrame.color = Color.white;
                if (craftingOutputFrame.gameObject != null)
                {
                    craftingOutputFrame.gameObject.SetActive(true);
                }
                Debug.Log("[Inventory] Crafting output slot: Frame bound successfully");
            }
        }

        // CancelButton'ı bul ve bağla
        Transform cancelButtonT = mainInventoryObject.transform.Find("CraftPanel/CancelButton");
        if (cancelButtonT == null)
        {
            // Alternatif path'ler dene
            cancelButtonT = craftSite.Find("CancelButton");
            if (cancelButtonT == null)
            {
                Transform craftPanel = mainInventoryObject.transform.Find("CraftPanel");
                if (craftPanel != null)
                {
                    cancelButtonT = craftPanel.Find("CancelButton");
                }
                if (cancelButtonT == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<Transform>();
                    foreach (var tr in all)
                    {
                        if (tr != null && tr.name == "CancelButton")
                        {
                            cancelButtonT = tr;
                            break;
                        }
                    }
                }
            }
        }
        
        if (cancelButtonT != null)
        {
            cancelCraftButton = cancelButtonT.GetComponent<Button>();
            if (cancelCraftButton != null)
            {
                cancelCraftButton.onClick.RemoveAllListeners();
                cancelCraftButton.onClick.AddListener(CancelCrafting);
                Debug.Log("<color=green>[Inventory]</color> CancelButton bound successfully");
            }
            else
            {
                Debug.LogWarning("[Inventory] CancelButton found but Button component is missing!");
            }
        }
        else
        {
            Debug.LogWarning("[Inventory] CancelButton not found! Craft cancel functionality will not work.");
        }

        Debug.Log($"<color=green>[Inventory]</color> Crafting UI bound. Input Icons: {craftingInputIcons.Count(i => i != null)}/3, Output Icon: {craftingOutputIcon != null}, Input Frames: {craftingInputFrames.Count(f => f != null)}/3, Output Frame: {craftingOutputFrame != null}");
    }

    private string GetChildrenNames(Transform parent)
    {
        if (parent == null || parent.childCount == 0) return "None";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < parent.childCount; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(parent.GetChild(i).name);
        }
        return sb.ToString();
    }

    public void AddItem(ItemData newItem)
    {
        if (!IsOwner) 
        {
            Debug.LogWarning($"[Inventory] AddItem called but not owner. IsOwner={IsOwner}");
            return; // only our own inventory
        }
        if (newItem == null) 
        {
            Debug.LogError("[Inventory] AddItem called with null item!");
            return;
        }

        // Önce aynı itemID'ye sahip stack edilebilir item var mı kontrol et
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && !items[i].IsEmpty && items[i].item.itemID == newItem.itemID)
            {
                // Aynı item bulundu, stack edilebilir mi kontrol et
                if (items[i].CanStack(newItem))
                {
                    // Stack et
                    items[i].quantity++;
                    Debug.Log($"<color=green>[Inventory]</color> Item stacked: {newItem.itemName} (x{items[i].quantity})");
            UpdateUI();
                    return;
                }
            }
        }

        // Stack edilemedi veya aynı item bulunamadı, yeni slot'a ekle
        if (items.Count < MAX_INVENTORY_SIZE)
        {
            items.Add(new InventorySlot(newItem, 1));
            int addedIndex = items.Count - 1;
            Debug.Log($"<color=green>[Inventory]</color> Item added to inventory slot {addedIndex}: {newItem.itemName} (x1) (ID: {newItem.itemID}). Total slots: {items.Count}");
            
            // Eğer hotbar'da boş slot varsa, yeni eklenen item'ı ilk boş hotbar slot'una ata
            for (int i = 0; i < HOTBAR_SIZE; i++)
            {
                if (hotbarSlots[i] == -1) // Boş hotbar slot bulundu
                {
                    hotbarSlots[i] = addedIndex;
                    Debug.Log($"<color=green>[Inventory]</color> Item automatically assigned to hotbar slot {i + 1}");
                    break;
                }
            }
            
            // Backpack alınca hotbar'ı aç
            if (newItem.itemID == "backpack" && hotbarObject != null)
            {
                hotbarObject.SetActive(true);
                Debug.Log("[Inventory] Backpack acquired! Hotbar unlocked.");
            }
            
            // Bind UI if not bound
            if (nameTexts == null || iconImages == null || nameTexts.Length == 0 || iconImages.Length == 0)
            {
                Debug.LogWarning("[Inventory] UI not bound, calling BindUIRuntime()...");
                BindUIRuntime();
            }
            
            if (hotbarObject == null)
            {
                Debug.LogError("[Inventory] hotbarObject is NULL! Cannot update UI.");
                return;
            }
            
            UpdateUI();
        }
        else
        {
            Debug.LogWarning($"[Inventory] Cannot add item {newItem.itemName}: Inventory full ({MAX_INVENTORY_SIZE}/{MAX_INVENTORY_SIZE})");
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

        if (hotbarObject == null)
        {
            Debug.LogError("[Inventory] UpdateUI: hotbarObject is NULL!");
            return;
        }

        // Update Hotbar UI (5 slots)
        for (int i = 0; i < HOTBAR_SIZE; i++)
        {
            int inventoryIndex = hotbarSlots[i]; // Ana envanterdeki item index'i

            // Hotbar slot'unda item var mı?
            if (inventoryIndex >= 0 && inventoryIndex < items.Count && items[inventoryIndex] != null && !items[inventoryIndex].IsEmpty)
            {
                InventorySlot slot = items[inventoryIndex];
                ItemData item = slot.item;
                
                if (nameTexts[i] != null)
                {
                    // Quantity göster: her zaman (x1), (x2), (x5) şeklinde
                    nameTexts[i].text = $"{item.itemName} (x{slot.quantity})";
                }

                if (iconImages[i] != null)
                {
                    if (item.itemIcon == null)
                    {
                        Debug.LogWarning($"[Inventory] Item '{item.itemName}' has null itemIcon!");
                    }
                    else
                    {
                        iconImages[i].sprite = item.itemIcon;
                        iconImages[i].color = Color.white; // visible
                        iconImages[i].enabled = true;
                        
                        if (iconImages[i].gameObject != null)
                        {
                            iconImages[i].gameObject.SetActive(true);
                        }
                    }
                }
            }
            else
            {
                // Boş hotbar slot
                if (nameTexts[i] != null) nameTexts[i].text = "Boş";
                if (iconImages[i] != null)
                {
                    iconImages[i].sprite = null;
                    iconImages[i].color = new Color(1, 1, 1, 0);
                }
            }
        }

        // Update Main Inventory UI (12 slots) if bound
        if (inventorySlotIcons != null && inventorySlotNames != null && inventorySlotFrames != null &&
            inventorySlotIcons.Length == MAX_INVENTORY_SIZE && inventorySlotNames.Length == MAX_INVENTORY_SIZE && inventorySlotFrames.Length == MAX_INVENTORY_SIZE)
        {
            for (int i = 0; i < MAX_INVENTORY_SIZE; i++)
            {
                // Frame'i her zaman görünür tut (hotbar'daki gibi) - TÜM slot'lar için
                if (inventorySlotFrames[i] != null)
                {
                    inventorySlotFrames[i].enabled = true;
                    inventorySlotFrames[i].color = Color.white;
                }
                
                if (i < items.Count && items[i] != null && !items[i].IsEmpty)
                {
                    InventorySlot slot = items[i];
                    ItemData item = slot.item;
                    
                    if (inventorySlotNames[i] != null)
                    {
                        // Quantity göster: her zaman (x1), (x2), (x5) şeklinde
                        inventorySlotNames[i].text = $"{item.itemName} (x{slot.quantity})";
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
                                // Parent'ları aktif etme - sadece icon GameObject'ini aktif et
                            }
                            
                            // RectTransform size kontrolü
                            RectTransform iconRect = inventorySlotIcons[i].GetComponent<RectTransform>();
                            if (iconRect != null)
                            {
                                if (iconRect.sizeDelta.x == 0 || iconRect.sizeDelta.y == 0)
                                {
                                    iconRect.sizeDelta = new Vector2(50, 50);
                                }
                                // Anchor ve pivot ayarları
                                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                                iconRect.pivot = new Vector2(0.5f, 0.5f);
                                iconRect.anchoredPosition = Vector2.zero;
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[Inventory] Item '{item.itemName}' has null itemIcon!");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Inventory] UpdateUI: Main inventory slot {i + 1} icon is NULL! Item: {item.itemName}");
                    }
                }
                else
                {
                    // Boş inventory slot - hotbar'daki gibi görünür tut
                    if (inventorySlotNames[i] != null) inventorySlotNames[i].text = "Boş";
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
                    
                    // Frame'i her zaman görünür tut (hotbar'daki gibi beyaz kare)
                    if (inventorySlotFrames[i] != null)
                    {
                        inventorySlotFrames[i].enabled = true;
                        inventorySlotFrames[i].color = Color.white;
                        if (inventorySlotFrames[i].gameObject != null)
                        {
                            inventorySlotFrames[i].gameObject.SetActive(true);
                        }
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning($"[Inventory] UpdateUI: Main Inventory UI not fully bound! Icons: {inventorySlotIcons != null && inventorySlotIcons.Length == MAX_INVENTORY_SIZE}, Names: {inventorySlotNames != null && inventorySlotNames.Length == MAX_INVENTORY_SIZE}, Frames: {inventorySlotFrames != null && inventorySlotFrames.Length == MAX_INVENTORY_SIZE}");
        }
        
        // Update Crafting UI
        UpdateCraftingUI();
    }

    // Drag & Drop Methods
    public void SwapHotbarSlots(int slot1, int slot2)
    {
        if (!IsOwner) return;
        if (slot1 < 0 || slot1 >= HOTBAR_SIZE || slot2 < 0 || slot2 >= HOTBAR_SIZE) return;
        
        int temp = hotbarSlots[slot1];
        hotbarSlots[slot1] = hotbarSlots[slot2];
        hotbarSlots[slot2] = temp;
        
        Debug.Log($"<color=green>[Inventory]</color> Swapped hotbar slots {slot1 + 1} and {slot2 + 1}");
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
        
        Debug.Log($"<color=green>[Inventory]</color> Swapped inventory slots {slot1 + 1} and {slot2 + 1}");
        UpdateUI();
    }

    public void MoveItemFromInventoryToHotbar(int inventoryIndex, int hotbarIndex)
    {
        if (!IsOwner) return;
        if (inventoryIndex < 0 || inventoryIndex >= items.Count || items[inventoryIndex] == null || items[inventoryIndex].IsEmpty) 
        {
            Debug.LogWarning($"[Inventory] MoveItemFromInventoryToHotbar: Invalid inventoryIndex {inventoryIndex} or empty slot");
            return;
        }
        if (hotbarIndex < 0 || hotbarIndex >= HOTBAR_SIZE) 
        {
            Debug.LogWarning($"[Inventory] MoveItemFromInventoryToHotbar: Invalid hotbarIndex {hotbarIndex}");
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
            Debug.Log($"<color=yellow>[Inventory]</color> Item already in hotbar slot {existingHotbarSlot + 1}, clearing old slot");
            hotbarSlots[existingHotbarSlot] = -1;
        }
        
        // Eğer hedef hotbar slot'u doluysa, swap yap
        if (hotbarSlots[hotbarIndex] >= 0 && hotbarSlots[hotbarIndex] != inventoryIndex)
        {
            int oldInventoryIndex = hotbarSlots[hotbarIndex];
            hotbarSlots[hotbarIndex] = inventoryIndex;
            Debug.Log($"<color=green>[Inventory]</color> Replaced: Hotbar slot {hotbarIndex + 1} now references Inventory slot {inventoryIndex + 1} (old: {oldInventoryIndex + 1})");
        }
        else
        {
            hotbarSlots[hotbarIndex] = inventoryIndex;
            Debug.Log($"<color=green>[Inventory]</color> Moved item from inventory slot {inventoryIndex + 1} to hotbar slot {hotbarIndex + 1}");
        }
        
        UpdateUI();
    }

    public void MoveItemFromHotbarToInventory(int hotbarIndex, int inventoryIndex)
    {
        if (!IsOwner) return;
        if (hotbarIndex < 0 || hotbarIndex >= HOTBAR_SIZE || hotbarSlots[hotbarIndex] < 0) 
        {
            Debug.LogWarning($"[Inventory] MoveItemFromHotbarToInventory: Invalid hotbarIndex {hotbarIndex} or empty slot");
            return;
        }
        if (inventoryIndex < 0 || inventoryIndex >= MAX_INVENTORY_SIZE) 
        {
            Debug.LogWarning($"[Inventory] MoveItemFromHotbarToInventory: Invalid inventoryIndex {inventoryIndex}");
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
                Debug.Log($"<color=green>[Inventory]</color> Swapped: Hotbar slot {hotbarIndex + 1} <-> Inventory slot {inventoryIndex + 1}");
            }
            else
            {
                hotbarSlots[hotbarIndex] = inventoryIndex;
                Debug.Log($"<color=green>[Inventory]</color> Replaced: Hotbar slot {hotbarIndex + 1} now references Inventory slot {inventoryIndex + 1}");
            }
        }
        else
        {
            hotbarSlots[hotbarIndex] = -1;
            Debug.Log($"<color=green>[Inventory]</color> Moved item from hotbar slot {hotbarIndex + 1} to empty inventory slot {inventoryIndex + 1}");
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
            Debug.Log($"<color=yellow>[Inventory]</color> Item '{item.itemName}' already in crafting slot {inputSlotIndex + 1}. Ignoring duplicate.");
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
                        Debug.Log($"<color=green>[Inventory]</color> Removed 1x '{item.itemName}' from inventory (quantity now: {items[inventoryIndex].quantity})");
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
                        
                        Debug.Log($"<color=green>[Inventory]</color> Removed last '{item.itemName}' from inventory slot {inventoryIndex + 1}");
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
                    Debug.Log($"<color=green>[Inventory]</color> Removed 1x '{item.itemName}' from inventory slot {sourceSlotIndex + 1} (quantity now: {items[sourceSlotIndex].quantity})");
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
                    
                    Debug.Log($"<color=green>[Inventory]</color> Removed last '{item.itemName}' from inventory slot {sourceSlotIndex + 1}");
                }
            }
        }
        
        Debug.Log($"<color=green>[Inventory]</color> Selected item '{item.itemName}' for crafting input slot {inputSlotIndex + 1}");
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
                // Frame'i her zaman görünür tut (envanterdeki gibi) - TÜM slot'lar için
                if (craftingInputFrames != null && craftingInputFrames.Length > i && craftingInputFrames[i] != null)
                {
                    craftingInputFrames[i].enabled = true;
                    craftingInputFrames[i].color = Color.white;
                    if (craftingInputFrames[i].gameObject != null)
                    {
                        craftingInputFrames[i].gameObject.SetActive(true);
                    }
                }
                
                if (craftingInputItems[i] != null)
                {
                    ItemData item = craftingInputItems[i];
                    if (craftingInputNames[i] != null)
                        craftingInputNames[i].text = item.itemName;
                    if (craftingInputIcons[i] != null && item.itemIcon != null)
                    {
                        craftingInputIcons[i].sprite = item.itemIcon;
                        craftingInputIcons[i].color = Color.white;
                        craftingInputIcons[i].enabled = true;
                    }
                }
                else
                {
                    // Boş crafting input slot - envanterdeki gibi görünür tut
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
                    
                    // Frame'i her zaman görünür tut (envanterdeki gibi beyaz kare)
                    if (craftingInputFrames != null && craftingInputFrames.Length > i && craftingInputFrames[i] != null)
                    {
                        craftingInputFrames[i].enabled = true;
                        craftingInputFrames[i].color = Color.white;
                        if (craftingInputFrames[i].gameObject != null)
                        {
                            craftingInputFrames[i].gameObject.SetActive(true);
                        }
                    }
                }
            }
        }
        
        // Update crafting output slot
        if (craftingOutputIcon != null && craftingOutputName != null)
        {
            // Frame'i her zaman görünür tut (envanterdeki gibi)
            if (craftingOutputFrame != null)
            {
                craftingOutputFrame.enabled = true;
                craftingOutputFrame.color = Color.white;
                if (craftingOutputFrame.gameObject != null)
                {
                    craftingOutputFrame.gameObject.SetActive(true);
                }
            }
            
            if (craftingOutputItem != null)
            {
                if (craftingOutputName != null)
                    craftingOutputName.text = craftingOutputItem.itemName;
                if (craftingOutputIcon != null && craftingOutputItem.itemIcon != null)
                {
                    craftingOutputIcon.sprite = craftingOutputItem.itemIcon;
                    craftingOutputIcon.color = Color.white;
                    craftingOutputIcon.enabled = true;
                }
            }
            else
            {
                // Boş crafting output slot - envanterdeki gibi görünür tut
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
                
                // Frame'i her zaman görünür tut (envanterdeki gibi beyaz kare)
                if (craftingOutputFrame != null)
                {
                    craftingOutputFrame.enabled = true;
                    craftingOutputFrame.color = Color.white;
                    if (craftingOutputFrame.gameObject != null)
                    {
                        craftingOutputFrame.gameObject.SetActive(true);
                    }
                }
            }
        }
    }

    // Craft iptal fonksiyonu - craft alanındaki tüm item'ları envantere geri ekler
    public void CancelCrafting()
    {
        if (!IsOwner) return;

        bool itemsReturned = false;

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
                            itemsReturned = true;
                            Debug.Log($"<color=green>[Inventory]</color> Returned '{item.itemName}' to inventory slot {j + 1} (stacked, quantity now: {items[j].quantity})");
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
                        itemsReturned = true;
                        Debug.Log($"<color=green>[Inventory]</color> Returned '{item.itemName}' to new inventory slot {addedIndex + 1}");
                        
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
                    else
                    {
                        Debug.LogWarning($"[Inventory] Cannot return '{item.itemName}' to inventory: Inventory full!");
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
        
        if (itemsReturned)
        {
            Debug.Log($"<color=green>[Inventory]</color> Craft cancelled. All items returned to inventory.");
        }
        else
        {
            Debug.Log($"<color=yellow>[Inventory]</color> Craft cancelled. No items to return.");
        }
        
        // UI'ı güncelle
        UpdateUI();
        UpdateCraftingUI();
    }
    
    /// <summary>
    /// Item'ı yere atar (multiplayer için Server RPC)
    /// </summary>
    public void DropItemToGround(ItemData item, bool isHotbarSlot, int slotIndex)
    {
        if (!IsOwner) return;
        
        // Server'a istek gönder
        DropItemToGroundServerRpc(item.itemID, isHotbarSlot, slotIndex);
    }
    
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void DropItemToGroundServerRpc(string itemID, bool isHotbarSlot, int slotIndex)
    {
        // Sadece owner bu RPC'yi çağırabilir (güvenlik kontrolü)
        if (!IsOwner)
        {
            Debug.LogWarning($"[Inventory] Client {NetworkManager.Singleton.LocalClientId} tried to drop item but is not owner!");
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
            Debug.LogWarning($"[Inventory] Item '{itemID}' not found in slot!");
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
        
        Debug.Log($"<color=green>[Inventory]</color> Dropped '{item.itemName}' (quantity: {quantity}) to ground at {spawnPosition}");
    }
    
    private Vector3 GetDropPosition(ulong clientId)
    {
        // Local player'ı bul
        NetworkObject playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
        if (playerObj == null)
        {
            Debug.LogWarning("[Inventory] Player not found, using default drop position");
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
            Debug.LogWarning("[Inventory] Camera not found, using player position");
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

