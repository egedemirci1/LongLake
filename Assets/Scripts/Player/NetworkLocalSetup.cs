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

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            DisableControls();
            if (vcam != null) vcam.enabled = false;
            return;
        }

        // --- MOUSE SORUNUNUN ��Z�M� ---
        // Se�im ekran�nda mouse'un gelmesini sa�lar
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SetupCameraRig();

        // Karakter seçimi lobby'de yapılır; gameplay sahnesine geçilene kadar kontroller kapalı.
        DisableControls();
        SafeDisableInteraction();
        controlsEnabled = false;

        HookSceneEventsWithRetry();
        TryEnableControlsIfAlreadyInGameplayScene();
    }

    private void SetupCameraRig()
    {
        // Y�n sorunu ��z�m�: Kameray� PlayerController'a ba�l�yoruz
        if (playerController != null)
        {
            playerController.cameraTransform = Camera.main.transform;
        }

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
        
        // Her oyuncu için farklı spawn pozisyonu hesapla
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
        if (!IsOwner) return;
        if (sceneName == gameplaySceneName) EnterGameplay();
        else SafeDisableInteraction();
    }

    /// <summary>Karakter lobby'de seçildi; gameplay sahnesine girince doğru noktaya taşı, kontrolleri aç ve görevi başlat.</summary>
    private void EnterGameplay()
    {
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
        for (int i = 0; i < tries; i++) { if (TryHookSceneEvents()) yield break; yield return new WaitForSeconds(waitSeconds); }
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
    private void TryEnableControlsIfAlreadyInGameplayScene() { if (SceneManager.GetActiveScene().name == gameplaySceneName) EnterGameplay(); }

    public void EnableControlsAfterCharacterSelection()
    {
        if (!IsOwner) return;
        
        EnableControlsWithoutInteraction();
        SafeEnableInteraction();
        controlsEnabled = true;
        
        // Mouse'u kilitle
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (!IsOwner || !controlsEnabled) return;

        // Diyalog vb. cursor'u serbest bırakabilir; ona karışma.
        if (DialogueManager.IsDialogueOpen) return;

        // Kontroller aktifse cursor'un lock olduğundan emin ol
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}