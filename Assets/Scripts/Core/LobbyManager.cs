using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// MainMenu lobby: tracks Ready per connected client. Host starts CrashSite when all are ready
/// and have selected a character.
/// </summary>
public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance { get; private set; }

    private NetworkList<ulong> readyClientIds;
    private NetworkList<ulong> syncedConnectedIds;
    private NetworkVariable<bool> gameplayLoading =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private bool _disconnectHooked;
    private bool _loadingUiApplied;

    public event Action OnLobbyStateChanged;

    public int ReadyCount => readyClientIds != null ? readyClientIds.Count : 0;
    public int ConnectedCount => syncedConnectedIds != null ? syncedConnectedIds.Count : 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        readyClientIds = new NetworkList<ulong>();
        syncedConnectedIds = new NetworkList<ulong>();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        readyClientIds.OnListChanged += OnReadyListChanged;
        syncedConnectedIds.OnListChanged += OnConnectedListChanged;
        gameplayLoading.OnValueChanged += OnGameplayLoadingChanged;

        if (IsServer && !_disconnectHooked && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            _disconnectHooked = true;
            RebuildSyncedConnectedList();
        }

        if (gameplayLoading.Value)
            ApplyGameplayLoadingUi();

        OnLobbyStateChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        readyClientIds.OnListChanged -= OnReadyListChanged;
        syncedConnectedIds.OnListChanged -= OnConnectedListChanged;
        gameplayLoading.OnValueChanged -= OnGameplayLoadingChanged;

        if (_disconnectHooked && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            _disconnectHooked = false;
        }
    }

    private void OnGameplayLoadingChanged(bool previous, bool current)
    {
        if (current)
            ApplyGameplayLoadingUi();
    }

    private void OnReadyListChanged(NetworkListEvent<ulong> _) => OnLobbyStateChanged?.Invoke();

    private void OnConnectedListChanged(NetworkListEvent<ulong> _) => OnLobbyStateChanged?.Invoke();

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        AddSyncedConnected(clientId);
        OnLobbyStateChanged?.Invoke();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        RemoveReady(clientId);
        RemoveSyncedConnected(clientId);
        OnLobbyStateChanged?.Invoke();
    }

    private void RebuildSyncedConnectedList()
    {
        if (!IsServer || NetworkManager == null || syncedConnectedIds == null) return;
        syncedConnectedIds.Clear();
        foreach (ulong id in NetworkManager.ConnectedClientsIds)
            syncedConnectedIds.Add(id);
    }

    private void AddSyncedConnected(ulong clientId)
    {
        if (syncedConnectedIds == null) return;
        for (int i = 0; i < syncedConnectedIds.Count; i++)
        {
            if (syncedConnectedIds[i] == clientId)
                return;
        }
        syncedConnectedIds.Add(clientId);
    }

    private void RemoveSyncedConnected(ulong clientId)
    {
        if (syncedConnectedIds == null) return;
        for (int i = syncedConnectedIds.Count - 1; i >= 0; i--)
        {
            if (syncedConnectedIds[i] == clientId)
                syncedConnectedIds.RemoveAt(i);
        }
    }

    /// <summary>Host önce, sonra diğer client'lar — UI slot sırası.</summary>
    public void CopyConnectedClientsOrdered(List<ulong> into)
    {
        into.Clear();
        if (syncedConnectedIds == null || NetworkManager == null) return;

        ulong hostId = NetworkManager.ServerClientId;
        bool hostPresent = false;
        for (int i = 0; i < syncedConnectedIds.Count; i++)
        {
            if (syncedConnectedIds[i] == hostId)
            {
                hostPresent = true;
                break;
            }
        }
        if (hostPresent)
            into.Add(hostId);

        for (int i = 0; i < syncedConnectedIds.Count; i++)
        {
            ulong id = syncedConnectedIds[i];
            if (id != hostId)
                into.Add(id);
        }
    }

    public bool IsClientReady(ulong clientId)
    {
        if (readyClientIds == null) return false;
        for (int i = 0; i < readyClientIds.Count; i++)
        {
            if (readyClientIds[i] == clientId)
                return true;
        }
        return false;
    }

    public bool IsLocalReady()
    {
        if (NetworkManager.Singleton == null || readyClientIds == null) return false;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < readyClientIds.Count; i++)
        {
            if (readyClientIds[i] == localId)
                return true;
        }
        return false;
    }

    public bool AllConnectedAreReady()
    {
        if (NetworkManager == null || readyClientIds == null) return false;
        var ids = NetworkManager.ConnectedClientsIds;
        if (ids.Count == 0) return false;

        foreach (ulong id in ids)
        {
            bool found = false;
            for (int i = 0; i < readyClientIds.Count; i++)
            {
                if (readyClientIds[i] == id)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    public bool AllConnectedHaveCharacter()
    {
        if (NetworkManager == null) return false;
        foreach (ulong id in NetworkManager.ConnectedClientsIds)
        {
            var pc = GetPlayerController(id);
            if (pc == null || !pc.HasSelectedCharacter)
                return false;
        }
        return NetworkManager.ConnectedClientsIds.Count > 0;
    }

    public bool CanHostStartGame()
    {
        return IsServer && AllConnectedAreReady() && AllConnectedHaveCharacter();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetReadyServerRpc(bool ready, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        ulong clientId = rpcParams.Receive.SenderClientId;

        var pc = GetPlayerController(clientId);
        if (ready)
        {
            if (pc == null || !pc.HasSelectedCharacter)
            {
                Debug.LogWarning($"[LobbyManager] Client {clientId} tried Ready without character.");
                return;
            }
            AddReady(clientId);
        }
        else
        {
            RemoveReady(clientId);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestStartGameServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        // Only host/server local client may start.
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
        {
            Debug.LogWarning("[LobbyManager] Non-host tried to start game.");
            return;
        }

        if (!CanHostStartGame())
        {
            Debug.LogWarning("[LobbyManager] Cannot start — not everyone ready/selected.");
            return;
        }

        // NetworkVariable: Rpc kaçsa bile client yükleme ekranını alır.
        gameplayLoading.Value = true;
        BeginGameplayLoadingRpc();
        ApplyGameplayLoadingUi();

        var menu = FindFirstObjectByType<MainMenuUI>();
        if (menu != null)
            menu.StartGameplayFromLobby();
        else
            Debug.LogError("[LobbyManager] MainMenuUI missing — cannot load scene.");
    }

    /// <summary>
    /// Host + tüm client'larda lobiyi kapatıp yükleme ekranını eşzamanlı gösterir.
    /// Scene Load event'inden önce çağrılır; client'ın lobide beklemesini önler.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void BeginGameplayLoadingRpc()
    {
        ApplyGameplayLoadingUi();
    }

    private void ApplyGameplayLoadingUi()
    {
        if (_loadingUiApplied) return;
        _loadingUiApplied = true;

        var lobbyUi = FindFirstObjectByType<LobbyUI>();
        if (lobbyUi != null)
            lobbyUi.HideForGameplayLoad();

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            Transform panelTransform = canvas.transform.Find("Panel");
            if (panelTransform != null)
                panelTransform.gameObject.SetActive(false);
        }

        var loading = LoadingScreenManager.Resolve();
        if (loading != null)
            loading.ShowLoadingScreenForScene("CrashSite_Main", "Bölüm 1: Uzungöl Tatili");
        else
            Debug.LogError("[LobbyManager] LoadingScreenManager not found — client cannot show loading UI.");
    }

    private void AddReady(ulong clientId)
    {
        for (int i = 0; i < readyClientIds.Count; i++)
        {
            if (readyClientIds[i] == clientId)
                return;
        }
        readyClientIds.Add(clientId);
    }

    private void RemoveReady(ulong clientId)
    {
        for (int i = readyClientIds.Count - 1; i >= 0; i--)
        {
            if (readyClientIds[i] == clientId)
                readyClientIds.RemoveAt(i);
        }
    }

    private static PlayerController GetPlayerController(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return null;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return null;
        if (client.PlayerObject == null) return null;
        return client.PlayerObject.GetComponent<PlayerController>();
    }
}
