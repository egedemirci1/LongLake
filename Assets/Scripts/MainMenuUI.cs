using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class MainMenuUI : MonoBehaviour
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
    [SerializeField] private string defaultIp = "10.171.156.166";
    [SerializeField] private ushort defaultPort = 7777;

    [Header("Scene")]
    [SerializeField] private string gameplaySceneName = "CrashSite_Main";

    private bool callbacksRegistered;
    private bool sceneEventsHooked;
    private bool isTryingToConnectClient;
    private float connectStartTime;

    private void Awake()
    {
        if (networkManager == null) networkManager = NetworkManager.Singleton;

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

        HookUiButtons();
        RegisterCallbacksOnce();

        SetStatus($"Ready. ActiveScene={SceneManager.GetActiveScene().name}");
        Debug.Log($"[MainMenuUI] Ready. ActiveScene={SceneManager.GetActiveScene().name}");

        // Client debug ticker (prints status every 1 second; only active when client is trying to connect)
        InvokeRepeating(nameof(TickClientDebug), 1f, 1f);
    }

    private void OnDestroy()
    {
        UnhookSceneEvents();
        UnregisterCallbacks();
    }

    private void HookUiButtons()
    {
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
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

    public void StartHost()
    {
        if (!ApplyConnectionDataFromUIOrDefaults(out string ip, out ushort port))
            return;

        // For host bind, safest: UnityTransport Address=0.0.0.0 (Inspector)
        // Here SetConnectionData is not for server bind, it's for connection data. Still useful to log.
        SetStatus($"Starting host... (listen {ip}:{port})");
        Debug.Log($"[MainMenuUI] Transport set to {ip}:{port}");
        Debug.Log("[MainMenuUI] Starting host...");

        bool ok = networkManager.StartHost();
        if (!ok)
        {
            Debug.LogError("[MainMenuUI] StartHost() returned false");
            SetStatus("StartHost failed");
            return;
        }

        // Scene events hook: try after StartHost (can be null in Awake)
        TryHookSceneEventsWithRetry();

        // Scene loading: only server/host does this
        TryLoadGameplayScene_Server();
    }

    public void StartClient()
    {
        if (!ApplyConnectionDataFromUIOrDefaults(out string ip, out ushort port))
            return;

        // 0.0.0.0 is invalid target IP on client side, convert to localhost
        if (ip == "0.0.0.0")
        {
            ip = "127.0.0.1";
            Debug.Log("[MainMenuUI] Client IP 0.0.0.0 detected, converting to 127.0.0.1 (localhost)");
            unityTransport.SetConnectionData(ip, port);
        }

        SetStatus($"Starting client to {ip}:{port} ...");
        Debug.Log($"[MainMenuUI] Transport set to {ip}:{port}");
        Debug.Log($"[MainMenuUI] Starting client to {ip}:{port}");

        isTryingToConnectClient = true;
        connectStartTime = Time.realtimeSinceStartup;

        bool ok = networkManager.StartClient();
        if (!ok)
        {
            Debug.LogError("[MainMenuUI] StartClient() returned false");
            SetStatus("StartClient failed");
            isTryingToConnectClient = false;
            return;
        }

        // Scene events hook: try after StartClient (can be null in Awake)
        TryHookSceneEventsWithRetry();

        // Print status if not connected after 10 seconds
        Invoke(nameof(LogClientStillConnecting), 10f);
    }

    // ---- Connection data ----

    private bool ApplyConnectionDataFromUIOrDefaults(out string ip, out ushort port)
    {
        ip = defaultIp;
        port = defaultPort;

        if (ipInput != null && !string.IsNullOrWhiteSpace(ipInput.text))
            ip = ipInput.text.Trim();

        if (portInput != null && !string.IsNullOrWhiteSpace(portInput.text))
        {
            if (!ushort.TryParse(portInput.text.Trim(), out port))
            {
                Debug.LogError("[MainMenuUI] Invalid port in UI input.");
                SetStatus("Invalid port (example: 7777)");
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(ip))
        {
            Debug.LogError("[MainMenuUI] IP empty.");
            SetStatus("IP is empty");
            return false;
        }

        // Connection timeouts (for VPN/ZeroTier)
        unityTransport.ConnectTimeoutMS = 10000;
        unityTransport.DisconnectTimeoutMS = 60000;
        unityTransport.MaxConnectAttempts = 60;

        unityTransport.SetConnectionData(ip, port);
        return true;
    }

    // ---- Scene management ----

    private void TryLoadGameplayScene_Server()
    {
        if (!networkManager.IsServer)
        {
            Debug.LogWarning("[MainMenuUI] TryLoadGameplayScene_Server called but this is not server.");
            return;
        }

        // Build list check
        if (!Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            Debug.LogError($"[MainMenuUI] Scene '{gameplaySceneName}' not in build list or name is wrong.");
            SetStatus($"Scene missing in build: {gameplaySceneName}");
            return;
        }

        if (networkManager.SceneManager == null)
        {
            Debug.LogError("[MainMenuUI] SceneManager is null even after StartHost. Is Enable Scene Management checked? Is there only one NetworkManager?");
            SetStatus("SceneManager null (Enable Scene Management?)");
            return;
        }

        Debug.Log($"[MainMenuUI] Loading gameplay scene via Netcode: {gameplaySceneName}");
        SetStatus($"Loading: {gameplaySceneName}");
        networkManager.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
    }

    private void TryHookSceneEventsWithRetry()
    {
        // SceneManager in some versions becomes ready the frame after StartHost/StartClient call
        // So try 5 times
        const int maxTries = 5;
        StartCoroutine(SceneHookRetryRoutine(maxTries, 0.2f));
    }

    private System.Collections.IEnumerator SceneHookRetryRoutine(int tries, float waitSeconds)
    {
        for (int i = 0; i < tries; i++)
        {
            if (TryHookSceneEvents())
                yield break;

            yield return new WaitForSeconds(waitSeconds);
        }

        Debug.LogWarning("[MainMenuUI] Scene events hook failed after retries. (SceneManager still null)");
    }

    private bool TryHookSceneEvents()
    {
        if (sceneEventsHooked) return true;
        if (networkManager == null) return false;

        var sm = networkManager.SceneManager;
        if (sm == null)
        {
            Debug.LogWarning("[MainMenuUI] SceneManager null (might not be initialized yet).");
            return false;
        }

        sm.OnLoadEventCompleted -= OnNetcodeSceneLoadCompleted;
        sm.OnLoadEventCompleted += OnNetcodeSceneLoadCompleted;

        sceneEventsHooked = true;
        Debug.Log("[MainMenuUI] SceneManager ready, hooked OnLoadEventCompleted.");
        return true;
    }

    private void UnhookSceneEvents()
    {
        if (!sceneEventsHooked || networkManager == null || networkManager.SceneManager == null) return;

        networkManager.SceneManager.OnLoadEventCompleted -= OnNetcodeSceneLoadCompleted;
        sceneEventsHooked = false;
    }

    private void OnNetcodeSceneLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        Debug.Log($"[MainMenuUI] Netcode scene load completed: {sceneName} mode={mode} completed={clientsCompleted?.Count ?? 0} timedOut={clientsTimedOut?.Count ?? 0}");
        SetStatus($"Loaded: {sceneName}");
    }

    // ---- Netcode callbacks ----

    private void OnServerStarted()
    {
        Debug.Log("[MainMenuUI] Server started");
        SetStatus("Server started");
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log("[MainMenuUI] Connected. clientId=" + clientId);
        SetStatus("Connected. clientId=" + clientId);

        // Exit "connecting" mode if client connected
        if (!networkManager.IsServer)
            isTryingToConnectClient = false;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.LogWarning("[MainMenuUI] Disconnected. clientId=" + clientId);
        SetStatus("Disconnected. clientId=" + clientId);

        if (!networkManager.IsServer)
            isTryingToConnectClient = false;
    }

    private void OnTransportFailure()
    {
        Debug.LogError("[MainMenuUI] Transport failure (Firewall/UDP/VPN adapter/bind?)");
        SetStatus("Transport failure");
        isTryingToConnectClient = false;
    }

    // ---- Client debug ticker ----

    private void TickClientDebug()
    {
        if (networkManager == null) return;
        if (!isTryingToConnectClient) return;
        if (networkManager.IsServer) return; // not for host, client debug only

        float elapsed = Time.realtimeSinceStartup - connectStartTime;

        Debug.Log(
            $"[ClientDebug t+{elapsed:0.0}s] " +
            $"IsClient={networkManager.IsClient} " +
            $"IsConnectedClient={networkManager.IsConnectedClient} " +
            $"ShutdownInProgress={networkManager.ShutdownInProgress} " +
            $"LocalClientId={networkManager.LocalClientId} " +
            $"ActiveScene={SceneManager.GetActiveScene().name}"
        );
    }

    private void LogClientStillConnecting()
    {
        if (networkManager == null) return;
        if (networkManager.IsServer) return;

        if (isTryingToConnectClient && !networkManager.IsConnectedClient)
        {
            Debug.LogWarning("[MainMenuUI] Client still not connected after 10s. This is usually UDP/firewall/bind/VPN rule issue.");
            SetStatus("Still connecting... (check UDP 7777 firewall)");
        }
    }

    // ---- Misc ----

    private void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
