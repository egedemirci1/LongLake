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
        if (_lockedForGameplayLoad) return;

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
        EnsureProfessionalLayout();

        var nm = NetworkManager.Singleton;
        var pc = GetLocalPc();
        int localChar = pc != null ? pc.characterIndex.Value : -1;

        bool ahuTaken = PlayerController.IsCharacterIndexTaken(0, nm != null ? nm.LocalClientId : ulong.MaxValue);
        bool yamanTaken = PlayerController.IsCharacterIndexTaken(1, nm != null ? nm.LocalClientId : ulong.MaxValue);

        bool ahuSelected = localChar == 0;
        bool yamanSelected = localChar == 1;

        if (ahuButton != null)
            ahuButton.interactable = !_waitingSelect && (ahuSelected || !ahuTaken);
        if (yamanButton != null)
            yamanButton.interactable = !_waitingSelect && (yamanSelected || !yamanTaken);

        ApplyCharCardVisual(0, ahuSelected, ahuTaken && !ahuSelected);
        ApplyCharCardVisual(1, yamanSelected, yamanTaken && !yamanSelected);

        bool isReady = _lobby != null && _lobby.IsLocalReady();
        bool canReady = pc != null && pc.HasSelectedCharacter;
        if (readyButton != null)
            readyButton.interactable = canReady;
        if (readyButtonLabel != null)
            readyButtonLabel.text = isReady ? "Hazır!" : "Hazırım";
        if (_readyBg != null)
            _readyBg.color = isReady ? ReadyAccent : ButtonIdle;
        if (readyButtonLabel != null)
            readyButtonLabel.color = isReady ? new Color(0.12f, 0.10f, 0.06f, 1f) : BodyText;

        bool isHost = nm != null && nm.IsHost;
        bool canStart = _lobby != null && _lobby.CanHostStartGame();
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = canStart;
        }
        if (_startBg != null)
            _startBg.color = canStart ? StartAccent : ButtonIdle;

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
                hostIpText.text = $"Oda IP  ·  {GetLocalIpSummary()}";
            }
            else
            {
                hostIpText.gameObject.SetActive(false);
            }
        }

        RefreshPlayerRoster();
    }

    private void HookPlayerControllers()
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (pc == null || _hookedPlayers.Contains(pc)) continue;
            _hookedPlayers.Add(pc);
            pc.characterIndex.OnValueChanged += OnAnyCharacterChanged;
        }
    }

    private void OnAnyCharacterChanged(int previous, int current) => Refresh();

    private void RefreshPlayerRoster()
    {
        if (_playerSlots[0].Title == null) return;

        var nm = NetworkManager.Singleton;
        _orderedClients.Clear();
        if (_lobby != null)
            _lobby.CopyConnectedClientsOrdered(_orderedClients);

        int connected = _orderedClients.Count;
        if (_playersEyebrow != null)
            _playersEyebrow.text = $"OYUNCULAR  ·  {connected}/2";

        for (int slot = 0; slot < 2; slot++)
        {
            ref PlayerSlotUi ui = ref _playerSlots[slot];
            if (ui.Title == null) continue;

            if (slot >= _orderedClients.Count)
            {
                ui.Bg.color = new Color(0.08f, 0.09f, 0.10f, 0.85f);
                ui.Dot.color = new Color(0.35f, 0.36f, 0.34f, 1f);
                ui.Title.text = slot == 1 ? "2. Oyuncu" : "Oyuncu";
                ui.Title.color = MutedText;
                ui.Detail.text = "Bağlantı bekleniyor…";
                ui.Detail.color = new Color(MutedText.r, MutedText.g, MutedText.b, 0.75f);
                continue;
            }

            ulong clientId = _orderedClients[slot];
            bool isLocal = nm != null && clientId == nm.LocalClientId;
            bool isHostClient = clientId == NetworkManager.ServerClientId;
            bool ready = _lobby != null && _lobby.IsClientReady(clientId);

            string role = isHostClient ? "Oda sahibi" : "Misafir";
            string who = isLocal ? $"Sen · {role}" : role;

            int charIdx = GetCharacterForClient(clientId);
            string charName = charIdx == 0 ? "Ahu" : charIdx == 1 ? "Yaman" : "Karakter seçiyor";
            string readyLabel = ready ? "Hazır" : "Hazır değil";

            ui.Bg.color = isLocal ? new Color(0.14f, 0.13f, 0.10f, 0.98f) : new Color(0.10f, 0.11f, 0.13f, 0.95f);
            ui.Dot.color = ready ? StartAccent : AccentColor;
            ui.Title.text = who;
            ui.Title.color = BodyText;
            ui.Detail.text = $"{charName}  ·  {readyLabel}";
            ui.Detail.color = ready ? StartAccent : MutedText;
        }
    }

    private static int GetCharacterForClient(ulong clientId)
    {
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (pc == null || !pc.IsSpawned) continue;
            if (pc.OwnerClientId == clientId)
                return pc.characterIndex.Value;
        }
        return -1;
    }

    private void ApplyCharCardVisual(int index, bool selected, bool takenByOther)
    {
        bool isAhu = index == 0;
        Image bg = isAhu ? _ahuCardBg : _yamanCardBg;
        Image ring = isAhu ? _ahuRing : _yamanRing;
        TextMeshProUGUI name = isAhu ? _ahuName : _yamanName;
        TextMeshProUGUI meta = isAhu ? _ahuMeta : _yamanMeta;
        TextMeshProUGUI badge = isAhu ? _ahuBadge : _yamanBadge;
        Color tone = isAhu ? AhuTone : YamanTone;

        if (bg != null)
        {
            if (selected) bg.color = CharCardSelected;
            else if (takenByOther) bg.color = CharCardTaken;
            else bg.color = CharCardIdle;
        }

        if (ring != null)
        {
            ring.enabled = selected;
            // Rounded sprite dolu bir gorsel oldugu icin sari kullanmak kartin
            // tamamini boyuyordu. Koyu katman metin kontrastini korur;
            // secim vurgusu alttaki sari badge ile verilir.
            ring.color = SelectionOverlay;
        }

        if (name != null)
            name.color = takenByOther ? MutedText : (selected ? Color.white : BodyText);

        if (meta != null)
            meta.color = takenByOther
                ? new Color(MutedText.r, MutedText.g, MutedText.b, 0.55f)
                : (selected ? SelectedMetaText : MutedText);

        if (badge != null)
        {
            if (selected)
            {
                badge.text = "SEÇİLDİ";
                badge.color = AccentColor;
                badge.fontSize = 13f;
            }
            else if (takenByOther)
            {
                badge.text = "ALINDI";
                badge.color = new Color(0.75f, 0.35f, 0.35f, 1f);
                badge.fontSize = 12f;
            }
            else
            {
                badge.text = "SEÇ";
                badge.color = tone;
                badge.fontSize = 12f;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Layout
    // -------------------------------------------------------------------------

    private void EnsureProfessionalLayout()
    {
        if (_layoutReady || lobbyPanel == null) return;
        _layoutReady = true;

        var panelRt = lobbyPanel.GetComponent<RectTransform>();
        var panelImg = lobbyPanel.GetComponent<Image>();
        if (panelImg != null)
        {
            panelImg.color = ScrimColor;
            panelImg.raycastTarget = true;
        }

        // Eski çocukları gizle — referanslar korunur, yeni karta taşınır.
        for (int i = 0; i < lobbyPanel.transform.childCount; i++)
            lobbyPanel.transform.GetChild(i).gameObject.SetActive(false);

        var root = new GameObject("LobbyRoot", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(lobbyPanel.transform, false);
        var rootRt = root.GetComponent<RectTransform>();
        StretchFull(rootRt);
        _rootGroup = root.GetComponent<CanvasGroup>();

        var card = new GameObject("LobbyCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(root.transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(860f, 680f);
        cardRt.anchoredPosition = new Vector2(0f, 0f);

        var cardImg = card.GetComponent<Image>();
        cardImg.sprite = RuntimeUiSprites.GetRoundedSprite(14);
        cardImg.type = Image.Type.Sliced;
        cardImg.color = CardColor;
        cardImg.raycastTarget = true;

        var accent = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accent.transform.SetParent(card.transform, false);
        var aRt = accent.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(0f, 0f);
        aRt.anchorMax = new Vector2(0f, 1f);
        aRt.pivot = new Vector2(0f, 0.5f);
        aRt.anchoredPosition = new Vector2(10f, 0f);
        aRt.sizeDelta = new Vector2(4f, -36f);
        var aImg = accent.GetComponent<Image>();
        aImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        aImg.type = Image.Type.Sliced;
        aImg.color = AccentColor;
        aImg.raycastTarget = false;

        // Header
        var eyebrow = CreateTmp(card.transform, "Eyebrow", 14f, FontStyles.Bold, AccentColor);
        Place(eyebrow.rectTransform, 36f, -28f, 36f, 22f);
        eyebrow.characterSpacing = 8f;
        eyebrow.alignment = TextAlignmentOptions.MidlineLeft;
        eyebrow.text = "LOBI";

        var title = CreateTmp(card.transform, "Title", 34f, FontStyles.Bold, BodyText);
        Place(title.rectTransform, 36f, -52f, 36f, 44f);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.text = "Karakterini Seç";

        var subtitle = CreateTmp(card.transform, "Subtitle", 15f, FontStyles.Normal, MutedText);
        Place(subtitle.rectTransform, 36f, -96f, 36f, 24f);
        subtitle.alignment = TextAlignmentOptions.MidlineLeft;
        subtitle.text = "Her oyuncu farklı bir karakter alır. Seçimin kalıcıdır.";

        // Bağlı oyuncular — host + misafir her iki tarafta da görünür
        BuildPlayerRoster(card.transform);

        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rule.transform.SetParent(card.transform, false);
        var ruleRt = rule.GetComponent<RectTransform>();
        ruleRt.anchorMin = new Vector2(0f, 1f);
        ruleRt.anchorMax = new Vector2(0f, 1f);
        ruleRt.pivot = new Vector2(0f, 0.5f);
        ruleRt.anchoredPosition = new Vector2(36f, -210f);
        ruleRt.sizeDelta = new Vector2(72f, 2f);
        var ruleImg = rule.GetComponent<Image>();
        ruleImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        ruleImg.type = Image.Type.Sliced;
        ruleImg.color = AccentColor;
        ruleImg.raycastTarget = false;

        // Character row — altta aksiyon butonlarına boşluk bırak
        var row = new GameObject("CharacterRow", typeof(RectTransform));
        row.transform.SetParent(card.transform, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 1f);
        rowRt.anchorMax = new Vector2(0.5f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -220f);
        rowRt.sizeDelta = new Vector2(780f, 240f);

        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 28f;
        h.childAlignment = TextAnchor.UpperCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        h.padding = new RectOffset(0, 0, 0, 0);

        RestyleCharacterButton(
            ahuButton, row.transform, "Ahu", "Meraklı · Gözlemci", AhuTone, ahuPortrait,
            out _ahuCardBg, out _ahuRing, out _ahuPortraitImg, out _ahuName, out _ahuMeta, out _ahuBadge);

        RestyleCharacterButton(
            yamanButton, row.transform, "Yaman", "Kararlı · Koruyucu", YamanTone, yamanPortrait,
            out _yamanCardBg, out _yamanRing, out _yamanPortraitImg, out _yamanName, out _yamanMeta, out _yamanBadge);

        // Actions — kartların ALTINDA (bottom-anchored, status/IP üstünde)
        var actions = new GameObject("Actions", typeof(RectTransform));
        actions.transform.SetParent(card.transform, false);
        actions.transform.SetAsLastSibling();
        var actRt = actions.GetComponent<RectTransform>();
        actRt.anchorMin = new Vector2(0.5f, 0f);
        actRt.anchorMax = new Vector2(0.5f, 0f);
        actRt.pivot = new Vector2(0.5f, 0f);
        actRt.anchoredPosition = new Vector2(0f, 78f);
        actRt.sizeDelta = new Vector2(780f, 48f);

        var actH = actions.AddComponent<HorizontalLayoutGroup>();
        actH.spacing = 16f;
        actH.childAlignment = TextAnchor.MiddleCenter;
        actH.childControlWidth = true;
        actH.childControlHeight = true;
        actH.childForceExpandWidth = true;
        actH.childForceExpandHeight = false;
        actH.padding = new RectOffset(0, 0, 0, 0);

        RestyleActionButton(readyButton, actions.transform, readyButtonLabel, false, out _readyBg);
        if (readyButtonLabel != null)
            readyButtonLabel.text = "Hazırım";

        RestyleActionButton(startGameButton, actions.transform, null, true, out _startBg);
        var startLabel = startGameButton != null
            ? startGameButton.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (startLabel != null)
        {
            startLabel.text = "Oyunu Başlat";
            startLabel.fontSize = 18f;
            startLabel.fontStyle = FontStyles.Bold;
            startLabel.color = BodyText;
            startLabel.alignment = TextAlignmentOptions.Center;
        }

        // Status + IP — en altta, butonların altında
        if (statusText != null)
        {
            statusText.transform.SetParent(card.transform, false);
            statusText.transform.SetAsLastSibling();
            statusText.gameObject.SetActive(true);
            Place(statusText.rectTransform, 36f, 44f, 36f, 24f, fromBottom: true);
            statusText.fontSize = 14f;
            statusText.fontStyle = FontStyles.Normal;
            statusText.color = MutedText;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.raycastTarget = false;
        }

        if (hostIpText != null)
        {
            hostIpText.transform.SetParent(card.transform, false);
            hostIpText.transform.SetAsLastSibling();
            hostIpText.gameObject.SetActive(true);
            Place(hostIpText.rectTransform, 36f, 18f, 36f, 20f, fromBottom: true);
            hostIpText.fontSize = 12f;
            hostIpText.fontStyle = FontStyles.Normal;
            hostIpText.color = new Color(MutedText.r, MutedText.g, MutedText.b, 0.85f);
            hostIpText.alignment = TextAlignmentOptions.Center;
            hostIpText.characterSpacing = 1f;
            hostIpText.raycastTarget = false;
        }
    }

    private void BuildPlayerRoster(Transform card)
    {
        _playersEyebrow = CreateTmp(card, "PlayersEyebrow", 12f, FontStyles.Bold, AccentColor);
        Place(_playersEyebrow.rectTransform, 36f, -128f, 36f, 18f);
        _playersEyebrow.characterSpacing = 4f;
        _playersEyebrow.alignment = TextAlignmentOptions.MidlineLeft;
        _playersEyebrow.text = "OYUNCULAR  ·  1/2";

        var roster = new GameObject("PlayerRoster", typeof(RectTransform));
        roster.transform.SetParent(card, false);
        var rosterRt = roster.GetComponent<RectTransform>();
        rosterRt.anchorMin = new Vector2(0.5f, 1f);
        rosterRt.anchorMax = new Vector2(0.5f, 1f);
        rosterRt.pivot = new Vector2(0.5f, 1f);
        rosterRt.anchoredPosition = new Vector2(0f, -150f);
        rosterRt.sizeDelta = new Vector2(780f, 52f);

        var h = roster.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;

        for (int i = 0; i < 2; i++)
            _playerSlots[i] = CreatePlayerSlot(roster.transform, i);
    }

    private PlayerSlotUi CreatePlayerSlot(Transform parent, int index)
    {
        var go = new GameObject($"PlayerSlot{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var bg = go.GetComponent<Image>();
        bg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.08f, 0.09f, 0.10f, 0.85f);
        bg.raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = 52f;
        le.preferredHeight = 52f;

        var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dotGo.transform.SetParent(go.transform, false);
        var dotRt = dotGo.GetComponent<RectTransform>();
        dotRt.anchorMin = new Vector2(0f, 0.5f);
        dotRt.anchorMax = new Vector2(0f, 0.5f);
        dotRt.pivot = new Vector2(0.5f, 0.5f);
        dotRt.anchoredPosition = new Vector2(22f, 0f);
        dotRt.sizeDelta = new Vector2(10f, 10f);
        var dot = dotGo.GetComponent<Image>();
        dot.sprite = RuntimeUiSprites.GetRoundedSprite(8);
        dot.type = Image.Type.Sliced;
        dot.color = new Color(0.35f, 0.36f, 0.34f, 1f);
        dot.raycastTarget = false;

        var title = CreateTmp(go.transform, "Title", 15f, FontStyles.Bold, BodyText);
        var titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 0.5f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(40f, 2f);
        titleRt.offsetMax = new Vector2(-14f, -4f);
        title.alignment = TextAlignmentOptions.BottomLeft;
        title.text = index == 0 ? "Oda sahibi" : "2. Oyuncu";

        var detail = CreateTmp(go.transform, "Detail", 12f, FontStyles.Normal, MutedText);
        var detailRt = detail.rectTransform;
        detailRt.anchorMin = new Vector2(0f, 0f);
        detailRt.anchorMax = new Vector2(1f, 0.5f);
        detailRt.offsetMin = new Vector2(40f, 6f);
        detailRt.offsetMax = new Vector2(-14f, -2f);
        detail.alignment = TextAlignmentOptions.TopLeft;
        detail.text = "Bağlantı bekleniyor…";

        return new PlayerSlotUi
        {
            Root = go,
            Dot = dot,
            Bg = bg,
            Title = title,
            Detail = detail
        };
    }

    private void RestyleCharacterButton(
        Button button, Transform parent, string displayName, string meta,
        Color tone, Sprite portrait,
        out Image cardBg, out Image ring, out Image portraitImg,
        out TextMeshProUGUI nameTmp, out TextMeshProUGUI metaTmp, out TextMeshProUGUI badgeTmp)
    {
        cardBg = null;
        ring = null;
        portraitImg = null;
        nameTmp = null;
        metaTmp = null;
        badgeTmp = null;
        if (button == null) return;

        // Eski child label'ları temizle (Destroy gecikmeli; Immediate gerekir)
        while (button.transform.childCount > 0)
            DestroyImmediate(button.transform.GetChild(0).gameObject);

        button.transform.SetParent(parent, false);
        button.gameObject.SetActive(true);

        var le = button.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = button.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;
        le.minWidth = 300f;
        le.minHeight = 240f;
        le.preferredWidth = 370f;
        le.preferredHeight = 240f;

        var btnRt = button.GetComponent<RectTransform>();
        btnRt.sizeDelta = new Vector2(370f, 240f);

        cardBg = button.GetComponent<Image>();
        if (cardBg == null) cardBg = button.gameObject.AddComponent<Image>();
        cardBg.sprite = RuntimeUiSprites.GetRoundedSprite(12);
        cardBg.type = Image.Type.Sliced;
        cardBg.color = CharCardIdle;
        button.targetGraphic = cardBg;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.85f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        // Selection ring
        var ringGo = new GameObject("SelectRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ringGo.transform.SetParent(button.transform, false);
        var ringRt = ringGo.GetComponent<RectTransform>();
        StretchFull(ringRt);
        ringRt.offsetMin = new Vector2(-3f, -3f);
        ringRt.offsetMax = new Vector2(3f, 3f);
        ring = ringGo.GetComponent<Image>();
        ring.sprite = RuntimeUiSprites.GetRoundedSprite(14);
        ring.type = Image.Type.Sliced;
        ring.color = AccentColor;
        ring.raycastTarget = false;
        ring.enabled = false;

        // Portrait plate — sprite varsa portre, yoksa monogram
        var plate = new GameObject("PortraitPlate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        plate.transform.SetParent(button.transform, false);
        var plateRt = plate.GetComponent<RectTransform>();
        plateRt.anchorMin = new Vector2(0.5f, 1f);
        plateRt.anchorMax = new Vector2(0.5f, 1f);
        plateRt.pivot = new Vector2(0.5f, 1f);
        plateRt.anchoredPosition = new Vector2(0f, -12f);
        plateRt.sizeDelta = new Vector2(300f, 130f);
        var plateImg = plate.GetComponent<Image>();
        plateImg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        plateImg.type = Image.Type.Sliced;
        plateImg.color = portrait != null
            ? new Color(0.08f, 0.09f, 0.10f, 1f)
            : new Color(tone.r * 0.35f, tone.g * 0.35f, tone.b * 0.35f, 1f);
        plateImg.raycastTarget = false;

        // Mask so portrait fits rounded plate
        var maskGo = new GameObject("Mask", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        maskGo.transform.SetParent(plate.transform, false);
        StretchFull(maskGo.GetComponent<RectTransform>());
        var maskImg = maskGo.GetComponent<Image>();
        maskImg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        maskImg.type = Image.Type.Sliced;
        maskImg.color = Color.white;
        maskImg.raycastTarget = false;
        maskGo.GetComponent<Mask>().showMaskGraphic = false;

        portraitImg = null;
        if (portrait != null)
        {
            var pGo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pGo.transform.SetParent(maskGo.transform, false);
            StretchFull(pGo.GetComponent<RectTransform>());
            portraitImg = pGo.GetComponent<Image>();
            portraitImg.sprite = portrait;
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;
            portraitImg.color = Color.white;
            // Hafif zoom — yüz kartta daha büyük dursun
            var prt = pGo.GetComponent<RectTransform>();
            prt.localScale = new Vector3(1.15f, 1.15f, 1f);
        }
        else
        {
            var mono = CreateTmp(plate.transform, "Monogram", 72f, FontStyles.Bold, tone);
            StretchFull(mono.rectTransform);
            mono.alignment = TextAlignmentOptions.Center;
            mono.text = displayName.Length > 0 ? displayName.Substring(0, 1).ToUpperInvariant() : "?";
            mono.characterSpacing = 4f;
        }

        // Soft bottom fade on plate
        var fade = new GameObject("Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fade.transform.SetParent(plate.transform, false);
        var fadeRt = fade.GetComponent<RectTransform>();
        fadeRt.anchorMin = new Vector2(0f, 0f);
        fadeRt.anchorMax = new Vector2(1f, 0.35f);
        fadeRt.offsetMin = Vector2.zero;
        fadeRt.offsetMax = Vector2.zero;
        var fadeImg = fade.GetComponent<Image>();
        fadeImg.color = new Color(0.06f, 0.07f, 0.09f, 0.45f);
        fadeImg.raycastTarget = false;

        nameTmp = CreateTmp(button.transform, "Name", 28f, FontStyles.Bold, BodyText);
        var nameRt = nameTmp.rectTransform;
        nameRt.anchorMin = new Vector2(0f, 0f);
        nameRt.anchorMax = new Vector2(1f, 0f);
        nameRt.pivot = new Vector2(0.5f, 0f);
        nameRt.anchoredPosition = new Vector2(0f, 68f);
        nameRt.sizeDelta = new Vector2(-32f, 32f);
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.text = displayName;
        nameTmp.fontSize = 26f;

        metaTmp = CreateTmp(button.transform, "Meta", 15f, FontStyles.Bold, MutedText);
        var metaRt = metaTmp.rectTransform;
        metaRt.anchorMin = new Vector2(0f, 0f);
        metaRt.anchorMax = new Vector2(1f, 0f);
        metaRt.pivot = new Vector2(0.5f, 0f);
        metaRt.anchoredPosition = new Vector2(0f, 42f);
        metaRt.sizeDelta = new Vector2(-24f, 24f);
        metaTmp.alignment = TextAlignmentOptions.Center;
        metaTmp.text = meta;

        badgeTmp = CreateTmp(button.transform, "Badge", 12f, FontStyles.Bold, tone);
        var badgeRt = badgeTmp.rectTransform;
        badgeRt.anchorMin = new Vector2(0f, 0f);
        badgeRt.anchorMax = new Vector2(1f, 0f);
        badgeRt.pivot = new Vector2(0.5f, 0f);
        badgeRt.anchoredPosition = new Vector2(0f, 14f);
        badgeRt.sizeDelta = new Vector2(-32f, 20f);
        badgeTmp.alignment = TextAlignmentOptions.Center;
        badgeTmp.characterSpacing = 3f;
        badgeTmp.text = "SEÇ";
    }

    private void RestyleActionButton(
        Button button, Transform parent, TextMeshProUGUI existingLabel, bool isStart, out Image bg)
    {
        bg = null;
        if (button == null) return;

        button.transform.SetParent(parent, false);
        button.gameObject.SetActive(true);

        var le = button.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = button.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;
        le.minHeight = 48f;
        le.preferredHeight = 48f;
        le.minWidth = 200f;

        var btnRt = button.GetComponent<RectTransform>();
        btnRt.localScale = Vector3.one;
        btnRt.sizeDelta = new Vector2(360f, 48f);

        bg = button.GetComponent<Image>();
        if (bg == null) bg = button.gameObject.AddComponent<Image>();
        bg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        bg.type = Image.Type.Sliced;
        bg.color = isStart ? StartAccent : ButtonIdle;
        button.targetGraphic = bg;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI label = existingLabel;
        if (label == null)
            label = button.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label != null)
        {
            label.transform.SetParent(button.transform, false);
            StretchFull(label.rectTransform);
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.color = isStart ? new Color(0.08f, 0.14f, 0.10f, 1f) : BodyText;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
        else
        {
            label = CreateTmp(button.transform, "Label", 18f, FontStyles.Bold,
                isStart ? new Color(0.08f, 0.14f, 0.10f, 1f) : BodyText);
            StretchFull(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            if (isStart) label.text = "Oyunu Başlat";
        }

        if (!isStart && readyButtonLabel == null)
            readyButtonLabel = label;
    }

    private static TextMeshProUGUI CreateTmp(
        Transform parent, string name, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Place(RectTransform rt, float left, float yFromTop, float right, float height, bool fromBottom = false)
    {
        if (fromBottom)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, yFromTop);
            rt.offsetMax = new Vector2(-right, yFromTop + height);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, yFromTop - height);
            rt.offsetMax = new Vector2(-right, yFromTop);
        }
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

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
                    if (ip.StartsWith("169.254")) continue;
                    if (!ips.Contains(ip)) ips.Add(ip);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LobbyUI] IP listesi alınamadı: {e.Message}");
        }

        if (ips.Count == 0) return "bulunamadı";

        ips.Sort((a, b) => IpPriority(b).CompareTo(IpPriority(a)));
        return string.Join("  ·  ", ips.GetRange(0, Mathf.Min(2, ips.Count)));
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
