using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MainMenu lobby panel: character pick, Ready, Host Start (when all ready).
/// All UI references are authored and assigned in the MainMenu scene.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [SerializeField] private GameObject connectPanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private Button ahuButton;
    [SerializeField] private Button yamanButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI readyButtonLabel;
    [SerializeField] private TextMeshProUGUI hostIpText;

    private LobbyManager _lobby;
    private bool _waitingSelect;

    private void Awake()
    {
        ValidateSceneReferences();
        WireButtons();
        if (lobbyPanel != null)
            lobbyPanel.SetActive(false);
    }

    private void OnEnable()
    {
        PlayerController.OnLocalCharacterSelectResult += OnSelectResult;
        TryBindLobby();
    }

    private void OnDisable()
    {
        PlayerController.OnLocalCharacterSelectResult -= OnSelectResult;
        UnbindLobby();
    }

    private void Update()
    {
        if (_lobby == null)
            TryBindLobby();

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        bool inSession = nm.IsListening && (nm.IsHost || nm.IsClient);
        if (inSession)
            ShowLobby();
    }

    private void TryBindLobby()
    {
        if (_lobby != null) return;
        _lobby = LobbyManager.Instance != null
            ? LobbyManager.Instance
            : FindFirstObjectByType<LobbyManager>();
        if (_lobby == null) return;
        _lobby.OnLobbyStateChanged += Refresh;
        Refresh();
    }

    private void UnbindLobby()
    {
        if (_lobby != null)
            _lobby.OnLobbyStateChanged -= Refresh;
        _lobby = null;
    }

    private void ShowLobby()
    {
        if (connectPanel != null && connectPanel.activeSelf)
            connectPanel.SetActive(false);
        if (lobbyPanel != null && !lobbyPanel.activeSelf)
            lobbyPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Refresh();
    }

    private void WireButtons()
    {
        if (ahuButton != null)
        {
            ahuButton.onClick.RemoveAllListeners();
            ahuButton.onClick.AddListener(() => SelectCharacter(0));
        }
        if (yamanButton != null)
        {
            yamanButton.onClick.RemoveAllListeners();
            yamanButton.onClick.AddListener(() => SelectCharacter(1));
        }
        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(ToggleReady);
        }
        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(StartGame);
        }
    }

    private void SelectCharacter(int index)
    {
        if (_waitingSelect) return;
        var nm = NetworkManager.Singleton;
        if (nm?.LocalClient?.PlayerObject == null)
        {
            SetStatus("Oyuncu henüz spawn olmadı...");
            return;
        }

        if (PlayerController.IsCharacterIndexTaken(index, nm.LocalClientId))
        {
            SetStatus(index == 0 ? "Ahu zaten seçildi!" : "Yaman zaten seçildi!");
            Refresh();
            return;
        }

        var pc = nm.LocalClient.PlayerObject.GetComponent<PlayerController>();
        if (pc == null) return;

        _waitingSelect = true;
        SetStatus("Seçim onaylanıyor...");
        pc.SelectCharacter(index);
    }

    private void OnSelectResult(bool success, int index)
    {
        _waitingSelect = false;
        if (!success)
        {
            SetStatus(index == 0 ? "Ahu zaten seçildi!" : "Yaman zaten seçildi!");
            Refresh();
            return;
        }

        // Selecting after ready → unready so everyone reconfirms with new pick.
        if (_lobby != null && _lobby.IsLocalReady())
            _lobby.SetReadyServerRpc(false);

        SetStatus(index == 0 ? "Ahu seçildi." : "Yaman seçildi.");
        Refresh();
    }

    private void ToggleReady()
    {
        if (_lobby == null) return;
        var pc = GetLocalPc();
        if (pc == null || !pc.HasSelectedCharacter)
        {
            SetStatus("Önce karakter seç.");
            return;
        }

        _lobby.SetReadyServerRpc(!_lobby.IsLocalReady());
    }

    private void StartGame()
    {
        if (_lobby == null) return;
        if (!NetworkManager.Singleton.IsHost)
        {
            SetStatus("Sadece oda sahibi başlatabilir.");
            return;
        }
        if (!_lobby.CanHostStartGame())
        {
            SetStatus("Herkes hazır ve karakter seçmiş olmalı.");
            return;
        }
        _lobby.RequestStartGameServerRpc();
    }

    private void Refresh()
    {
        if (lobbyPanel == null || !lobbyPanel.activeSelf) return;

        var nm = NetworkManager.Singleton;
        var pc = GetLocalPc();
        int localChar = pc != null ? pc.characterIndex.Value : -1;

        bool ahuTaken = PlayerController.IsCharacterIndexTaken(0, nm != null ? nm.LocalClientId : ulong.MaxValue);
        bool yamanTaken = PlayerController.IsCharacterIndexTaken(1, nm != null ? nm.LocalClientId : ulong.MaxValue);

        if (ahuButton != null)
        {
            ahuButton.interactable = !_waitingSelect && (localChar == 0 || !ahuTaken);
            SetButtonLabel(ahuButton, localChar == 0 ? "Ahu (Seçili)" : "Ahu");
        }
        if (yamanButton != null)
        {
            yamanButton.interactable = !_waitingSelect && (localChar == 1 || !yamanTaken);
            SetButtonLabel(yamanButton, localChar == 1 ? "Yaman (Seçili)" : "Yaman");
        }

        bool isReady = _lobby != null && _lobby.IsLocalReady();
        bool canReady = pc != null && pc.HasSelectedCharacter;
        if (readyButton != null)
            readyButton.interactable = canReady;
        if (readyButtonLabel != null)
            readyButtonLabel.text = isReady ? "Hazır!" : "Hazırım";

        bool isHost = nm != null && nm.IsHost;
        bool canStart = _lobby != null && _lobby.CanHostStartGame();
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = canStart;
        }

        if (statusText != null && !_waitingSelect)
        {
            int ready = _lobby != null ? _lobby.ReadyCount : 0;
            int needed = _lobby != null ? _lobby.ConnectedCount : 0;
            string role = isHost ? "Oda sahibi" : "Misafir";
            string startHint = isHost
                ? (canStart ? " — Oyunu başlatabilirsin" : " — Herkes hazır olunca başlat")
                : " — Oda sahibi başlatacak";
            statusText.text = $"{role} · Hazır {ready}/{needed}{startHint}";
        }

        if (hostIpText != null)
        {
            if (isHost)
            {
                hostIpText.gameObject.SetActive(true);
                hostIpText.text = $"Oda IP: {GetLocalIpSummary()}";
            }
            else
            {
                hostIpText.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>ZeroTier (10.x) öncelikli yerel IPv4 listesi; misafir bunlardan biriyle bağlanır.</summary>
    private static string GetLocalIpSummary()
    {
        var ips = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = ua.Address.ToString();
                    if (ip.StartsWith("169.254")) continue; // APIPA
                    if (!ips.Contains(ip)) ips.Add(ip);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LobbyUI] IP listesi alınamadı: {e.Message}");
        }

        if (ips.Count == 0) return "bulunamadı";

        // ZeroTier IP'leri (10.*) başa al — uzak arkadaş onunla bağlanacak.
        ips.Sort((a, b) => IpPriority(b).CompareTo(IpPriority(a)));
        return string.Join("  |  ", ips.GetRange(0, Mathf.Min(2, ips.Count)));
    }

    private static int IpPriority(string ip)
    {
        if (ip.StartsWith("10.")) return 2;
        if (ip.StartsWith("192.168.")) return 1;
        return 0;
    }

    private static PlayerController GetLocalPc()
    {
        var nm = NetworkManager.Singleton;
        if (nm?.LocalClient?.PlayerObject == null) return null;
        return nm.LocalClient.PlayerObject.GetComponent<PlayerController>();
    }

    private void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    private static void SetButtonLabel(Button btn, string text)
    {
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null) tmp.text = text;
        var legacy = btn.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = text;
    }

    private void ValidateSceneReferences()
    {
        if (connectPanel == null || lobbyPanel == null ||
            ahuButton == null || yamanButton == null ||
            readyButton == null || startGameButton == null ||
            statusText == null || readyButtonLabel == null || hostIpText == null)
        {
            Debug.LogError("[LobbyUI] MainMenu scene UI references are incomplete.", this);
        }
    }
}
