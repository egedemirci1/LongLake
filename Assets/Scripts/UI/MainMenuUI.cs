using System.Collections.Generic;
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

        HookUiButtons();
        RegisterCallbacksOnce();
        SetStatus("");
    }

    private void OnDestroy()
    {
        UnhookSceneEvents();
        UnregisterCallbacks();
    }

    private void HookUiButtons()
    {
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(ShowJoinPanel);
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
        ushort port = ResolvePort();

        // Host always listens on all interfaces (LAN + ZeroTier).
        ApplyTransportTuning();
        unityTransport.SetConnectionData("0.0.0.0", port, "0.0.0.0");

        SetStatus("Oda açılıyor…");

        bool ok = networkManager.StartHost();
        if (!ok)
        {
            Debug.LogError("[MainMenuUI] StartHost() failed.");
            SetStatus("Oda açılamadı.");
            return;
        }

        TryHookSceneEventsWithRetry();
        SetStatus("Lobi: karakter seç → Hazırım. Herkes hazır olunca Oyunu Başlat.");
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
        connectBtn.onClick.AddListener(ConnectFromJoinPanel);

        var backBtn = CreateTmpButton(box.transform, "BackButton", "Geri",
            new Vector2(0.5f, 0f), new Vector2(110f, 46f), new Vector2(200f, 52f));
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
        if (!ApplyClientConnectionData(out string ip, out ushort port))
            return;

        // Keep menu music until gameplay; lobby stays on MainMenu.

        SetStatus("Bağlanılıyor…");

        isTryingToConnectClient = true;

        bool ok = networkManager.StartClient();
        if (!ok)
        {
            Debug.LogError("[MainMenuUI] StartClient() failed.");
            SetStatus("Bağlantı başarısız.");
            isTryingToConnectClient = false;
            return;
        }

        TryHookSceneEventsWithRetry();
        Invoke(nameof(LogClientStillConnecting), 10f);
    }

    // ---- Connection data ----

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
        // ZeroTier / Editor burst traffic can fill the default 128 receive queue.
        if (unityTransport.MaxPacketQueueSize < 512)
            unityTransport.MaxPacketQueueSize = 512;

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

        StartCoroutine(LoadSceneWithLoadingScreen());
    }

    private System.Collections.IEnumerator LoadSceneWithLoadingScreen()
    {
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

        // Loading Rpc / NetworkVariable ile gelmiş olabilir; yoksa host'ta da göster.
        var loading = LoadingScreenManager.Resolve();
        if (loading != null)
            loading.ShowLoadingScreenForScene(gameplaySceneName, "Bölüm 1: Uzungöl Tatili");

        // Video warm-up + UI layout için kısa bekle — client ile senkron.
        Canvas.ForceUpdateCanvases();
        yield return null;
        yield return new WaitForEndOfFrame();
        // Video prepare için ekstra frame (client flash önleme).
        yield return null;

        SetStatus("Yükleniyor…");
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
        // Client: host sahne yüklemeyi başlattığında yükleme ekranını göster.
        // (Host kendi coroutine'inde zaten gösteriyor.)
        if (networkManager.IsServer) return;
        if (sceneEvent.SceneEventType != SceneEventType.Load) return;
        if (sceneEvent.SceneName != gameplaySceneName) return;

        var loading = LoadingScreenManager.Resolve();
        if (loading != null)
            loading.ShowLoadingScreenForScene(sceneEvent.SceneName, "Bölüm 1: Uzungöl Tatili");
    }

    private void OnNetcodeSceneLoadCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        SetStatus("");

        var loading = LoadingScreenManager.Resolve();
        if (loading != null)
            loading.HideLoadingScreen();
    }

    private void OnServerStarted()
    {
        SetStatus("Lobi hazır.");
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
        Debug.LogError("[MainMenuUI] Transport failure (firewall / UDP / VPN?).");
        SetStatus("Bağlantı hatası.");
        isTryingToConnectClient = false;
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
        if (menuMusic != null)
            menuMusic.FadeOut();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
