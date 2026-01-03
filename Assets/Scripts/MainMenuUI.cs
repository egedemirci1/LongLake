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

    // (Sizde þu an None, sorun deðil. Default Ip/Port kullanýlacak.)
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
            Debug.LogError("[MainMenuUI] NetworkManager.Singleton bulunamadý! Scene'de NetworkManager olmalý.");
            return;
        }

        if (unityTransport == null)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        if (unityTransport == null)
        {
            Debug.LogError("[MainMenuUI] UnityTransport bulunamadý! NetworkManager transport UnityTransport olmalý.");
            return;
        }

        HookUiButtons();
        RegisterCallbacksOnce();

        SetStatus($"Ready. ActiveScene={SceneManager.GetActiveScene().name}");
        Debug.Log($"[MainMenuUI] Ready. ActiveScene={SceneManager.GetActiveScene().name}");

        // Client debug ticker (her 1 saniyede bir durum basar; sadece client denemesi varken aktif)
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

        // Host tarafýnda bind için en güvenlisi: UnityTransport Address=0.0.0.0 (Inspector)
        // Burada SetConnectionData server bind deðil, connection data içindir. Yine de loglamak faydalý.
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

        // Scene events hook: StartHost sonrasý dene (Awake'te null olabiliyor)
        TryHookSceneEventsWithRetry();

        // Sahne yükleme: sadece server/host yapar
        TryLoadGameplayScene_Server();
    }

    public void StartClient()
    {
        if (!ApplyConnectionDataFromUIOrDefaults(out string ip, out ushort port))
            return;

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

        // Scene events hook: StartClient sonrasý dene (Awake'te null olabiliyor)
        TryHookSceneEventsWithRetry();

        // 10 sn sonra baðlanmadýysa durumu bas
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

        // Baðlantý toleranslarý (VPN/ZeroTier için)
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
            Debug.LogError($"[MainMenuUI] Scene '{gameplaySceneName}' build list'te yok veya isim yanlýþ.");
            SetStatus($"Scene missing in build: {gameplaySceneName}");
            return;
        }

        if (networkManager.SceneManager == null)
        {
            Debug.LogError("[MainMenuUI] SceneManager is null even after StartHost. Enable Scene Management açýk mý? Tek NetworkManager mý var?");
            SetStatus("SceneManager null (Enable Scene Management?)");
            return;
        }

        Debug.Log($"[MainMenuUI] Loading gameplay scene via Netcode: {gameplaySceneName}");
        SetStatus($"Loading: {gameplaySceneName}");
        networkManager.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
    }

    private void TryHookSceneEventsWithRetry()
    {
        // SceneManager bazý sürümlerde StartHost/StartClient çaðrýsýndan sonraki frame’de hazýr oluyor
        // O yüzden 5 kere dene
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
            Debug.LogWarning("[MainMenuUI] SceneManager null (henüz init olmamýþ olabilir).");
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

        // Client baðlandýysa "connecting" modundan çýk
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
        if (networkManager.IsServer) return; // host için deðil, client debug

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
