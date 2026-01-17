using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

public class NotebookUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject notebookPanelObject;
    [SerializeField] private Image notebookBackgroundImage; // Defter görünümü için arka plan
    [SerializeField] private Transform questEntriesContainer;
    [SerializeField] private GameObject questEntryPrefab;
    [SerializeField] private TextMeshProUGUI emptyNotebookText;
    
    [Header("Quest Manager")]
    [SerializeField] private QuestManager questManager;
    
    [Header("Notebook Item Check")]
    [SerializeField] private InventoryManager inventoryManager;
    [SerializeField] private string notebookItemID = "notebook"; // Not defteri item ID'si
    
    private List<QuestUIItem> questEntryItems = new List<QuestUIItem>();
    private bool isNotebookOpen = false;
    
    private void Start()
    {
        Debug.Log("[NotebookUI] Start() called!");
        
        // Notebook başlangıçta kapalı
        if (notebookPanelObject != null)
            notebookPanelObject.SetActive(false);
        
        // Background image'i otomatik bul (eğer atanmamışsa)
        if (notebookBackgroundImage == null && notebookPanelObject != null)
        {
            // NotebookPanel'in kendisinde Image component'i var mı kontrol et
            notebookBackgroundImage = notebookPanelObject.GetComponent<Image>();
            
            // Yoksa child'ında Background adında bir GameObject ara
            if (notebookBackgroundImage == null)
            {
                Transform backgroundTransform = notebookPanelObject.transform.Find("Background");
                if (backgroundTransform != null)
                {
                    notebookBackgroundImage = backgroundTransform.GetComponent<Image>();
                }
            }
        }
        
        // QuestManager'ı otomatik bul (Player'da olmalı)
        if (questManager == null)
            questManager = GetComponentInParent<QuestManager>();
        
        if (questManager == null)
        {
            // Daha geniş arama
            questManager = FindFirstObjectByType<QuestManager>();
        }
        
        if (questManager == null)
        {
            Debug.LogWarning("[NotebookUI] QuestManager not found! Notebook will not be able to display quests.");
        }
        
        // InventoryManager'ı otomatik bul (Local Player'da olmalı)
        if (inventoryManager == null)
        {
            // Önce NetworkManager üzerinden local player'ı bul
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localPlayer != null)
                {
                    inventoryManager = localPlayer.GetComponentInChildren<InventoryManager>(true);
                }
            }
            
            // Eğer hala bulunamadıysa, FindObjectOfType ile dene
            if (inventoryManager == null)
            {
                inventoryManager = FindFirstObjectByType<InventoryManager>();
            }
        }
    }
    
    private void Update()
    {
        // Debug: İlk birkaç frame'de çalışıp çalışmadığını kontrol et
        if (Time.frameCount <= 5)
        {
            Debug.Log($"[NotebookUI] Update() çalışıyor! Frame: {Time.frameCount}");
        }
        
        // InventoryManager'ı tekrar ara (eğer Start()'ta bulunamadıysa - network spawn gecikmesi için)
        if (inventoryManager == null)
        {
            // NetworkManager üzerinden local player'ı bul
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                var localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localPlayer != null)
                {
                    inventoryManager = localPlayer.GetComponentInChildren<InventoryManager>(true);
                    if (inventoryManager != null)
                    {
                        Debug.Log("[NotebookUI] InventoryManager found in Update()!");
                    }
                }
            }
            
            // Eğer hala bulunamadıysa, FindObjectOfType ile dene (fallback)
            if (inventoryManager == null)
            {
                inventoryManager = FindFirstObjectByType<InventoryManager>();
            }
        }
        
        // L tuşu ile notebook aç/kapat (sadece envanterde notebook varsa)
        if (Input.GetKeyDown(KeyCode.L))
        {
            Debug.Log("[NotebookUI] L tuşuna basıldı!");
            
            if (inventoryManager == null)
            {
                Debug.LogWarning("[NotebookUI] L tuşu: InventoryManager hala null!");
                return;
            }
            
            // Envanterde notebook var mı kontrol et
            if (HasNotebook())
            {
                Debug.Log("[NotebookUI] Notebook bulundu, açılıyor...");
                ToggleNotebook();
            }
            else
            {
                Debug.Log("[NotebookUI] Envanterde not defteri yok!");
            }
        }
        
        // Notebook açıkken ESC ile kapat
        if (isNotebookOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseNotebook();
        }
    }
    
    private bool HasNotebook()
    {
        if (inventoryManager == null)
        {
            Debug.LogWarning("[NotebookUI] InventoryManager not found! Cannot check for notebook.");
            return false;
        }
        
        // Envanterde notebook var mı kontrol et
        bool hasItem = inventoryManager.HasItem(notebookItemID);
        Debug.Log($"[NotebookUI] HasNotebook check: {hasItem} (ItemID: {notebookItemID})");
        return hasItem;
    }
    
    public void OpenNotebook()
    {
        if (notebookPanelObject == null)
        {
            Debug.LogWarning("[NotebookUI] Notebook panel object is not assigned!");
            return;
        }
        
        isNotebookOpen = true;
        notebookPanelObject.SetActive(true);
        
        // Mouse cursor'ı göster
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        // Geçmiş görevleri güncelle
        UpdateQuestEntries();
    }
    
    public void CloseNotebook()
    {
        if (notebookPanelObject == null) return;
        
        isNotebookOpen = false;
        notebookPanelObject.SetActive(false);
        
        // Mouse cursor'ı kilitle
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    
    public void ToggleNotebook()
    {
        if (isNotebookOpen)
            CloseNotebook();
        else
            OpenNotebook();
    }
    
    private void UpdateQuestEntries()
    {
        if (questEntriesContainer == null || questEntryPrefab == null)
        {
            Debug.LogWarning("[NotebookUI] Quest entries container or prefab not assigned!");
            return;
        }
        
        if (questManager == null)
        {
            Debug.LogWarning("[NotebookUI] QuestManager is null! Cannot update quest entries.");
            if (emptyNotebookText != null)
                emptyNotebookText.text = "QuestManager bulunamadı.";
            return;
        }
        
        // Mevcut quest entry'leri temizle
        foreach (var item in questEntryItems)
        {
            if (item != null && item.gameObject != null)
                Destroy(item.gameObject);
        }
        questEntryItems.Clear();
        
        // QuestManager'dan tamamlanan görevleri al
        List<int> completedQuestIndices = questManager.GetCompletedQuestIndices();
        QuestData[] availableQuests = questManager.GetAvailableQuests();
        
        if (completedQuestIndices == null || completedQuestIndices.Count == 0 || 
            availableQuests == null || availableQuests.Length == 0)
        {
            // Geçmiş görev yok
            if (emptyNotebookText != null)
                emptyNotebookText.text = "Henüz tamamlanmış görev yok.";
            else
                Debug.Log("[NotebookUI] No completed quests found.");
            return;
        }
        
        // Empty text'i gizle
        if (emptyNotebookText != null)
            emptyNotebookText.gameObject.SetActive(false);
        
        // Her tamamlanan görev için entry oluştur
        foreach (int questIndex in completedQuestIndices)
        {
            if (questIndex < 0 || questIndex >= availableQuests.Length) continue;
            
            QuestData quest = availableQuests[questIndex];
            if (quest == null) continue;
            
            // Quest entry prefab'ını instantiate et
            GameObject questEntryObj = Instantiate(questEntryPrefab, questEntriesContainer);
            QuestUIItem questUIItem = questEntryObj.GetComponent<QuestUIItem>();
            
            if (questUIItem != null)
            {
                questUIItem.SetupQuest(quest);
                questEntryItems.Add(questUIItem);
            }
            else
            {
                Debug.LogWarning("[NotebookUI] QuestEntryPrefab doesn't have QuestUIItem component!");
                Destroy(questEntryObj);
            }
        }
        
        Debug.Log($"[NotebookUI] Updated notebook with {questEntryItems.Count} completed quest entries.");
    }
    
    public bool IsNotebookOpen => isNotebookOpen;
}
