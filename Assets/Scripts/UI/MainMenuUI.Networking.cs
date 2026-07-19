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

// Partial: split for maintainability. Type identity unchanged.
public partial class MainMenuUI : MonoBehaviour
{
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

}
