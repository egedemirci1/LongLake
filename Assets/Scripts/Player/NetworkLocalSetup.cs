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
    [Tooltip("Y yok sayılır — terrain raycast ile oturtulur. Sadece XZ önemli.")]
    [SerializeField] private Vector3 ahuSpawnPosition = new Vector3(1755.412f, 56f, 528f);
    [SerializeField] private Vector3 yamanSpawnPosition = new Vector3(1753.697f, 56f, 523f);

    [Header("Spawn Ground Snap")]
    [SerializeField] private int spawnSettleFrames = 2;
    [SerializeField] private float spawnRaycastHeight = 500f;
    [SerializeField] private float spawnGroundPadding = 0.05f;

    private bool sceneEventHooked;
    private bool controlsEnabled = false;
    private Coroutine _enterGameplayRoutine;
    private InventoryManager inventoryManager;
    private NotebookUI notebookUI;
    private bool _lobbyVisualsHidden;
    private int _cameraWarmupFrames;

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
        string sceneName = SceneManager.GetActiveScene().name;
        return sceneName == gameplaySceneName || sceneName == "TestScene";
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
        if (!IsOwner)
            return;

        ResolveCameraReferences();

        CinemachineBrain brain = EnsureGameplayBrain();
        Camera outputCamera = brain != null ? brain.OutputCamera : Camera.main;
        if (playerController != null && outputCamera != null)
            playerController.cameraTransform = outputCamera.transform;

        if (vcam == null || cameraRoot == null)
        {
            Debug.LogWarning("[NetworkLocalSetup] vcam or cameraRoot missing — Main Camera may stay at scene origin.");
            return;
        }

        // FP rig: collider can shove the camera underground when distance is zero.
        var vcamCollider = vcam.GetComponent<CinemachineCollider>();
        if (vcamCollider != null)
            vcamCollider.enabled = false;

        vcam.Follow = cameraRoot.transform;
        vcam.LookAt = cameraRoot.transform;
        vcam.enabled = true;
        vcam.Priority = 100;

        if (brain != null)
        {
            brain.enabled = true;
            brain.ManualUpdate();
        }
        else
        {
            SnapMainCameraToEye();
        }

        _cameraWarmupFrames = 10;
    }

    private void ResolveCameraReferences()
    {
        if (cameraRoot == null)
        {
            var target = transform.Find("PlayerCameraRoot");
            if (target != null)
                cameraRoot = target.gameObject;
        }

        if (vcam == null)
            vcam = GetComponentInChildren<CinemachineVirtualCamera>(true);
    }

    private static CinemachineBrain EnsureGameplayBrain()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].CompareTag("MainCamera"))
                {
                    cam = cameras[i];
                    break;
                }
            }
        }

        if (cam == null)
        {
            Debug.LogWarning("[NetworkLocalSetup] No Main Camera found in gameplay scene.");
            return null;
        }

        if (!cam.TryGetComponent(out CinemachineBrain brain))
            brain = cam.gameObject.AddComponent<CinemachineBrain>();

        return brain;
    }

    private static CinemachineBrain FindGameplayBrain()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.GetComponent<CinemachineBrain>() : null;
    }

    private void SnapMainCameraToEye()
    {
        if (cameraRoot == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Transform eye = cameraRoot.transform;
        cam.transform.SetPositionAndRotation(eye.position, eye.rotation);
    }

    private void TryTeleportToStart()
    {
        Vector3 spawnPos = SnapPositionToGround(GetSpawnPosition());
        ApplyPosition(spawnPos);
    }

    private void ApplyPosition(Vector3 worldPos)
    {
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        transform.position = worldPos;

        if (cc != null) cc.enabled = true;
    }

    /// <summary>XZ spawn noktasında önce terrain — gökyüzündeki dev collider'ları atla.</summary>
    private Vector3 SnapPositionToGround(Vector3 spawnPos)
    {
        float feetToPivot = GetFeetToPivotOffset();
        float terrainY = SampleTerrainHeight(spawnPos.x, spawnPos.z);

        if (!float.IsNegativeInfinity(terrainY))
        {
            const float maxAboveTerrain = 6f;
            Vector3 rayOrigin = new Vector3(spawnPos.x, terrainY + maxAboveTerrain + 2f, spawnPos.z);
            float rayLen = maxAboveTerrain + 4f;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayLen, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.point.y <= terrainY + maxAboveTerrain)
                {
                    float y = hit.point.y + spawnGroundPadding + feetToPivot;
                    return new Vector3(spawnPos.x, y, spawnPos.z);
                }
            }

            float yTerrain = terrainY + spawnGroundPadding + feetToPivot;
            return new Vector3(spawnPos.x, yTerrain, spawnPos.z);
        }

        Vector3 fallbackOrigin = new Vector3(spawnPos.x, spawnRaycastHeight, spawnPos.z);
        const float maxDistance = 1200f;
        if (Physics.Raycast(fallbackOrigin, Vector3.down, out RaycastHit fallbackHit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            float y = fallbackHit.point.y + spawnGroundPadding + feetToPivot;
            return new Vector3(spawnPos.x, y, spawnPos.z);
        }

        Debug.LogWarning($"[NetworkLocalSetup] Ground snap failed at {spawnPos}; using configured Y.");
        return spawnPos;
    }

    private float GetFeetToPivotOffset()
    {
        var cc = GetComponent<CharacterController>();
        if (cc == null) return 0f;
        return cc.center.y - cc.height * 0.5f;
    }

    private static float SampleTerrainHeight(float worldX, float worldZ)
    {
        float best = float.NegativeInfinity;
        var terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
            return best;

        var probe = new Vector3(worldX, 0f, worldZ);
        foreach (var terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;

            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float lx = worldX - tp.x;
            float lz = worldZ - tp.z;
            if (lx < 0f || lz < 0f || lx > size.x || lz > size.z)
                continue;

            float h = terrain.SampleHeight(probe) + tp.y;
            if (h > best)
                best = h;
        }

        return best;
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
        if (!IsOwner) return;

        if (_enterGameplayRoutine != null)
            StopCoroutine(_enterGameplayRoutine);
        _enterGameplayRoutine = StartCoroutine(EnterGameplayRoutine());
    }

    private System.Collections.IEnumerator EnterGameplayRoutine()
    {
        SetWorldVisualsVisible(true);
        SetupCameraRig();

        for (int i = 0; i < spawnSettleFrames; i++)
            yield return null;

        Physics.SyncTransforms();
        yield return null;

        TryTeleportToStart();

        // Bir frame daha — CharacterController zemini tanısın, sonra yerçekimi.
        yield return null;
        Physics.SyncTransforms();

        SetupCameraRig();

        EnableControlsAfterCharacterSelection();

        var quests = QuestManager.Instance != null
            ? QuestManager.Instance
            : FindFirstObjectByType<QuestManager>();
        if (quests != null)
            quests.RequestStartOpeningQuest();
        else
            Debug.LogWarning("[NetworkLocalSetup] QuestManager not found in gameplay scene.");

        _enterGameplayRoutine = null;
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

        if (!IsOwner || !IsGameplayScene() || _cameraWarmupFrames <= 0)
            return;

        var brain = FindGameplayBrain();
        if (brain != null)
            brain.ManualUpdate();
        else
            SnapMainCameraToEye();

        _cameraWarmupFrames--;
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
        if (GameMenuUI.IsBlockingGameplay) return true;
        if (DialogueManager.IsDialogueOpen) return true;

        if (inventoryManager == null) inventoryManager = GetComponent<InventoryManager>();
        if (inventoryManager != null && inventoryManager.mainInventoryObject != null &&
            inventoryManager.mainInventoryObject.activeSelf)
            return true;

        if (notebookUI == null) notebookUI = FindFirstObjectByType<NotebookUI>();
        if (notebookUI != null && notebookUI.IsNotebookOpen) return true;

        if (QuestManager.Instance != null && QuestManager.Instance.IsPanelOpen) return true;

        if (SceneManager.GetActiveScene().name == "TestScene" && TestSceneHelper.UnlockCursor) return true;

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
