using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MainMenu ve gameplay tarafından paylaşılan ayarlar paneli.
/// Gameplay'de Escape menüsünü yönetir; co-op akışında timeScale değiştirmez.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class GameMenuUI : MonoBehaviour
{
    private const string MainMenuScene = "MainMenu";
    private static readonly string[] GameplayScenes = { "CrashSite_Main", "TestScene" };

    public static GameMenuUI Instance { get; private set; }
    public static bool IsBlockingGameplay =>
        Instance != null && Instance._isGameplay &&
        (Instance._pauseOpen ||
         (Instance._settingsPanel != null && Instance._settingsPanel.activeSelf));

    private Canvas _canvas;
    private GameObject _pausePanel;
    private GameObject _settingsPanel;
    private GameObject _mainMenuSettingsButton;
    private bool _isGameplay;
    private bool _pauseOpen;
    private bool _settingsFromPause;
    private bool _returningToMenu;
    private NetworkManager _hookedNetworkManager;

    // LobbyUI / DialogueUI ile aynı görsel dil: koyu kart + kehribar vurgu.
    private static readonly Color Scrim = new Color(0.02f, 0.03f, 0.04f, 0.78f);
    private static readonly Color Card = new Color(0.06f, 0.07f, 0.09f, 0.96f);
    private static readonly Color CardInner = new Color(0.085f, 0.095f, 0.115f, 1f);
    private static readonly Color ButtonColor = new Color(0.145f, 0.16f, 0.185f, 1f);
    private static readonly Color Accent = new Color(0.95f, 0.77f, 0.32f, 1f);
    private static readonly Color AccentDim = new Color(0.95f, 0.77f, 0.32f, 0.35f);
    private static readonly Color OnAccent = new Color(0.12f, 0.10f, 0.05f, 1f);
    private static readonly Color DangerTone = new Color(0.72f, 0.38f, 0.36f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.94f, 0.92f, 1f);
    private static readonly Color Muted = new Color(0.62f, 0.64f, 0.60f, 1f);
    private static readonly Color TrackColor = new Color(0.11f, 0.12f, 0.145f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        var go = new GameObject("GameMenuUI");
        DontDestroyOnLoad(go);
        go.AddComponent<GameMenuUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCanvas();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        ConfigureForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnhookNetworkManager();
        if (Instance == this)
            Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ConfigureForScene(scene.name);
    }

    private void ConfigureForScene(string sceneName)
    {
        _isGameplay = IsGameplayScene(sceneName);
        _pauseOpen = false;
        _settingsFromPause = false;
        _returningToMenu = false;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);
        _mainMenuSettingsButton.SetActive(sceneName == MainMenuScene);
        SetCursorForGameplay(false);

        UnhookNetworkManager();
        if (_isGameplay)
            StartCoroutine(HookNetworkManagerNextFrame());
    }

    private static bool IsGameplayScene(string sceneName)
    {
        for (int i = 0; i < GameplayScenes.Length; i++)
        {
            if (GameplayScenes[i] == sceneName)
                return true;
        }
        return false;
    }

    private IEnumerator HookNetworkManagerNextFrame()
    {
        yield return null;
        _hookedNetworkManager = NetworkManager.Singleton;
        if (_hookedNetworkManager != null)
            _hookedNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void UnhookNetworkManager()
    {
        if (_hookedNetworkManager != null)
            _hookedNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        _hookedNetworkManager = null;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!_isGameplay || _returningToMenu || _hookedNetworkManager == null)
            return;
        if (_hookedNetworkManager.IsServer)
            return;
        StartCoroutine(ReturnToMainMenuRoutine());
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape) || _returningToMenu)
            return;

        if (_settingsPanel.activeSelf)
        {
            CloseSettings();
            return;
        }

        if (!_isGameplay)
            return;

        if (_pauseOpen)
        {
            ResumeGame();
            return;
        }

        // Escape önce açık olan envanter/notebook/görev/diyalog tarafından tüketilsin.
        if (OtherUiOwnsCursor())
            return;

        OpenPause();
    }

    private static bool OtherUiOwnsCursor()
    {
        if (DialogueManager.IsDialogueOpen)
            return true;

        var inventory = FindFirstObjectByType<InventoryManager>();
        if (inventory != null && inventory.mainInventoryObject != null &&
            inventory.mainInventoryObject.activeSelf)
            return true;

        var notebook = FindFirstObjectByType<NotebookUI>();
        if (notebook != null && notebook.IsNotebookOpen)
            return true;

        return QuestManager.Instance != null && QuestManager.Instance.IsPanelOpen;
    }

    private void OpenPause()
    {
        _pauseOpen = true;
        _pausePanel.SetActive(true);
        SetCursorForGameplay(true);
    }

    private void ResumeGame()
    {
        _pauseOpen = false;
        _settingsFromPause = false;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);
        SetCursorForGameplay(false);
    }

    private void OpenSettingsFromMainMenu()
    {
        PlayClick();
        _settingsFromPause = false;
        _settingsPanel.SetActive(true);
    }

    private void OpenSettingsFromPause()
    {
        PlayClick();
        _settingsFromPause = true;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(true);
    }

    private void CloseSettings()
    {
        _settingsPanel.SetActive(false);
        if (_settingsFromPause && _pauseOpen)
            _pausePanel.SetActive(true);
    }

    private void ReturnToMainMenu()
    {
        if (_returningToMenu)
            return;
        PlayClick();
        StartCoroutine(ReturnToMainMenuRoutine());
    }

    private IEnumerator ReturnToMainMenuRoutine()
    {
        _returningToMenu = true;
        _pausePanel.SetActive(false);
        _settingsPanel.SetActive(false);

        NetworkManager nm = NetworkManager.Singleton;
        UnhookNetworkManager();
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

        if (nm != null)
        {
            Destroy(nm.gameObject);
            yield return null;
        }

        SceneManager.LoadScene(MainMenuScene, LoadSceneMode.Single);
    }

    private static void SetCursorForGameplay(bool menuOpen)
    {
        if (!IsGameplayScene(SceneManager.GetActiveScene().name))
            return;
        Cursor.lockState = menuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = menuOpen;
    }

    private void BuildCanvas()
    {
        var canvasGo = new GameObject(
            "GameMenuCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildMainMenuSettingsButton(canvasGo.transform);
        BuildPausePanel(canvasGo.transform);
        BuildSettingsPanel(canvasGo.transform);
    }

    private void BuildMainMenuSettingsButton(Transform parent)
    {
        // Ana menüde sade "ghost" buton — sahnedeki ana CTA'larla yarışmasın.
        Button button = CreateButton(parent, "SettingsButton", "AYARLAR", ButtonStyle.Ghost);
        RectTransform rt = button.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-36f, -32f);
        rt.sizeDelta = new Vector2(148f, 42f);
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        label.fontSize = 14f;
        label.characterSpacing = 4f;
        button.onClick.AddListener(OpenSettingsFromMainMenu);
        _mainMenuSettingsButton = button.gameObject;
    }

    private void BuildPausePanel(Transform parent)
    {
        _pausePanel = CreateOverlay(parent, "PausePanel");
        Transform card = CreateCard(_pausePanel.transform, "PauseCard", new Vector2(460f, 490f));
        AddHeader(card, "OYUN MENÜSÜ", "Duraklatıldı",
            "Çevrimiçi oyun arka planda devam eder.");

        Button resume = CreateButton(card, "ResumeButton", "Devam Et", ButtonStyle.Primary);
        PlaceCentered(resume.GetComponent<RectTransform>(), new Vector2(0f, 8f), new Vector2(356f, 54f));
        resume.onClick.AddListener(() => { PlayClick(); ResumeGame(); });

        Button settings = CreateButton(card, "SettingsButton", "Ayarlar", ButtonStyle.Secondary);
        PlaceCentered(settings.GetComponent<RectTransform>(), new Vector2(0f, -58f), new Vector2(356f, 54f));
        settings.onClick.AddListener(OpenSettingsFromPause);

        Button menu = CreateButton(card, "MainMenuButton", "Ana Menüye Dön", ButtonStyle.Danger);
        PlaceCentered(menu.GetComponent<RectTransform>(), new Vector2(0f, -124f), new Vector2(356f, 54f));
        menu.onClick.AddListener(ReturnToMainMenu);

        AddFooterHint(card, "ESC  ·  menüyü kapat");
        _pausePanel.SetActive(false);
    }

    private void BuildSettingsPanel(Transform parent)
    {
        _settingsPanel = CreateOverlay(parent, "SettingsPanel");
        Transform card = CreateCard(_settingsPanel.transform, "SettingsCard", new Vector2(560f, 620f));
        AddHeader(card, "AYARLAR", "Ses",
            "Değişiklikler anında uygulanır ve kaydedilir.");

        float y = 92f;
        const float rowStep = 84f;
        CreateSliderRow(card, "ANA SES", new Vector2(0f, y),
            () => AudioSettingsService.MasterVolume, AudioSettingsService.SetMasterVolume);
        CreateSliderRow(card, "MÜZİK", new Vector2(0f, y -= rowStep),
            () => AudioSettingsService.MusicVolume, AudioSettingsService.SetMusicVolume);
        CreateSliderRow(card, "EFEKTLER", new Vector2(0f, y -= rowStep),
            () => AudioSettingsService.SfxVolume, AudioSettingsService.SetSfxVolume);
        CreateSliderRow(card, "ORTAM", new Vector2(0f, y -= rowStep),
            () => AudioSettingsService.AmbienceVolume, AudioSettingsService.SetAmbienceVolume);

        Button back = CreateButton(card, "BackButton", "Geri", ButtonStyle.Secondary);
        PlaceCentered(back.GetComponent<RectTransform>(), new Vector2(0f, -238f), new Vector2(356f, 54f));
        back.onClick.AddListener(() => { PlayClick(); CloseSettings(); });

        AddFooterHint(card, "ESC  ·  geri dön");
        _settingsPanel.SetActive(false);
    }

    /// <summary>Sol hizalı eyebrow + başlık + alt metin ve kehribar ayraç çizgisi.</summary>
    private static void AddHeader(Transform card, string eyebrow, string title, string subtitle)
    {
        RectTransform cardRt = card.GetComponent<RectTransform>();
        float w = cardRt.sizeDelta.x - 88f;

        // Sol kenarda dikey vurgu şeridi
        var accent = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accent.transform.SetParent(card, false);
        var aRt = accent.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(0f, 0f);
        aRt.anchorMax = new Vector2(0f, 1f);
        aRt.pivot = new Vector2(0f, 0.5f);
        aRt.anchoredPosition = new Vector2(12f, 0f);
        aRt.sizeDelta = new Vector2(4f, -44f);
        var aImg = accent.GetComponent<Image>();
        aImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        aImg.type = Image.Type.Sliced;
        aImg.color = Accent;
        aImg.raycastTarget = false;

        TextMeshProUGUI eye = CreateLabel(card, eyebrow, 13f, Accent, Vector2.zero, Vector2.zero);
        PlaceTop(eye.rectTransform, 44f, -34f, new Vector2(w, 22f));
        eye.characterSpacing = 7f;
        eye.fontStyle = FontStyles.Bold;
        eye.alignment = TextAlignmentOptions.MidlineLeft;

        TextMeshProUGUI titleTmp = CreateLabel(card, title, 32f, TextColor, Vector2.zero, Vector2.zero);
        PlaceTop(titleTmp.rectTransform, 44f, -58f, new Vector2(w, 44f));
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.MidlineLeft;

        TextMeshProUGUI sub = CreateLabel(card, subtitle, 14f, Muted, Vector2.zero, Vector2.zero);
        PlaceTop(sub.rectTransform, 44f, -102f, new Vector2(w, 22f));
        sub.alignment = TextAlignmentOptions.MidlineLeft;

        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rule.transform.SetParent(card, false);
        var ruleRt = rule.GetComponent<RectTransform>();
        PlaceTop(ruleRt, 44f, -136f, new Vector2(64f, 2f));
        ruleRt.anchorMax = new Vector2(0f, 1f);
        var ruleImg = rule.GetComponent<Image>();
        ruleImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        ruleImg.type = Image.Type.Sliced;
        ruleImg.color = AccentDim;
        ruleImg.raycastTarget = false;
    }

    private static void AddFooterHint(Transform card, string text)
    {
        TextMeshProUGUI hint = CreateLabel(card, text, 12.5f, new Color(Muted.r, Muted.g, Muted.b, 0.8f),
            Vector2.zero, Vector2.zero);
        var rt = hint.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(24f, 16f);
        rt.offsetMax = new Vector2(-24f, 36f);
        hint.characterSpacing = 2f;
        hint.alignment = TextAlignmentOptions.Center;
    }

    private void CreateSliderRow(
        Transform parent,
        string label,
        Vector2 position,
        System.Func<float> getValue,
        UnityEngine.Events.UnityAction<float> setValue)
    {
        // Satır: hafif iç kart; üstte etiket + değer, altta tam genişlik slider.
        var row = new GameObject(label + "Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        row.transform.SetParent(parent, false);
        PlaceCentered(row.GetComponent<RectTransform>(), position, new Vector2(472f, 72f));
        Image rowBg = row.GetComponent<Image>();
        rowBg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        rowBg.type = Image.Type.Sliced;
        rowBg.color = CardInner;
        rowBg.raycastTarget = false;

        TextMeshProUGUI name = CreateLabel(
            row.transform, label, 13f, Muted, new Vector2(-140f, 17f), new Vector2(170f, 22f));
        name.characterSpacing = 3f;
        name.fontStyle = FontStyles.Bold;
        name.alignment = TextAlignmentOptions.MidlineLeft;

        TextMeshProUGUI valueText = CreateLabel(
            row.transform, "", 15f, Accent, new Vector2(190f, 17f), new Vector2(60f, 22f));
        valueText.fontStyle = FontStyles.Bold;
        valueText.alignment = TextAlignmentOptions.MidlineRight;

        Slider slider = CreateSlider(row.transform);
        RectTransform sliderRt = slider.GetComponent<RectTransform>();
        sliderRt.anchorMin = new Vector2(0.5f, 0.5f);
        sliderRt.anchorMax = new Vector2(0.5f, 0.5f);
        sliderRt.pivot = new Vector2(0.5f, 0.5f);
        sliderRt.anchoredPosition = new Vector2(0f, -14f);
        sliderRt.sizeDelta = new Vector2(424f, 18f);
        slider.SetValueWithoutNotify(getValue());
        valueText.text = $"%{Mathf.RoundToInt(getValue() * 100f)}";
        slider.onValueChanged.AddListener(value =>
        {
            setValue(value);
            valueText.text = $"%{Mathf.RoundToInt(value * 100f)}";
        });
    }

    private static Slider CreateSlider(Transform parent)
    {
        var root = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(parent, false);

        // İnce ray — modern görünüm için track slider'dan daha dar tutulur.
        var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        background.transform.SetParent(root.transform, false);
        var bgRt = background.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.pivot = new Vector2(0.5f, 0.5f);
        bgRt.offsetMin = new Vector2(0f, -3f);
        bgRt.offsetMax = new Vector2(0f, 3f);
        Image bg = background.GetComponent<Image>();
        bg.sprite = RuntimeUiSprites.GetRoundedSprite(3);
        bg.type = Image.Type.Sliced;
        bg.color = TrackColor;

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        var faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.5f);
        faRt.anchorMax = new Vector2(1f, 0.5f);
        faRt.pivot = new Vector2(0.5f, 0.5f);
        faRt.offsetMin = new Vector2(0f, -3f);
        faRt.offsetMax = new Vector2(0f, 3f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Stretch(fill.GetComponent<RectTransform>(), 0f, 0f);
        Image fillImage = fill.GetComponent<Image>();
        fillImage.sprite = RuntimeUiSprites.GetRoundedSprite(3);
        fillImage.type = Image.Type.Sliced;
        fillImage.color = Accent;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        Stretch(handleArea.GetComponent<RectTransform>(), 9f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        RectTransform handleRt = handle.GetComponent<RectTransform>();
        handleRt.sizeDelta = new Vector2(18f, 18f);
        Image handleImage = handle.GetComponent<Image>();
        handleImage.sprite = RuntimeUiSprites.GetRoundedSprite(9);
        handleImage.type = Image.Type.Sliced;
        handleImage.color = TextColor;

        // Tutamacın içinde küçük kehribar nokta — odak hissi verir.
        var dot = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dot.transform.SetParent(handle.transform, false);
        var dotRt = dot.GetComponent<RectTransform>();
        PlaceCentered(dotRt, Vector2.zero, new Vector2(6f, 6f));
        Image dotImg = dot.GetComponent<Image>();
        dotImg.sprite = RuntimeUiSprites.GetRoundedSprite(3);
        dotImg.type = Image.Type.Sliced;
        dotImg.color = OnAccent;
        dotImg.raycastTarget = false;

        Slider slider = root.GetComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handleRt;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        ColorBlock colors = slider.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.06f, 1.06f, 1.06f, 1f);
        colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colors.fadeDuration = 0.08f;
        slider.colors = colors;
        return slider;
    }

    private static GameObject CreateOverlay(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), 0f, 0f);
        Image image = go.GetComponent<Image>();
        image.color = Scrim;
        image.raycastTarget = true;
        return go;
    }

    private static Transform CreateCard(Transform parent, string name, Vector2 size)
    {
        // Yumuşak dış gölge katmanı — kartı scrim'den ayırır.
        var shadow = new GameObject(name + "Shadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        shadow.transform.SetParent(parent, false);
        RectTransform shadowRt = shadow.GetComponent<RectTransform>();
        PlaceCentered(shadowRt, new Vector2(0f, -6f), size + new Vector2(22f, 22f));
        Image shadowImg = shadow.GetComponent<Image>();
        shadowImg.sprite = RuntimeUiSprites.GetRoundedSprite(22);
        shadowImg.type = Image.Type.Sliced;
        shadowImg.color = new Color(0f, 0f, 0f, 0.45f);
        shadowImg.raycastTarget = false;

        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        PlaceCentered(rt, Vector2.zero, size);
        Image image = go.GetComponent<Image>();
        image.sprite = RuntimeUiSprites.GetRoundedSprite(16);
        image.type = Image.Type.Sliced;
        image.color = Card;

        // İnce üst çizgi — cam kenarı hissi.
        var topEdge = new GameObject("TopEdge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        topEdge.transform.SetParent(go.transform, false);
        var edgeRt = topEdge.GetComponent<RectTransform>();
        edgeRt.anchorMin = new Vector2(0f, 1f);
        edgeRt.anchorMax = new Vector2(1f, 1f);
        edgeRt.pivot = new Vector2(0.5f, 1f);
        edgeRt.offsetMin = new Vector2(24f, -3f);
        edgeRt.offsetMax = new Vector2(-24f, -1f);
        Image edgeImg = topEdge.GetComponent<Image>();
        edgeImg.color = new Color(1f, 1f, 1f, 0.06f);
        edgeImg.raycastTarget = false;

        return go.transform;
    }

    private enum ButtonStyle { Primary, Secondary, Danger, Ghost }

    private static Button CreateButton(Transform parent, string name, string text, ButtonStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        image.type = Image.Type.Sliced;

        Color bgColor;
        Color labelColor;
        switch (style)
        {
            case ButtonStyle.Primary:
                bgColor = Accent;
                labelColor = OnAccent;
                break;
            case ButtonStyle.Danger:
                bgColor = ButtonColor;
                labelColor = DangerTone;
                break;
            case ButtonStyle.Ghost:
                bgColor = new Color(0.08f, 0.09f, 0.11f, 0.72f);
                labelColor = Muted;
                break;
            default:
                bgColor = ButtonColor;
                labelColor = TextColor;
                break;
        }
        image.color = bgColor;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
        colors.selectedColor = new Color(1.06f, 1.06f, 1.06f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI label = CreateLabel(go.transform, text, 16f, labelColor, Vector2.zero, Vector2.zero);
        Stretch(label.rectTransform, 8f, 4f);
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static TextMeshProUGUI CreateLabel(
        Transform parent, string text, float size, Color color, Vector2 position, Vector2 dimensions)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        PlaceCentered(rt, position, dimensions);
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    private static void PlaceCentered(RectTransform rt, Vector2 position, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    /// <summary>Kartın sol üstünden hizalar (x = soldan, y = üstten negatif).</summary>
    private static void PlaceTop(RectTransform rt, float left, float yFromTop, Vector2 size)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(left, yFromTop);
        rt.sizeDelta = size;
    }

    private static void Stretch(RectTransform rt, float horizontalInset, float verticalInset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(horizontalInset, verticalInset);
        rt.offsetMax = new Vector2(-horizontalInset, -verticalInset);
    }

    private static void PlayClick()
    {
        if (GameAudio.Instance != null)
            GameAudio.Instance.PlayButtonClick();
    }
}
