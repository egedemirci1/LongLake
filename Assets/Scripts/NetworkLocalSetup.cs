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

    [Header("Spawn Position")]
    [SerializeField] private Vector3 startPosition = new Vector3(1611f, 135f, 633f);

    [Header("Character Selection")]
    [SerializeField] private GameObject selectionPanel; // Auto-found at runtime (CharacterSelector'daki panel)

    private bool sceneEventHooked;
    private bool controlsEnabled = false;

    public override void OnNetworkSpawn()
    {
        if (IsServer || IsOwner) TryTeleportToStart();

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
        
        // Karakter seçim panelini otomatik bul
        FindSelectionPanel();
        
        // Başlangıçta kontrolleri devre dışı bırak (karakter seçilene kadar)
        DisableControls();
        SafeDisableInteraction();
        controlsEnabled = false;
        
        HookSceneEventsWithRetry();
        TryEnableInteractionIfAlreadyInGameplayScene();
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
        transform.position = startPosition;
        if (cc != null) cc.enabled = true;
    }

    private void FindSelectionPanel()
    {
        // Eğer manuel atanmamışsa, CharacterSelector'dan otomatik bul
        if (selectionPanel == null)
        {
            CharacterSelector selector = FindObjectOfType<CharacterSelector>();
            if (selector != null && selector.selectionPanel != null)
            {
                selectionPanel = selector.selectionPanel;
                Debug.Log($"[NetworkLocalSetup] Selection panel auto-found: {selectionPanel.name}");
            }
            else
            {
                Debug.LogWarning("[NetworkLocalSetup] CharacterSelector or selectionPanel not found. Controls will work but panel check will be skipped.");
            }
        }
    }

    private void EnableControlsWithoutInteraction() { if (playerController) playerController.enabled = true; }
    private void DisableControls() { if (playerController) playerController.enabled = false; }

    private void OnNetcodeSceneLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (!IsOwner) return;
        if (sceneName == gameplaySceneName) SafeEnableInteraction();
        else SafeDisableInteraction();
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
    private void TryEnableInteractionIfAlreadyInGameplayScene() { if (SceneManager.GetActiveScene().name == gameplaySceneName) SafeEnableInteraction(); }

    // CharacterSelector'dan çağrılacak - karakter seçildiğinde kontrolleri aktif et
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
        if (!IsOwner) return;
        
        // Karakter seçim paneli açıksa kontrolleri devre dışı bırak
        if (selectionPanel != null && selectionPanel.activeSelf)
        {
            if (controlsEnabled)
            {
                DisableControls();
                SafeDisableInteraction();
                controlsEnabled = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}