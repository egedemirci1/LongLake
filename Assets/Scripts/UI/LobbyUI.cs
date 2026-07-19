using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MainMenu lobby: karakter seçimi (Ahu / Yaman), Ready, Host Start.
/// Görsel dil DialogueUI / Quest HUD ile uyumlu (kehribar + koyu kart).
/// </summary>
public partial class LobbyUI : MonoBehaviour
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

    [Header("Optional Portraits")]
    [SerializeField] private Sprite ahuPortrait;
    [SerializeField] private Sprite yamanPortrait;

    private static readonly Color AccentColor = new Color(0.95f, 0.77f, 0.32f, 1f);
    private static readonly Color ScrimColor = new Color(0.02f, 0.03f, 0.04f, 0.72f);
    private static readonly Color CardColor = new Color(0.06f, 0.07f, 0.09f, 0.94f);
    private static readonly Color CharCardIdle = new Color(0.10f, 0.11f, 0.13f, 0.98f);
    private static readonly Color CharCardSelected = new Color(0.035f, 0.055f, 0.065f, 1f);
    private static readonly Color SelectionOverlay = new Color(0.025f, 0.045f, 0.055f, 0.96f);
    private static readonly Color SelectedMetaText = new Color(0.82f, 0.86f, 0.88f, 1f);
    private static readonly Color CharCardTaken = new Color(0.07f, 0.07f, 0.08f, 0.75f);
    private static readonly Color MutedText = new Color(0.62f, 0.64f, 0.60f, 1f);
    private static readonly Color BodyText = new Color(0.93f, 0.94f, 0.92f, 1f);
    private static readonly Color ButtonIdle = new Color(0.16f, 0.17f, 0.19f, 1f);
    private static readonly Color ReadyAccent = new Color(0.95f, 0.77f, 0.32f, 1f);
    private static readonly Color StartAccent = new Color(0.32f, 0.72f, 0.48f, 1f);
    private static readonly Color AhuTone = new Color(0.72f, 0.42f, 0.48f, 1f);
    private static readonly Color YamanTone = new Color(0.35f, 0.55f, 0.62f, 1f);

    private LobbyManager _lobby;
    private bool _waitingSelect;
    private bool _lockedForGameplayLoad;
    private bool _returningToMenu;
    private bool _layoutReady;

    private Image _ahuCardBg;
    private Image _yamanCardBg;
    private Image _ahuRing;
    private Image _yamanRing;
    private Image _ahuPortraitImg;
    private Image _yamanPortraitImg;
    private TextMeshProUGUI _ahuName;
    private TextMeshProUGUI _yamanName;
    private TextMeshProUGUI _ahuMeta;
    private TextMeshProUGUI _yamanMeta;
    private TextMeshProUGUI _ahuBadge;
    private TextMeshProUGUI _yamanBadge;
    private Image _readyBg;
    private Image _startBg;
    private CanvasGroup _rootGroup;

    private TextMeshProUGUI _playersEyebrow;
    private readonly PlayerSlotUi[] _playerSlots = new PlayerSlotUi[2];
    private readonly List<ulong> _orderedClients = new List<ulong>(2);
    private readonly HashSet<PlayerController> _hookedPlayers = new HashSet<PlayerController>();
    private float _hookPollTimer;

    private struct PlayerSlotUi
    {
        public GameObject Root;
        public Image Dot;
        public Image Bg;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Detail;
    }

    private void Awake()
    {
        ValidateSceneReferences();
        WireButtons();
        if (lobbyPanel != null)
            lobbyPanel.SetActive(false);
    }

    public void HideForGameplayLoad()
    {
        _lockedForGameplayLoad = true;
        if (lobbyPanel != null)
            lobbyPanel.SetActive(false);
        if (connectPanel != null)
            connectPanel.SetActive(false);
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
        UnhookPlayerControllers();
    }

    private void UnhookPlayerControllers()
    {
        foreach (var pc in _hookedPlayers)
        {
            if (pc != null)
                pc.characterIndex.OnValueChanged -= OnAnyCharacterChanged;
        }
        _hookedPlayers.Clear();
    }

    private void Update()
    {
        if (_lockedForGameplayLoad || _returningToMenu) return;

        if (_lobby == null)
            TryBindLobby();

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        bool inSession = nm.IsListening && (nm.IsHost || nm.IsClient);
        if (inSession)
        {
            ShowLobby();
            _hookPollTimer -= Time.unscaledDeltaTime;
            if (_hookPollTimer <= 0f)
            {
                _hookPollTimer = 0.35f;
                HookPlayerControllers();
            }
        }
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

        EnsureProfessionalLayout();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Refresh();
    }

    private void WireButtons()
    {
        if (ahuButton != null)
        {
            ahuButton.onClick.RemoveAllListeners();
            ahuButton.onClick.AddListener(PlayUiClick);
            ahuButton.onClick.AddListener(() => SelectCharacter(0));
        }
        if (yamanButton != null)
        {
            yamanButton.onClick.RemoveAllListeners();
            yamanButton.onClick.AddListener(PlayUiClick);
            yamanButton.onClick.AddListener(() => SelectCharacter(1));
        }
        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(PlayUiClick);
            readyButton.onClick.AddListener(ToggleReady);
        }
        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(PlayUiClick);
            startGameButton.onClick.AddListener(StartGame);
        }
    }

    private static void PlayUiClick()
    {
        if (GameAudio.Instance != null)
            GameAudio.Instance.PlayButtonClick();
    }

    private void ReturnToMainMenu()
    {
        if (_returningToMenu)
            return;

        PlayUiClick();
        StartCoroutine(ReturnToMainMenuRoutine());
    }

    private IEnumerator ReturnToMainMenuRoutine()
    {
        _returningToMenu = true;
        _waitingSelect = false;

        if (lobbyPanel != null)
            lobbyPanel.SetActive(false);

        UnbindLobby();
        var nm = NetworkManager.Singleton;
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
        if (connectPanel != null)
            connectPanel.SetActive(true);

        _returningToMenu = false;
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

    // -------------------------------------------------------------------------
    // Layout
    // -------------------------------------------------------------------------

}
