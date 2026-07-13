using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public class CharacterSelector : MonoBehaviour
{
    public GameObject selectionPanel;

    [Header("Optional UI")]
    [SerializeField] private Button ahuButton;
    [SerializeField] private Button yamanButton;
    [SerializeField] private TextMeshProUGUI statusText;

    private bool waitingForServer;

    private void OnEnable()
    {
        PlayerController.OnLocalCharacterSelectResult += OnSelectResult;
        TryAutoFindButtons();
    }

    private void OnDisable()
    {
        PlayerController.OnLocalCharacterSelectResult -= OnSelectResult;
    }

    private void Update()
    {
        if (selectionPanel == null || !selectionPanel.activeSelf) return;
        RefreshButtonStates();
    }

    public void SelectAhu() => SetCharacter(0);
    public void SelectYaman() => SetCharacter(1);

    private void SetCharacter(int index)
    {
        if (waitingForServer) return;

        if (NetworkManager.Singleton == null ||
            NetworkManager.Singleton.LocalClient == null ||
            NetworkManager.Singleton.LocalClient.PlayerObject == null)
        {
            Debug.LogWarning("[CharacterSelector] Network not ready / player not spawned. Start from MainMenu → Host.");
            SetStatus("Ağ hazır değil. MainMenu → Host ile başla.");
            return;
        }

        if (PlayerController.IsCharacterIndexTaken(index))
        {
            SetStatus(index == 0 ? "Ahu zaten seçildi!" : "Yaman zaten seçildi!");
            RefreshButtonStates();
            return;
        }

        var pc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
        if (pc == null) return;

        waitingForServer = true;
        SetStatus("Seçim onaylanıyor...");
        pc.SelectCharacter(index);
    }

    private void OnSelectResult(bool success, int index)
    {
        waitingForServer = false;

        if (!success)
        {
            SetStatus(index == 0 ? "Ahu zaten seçildi!" : "Yaman zaten seçildi!");
            RefreshButtonStates();
            return;
        }

        // Accepted — close panel & enable controls
        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        var playerObject = NetworkManager.Singleton?.LocalClient?.PlayerObject;
        var setup = playerObject != null ? playerObject.GetComponent<NetworkLocalSetup>() : null;

        if (setup != null)
            setup.EnableControlsAfterCharacterSelection();
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        var quests = QuestManager.Instance != null
            ? QuestManager.Instance
            : FindFirstObjectByType<QuestManager>();
        if (quests != null)
            quests.RequestStartOpeningQuest();
        else
            Debug.LogWarning("[CharacterSelector] QuestManager not found in scene.");
    }

    private void RefreshButtonStates()
    {
        bool ahuTaken = PlayerController.IsCharacterIndexTaken(0);
        bool yamanTaken = PlayerController.IsCharacterIndexTaken(1);

        if (ahuButton != null)
            ahuButton.interactable = !waitingForServer && !ahuTaken;
        if (yamanButton != null)
            yamanButton.interactable = !waitingForServer && !yamanTaken;

        if (statusText == null || waitingForServer) return;

        if (ahuTaken && !yamanTaken)
            statusText.text = "Ahu alındı — Yaman'ı seç.";
        else if (yamanTaken && !ahuTaken)
            statusText.text = "Yaman alındı — Ahu'yu seç.";
        else if (!ahuTaken && !yamanTaken)
            statusText.text = "Karakterini seç.";
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
        else
            Debug.Log($"[CharacterSelector] {message}");
    }

    private void TryAutoFindButtons()
    {
        if (selectionPanel == null) return;
        if (ahuButton != null && yamanButton != null) return;

        var buttons = selectionPanel.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                string method = button.onClick.GetPersistentMethodName(i);
                if (method == "SelectAhu" && ahuButton == null)
                    ahuButton = button;
                else if (method == "SelectYaman" && yamanButton == null)
                    yamanButton = button;
            }
        }
    }
}
