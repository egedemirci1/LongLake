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

    public void StartHost()
    {
        if (hostStartRoutine != null)
            StopCoroutine(hostStartRoutine);

        EnsureNetworkStopped();
        hostPortRetries = 0;
        hostPortBase = ResolvePreferredPort();
        hostPortAttempt = FindFreeUdpPort(hostPortBase, 32);
        hostStartRoutine = StartCoroutine(StartHostRoutine(hostPortAttempt));
    }

    private IEnumerator StartHostRoutine(ushort port)
    {
        isStartingHost = true;
        hostPortAttempt = port;

        // Shutdown bitmeden StartHost çağırma — aksi halde aynı 7777'ye tekrar yapışır.
        float wait = 0f;
        while (networkManager != null && networkManager.ShutdownInProgress && wait < 2f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return null;
        yield return null;

        ApplyTransportTuning();
        unityTransport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
        SetStatus(hostPortRetries == 0
            ? "Oda açılıyor…"
            : $"Port {port} deneniyor…");

        bool ok = networkManager.StartHost();
        hostStartRoutine = null;

        if (ok)
            yield break;

        // NGO çoğu zaman OnTransportFailure'ı StartHost içinde çağırır (retry orada).
        // Çağrılmadıysa burada yedekle.
        yield return null;
        if (isStartingHost && hostStartRoutine == null)
        {
            if (!ScheduleNextHostPort())
            {
                isStartingHost = false;
                SetStatus("Oda açılamadı (port meşgul). Unity'yi kapatıp aç.");
            }
        }
    }

    private bool ScheduleNextHostPort()
    {
        if (!isStartingHost) return false;
        if (hostPortRetries >= 8) return false;
        if (hostStartRoutine != null) return true; // zaten planlandı

        hostPortRetries++;
        ushort next = FindFreeUdpPort((ushort)(hostPortBase + hostPortRetries), 32);
        EnsureNetworkStopped();
        hostStartRoutine = StartCoroutine(StartHostRoutine(next));
        return true;
    }

    /// <summary>Transport.ConnectionData.Port değil — sabit tercih + boş port ara.</summary>
    private ushort ResolvePreferredPort()
    {
        if (portInput != null && !string.IsNullOrWhiteSpace(portInput.text) &&
            ushort.TryParse(portInput.text.Trim(), out ushort uiPort))
            return uiPort;

        return defaultPort;
    }

    private static ushort FindFreeUdpPort(ushort start, int span)
    {
        for (int i = 0; i < span; i++)
        {
            ushort port = (ushort)(start + i);
            if (IsUdpPortFree(port))
                return port;
        }

        return start;
    }

    private static bool IsUdpPortFree(ushort port)
    {
        Socket socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (socket != null)
            {
                try { socket.Close(); }
                catch { /* ignore */ }
            }
        }
    }

    /// <summary>Called by LobbyManager when host starts the session (server only).</summary>
    public void StartGameplayFromLobby()
    {
        if (menuMusic != null)
            menuMusic.FadeOut();

        TryLoadGameplayScene_Server();
    }

    // ---- Join panel (client IP entry) ----

    private GameObject joinPanel;
    private TMP_InputField joinIpInput;

    public void ShowJoinPanel()
    {
        if (joinPanel == null)
            BuildJoinPanel();

        if (joinPanel == null)
        {
            // Panel kurulamazsa eski davranış: direkt bağlan.
            StartClient();
            return;
        }

        // Öneri: transport'taki adres ya da defaultIp.
        if (joinIpInput != null && string.IsNullOrWhiteSpace(joinIpInput.text))
        {
            string suggested = unityTransport != null ? unityTransport.ConnectionData.Address : null;
            if (string.IsNullOrWhiteSpace(suggested) || suggested == "0.0.0.0")
                suggested = defaultIp;
            joinIpInput.text = suggested ?? "";
        }

        joinPanel.SetActive(true);
    }

    private void HideJoinPanel()
    {
        if (joinPanel != null)
            joinPanel.SetActive(false);
    }

    private void ConnectFromJoinPanel()
    {
        HideJoinPanel();
        StartClient();
    }

    private void BuildJoinPanel()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        joinPanel = new GameObject("JoinPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        joinPanel.transform.SetParent(canvas.transform, false);
        var rt = joinPanel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var bg = joinPanel.GetComponent<Image>();
        bg.color = new Color(0.02f, 0.05f, 0.06f, 0.72f);
        bg.raycastTarget = true;

        var box = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        box.transform.SetParent(joinPanel.transform, false);
        var boxRt = box.GetComponent<RectTransform>();
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.anchoredPosition = Vector2.zero;
        boxRt.sizeDelta = new Vector2(520f, 260f);
        box.GetComponent<Image>().color = new Color(0.07f, 0.1f, 0.11f, 0.95f);

        CreateTmpText(box.transform, "Title", "Odaya Katıl", 30, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -60f), new Vector2(-24f, -16f), TextAlignmentOptions.Center);

        joinIpInput = CreateTmpInput(box.transform, "IpInput",
            new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(420f, 52f), "Host IP (örn. 10.171.156.166)");

        var connectBtn = CreateTmpButton(box.transform, "ConnectButton", "Bağlan",
            new Vector2(0.5f, 0f), new Vector2(-110f, 46f), new Vector2(200f, 52f));
        connectBtn.onClick.AddListener(PlayUiClick);
        connectBtn.onClick.AddListener(ConnectFromJoinPanel);

        var backBtn = CreateTmpButton(box.transform, "BackButton", "Geri",
            new Vector2(0.5f, 0f), new Vector2(110f, 46f), new Vector2(200f, 52f));
        backBtn.onClick.AddListener(PlayUiClick);
        backBtn.onClick.AddListener(HideJoinPanel);

        joinPanel.SetActive(false);
    }

    private static TextMeshProUGUI CreateTmpText(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 aMin, Vector2 aMax, Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = new Color(0.93f, 0.94f, 0.92f, 1f);
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateTmpButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.18f, 0.95f);

        CreateTmpText(go.transform, "Label", label, 24, FontStyles.Normal,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);

        return go.GetComponent<Button>();
    }

    private static TMP_InputField CreateTmpInput(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string placeholder)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.16f, 0.22f, 0.22f, 1f);

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var areaRt = area.GetComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(12f, 6f);
        areaRt.offsetMax = new Vector2(-12f, -6f);

        var placeholderTmp = CreateTmpText(area.transform, "Placeholder", placeholder, 22, FontStyles.Italic,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
        placeholderTmp.color = new Color(0.7f, 0.74f, 0.73f, 0.6f);

        var textTmp = CreateTmpText(area.transform, "Text", "", 22, FontStyles.Normal,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = areaRt;
        input.textComponent = textTmp;
        input.placeholder = placeholderTmp;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 64;
        return input;
    }

    public void StartClient()
    {
        EnsureNetworkStopped();

        if (!ApplyClientConnectionData(out string ip, out ushort port))
            return;

        // Keep menu music until gameplay; lobby stays on MainMenu.

        SetStatus("Bağlanılıyor…");

        isTryingToConnectClient = true;

        bool ok = networkManager.StartClient();
        if (!ok)
        {
            SetStatus("Bağlantı başarısız.");
            isTryingToConnectClient = false;
            return;
        }

        TryHookSceneEventsWithRetry();
        Invoke(nameof(LogClientStillConnecting), 10f);
    }

    // ---- Connection data ----

    /// <summary>
    /// Play Mode / önceki host bitmeden tekrar StartHost denenirse UDP 7777 meşgul kalır.
    /// </summary>
    private void EnsureNetworkStopped()
    {
        if (networkManager == null) return;
        if (networkManager.ShutdownInProgress) return;

        if (networkManager.IsListening || networkManager.IsServer || networkManager.IsClient)
            networkManager.Shutdown();
    }

    private ushort ResolvePort()
    {
        if (portInput != null && !string.IsNullOrWhiteSpace(portInput.text))
        {
            if (ushort.TryParse(portInput.text.Trim(), out ushort uiPort))
                return uiPort;
        }

        // Prefer Inspector UnityTransport port if it looks intentional.
        ushort transportPort = unityTransport.ConnectionData.Port;
        if (transportPort != 0)
            return transportPort;

        return defaultPort;
    }

    private bool ApplyClientConnectionData(out string ip, out ushort port)
    {
        port = ResolvePort();

        // Priority: Join panel → legacy UI input → UnityTransport Address (Inspector) → defaultIp
        ip = null;

        if (joinIpInput != null && !string.IsNullOrWhiteSpace(joinIpInput.text))
            ip = joinIpInput.text.Trim();

        if (string.IsNullOrWhiteSpace(ip) && ipInput != null && !string.IsNullOrWhiteSpace(ipInput.text))
            ip = ipInput.text.Trim();

        if (string.IsNullOrWhiteSpace(ip))
        {
            string transportAddress = unityTransport.ConnectionData.Address;
            if (!string.IsNullOrWhiteSpace(transportAddress) &&
                transportAddress != "0.0.0.0")
            {
                ip = transportAddress.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(ip))
            ip = defaultIp;

        if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
        {
            Debug.LogError("[MainMenuUI] Host IP gerekli.");
            SetStatus("Host IP gir.");
            return false;
        }

        ApplyTransportTuning();
        unityTransport.SetConnectionData(ip, port);
        return true;
    }

    private void ApplyTransportTuning()
    {
        // ZeroTier / Editor burst traffic can fill the default queue during scene load spikes.
        if (unityTransport.MaxPacketQueueSize < 1024)
            unityTransport.MaxPacketQueueSize = 1024;

        unityTransport.ConnectTimeoutMS = 10000;
        unityTransport.DisconnectTimeoutMS = 60000;
        unityTransport.MaxConnectAttempts = 60;
    }

    // ---- Scene management ----

    private void TryLoadGameplayScene_Server()
    {
        if (!networkManager.IsServer)
            return;

        if (!Application.CanStreamedLevelBeLoaded(gameplaySceneName))
        {
            Debug.LogError($"[MainMenuUI] Scene '{gameplaySceneName}' is not in Build Settings.");
            SetStatus($"Sahne eksik: {gameplaySceneName}");
            return;
        }

        if (networkManager.SceneManager == null)
        {
            Debug.LogError("[MainMenuUI] SceneManager null — Enable Scene Management?");
            SetStatus("Sahne yöneticisi hazır değil.");
            return;
        }

        // Lobi + ana panel (Rpc zaten kapatmış olabilir; yine de garanti).
        var lobbyUi = FindFirstObjectByType<LobbyUI>();
        if (lobbyUi != null)
            lobbyUi.HideForGameplayLoad();

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            Transform panelTransform = canvas.transform.Find("Panel");
            if (panelTransform != null)
                panelTransform.gameObject.SetActive(false);

            Transform lobbyTransform = canvas.transform.Find("LobbyPanel");
            if (lobbyTransform != null)
                lobbyTransform.gameObject.SetActive(false);
        }

        SetStatus("Yükleniyor…");

        if (GameplaySceneLoader.Instance != null)
            GameplaySceneLoader.Instance.BeginHostLoad(gameplaySceneName);
        else
            Debug.LogError("[MainMenuUI] GameplaySceneLoader missing.");
    }

    private void TryHookSceneEventsWithRetry()
    {
        if (GameplaySceneLoader.Instance != null)
            GameplaySceneLoader.Instance.TryHookWhenNetworkReady();

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

        Debug.LogWarning("[MainMenuUI] SceneManager hook failed after retries.");
    }

    private bool TryHookSceneEvents()
    {
        if (sceneEventsHooked) return true;
        if (networkManager == null) return false;

        var sm = networkManager.SceneManager;
        if (sm == null)
            return false;

        sm.OnLoadEventCompleted -= OnNetcodeSceneLoadCompleted;
        sm.OnLoadEventCompleted += OnNetcodeSceneLoadCompleted;
        sm.OnSceneEvent -= OnNetcodeSceneEvent;
        sm.OnSceneEvent += OnNetcodeSceneEvent;

        sceneEventsHooked = true;
        return true;
    }

    private void UnhookSceneEvents()
    {
        if (!sceneEventsHooked || networkManager == null || networkManager.SceneManager == null) return;

        networkManager.SceneManager.OnLoadEventCompleted -= OnNetcodeSceneLoadCompleted;
        networkManager.SceneManager.OnSceneEvent -= OnNetcodeSceneEvent;
        sceneEventsHooked = false;
    }

    private void OnNetcodeSceneEvent(SceneEvent sceneEvent)
    {
        // Client fallback: loader henüz UI açmadıysa SceneEvent Load'da aç.
        if (networkManager.IsServer) return;
        if (sceneEvent.SceneEventType != SceneEventType.Load) return;
        if (sceneEvent.SceneName != gameplaySceneName) return;

        if (GameplaySceneLoader.Instance != null)
            GameplaySceneLoader.Instance.BeginClientLoadingUi(sceneEvent.SceneName);
    }

    private void OnNetcodeSceneLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        SetStatus("");
        // Hide LoadingScreenManager → GameplaySceneLoader yapar (progress + min süre).
    }

    private void OnServerStarted()
    {
        isStartingHost = false;
        TryHookSceneEventsWithRetry();
        string portNote = hostPortAttempt != hostPortBase && hostPortBase != 0
            ? $" (port {hostPortAttempt})"
            : "";
        SetStatus($"Lobi: karakter seç → Hazırım. Herkes hazır olunca Oyunu Başlat.{portNote}");
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!networkManager.IsServer)
            isTryingToConnectClient = false;

        SetStatus("Bağlandı.");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!networkManager.IsServer)
            isTryingToConnectClient = false;

        SetStatus("Bağlantı kesildi.");
    }

    private void OnTransportFailure()
    {
        isTryingToConnectClient = false;

        // StartHost içinde senkron fail + async failure çakışmasın diye gecikmeli dene.
        if (isStartingHost && hostPortRetries < 8)
        {
            ScheduleNextHostPort();
            return;
        }

        isStartingHost = false;
        SetStatus("Bağlantı hatası (port meşgul). Unity'yi kapatıp aç, sonra tekrar Host.");
        EnsureNetworkStopped();
    }

    private void LogClientStillConnecting()
    {
        if (networkManager == null || networkManager.IsServer) return;

        if (isTryingToConnectClient && !networkManager.IsConnectedClient)
            SetStatus("Hâlâ bağlanıyor… (UDP 7777 / firewall kontrol et)");
    }

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
