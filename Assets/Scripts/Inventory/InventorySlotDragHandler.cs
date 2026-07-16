using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class InventorySlotDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Slot Info")]
    public bool isHotbarSlot = false; // true = hotbar slot, false = inventory slot
    public int slotIndex = -1; // Hotbar için 0-4, Inventory için 0-11
    
    private InventoryManager inventoryManager;
    private Canvas canvas;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Image dragImage;
    private GameObject dragObject;
    
    private void Start()
    {
        inventoryManager = GetComponentInParent<InventoryManager>();
        if (inventoryManager == null)
        {
            inventoryManager = FindFirstObjectByType<InventoryManager>();
        }
        
        canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindFirstObjectByType<Canvas>();
        }
        
        rectTransform = GetComponent<RectTransform>();
        
        // CanvasGroup ekle (raycast'i geçici olarak kapatmak için)
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }
    
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (inventoryManager == null || !inventoryManager.IsOwner) return;
        
        // Slot'ta item var mı kontrol et
        ItemData item = GetItemFromSlot();
        if (item == null) return;
        
        // Drag görselini oluştur
        CreateDragVisual(item);
        
        // Raycast'i kapat (drop detection için)
        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;
    }
    
    public void OnDrag(PointerEventData eventData)
    {
        if (dragObject != null && canvas != null)
        {
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform,
                eventData.position,
                canvas.worldCamera,
                out localPoint);
            
            dragObject.transform.localPosition = localPoint;
        }
    }
    
    public void OnEndDrag(PointerEventData eventData)
    {
        // Raycast'i tekrar aç
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        
        // Drop target'ı kontrol et - hem pointerCurrentRaycast hem de pointerEnter kullan
        GameObject dropTarget = eventData.pointerCurrentRaycast.gameObject;
        if (dropTarget == null)
        {
            dropTarget = eventData.pointerEnter;
        }
        
        bool itemDropped = false;
        
        if (dropTarget != null)
        {
            // Önce CraftingSlotDropHandler ara (öncelik crafting slot'lara verilir)
            CraftingSlotDropHandler craftingDropHandler = dropTarget.GetComponent<CraftingSlotDropHandler>();
            if (craftingDropHandler == null)
            {
                craftingDropHandler = dropTarget.GetComponentInParent<CraftingSlotDropHandler>();
            }
            
            // Child'larda da ara (InputSlot içindeki child'lar için)
            if (craftingDropHandler == null)
            {
                CraftingSlotDropHandler[] allHandlers = dropTarget.GetComponentsInChildren<CraftingSlotDropHandler>();
                if (allHandlers != null && allHandlers.Length > 0)
                {
                    craftingDropHandler = allHandlers[0];
                }
            }
            
            if (craftingDropHandler != null)
            {
                // Crafting slot'a drop
                HandleCraftingDrop(craftingDropHandler);
                DestroyDragVisual();
                return; // Crafting drop yapıldı, inventory drop'a geçme
            }
            
            // InventorySlotDropHandler ara
            InventorySlotDropHandler inventoryDropHandler = dropTarget.GetComponent<InventorySlotDropHandler>();
            if (inventoryDropHandler == null)
            {
                inventoryDropHandler = dropTarget.GetComponentInParent<InventorySlotDropHandler>();
            }
            
            // Eğer hala bulunamazsa, tüm parent'ları kontrol et
            if (inventoryDropHandler == null)
            {
                Transform parent = dropTarget.transform.parent;
                while (parent != null && inventoryDropHandler == null)
                {
                    inventoryDropHandler = parent.GetComponent<InventorySlotDropHandler>();
                    parent = parent.parent;
                }
            }
            
            if (inventoryDropHandler != null)
            {
                // Inventory/Hotbar slot'a drop
                HandleDrop(inventoryDropHandler);
                itemDropped = true;
            }
        }
        
        // Eğer UI dışına bırakıldıysa (dropTarget null veya UI element değilse), yere at
        if (!itemDropped)
        {
            // UI dışına bırakıldı - yere at
            ItemData item = GetItemFromSlot();
            if (item != null && inventoryManager != null)
            {
                // Canvas veya UI element'ine bırakıldıysa yere atma (sadece gerçekten UI dışına bırakıldıysa)
                bool isUIElement = dropTarget != null && 
                                  (dropTarget.GetComponent<Canvas>() != null || 
                                   dropTarget.GetComponentInParent<Canvas>() != null ||
                                   dropTarget.GetComponent<Graphic>() != null);
                
                if (!isUIElement)
                {
                    inventoryManager.DropItemToGround(item, isHotbarSlot, slotIndex);
                }
            }
        }
        
        // Drag görselini temizle
        DestroyDragVisual();
    }
    
    private ItemData GetItemFromSlot()
    {
        if (inventoryManager == null) return null;
        
        if (isHotbarSlot)
        {
            // Hotbar slot'undan item al
            if (slotIndex >= 0 && slotIndex < inventoryManager.hotbarSlots.Length)
            {
                int inventoryIndex = inventoryManager.hotbarSlots[slotIndex];
                if (inventoryIndex >= 0 && inventoryIndex < inventoryManager.items.Count && 
                    inventoryManager.items[inventoryIndex] != null && !inventoryManager.items[inventoryIndex].IsEmpty)
                {
                    return inventoryManager.items[inventoryIndex].item;
                }
            }
        }
        else
        {
            // Inventory slot'undan item al
            if (slotIndex >= 0 && slotIndex < inventoryManager.items.Count && 
                inventoryManager.items[slotIndex] != null && !inventoryManager.items[slotIndex].IsEmpty)
            {
                return inventoryManager.items[slotIndex].item;
            }
        }
        
        return null;
    }
    
    private void CreateDragVisual(ItemData item)
    {
        if (canvas == null || item == null || item.itemIcon == null) return;
        
        dragObject = new GameObject("DragIcon");
        dragObject.transform.SetParent(canvas.transform, false);
        dragObject.transform.SetAsLastSibling();
        
        dragImage = dragObject.AddComponent<Image>();
        dragImage.sprite = item.itemIcon;
        dragImage.raycastTarget = false;
        
        RectTransform dragRect = dragObject.GetComponent<RectTransform>();
        dragRect.sizeDelta = new Vector2(50, 50);
    }
    
    private void DestroyDragVisual()
    {
        if (dragObject != null)
        {
            Destroy(dragObject);
            dragObject = null;
            dragImage = null;
        }
    }
    
    private void HandleDrop(InventorySlotDropHandler dropHandler)
    {
        if (inventoryManager == null || !inventoryManager.IsOwner) return;
        
        ItemData sourceItem = GetItemFromSlot();
        if (sourceItem == null) return;
        
        // Aynı slot'a drop edilirse iptal
        if (isHotbarSlot == dropHandler.isHotbarSlot && slotIndex == dropHandler.slotIndex)
        {
            return;
        }
        
        // Swap işlemini yap
        if (isHotbarSlot && dropHandler.isHotbarSlot)
        {
            // Hotbar -> Hotbar swap
            inventoryManager.SwapHotbarSlots(slotIndex, dropHandler.slotIndex);
        }
        else if (!isHotbarSlot && !dropHandler.isHotbarSlot)
        {
            // Inventory -> Inventory swap
            inventoryManager.SwapInventorySlots(slotIndex, dropHandler.slotIndex);
        }
        else if (isHotbarSlot && !dropHandler.isHotbarSlot)
        {
            // Hotbar -> Inventory
            inventoryManager.MoveItemFromHotbarToInventory(slotIndex, dropHandler.slotIndex);
        }
        else // !isHotbarSlot && dropHandler.isHotbarSlot
        {
            // Inventory -> Hotbar
            inventoryManager.MoveItemFromInventoryToHotbar(slotIndex, dropHandler.slotIndex);
        }
    }
    
    private void HandleCraftingDrop(CraftingSlotDropHandler dropHandler)
    {
        if (inventoryManager == null || !inventoryManager.IsOwner) 
        {
            return;
        }
        
        ItemData sourceItem = GetItemFromSlot();
        if (sourceItem == null) 
        {
            return;
        }
        
        // Crafting slot sadece craft malzemesi kabul eder
        bool canAccept = dropHandler.CanAcceptItem(sourceItem);
        if (!canAccept)
        {
            return;
        }
        
        // Input slot'a item ekle
        if (dropHandler.isInputSlot)
        {
            inventoryManager.AddItemToCraftingInput(dropHandler.slotIndex, sourceItem, isHotbarSlot, slotIndex);
        }
    }
}

