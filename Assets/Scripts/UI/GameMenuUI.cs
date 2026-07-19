using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MainMenu ve gameplay tarafından paylaşılan ayarlar paneli.
/// Gameplay'de Escape menüsünü yönetir; co-op akışında timeScale değiştirmez.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed partial class GameMenuUI : MonoBehaviour
{
    private const string MainMenuScene = "MainMenu";
    private static readonly string[] GameplayScenes = { "CrashSite_Main", "TestScene" };

    public static GameMenuUI Instance { get; private set; }
    public static bool IsBlockingGameplay =>
        Instance != null && Instance._isGameplay &&
        (Instance._pauseOpen ||
         (Instance._settingsPanel != null && Instance._settingsPanel.activeSelf));

    private Canvas _canvas;
    private GameObject _pausePanel;
    private GameObject _settingsPanel;
    private GameObject _mainMenuSettingsButton;
    private bool _isGameplay;
    private bool _pauseOpen;
    private bool _settingsFromPause;
    private bool _returningToMenu;
    private NetworkManager _hookedNetworkManager;

    // LobbyUI / DialogueUI ile aynı görsel dil: koyu kart + kehribar vurgu.
    private static readonly Color Scrim = new Color(0.02f, 0.03f, 0.04f, 0.78f);
    private static readonly Color Card = new Color(0.06f, 0.07f, 0.09f, 0.96f);
    private static readonly Color CardInner = new Color(0.085f, 0.095f, 0.115f, 1f);
    private static readonly Color ButtonColor = new Color(0.145f, 0.16f, 0.185f, 1f);
    private static readonly Color Accent = new Color(0.95f, 0.77f, 0.32f, 1f);
    private static readonly Color AccentDim = new Color(0.95f, 0.77f, 0.32f, 0.35f);
    private static readonly Color OnAccent = new Color(0.12f, 0.10f, 0.05f, 1f);
    private static readonly Color DangerTone = new Color(0.72f, 0.38f, 0.36f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.94f, 0.92f, 1f);
    private static readonly Color Muted = new Color(0.62f, 0.64f, 0.60f, 1f);
    private static readonly Color TrackColor = new Color(0.11f, 0.12f, 0.145f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        var go = new GameObject("GameMenuUI");
        DontDestroyOnLoad(go);
        go.AddComponent<GameMenuUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCanvas();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        ConfigureForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnhookNetworkManager();
        if (Instance == this)
            Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ConfigureForScene(scene.name);
    }

    private void ConfigureForScene(string sceneName)
    {
        _isGameplay = IsGameplayScene(sceneName);
        _pauseOpen = false;
        _settingsFromPause = false;
        _returningToMenu = false;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);
        _mainMenuSettingsButton.SetActive(sceneName == MainMenuScene);
        SetCursorForGameplay(false);

        UnhookNetworkManager();
        if (_isGameplay)
            StartCoroutine(HookNetworkManagerNextFrame());
    }

    private static bool IsGameplayScene(string sceneName)
    {
        for (int i = 0; i < GameplayScenes.Length; i++)
        {
            if (GameplayScenes[i] == sceneName)
                return true;
        }
        return false;
    }

    private IEnumerator HookNetworkManagerNextFrame()
    {
        yield return null;
        _hookedNetworkManager = NetworkManager.Singleton;
        if (_hookedNetworkManager != null)
            _hookedNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void UnhookNetworkManager()
    {
        if (_hookedNetworkManager != null)
            _hookedNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        _hookedNetworkManager = null;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!_isGameplay || _returningToMenu || _hookedNetworkManager == null)
            return;
        if (_hookedNetworkManager.IsServer)
            return;
        StartCoroutine(ReturnToMainMenuRoutine());
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape) || _returningToMenu)
            return;

        if (_settingsPanel.activeSelf)
        {
            CloseSettings();
            return;
        }

        if (!_isGameplay)
            return;

        if (_pauseOpen)
        {
            ResumeGame();
            return;
        }

        // Escape önce açık olan envanter/notebook/görev/diyalog tarafından tüketilsin.
        if (OtherUiOwnsCursor())
            return;

        OpenPause();
    }

    private static bool OtherUiOwnsCursor()
    {
        if (DialogueManager.IsDialogueOpen)
            return true;

        var inventory = FindFirstObjectByType<InventoryManager>();
        if (inventory != null && inventory.mainInventoryObject != null &&
            inventory.mainInventoryObject.activeSelf)
            return true;

        var notebook = FindFirstObjectByType<NotebookUI>();
        if (notebook != null && notebook.IsNotebookOpen)
            return true;

        return QuestManager.Instance != null && QuestManager.Instance.IsPanelOpen;
    }

    private void OpenPause()
    {
        _pauseOpen = true;
        _pausePanel.SetActive(true);
        SetCursorForGameplay(true);
    }

    private void ResumeGame()
    {
        _pauseOpen = false;
        _settingsFromPause = false;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);
        SetCursorForGameplay(false);
    }

    private void OpenSettingsFromMainMenu()
    {
        PlayClick();
        _settingsFromPause = false;
        _settingsPanel.SetActive(true);
    }

    private void OpenSettingsFromPause()
    {
        PlayClick();
        _settingsFromPause = true;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(true);
    }

    private void CloseSettings()
    {
        _settingsPanel.SetActive(false);
        if (_settingsFromPause && _pauseOpen)
            _pausePanel.SetActive(true);
    }

    private void ReturnToMainMenu()
    {
        if (_returningToMenu)
            return;
        PlayClick();
        StartCoroutine(ReturnToMainMenuRoutine());
    }

    private IEnumerator ReturnToMainMenuRoutine()
    {
        _returningToMenu = true;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);

        NetworkManager nm = NetworkManager.Singleton;
        UnhookNetworkManager();
        if (nm != null && !nm.ShutdownInProgress &&
            (nm.IsListening || nm.IsServer || nm.IsClient))
        {
            nm.Shutdown();
        }

        float timeout = 2f;
        while (nm != null && nm.ShutdownInProgress && timeout > 0f)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (GameplaySceneLoader.Instance != null)
            GameplaySceneLoader.Instance.ResetForMenu();

        if (nm != null)
        {
            Destroy(nm.gameObject);
            yield return null;
        }

        SceneManager.LoadScene(MainMenuScene, LoadSceneMode.Single);
    }

    private static void SetCursorForGameplay(bool menuOpen)
    {
        if (!IsGameplayScene(SceneManager.GetActiveScene().name))
            return;
        Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = menuOpen;
    }

    private enum ButtonStyle { Primary, Secondary, Danger, Ghost }

}
