using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

public class QuestManager : NetworkBehaviour
{
    [Header("Quest Data")]
    [SerializeField] private QuestData[] availableQuests;
    
    [Header("UI References")]
    [SerializeField] private GameObject questPanelObject;
    [SerializeField] private TextMeshProUGUI currentQuestTitleText;
    [SerializeField] private TextMeshProUGUI currentQuestDescriptionText;
    
    // Network-synchronized quest states
    private NetworkVariable<int> currentQuestIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private NetworkList<int> completedQuestIndices = new NetworkList<int>(
        null,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private bool isPanelOpen = false;
    
    public override void OnNetworkSpawn()
    {
        currentQuestIndex.OnValueChanged += OnCurrentQuestChanged;
        completedQuestIndices.OnListChanged += OnCompletedQuestsChanged;
        
        if (IsOwner)
        {
            // Panel'i başlangıçta kapalı yap
            if (questPanelObject != null)
                questPanelObject.SetActive(false);
            
            UpdateQuestUI();
        }
    }
    
    public override void OnNetworkDespawn()
    {
        currentQuestIndex.OnValueChanged -= OnCurrentQuestChanged;
        completedQuestIndices.OnListChanged -= OnCompletedQuestsChanged;
    }
    
    private void Update()
    {
        if (!IsOwner) return;
        
        // M tuşu ile panel aç/kapat
        if (Input.GetKeyDown(KeyCode.M))
        {
            ToggleQuestPanel();
        }
        
        // Panel açıkken ESC ile kapatma
        if (isPanelOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseQuestPanel();
        }
    }
    
    private void ToggleQuestPanel()
    {
        if (questPanelObject == null)
        {
            Debug.LogWarning("[QuestManager] Quest panel object is not assigned!");
            return;
        }
        
        isPanelOpen = !isPanelOpen;
        questPanelObject.SetActive(isPanelOpen);
        
        // Panel açıldığında UI'ı güncelle
        if (isPanelOpen)
        {
            UpdateQuestUI();
            // Mouse cursor'ı göster
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // Mouse cursor'ı kilitle
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
    
    private void CloseQuestPanel()
    {
        if (questPanelObject == null) return;
        
        isPanelOpen = false;
        questPanelObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    
    private void UpdateQuestUI()
    {
        UpdateCurrentQuestDisplay();
    }
    
    private void UpdateCurrentQuestDisplay()
    {
        if (currentQuestIndex.Value < 0 || currentQuestIndex.Value >= availableQuests.Length)
        {
            if (currentQuestTitleText != null)
                currentQuestTitleText.text = "Güncel Görev Yok";
            if (currentQuestDescriptionText != null)
                currentQuestDescriptionText.text = "Aktif bir görev bulunmuyor.";
            return;
        }
        
        QuestData currentQuest = availableQuests[currentQuestIndex.Value];
        
        if (currentQuest == null)
        {
            Debug.LogWarning($"[QuestManager] Quest at index {currentQuestIndex.Value} is null!");
            return;
        }
        
        if (currentQuestTitleText != null)
            currentQuestTitleText.text = currentQuest.questTitle;
        
        if (currentQuestDescriptionText != null)
            currentQuestDescriptionText.text = currentQuest.questDescription;
    }
    
    private void OnCurrentQuestChanged(int oldValue, int newValue)
    {
        if (IsOwner)
            UpdateCurrentQuestDisplay();
    }
    
    private void OnCompletedQuestsChanged(NetworkListEvent<int> changeEvent)
    {
        // Geçmiş görevler değiştiğinde NotebookUI'yi güncellemek için event tetiklenebilir
        // Şimdilik sadece log
        if (IsOwner)
        {
            Debug.Log($"[QuestManager] Completed quests changed. Total: {completedQuestIndices.Count}");
        }
    }
    
    // Server-side görev yönetimi metodları
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetCurrentQuestServerRpc(int questIndex)
    {
        if (questIndex < -1 || questIndex >= availableQuests.Length) 
        {
            Debug.LogWarning($"[QuestManager] Invalid quest index: {questIndex}");
            return;
        }
        
        if (questIndex < 0)
        {
            currentQuestIndex.Value = -1;
            return;
        }
        
        // Eski görevi tamamlananlar listesine ekle (eğer aktif bir görev varsa)
        if (currentQuestIndex.Value >= 0 && currentQuestIndex.Value < availableQuests.Length)
        {
            if (!completedQuestIndices.Contains(currentQuestIndex.Value))
            {
                completedQuestIndices.Add(currentQuestIndex.Value);
            }
        }
        
        currentQuestIndex.Value = questIndex;
    }
    
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CompleteCurrentQuestServerRpc()
    {
        if (currentQuestIndex.Value < 0) 
        {
            Debug.LogWarning("[QuestManager] No active quest to complete!");
            return;
        }
        
        int completedIndex = currentQuestIndex.Value;
        if (!completedQuestIndices.Contains(completedIndex))
        {
            completedQuestIndices.Add(completedIndex);
        }
        
        currentQuestIndex.Value = -1; // Görev yok
    }
    
    // Test için editor'da kullanılabilir (Owner only)
    public void SetCurrentQuest(int questIndex)
    {
        if (!IsOwner)
        {
            Debug.LogWarning("[QuestManager] Only owner can set quest locally!");
            return;
        }
        
        SetCurrentQuestServerRpc(questIndex);
    }
    
    public void CompleteCurrentQuest()
    {
        if (!IsOwner)
        {
            Debug.LogWarning("[QuestManager] Only owner can complete quest locally!");
            return;
        }
        
        CompleteCurrentQuestServerRpc();
    }
    
    // Getter metodları - NotebookUI için
    public QuestData GetCurrentQuest()
    {
        if (currentQuestIndex.Value < 0 || currentQuestIndex.Value >= availableQuests.Length)
            return null;
        
        return availableQuests[currentQuestIndex.Value];
    }
    
    public List<int> GetCompletedQuestIndices()
    {
        List<int> result = new List<int>();
        foreach (int index in completedQuestIndices)
        {
            result.Add(index);
        }
        return result;
    }
    
    public QuestData[] GetAvailableQuests()
    {
        return availableQuests;
    }
    
    public bool IsPanelOpen => isPanelOpen;
}
