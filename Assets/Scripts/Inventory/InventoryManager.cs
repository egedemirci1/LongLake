using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using System;

public partial class InventoryManager : NetworkBehaviour
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

    // Hotbar slot görsel durumları
    private static readonly Color FrameEmptyColor = new Color(0.06f, 0.07f, 0.09f, 0.45f);   // boş: koyu, soluk
    private static readonly Color FrameFilledColor = new Color(0.13f, 0.15f, 0.19f, 0.85f);  // dolu: koyu, belirgin
    private static readonly Color FrameSelectedColor = new Color(0.95f, 0.77f, 0.32f, 0.95f); // seçili: kehribar vurgu
    private static readonly Color CraftOutputEmptyColor = new Color(0.12f, 0.15f, 0.18f, 0.96f);
    private static readonly Color CraftOutputFilledColor = new Color(0.24f, 0.20f, 0.10f, 0.98f);
    private static readonly Color CraftAccentColor = new Color(0.95f, 0.69f, 0.25f, 1f);
    
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
        
        UpdateUI();
    }

    public void ReduceItemDurability(string itemID, int amount)
    {
        if (!IsOwner) return;
        
        // Durability sistemi için (ileride eklenebilir)
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
        if (GameMenuUI.IsBlockingGameplay) return;

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
                return;
            }
            
            if (mainInventoryObject != null)
            {
                bool isOpening = !mainInventoryObject.activeSelf;
                SetMainInventoryOpen(isOpening);
            }
        }

        // ESC ile envanteri kapat (pause menüsünden önce tüketilir)
        if (Input.GetKeyDown(KeyCode.Escape) &&
            mainInventoryObject != null &&
            mainInventoryObject.activeSelf)
        {
            SetMainInventoryOpen(false);
        }

        // 1-2-3-4-5 tuşlarıyla slot seçme
        HandleSlotSelection();
    }

    public void AddItem(ItemData newItem)
    {
        if (!IsOwner) 
        {
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
            // Eğer hotbar'da boş slot varsa, yeni eklenen item'ı ilk boş hotbar slot'una ata
            int assignedHotbar = -1;
            for (int i = 0; i < HOTBAR_SIZE; i++)
            {
                if (hotbarSlots[i] == -1) // Boş hotbar slot bulundu
                {
                    hotbarSlots[i] = addedIndex;
                    assignedHotbar = i;
                    break;
                }
            }

            // Melee silahlar ele geçsin diye hotbar'da otomatik seç
            if (assignedHotbar >= 0 && newItem.itemType == ItemType.MeleeWeapon)
                SelectSlot(assignedHotbar);
            
            // Backpack alınca hotbar'ı aç
            if (newItem.itemID == "backpack" && hotbarObject != null)
            {
                hotbarObject.SetActive(true);
            }
            
            // Bind UI if not bound
            if (nameTexts == null || iconImages == null || nameTexts.Length == 0 || iconImages.Length == 0)
            {
                BindUIRuntime();
            }
            
            if (hotbarObject == null)
            {
                Debug.LogError("[Inventory] hotbarObject is NULL! Cannot update UI.");
                return;
            }
            
            UpdateUI();
        }
    }

}

