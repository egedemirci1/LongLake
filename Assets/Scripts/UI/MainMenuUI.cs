using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public partial class MainMenuUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button quitButton;

    // (Currently None in inspector, that's fine. Default IP/Port will be used.)
    [SerializeField] private InputField ipInput;
    [SerializeField] private InputField portInput;
    [SerializeField] private Text statusText;

    [Header("Networking")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;

    [Header("Defaults")]
    [Tooltip("Client connects here when Address in UnityTransport is 0.0.0.0 / empty. Use host ZeroTier IP.")]
    [SerializeField] private string defaultIp = "10.171.156.166";
    [SerializeField] private ushort defaultPort = 7777;

    [Header("Scene")]
    [SerializeField] private string gameplaySceneName = "CrashSite_Main";

    [Header("Audio")]
    [SerializeField] private MainMenuMusic menuMusic;

    private bool callbacksRegistered;
    private bool sceneEventsHooked;
    private bool isTryingToConnectClient;
    private bool isStartingHost;
    private ushort hostPortBase;
    private ushort hostPortAttempt;
    private int hostPortRetries;
    private Coroutine hostStartRoutine;

    private void Awake()
    {
        if (networkManager == null) networkManager = NetworkManager.Singleton;
        if (menuMusic == null) menuMusic = FindFirstObjectByType<MainMenuMusic>();

        if (networkManager == null)
        {
            Debug.LogError("[MainMenuUI] NetworkManager.Singleton not found! Scene must have NetworkManager.");
            return;
        }

        if (unityTransport == null)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        if (unityTransport == null)
        {
            Debug.LogError("[MainMenuUI] UnityTransport not found! NetworkManager transport must be UnityTransport.");
            return;
        }

        // Önceki Play'den asılı kalan loading / host state'i temizle
        if (GameplaySceneLoader.Instance != null)
            GameplaySceneLoader.Instance.ResetForMenu();

        HookUiButtons();
        RegisterCallbacksOnce();
        SetStatus("");
    }

    private void OnDestroy()
    {
        UnhookSceneEvents();
        UnregisterCallbacks();
    }

    private void OnApplicationQuit()
    {
        EnsureNetworkStopped();
    }

    private void HookUiButtons()
    {
        if (hostButton != null)
        {
            hostButton.onClick.AddListener(PlayUiClick);
            hostButton.onClick.AddListener(StartHost);
        }
        if (clientButton != null)
        {
            clientButton.onClick.AddListener(PlayUiClick);
            clientButton.onClick.AddListener(ShowJoinPanel);
        }
        if (quitButton != null)
        {
            quitButton.onClick.AddListener(PlayUiClick);
            quitButton.onClick.AddListener(QuitGame);
        }
    }

    private static void PlayUiClick()
    {
        if (GameAudio.Instance != null)
            GameAudio.Instance.PlayButtonClick();
    }

    private void RegisterCallbacksOnce()
    {
        if (callbacksRegistered) return;
        callbacksRegistered = true;

        // Connection/transport events
        networkManager.OnServerStarted += OnServerStarted;
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        networkManager.OnTransportFailure += OnTransportFailure;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered || networkManager == null) return;

        networkManager.OnServerStarted -= OnServerStarted;
        networkManager.OnClientConnectedCallback -= OnClientConnected;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        networkManager.OnTransportFailure -= OnTransportFailure;

        callbacksRegistered = false;
    }

    // ---- Public UI actions ----

    // ---- Join panel (client IP entry) ----

    private GameObject joinPanel;
    private TMP_InputField joinIpInput;

    // ---- Connection data ----

    // ---- Scene management ----

    // ---- Misc ----

    private void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    private void QuitGame()
    {
        EnsureNetworkStopped();

        if (menuMusic != null)
            menuMusic.FadeOut();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
