using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Bu script, InventoryManager'dan seçili eşyayı nasıl alacağınızı gösterir.
/// Bu dosyayı silmekte özgürsünüz - sadece örnek amaçlı.
/// </summary>
public class InventoryUsageExample : NetworkBehaviour
{
    private InventoryManager inventory;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        
        // InventoryManager'ı bul
        inventory = GetComponentInChildren<InventoryManager>(true);
        
        if (inventory != null)
        {
            // Event'lere abone ol
            inventory.OnItemSelected += OnItemSelected;
            inventory.OnItemDeselected += OnItemDeselected;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Event'lerden aboneliği kaldır
        if (inventory != null)
        {
            inventory.OnItemSelected -= OnItemSelected;
            inventory.OnItemDeselected -= OnItemDeselected;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (inventory == null) return;

        // YÖNTEM 1: Property ile direkt erişim
        if (inventory.HasSelectedItem)
        {
            ItemData selected = inventory.SelectedItem;
        }

        // YÖNTEM 2: Slot index ile kontrol
        if (inventory.SelectedSlotIndex >= 0)
        {
            int slotIndex = inventory.SelectedSlotIndex;
        }

        // YÖNTEM 3: Örnek kullanım - E tuşuna basınca seçili eşyayı kullan
        if (Input.GetKeyDown(KeyCode.E) && inventory.HasSelectedItem)
        {
            UseSelectedItem(inventory.SelectedItem);
        }
    }

    // Event handler: Eşya seçildiğinde çağrılır
    private void OnItemSelected(ItemData item, int slotIndex)
    {
        // Örnek: Seçili eşyaya göre bir şey yap
    }

    // Event handler: Eşya seçimi kaldırıldığında çağrılır
    private void OnItemDeselected()
    {
    }

    // Örnek: Seçili eşyayı kullan
    private void UseSelectedItem(ItemData item)
    {
        // Burada eşyaya özel mantık yazabilirsiniz
        // Örneğin: Anahtar ise kapıyı aç, tripod ise kamera kur, vb.
    }
}

