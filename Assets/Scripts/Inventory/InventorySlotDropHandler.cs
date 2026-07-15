using UnityEngine;
using UnityEngine.EventSystems;

public class InventorySlotDropHandler : MonoBehaviour, IDropHandler
{
    [Header("Slot Info")]
    public bool isHotbarSlot = false; // true = hotbar slot, false = inventory slot
    public int slotIndex = -1; // Hotbar için 0-4, Inventory için 0-11
    
    public void OnDrop(PointerEventData eventData)
    {
        // Drag handler zaten drop işlemini handle ediyor
        // Bu metod sadece drop target olarak işaretlemek için
    }
}

