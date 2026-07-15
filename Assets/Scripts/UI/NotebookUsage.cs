using UnityEngine;
using Unity.Netcode;

public class NotebookUsage : NetworkBehaviour
{
    [Header("Notebook Settings")]
    [SerializeField] private string notebookItemID = "notebook"; // Not defteri item ID'si
    
    [Header("References")]
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private NotebookUI notebookUI;
    [SerializeField] private PlayerController playerController;
    
    private void Start()
    {
        // Referansları otomatik bul
        if (inventoryManager == null)
            inventoryManager = GetComponentInChildren<InventoryManager>(true);
        
        if (notebookUI == null)
            notebookUI = FindFirstObjectByType<NotebookUI>();
        
        if (playerController == null)
            playerController = GetComponent<PlayerController>();
    }
    
    private void Update()
    {
        if (!IsOwner) return;
        
        if (inventoryManager == null || notebookUI == null) return;
        
        // Eğer notebook açıksa ve başka bir UI açıksa, önce onları kontrol et
        if (notebookUI.IsNotebookOpen)
        {
            // Notebook açıkken ESC ile kapanıyor zaten (NotebookUI'de)
            return;
        }
        
        // Hotbar'dan seçili item'ı kontrol et
        if (inventoryManager.HasSelectedItem)
        {
            ItemData selectedItem = inventoryManager.SelectedItem;
            
            // Eğer seçili item notebook ise ve E tuşuna basılırsa notebook'u aç
            if (selectedItem != null && selectedItem.itemID == notebookItemID)
            {
                if (Input.GetKeyDown(KeyCode.E))
                {
                    OpenNotebook();
                }
            }
        }
    }
    
    private void OpenNotebook()
    {
        if (notebookUI == null)
        {
            Debug.LogWarning("[NotebookUsage] NotebookUI not found!");
            return;
        }
        
        // Diğer UI'ları kontrol et (envanter, quest panel vb. açıksa kapatılabilir veya kapatılmaz)
        // Şimdilik sadece notebook'u aç
        
        notebookUI.OpenNotebook();
        
        // Kamera hareketini durdur (PlayerController'da kontrol edilmeli)
        // NotebookUI açıkken PlayerController'da kamera kontrolü yapılmalı
    }
    
    public void SetNotebookItemID(string itemID)
    {
        notebookItemID = itemID;
    }
}
