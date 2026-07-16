using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using UnityEngine.SceneManagement;

public class NetworkLocalSetup : NetworkBehaviour
{
    [Header("Components")]
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerInteraction playerInteraction;

    [Header("Camera Settings")]
    [SerializeField] private CinemachineVirtualCamera vcam;
    [SerializeField] private GameObject cameraRoot;

    [Header("Scene")]
    [SerializeField] private string gameplaySceneName = "CrashSite_Main";

    [Header("Character Spawn Positions")]
    [SerializeField] private Vector3 ahuSpawnPosition = new Vector3(1611f, 135f, 633f);
    [SerializeField] private Vector3 yamanSpawnPosition = new Vector3(1650f, 135f, 650f);

    private bool sceneEventHooked;
    private bool controlsEnabled = false;
    private InventoryManager inventoryManager;
    private NotebookUI notebookUI;
    private bool _lobbyVisualsHidden;

    public Vector3 YamanSpawnPosition => yamanSpawnPosition;

    public override void OnNetworkSpawn()
    {
        bool inGameplay = IsGameplayScene();

        // MainMenu / lobide PlayerCapsule hiç görünmesin (owner + remote).
        if (!inGameplay)
            SetWorldVisualsVisible(false);

        HookSceneEventsWithRetry();

        if (!IsOwner)
        {
            DisableControls();
            if (vcam != null) vcam.enabled = false;
            return;
        }

        inventoryManager = GetComponent<InventoryManager>();

        if (!inGameplay)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (vcam != null) vcam.enabled = false;
        }
        else
        {
            SetupCameraRig();
        }

        // Karakter seçimi lobby'de yapılır; gameplay sahnesine geçilene kadar kontroller kapalı.
        DisableControls();
        SafeDisableInteraction();
        controlsEnabled = false;

        TryEnableControlsIfAlreadyInGameplayScene();
    }

    private bool IsGameplayScene()
    {
        return SceneManager.GetActiveScene().name == gameplaySceneName;
    }

    /// <summary>
    /// Lobide mesh/capsule gizle; GameObject kapatma (NetworkAnimator uyarısı olmasın).
    /// </summary>
    private void SetWorldVisualsVisible(bool visible)
    {
        _lobbyVisualsHidden = !visible;
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }

    private void SetupCameraRig()
    {
        if (playerController != null && Camera.main != null)
            playerController.cameraTransform = Camera.main.transform;

        if (vcam != null && cameraRoot != null)
        {
            vcam.Follow = cameraRoot.transform;
            vcam.LookAt = cameraRoot.transform;
            vcam.enabled = true;
            vcam.Priority = 100;
        }
    }

    private void TryTeleportToStart()
    {
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Vector3 spawnPos = GetSpawnPosition();
        transform.position = spawnPos;

        if (cc != null) cc.enabled = true;
    }

    private Vector3 GetSpawnPosition()
    {
        int selectedCharacter = playerController != null
            ? playerController.characterIndex.Value
            : -1;

        if (selectedCharacter == 0)
            return ahuSpawnPosition;
        if (selectedCharacter == 1)
            return yamanSpawnPosition;

        Debug.LogWarning(
            $"[NetworkLocalSetup] Character is not selected for client {OwnerClientId}; using Ahu spawn as fallback.");
        return ahuSpawnPosition;
    }

    private void EnableControlsWithoutInteraction() { if (playerController) playerController.enabled = true; }
    private void DisableControls() { if (playerController) playerController.enabled = false; }

    private void OnNetcodeSceneLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameplaySceneName)
        {
            if (IsOwner)
                SafeDisableInteraction();
            return;
        }

        SetWorldVisualsVisible(true);
        if (IsOwner)
            EnterGameplay();
    }

    /// <summary>Karakter lobby'de seçildi; gameplay sahnesine girince doğru noktaya taşı, kontrolleri aç ve görevi başlat.</summary>
    private void EnterGameplay()
    {
        SetWorldVisualsVisible(true);
        SetupCameraRig();
        TryTeleportToStart();
        EnableControlsAfterCharacterSelection();

        var quests = QuestManager.Instance != null
            ? QuestManager.Instance
            : FindFirstObjectByType<QuestManager>();
        if (quests != null)
            quests.RequestStartOpeningQuest();
        else
            Debug.LogWarning("[NetworkLocalSetup] QuestManager not found in gameplay scene.");
    }

    private void HookSceneEventsWithRetry() => StartCoroutine(SceneHookRetryRoutine(5, 0.2f));
    private System.Collections.IEnumerator SceneHookRetryRoutine(int tries, float waitSeconds)
    {
        for (int i = 0; i < tries; i++)
        {
            if (TryHookSceneEvents()) yield break;
            yield return new WaitForSeconds(waitSeconds);
        }
    }

    private bool TryHookSceneEvents()
    {
        if (sceneEventHooked) return true;
        var nm = NetworkManager.Singleton;
        if (nm?.SceneManager == null) return false;
        nm.SceneManager.OnLoadEventCompleted += OnNetcodeSceneLoadCompleted;
        sceneEventHooked = true;
        return true;
    }

    private void SafeEnableInteraction() { if (playerInteraction) playerInteraction.enabled = true; }
    private void SafeDisableInteraction() { if (playerInteraction) playerInteraction.enabled = false; }

    private void TryEnableControlsIfAlreadyInGameplayScene()
    {
        if (IsGameplayScene())
            EnterGameplay();
    }

    public void EnableControlsAfterCharacterSelection()
    {
        if (!IsOwner) return;

        EnableControlsWithoutInteraction();
        SafeEnableInteraction();
        controlsEnabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LateUpdate()
    {
        // Karakter modeli seçilince Renderer'lar tekrar açılabilir — lobide kapalı tut.
        if (_lobbyVisualsHidden && !IsGameplayScene())
            SetWorldVisualsVisible(false);
    }

    private void Update()
    {
        if (!IsOwner || !controlsEnabled) return;

        if (AnyUiWantsCursor()) return;

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>Cursor'u serbest bırakması gereken bir UI (diyalog, envanter, not defteri, görev paneli) açık mı?</summary>
    private bool AnyUiWantsCursor()
    {
        if (DialogueManager.IsDialogueOpen) return true;

        if (inventoryManager == null) inventoryManager = GetComponent<InventoryManager>();
        if (inventoryManager != null && inventoryManager.mainInventoryObject != null &&
            inventoryManager.mainInventoryObject.activeSelf)
            return true;

        if (notebookUI == null) notebookUI = FindFirstObjectByType<NotebookUI>();
        if (notebookUI != null && notebookUI.IsNotebookOpen) return true;

        if (QuestManager.Instance != null && QuestManager.Instance.IsPanelOpen) return true;

        return false;
    }

    public override void OnNetworkDespawn()
    {
        if (sceneEventHooked && NetworkManager.Singleton?.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnNetcodeSceneLoadCompleted;
        sceneEventHooked = false;
        base.OnNetworkDespawn();
    }
}
