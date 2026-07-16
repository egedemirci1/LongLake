using UnityEngine;
using UnityEngine.EventSystems;

public class CraftingSlotDropHandler : MonoBehaviour, IDropHandler, IPointerClickHandler
{
    [Header("Slot Info")]
    public bool isInputSlot = true; // true = input slot, false = output slot
    public int slotIndex = -1; // Input için 0-2, Output için 0
    
    private InventoryManager inventoryManager;
    
    private void Start()
    {
        inventoryManager = GetComponentInParent<InventoryManager>();
        if (inventoryManager == null)
        {
            inventoryManager = FindFirstObjectByType<InventoryManager>();
        }
    }
    
    public void OnDrop(PointerEventData eventData)
    {
        // Drag handler zaten drop işlemini handle ediyor
        // Bu metod sadece drop target olarak işaretlemek için
    }
    
    public bool CanAcceptItem(ItemData item)
    {
        if (item == null) return false;
        
        // Output slot'a item drop edilemez (sadece craft sonucu olarak dolar)
        if (!isInputSlot) return false;
        
        // Sadece craft malzemesi olan item'lar kabul edilir
        return item.isMaterial;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        GameAudio.PlayClick();
    }
}

