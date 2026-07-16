using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Yükleme ekranı: loop video + bölüm başlığı + dönen ipuçları.
/// CrashSite gibi ağır sahnelerde ana thread kısa süre kilitlenebilir;
/// video/ipucu UnscaledTime + Update ile ayakta tutulur, donma sonrası Play yenilenir.
/// </summary>
public class LoadingScreenManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject loadingScreenPanel;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TextMeshProUGUI baslikText;
    [SerializeField] private TextMeshProUGUI aciklamaText;

    [Header("Video Background")]
    [SerializeField] private VideoClip loadingVideoClip;
    [SerializeField] private Sprite loadingPosterSprite;
    [SerializeField] private bool muteVideo = true;

    [Header("Tips Panel (optional — auto-built if missing)")]
    [SerializeField] private RectTransform tipPanel;
    [SerializeField] private TextMeshProUGUI tipEyebrowText;
    [SerializeField] private TextMeshProUGUI tipBodyText;
    [SerializeField] private CanvasGroup tipGroup;

    [Header("Screenshot (Optional fallback)")]
    [SerializeField] private Sprite[] screenshotSprites;

    [Header("Settings")]
    [SerializeField] private string defaultBaslik = "Oyun Yükleniyor";
    [SerializeField] private string defaultAciklama = "";
    [SerializeField] private float tipHoldSeconds = 2.0f;
    [SerializeField] private float tipFadeSeconds = 0.35f;

    [SerializeField]
    private string[] tips =
    {
        "Koşma hakkın sınırlıdır — stamina bitince biraz beklemen gerekir.",
        "Envanter ve craft paneli I tuşu ile açılır.",
        "E tuşu ile kapı, çekmece ve eşyalara etkileşime girersin.",
        "Görev günlüğünü M tuşu ile açıp kapatabilirsin.",
        "Sırt çantası bulunca hotbar açılır; slotlar 1–8 ile seçilir.",
        "Kapıyı çalmak için iki oyuncu da kapının yakınında olmalı.",
        "Diyalogda her iki oyuncu da Devam demeli veya aynı seçimi onaylamalı.",
        "Not defterini L tuşu ile açabilirsin — ipuçları orada birikir."
    };

    private static readonly Color AccentColor = new Color(0.95f, 0.77f, 0.32f, 1f);
    private static readonly Color CardColor = new Color(0.05f, 0.06f, 0.08f, 0.88f);

    private static LoadingScreenManager instance;
    public static LoadingScreenManager Instance => instance != null ? instance : Resolve();

    private int _tipIndex;
    private bool _layoutReady;
    private bool _isShowing;
    private bool _tipsActive;
    private float _tipPhaseStart;
    private float _tipFadeFrom = 1f;
    private float _tipFadeTo = 1f;
    private bool _tipFading;
    private enum TipPhase { Hold, FadeOut, FadeIn }
    private TipPhase _tipPhase = TipPhase.Hold;

    private VideoPlayer _videoPlayer;
    private RawImage _videoRaw;
    private Image _posterImage;
    private RenderTexture _videoRt;
    private bool _videoHooks;
    private float _nextVideoKickUnscaled;
    private long _lastVideoFrame = -1;
    private float _videoStallTimer;
    private bool _videoRecovering;
    private Coroutine _videoRecoverRoutine;
    private float _nextRecoverAllowedUnscaled;

    private Image _progressFill;
    private TextMeshProUGUI _progressLabel;
    private float _progressTarget;
    private float _progressShown;
    private Canvas _dedicatedCanvas;

    /// <summary>Poster açıldı / video hazır veya timeout — sahne yüklemeye geçilebilir.</summary>
    public bool IsVisualReady { get; private set; }

    public static LoadingScreenManager Resolve()
    {
        if (instance != null) return instance;
        instance = FindFirstObjectByType<LoadingScreenManager>(FindObjectsInactive.Include);
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        // Dedicated canvas'ı Awake'te taşıma — HDRP/UI native crash riski.
        // ShowLoadingScreen içinde taşınır.

        if (loadingScreenPanel != null)
            loadingScreenPanel.SetActive(false);
    }

    private void Start()
    {
        WarmUp();
    }

    private void Update()
    {
        if (!_isShowing) return;

        TickTips();
        KeepVideoAlive();
        TickProgressBar();
    }

    private void LateUpdate()
    {
        // Scene load spike'ından sonra VideoPlayer çoğu zaman LateUpdate'de toparlanır.
        if (_isShowing)
            KeepVideoAlive();
    }

    /// <summary>GameplaySceneLoader yükleme boyunca her frame çağırır.</summary>
    public void PulseVideo()
    {
        if (!_isShowing) return;
        KeepVideoAlive();
    }

    private void TickProgressBar()
    {
        if (_progressFill == null) return;
        _progressShown = Mathf.MoveTowards(_progressShown, _progressTarget, Time.unscaledDeltaTime * 1.1f);
        _progressFill.fillAmount = Mathf.Clamp01(_progressShown);
        if (_progressLabel != null)
            _progressLabel.text = $"{Mathf.RoundToInt(_progressShown * 100f)}%";
    }

    /// <summary>0..1 yükleme ilerlemesi (GameplaySceneLoader besler).</summary>
    public void SetProgress(float normalized, bool snap = false)
    {
        _progressTarget = Mathf.Clamp01(normalized);
        if (snap)
        {
            _progressShown = _progressTarget;
            if (_progressFill != null)
                _progressFill.fillAmount = _progressShown;
            if (_progressLabel != null)
                _progressLabel.text = $"{Mathf.RoundToInt(_progressShown * 100f)}%";
            return;
        }

        if (_progressShown < 0.01f && _progressTarget > 0f)
            _progressShown = Mathf.Min(_progressTarget, 0.05f);
    }

    public float ShownProgress => _progressShown;

    private void OnDestroy()
    {
        TeardownVideo();
        if (instance == this)
            instance = null;
    }

    /// <summary>
    /// MainMenu canvas'ından ayır — Single LoadScene MainMenu'yu silince loading UI yaşasın.
    /// </summary>
    private void EnsureDedicatedCanvas()
    {
        if (loadingScreenPanel == null) return;

        if (_dedicatedCanvas != null)
        {
            if (loadingScreenPanel.transform.parent != _dedicatedCanvas.transform)
                loadingScreenPanel.transform.SetParent(_dedicatedCanvas.transform, false);
            return;
        }

        // Sadece loading panel'i taşı — tüm MainMenu canvas'ını DDOL yapma.
        var go = new GameObject("LoadingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(go);
        _dedicatedCanvas = go.GetComponent<Canvas>();
        _dedicatedCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _dedicatedCanvas.sortingOrder = 5000;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        loadingScreenPanel.transform.SetParent(go.transform, false);
        StretchFull(loadingScreenPanel.GetComponent<RectTransform>());

        // Manager + VideoPlayer da aynı DDOL kökünde yaşasın (MainMenu unload'da kaybolmasın).
        if (transform.parent != go.transform)
            transform.SetParent(go.transform, true);
    }

    private void PersistLoadingCanvas()
    {
        EnsureDedicatedCanvas();
    }

    private void WarmUp()
    {
        if (loadingScreenPanel == null) return;

        PersistLoadingCanvas();
        EnsureLayout();
        HideLegacyChrome();
        ApplyPosterOnly();
        BeginVideoWarmup();
    }

    public void ShowLoadingScreen(string customBaslik = null, string customAciklama = null, Sprite screenshot = null)
    {
        PersistLoadingCanvas();
        EnsureLayout();
        HideLegacyChrome();
        ApplyPosterOnly(screenshot);
        IsVisualReady = false;
        _progressTarget = 0.02f;
        _progressShown = 0f;
        if (_progressFill != null)
            _progressFill.fillAmount = 0f;

        string chapter = string.IsNullOrWhiteSpace(customAciklama) ? defaultAciklama : customAciklama;
        if (string.IsNullOrWhiteSpace(chapter))
            chapter = "Bölüm 1: Uzungöl Tatili";

        if (baslikText != null)
        {
            baslikText.text = "YÜKLENİYOR";
            baslikText.fontSize = 18f;
            baslikText.fontStyle = FontStyles.Bold;
            baslikText.color = AccentColor;
            baslikText.characterSpacing = 10f;
            baslikText.alignment = TextAlignmentOptions.Center;
            baslikText.gameObject.SetActive(true);
        }

        if (aciklamaText != null)
        {
            aciklamaText.text = chapter;
            aciklamaText.fontSize = 42f;
            aciklamaText.fontStyle = FontStyles.Bold;
            aciklamaText.color = Color.white;
            aciklamaText.alignment = TextAlignmentOptions.Center;
            aciklamaText.gameObject.SetActive(true);
        }

        PauseMainMenuVideo();
        RevealPanel();
        StartTipRotation();

        _lastVideoFrame = -1;
        _videoStallTimer = 0f;
        _videoRecovering = false;
        if (_videoRecoverRoutine != null)
        {
            StopCoroutine(_videoRecoverRoutine);
            _videoRecoverRoutine = null;
        }

        BeginVideoWarmup();
        KickVideoPlay();

        // Video hazır olmasa bile kısa süre sonra yüklemeye izin ver (poster + ipucu çalışır).
        StartCoroutine(MarkReadyWhenAble());
    }

    public void ShowLoadingScreenForScene(string sceneName, string customAciklama = null)
    {
        Sprite selectedScreenshot = null;
        string aciklama = customAciklama;

        if (screenshotSprites != null && screenshotSprites.Length > 0)
        {
            string sceneLower = sceneName.ToLowerInvariant();
            if (sceneLower.Contains("crashsite") || sceneLower.Contains("gameplay"))
            {
                selectedScreenshot = screenshotSprites[0];
                if (string.IsNullOrEmpty(aciklama))
                    aciklama = "Bölüm 1: Uzungöl Tatili";
            }
        }

        if (string.IsNullOrEmpty(aciklama))
            aciklama = "Bölüm 1: Uzungöl Tatili";

        ShowLoadingScreen(defaultBaslik, aciklama, selectedScreenshot);
    }

    public void HideLoadingScreen()
    {
        _isShowing = false;
        IsVisualReady = false;
        _videoRecovering = false;
        if (_videoRecoverRoutine != null)
        {
            StopCoroutine(_videoRecoverRoutine);
            _videoRecoverRoutine = null;
        }
        StopTipRotation();
        StopLoadingVideo();
        if (loadingScreenPanel != null)
            loadingScreenPanel.SetActive(false);
    }

    private IEnumerator MarkReadyWhenAble()
    {
        float t = 0f;
        const float minVisible = 0.35f;
        const float timeout = 2.5f;

        while (t < timeout)
        {
            t += Time.unscaledDeltaTime;

            bool videoOk = loadingVideoClip == null
                           || _videoPlayer == null
                           || _videoPlayer.isPlaying
                           || (_videoPlayer.isPrepared && t > 0.6f);

            if (t >= minVisible && videoOk)
            {
                IsVisualReady = true;
                yield break;
            }

            yield return null;
        }

        IsVisualReady = true;
    }

    private void RevealPanel()
    {
        if (loadingScreenPanel == null) return;

        HideLegacyChrome();
        loadingScreenPanel.transform.SetAsLastSibling();
        loadingScreenPanel.SetActive(true);
        _isShowing = true;

        Canvas canvas = _dedicatedCanvas != null
            ? _dedicatedCanvas
            : loadingScreenPanel.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 5000);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
    }

    private void HideLegacyChrome()
    {
        if (loadingScreenPanel == null) return;

        if (backgroundImage != null)
            backgroundImage.gameObject.SetActive(false);

        Transform oldFooter = loadingScreenPanel.transform.Find("Footer");
        if (oldFooter != null)
            oldFooter.gameObject.SetActive(false);

        Transform oldBg = loadingScreenPanel.transform.Find("Background");
        if (oldBg != null)
            oldBg.gameObject.SetActive(false);
    }

    private void ApplyPosterOnly(Sprite screenshot = null)
    {
        if (backgroundImage != null)
            backgroundImage.gameObject.SetActive(false);

        if (_posterImage == null) return;

        Sprite poster = loadingPosterSprite;
        if (poster == null && screenshot != null) poster = screenshot;
        if (poster == null && screenshotSprites != null && screenshotSprites.Length > 0)
            poster = screenshotSprites[0];

        if (poster != null)
        {
            _posterImage.sprite = poster;
            _posterImage.color = Color.white;
            _posterImage.enabled = true;
            _posterImage.gameObject.SetActive(true);
        }
    }

    private void EnsureLayout()
    {
        if (_layoutReady || loadingScreenPanel == null) return;
        _layoutReady = true;

        var panelRt = loadingScreenPanel.GetComponent<RectTransform>();

        var rootImage = loadingScreenPanel.GetComponent<Image>();
        if (rootImage != null)
        {
            rootImage.color = new Color(0f, 0f, 0f, 1f);
            rootImage.raycastTarget = true;
        }

        HideLegacyChrome();
        EnsureVideoLayer(panelRt);

        var dim = new GameObject("Dim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dim.transform.SetParent(loadingScreenPanel.transform, false);
        var dimRt = dim.GetComponent<RectTransform>();
        StretchFull(dimRt);
        dim.transform.SetSiblingIndex(2);
        var dimImg = dim.GetComponent<Image>();
        dimImg.color = new Color(0.02f, 0.03f, 0.04f, 0.45f);
        dimImg.raycastTarget = false;

        var center = new GameObject("CenterTitle", typeof(RectTransform));
        center.transform.SetParent(loadingScreenPanel.transform, false);
        var centerRt = center.GetComponent<RectTransform>();
        centerRt.anchorMin = new Vector2(0.5f, 0.5f);
        centerRt.anchorMax = new Vector2(0.5f, 0.5f);
        centerRt.pivot = new Vector2(0.5f, 0.5f);
        centerRt.anchoredPosition = new Vector2(0f, 36f);
        centerRt.sizeDelta = new Vector2(900f, 140f);

        if (baslikText != null)
        {
            baslikText.transform.SetParent(center.transform, false);
            var brt = baslikText.rectTransform;
            brt.anchorMin = new Vector2(0f, 0.55f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
        }

        if (aciklamaText != null)
        {
            aciklamaText.transform.SetParent(center.transform, false);
            var art = aciklamaText.rectTransform;
            art.anchorMin = new Vector2(0f, 0f);
            art.anchorMax = new Vector2(1f, 0.62f);
            art.offsetMin = Vector2.zero;
            art.offsetMax = Vector2.zero;
        }

        var rule = new GameObject("TitleRule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rule.transform.SetParent(center.transform, false);
        var ruleRt = rule.GetComponent<RectTransform>();
        ruleRt.anchorMin = new Vector2(0.5f, 0.48f);
        ruleRt.anchorMax = new Vector2(0.5f, 0.48f);
        ruleRt.pivot = new Vector2(0.5f, 0.5f);
        ruleRt.sizeDelta = new Vector2(120f, 2f);
        var ruleImg = rule.GetComponent<Image>();
        ruleImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        ruleImg.type = Image.Type.Sliced;
        ruleImg.color = AccentColor;
        ruleImg.raycastTarget = false;

        BuildTipPanel(panelRt);
        BuildProgressBar(panelRt);
    }

    private void BuildProgressBar(RectTransform parent)
    {
        if (_progressFill != null) return;

        var root = new GameObject("ProgressRoot", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0f);
        rootRt.anchorMax = new Vector2(0.5f, 0f);
        rootRt.pivot = new Vector2(0.5f, 0f);
        rootRt.anchoredPosition = new Vector2(0f, 170f);
        rootRt.sizeDelta = new Vector2(520f, 28f);

        var track = new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        track.transform.SetParent(root.transform, false);
        StretchFull(track.GetComponent<RectTransform>());
        var trackImg = track.GetComponent<Image>();
        trackImg.sprite = RuntimeUiSprites.GetRoundedSprite(6);
        trackImg.type = Image.Type.Sliced;
        trackImg.color = new Color(0.08f, 0.09f, 0.10f, 0.9f);
        trackImg.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(track.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        StretchFull(fillRt);
        fillRt.offsetMin = new Vector2(3f, 3f);
        fillRt.offsetMax = new Vector2(-3f, -3f);
        _progressFill = fillGo.GetComponent<Image>();
        _progressFill.sprite = RuntimeUiSprites.GetRoundedSprite(4);
        _progressFill.type = Image.Type.Filled;
        _progressFill.fillMethod = Image.FillMethod.Horizontal;
        _progressFill.fillOrigin = 0;
        _progressFill.fillAmount = 0f;
        _progressFill.color = AccentColor;
        _progressFill.raycastTarget = false;

        _progressLabel = CreateTmpSimple(root.transform, "ProgressLabel", 13f, FontStyles.Bold, MutedProgressColor());
        var lblRt = _progressLabel.rectTransform;
        lblRt.anchorMin = new Vector2(0f, 1f);
        lblRt.anchorMax = new Vector2(1f, 1f);
        lblRt.pivot = new Vector2(0.5f, 0f);
        lblRt.anchoredPosition = new Vector2(0f, 8f);
        lblRt.sizeDelta = new Vector2(0f, 18f);
        _progressLabel.alignment = TextAlignmentOptions.Center;
        _progressLabel.text = "0%";
        _progressLabel.characterSpacing = 2f;
    }

    private static Color MutedProgressColor() => new Color(0.75f, 0.76f, 0.72f, 0.9f);

    private static TextMeshProUGUI CreateTmpSimple(Transform parent, string name, float size, FontStyles style, Color color)
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

    private void EnsureVideoLayer(RectTransform parent)
    {
        var videoGo = new GameObject("VideoLayer", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        videoGo.transform.SetParent(parent, false);
        videoGo.transform.SetSiblingIndex(0);
        StretchFull(videoGo.GetComponent<RectTransform>());
        _videoRaw = videoGo.GetComponent<RawImage>();
        _videoRaw.color = Color.white;
        _videoRaw.raycastTarget = false;

        var posterGo = new GameObject("VideoPoster", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        posterGo.transform.SetParent(parent, false);
        posterGo.transform.SetSiblingIndex(1);
        StretchFull(posterGo.GetComponent<RectTransform>());
        _posterImage = posterGo.GetComponent<Image>();
        _posterImage.preserveAspect = false;
        _posterImage.raycastTarget = false;
        if (loadingPosterSprite != null)
            _posterImage.sprite = loadingPosterSprite;

        if (loadingVideoClip == null)
            return;

        _videoRt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
        {
            name = "LoadingScreenVideoRT",
            hideFlags = HideFlags.HideAndDontSave
        };
        _videoRt.Create();
        _videoRaw.texture = _videoRt;

        _videoPlayer = gameObject.GetComponent<VideoPlayer>();
        if (_videoPlayer == null)
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();

        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;
        _videoPlayer.isLooping = true;
        _videoPlayer.clip = loadingVideoClip;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.targetTexture = _videoRt;
        _videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.playbackSpeed = 1f;
        _videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        _videoPlayer.audioOutputMode = muteVideo
            ? VideoAudioOutputMode.None
            : VideoAudioOutputMode.Direct;

        if (!_videoHooks)
        {
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.started += OnVideoStarted;
            _videoPlayer.loopPointReached += OnVideoLoop;
            _videoPlayer.errorReceived += OnVideoError;
            _videoHooks = true;
        }
    }

    private void BeginVideoWarmup()
    {
        if (_videoPlayer == null || loadingVideoClip == null) return;
        if (_videoPlayer.isPrepared || _videoPlayer.isPlaying) return;
        _videoPlayer.Prepare();
    }

    private void KickVideoPlay()
    {
        if (_videoPlayer == null || loadingVideoClip == null) return;

        if (_videoPlayer.isPrepared)
            _videoPlayer.Play();
        else
            _videoPlayer.Prepare();
    }

    private void KeepVideoAlive()
    {
        if (_videoPlayer == null || loadingVideoClip == null) return;
        if (_videoRecovering) return;

        if (!_videoPlayer.isPrepared)
        {
            if (Time.unscaledTime >= _nextVideoKickUnscaled)
            {
                _nextVideoKickUnscaled = Time.unscaledTime + 0.4f;
                try { _videoPlayer.Prepare(); }
                catch { /* ignore */ }
            }
            return;
        }

        long frame = -1;
        try { frame = _videoPlayer.frame; }
        catch
        {
            RequestVideoRecover();
            return;
        }

        bool advancing = frame != _lastVideoFrame && frame >= 0;
        if (advancing)
        {
            _lastVideoFrame = frame;
            _videoStallTimer = 0f;
            if (_posterImage != null)
                _posterImage.enabled = false;
            return;
        }

        if (!_videoPlayer.isPlaying)
        {
            try { _videoPlayer.Play(); }
            catch
            {
                RequestVideoRecover();
                return;
            }
        }

        // Play diyor ama kare ilerlemiyor → LoadScene spike sonrası tipik durum
        _videoStallTimer += Time.unscaledDeltaTime;
        if (_videoStallTimer >= 0.25f)
        {
            _videoStallTimer = 0f;
            RequestVideoRecover();
        }
    }

    private void RequestVideoRecover()
    {
        if (_videoRecovering || !_isShowing) return;
        if (Time.unscaledTime < _nextRecoverAllowedUnscaled) return;
        _nextRecoverAllowedUnscaled = Time.unscaledTime + 0.6f;
        if (_videoRecoverRoutine != null)
            StopCoroutine(_videoRecoverRoutine);
        _videoRecoverRoutine = StartCoroutine(RecoverVideoRoutine());
    }

    private IEnumerator RecoverVideoRoutine()
    {
        _videoRecovering = true;

        if (_videoPlayer != null)
        {
            try
            {
                _videoPlayer.Stop();
            }
            catch { /* ignore */ }
        }

        // RT kaybolmuş olabilir
        if (_videoRt == null || !_videoRt.IsCreated())
        {
            if (_videoRt != null)
            {
                _videoRt.Release();
                Destroy(_videoRt);
            }

            _videoRt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32)
            {
                name = "LoadingScreenVideoRT",
                hideFlags = HideFlags.HideAndDontSave
            };
            _videoRt.Create();
            if (_videoRaw != null)
                _videoRaw.texture = _videoRt;
        }

        if (_videoPlayer == null)
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();

        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;
        _videoPlayer.isLooping = true;
        _videoPlayer.clip = loadingVideoClip;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.targetTexture = _videoRt;
        _videoPlayer.aspectRatio = VideoAspectRatio.FitOutside;
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.playbackSpeed = 1f;
        _videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
        _videoPlayer.audioOutputMode = muteVideo
            ? VideoAudioOutputMode.None
            : VideoAudioOutputMode.Direct;

        bool prepared = false;
        void OnPrep(VideoPlayer vp)
        {
            prepared = true;
            vp.Play();
            if (_posterImage != null)
                _posterImage.enabled = false;
        }

        _videoPlayer.prepareCompleted += OnPrep;
        _videoPlayer.Prepare();

        float t = 0f;
        while (!prepared && t < 2f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        _videoPlayer.prepareCompleted -= OnPrep;

        if (!prepared && _videoPlayer.isPrepared)
        {
            _videoPlayer.Play();
            if (_posterImage != null)
                _posterImage.enabled = false;
        }

        _lastVideoFrame = -1;
        _videoStallTimer = 0f;
        _videoRecovering = false;
        _videoRecoverRoutine = null;
    }

    private void StopLoadingVideo()
    {
        if (_videoPlayer == null) return;
        if (_videoPlayer.isPlaying)
            _videoPlayer.Pause();
        _videoPlayer.time = 0;
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        if (_isShowing)
            source.Play();
    }

    private void OnVideoStarted(VideoPlayer source)
    {
        if (_posterImage != null)
            _posterImage.enabled = false;
    }

    private void OnVideoLoop(VideoPlayer source)
    {
        // Loop noktası bazı platformlarda pause bırakabiliyor.
        if (_isShowing && !source.isPlaying)
            source.Play();
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"[LoadingScreenManager] Video error: {message}");
        if (_posterImage != null)
            _posterImage.enabled = true;
    }

    private void TeardownVideo()
    {
        if (_videoPlayer != null && _videoHooks)
        {
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.started -= OnVideoStarted;
            _videoPlayer.loopPointReached -= OnVideoLoop;
            _videoPlayer.errorReceived -= OnVideoError;
            _videoHooks = false;
            _videoPlayer.Stop();
        }

        if (_videoRt != null)
        {
            _videoRt.Release();
            Destroy(_videoRt);
            _videoRt = null;
        }
    }

    private static void PauseMainMenuVideo()
    {
        var menuVideo = FindFirstObjectByType<MainMenuVideoBackground>();
        if (menuVideo != null)
            menuVideo.gameObject.SetActive(false);
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private void BuildTipPanel(RectTransform parent)
    {
        if (tipPanel != null && tipBodyText != null) return;

        var go = new GameObject("TipPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        tipPanel = go.GetComponent<RectTransform>();
        tipPanel.anchorMin = new Vector2(0.5f, 0f);
        tipPanel.anchorMax = new Vector2(0.5f, 0f);
        tipPanel.pivot = new Vector2(0.5f, 0f);
        tipPanel.anchoredPosition = new Vector2(0f, 48f);
        tipPanel.sizeDelta = new Vector2(760f, 110f);

        var bg = go.GetComponent<Image>();
        bg.sprite = RuntimeUiSprites.GetRoundedSprite(12);
        bg.type = Image.Type.Sliced;
        bg.color = CardColor;
        bg.raycastTarget = false;

        tipGroup = go.AddComponent<CanvasGroup>();
        tipGroup.blocksRaycasts = false;

        var accent = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accent.transform.SetParent(go.transform, false);
        var aRt = accent.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(0f, 0f);
        aRt.anchorMax = new Vector2(0f, 1f);
        aRt.pivot = new Vector2(0f, 0.5f);
        aRt.anchoredPosition = new Vector2(10f, 0f);
        aRt.sizeDelta = new Vector2(4f, -24f);
        var aImg = accent.GetComponent<Image>();
        aImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        aImg.type = Image.Type.Sliced;
        aImg.color = AccentColor;
        aImg.raycastTarget = false;

        tipEyebrowText = CreateCardLabel(go.transform, "TipEyebrow",
            left: 28f, topInset: 14f, right: 28f, height: 22f,
            13f, FontStyles.Bold, AccentColor, TextAlignmentOptions.MidlineLeft);
        tipEyebrowText.characterSpacing = 6f;
        tipEyebrowText.text = "İPUCU";

        tipBodyText = CreateCardLabel(go.transform, "TipBody",
            left: 28f, topInset: 40f, right: 28f, height: 56f,
            20f, FontStyles.Normal, new Color(0.93f, 0.94f, 0.92f, 1f), TextAlignmentOptions.TopLeft);
        tipBodyText.textWrappingMode = TextWrappingModes.Normal;
        tipBodyText.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static TextMeshProUGUI CreateCardLabel(
        Transform parent, string name, float left, float topInset, float right, float height,
        float fontSize, FontStyles style, Color color, TextAlignmentOptions align)
    {
        topInset = Mathf.Abs(topInset);

        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, height);
        rt.offsetMin = new Vector2(left, -(topInset + height));
        rt.offsetMax = new Vector2(-right, -topInset);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.margin = Vector4.zero;
        return tmp;
    }

    private void StartTipRotation()
    {
        if (tips == null || tips.Length == 0 || tipBodyText == null)
        {
            _tipsActive = false;
            return;
        }

        _tipIndex = Random.Range(0, tips.Length);
        tipBodyText.text = tips[_tipIndex];
        if (tipGroup != null) tipGroup.alpha = 1f;

        _tipsActive = true;
        _tipPhase = TipPhase.Hold;
        _tipPhaseStart = Time.unscaledTime;
        _tipFading = false;
    }

    private void StopTipRotation()
    {
        _tipsActive = false;
        _tipFading = false;
    }

    private void TickTips()
    {
        if (!_tipsActive || tipBodyText == null) return;

        float elapsed = Time.unscaledTime - _tipPhaseStart;

        if (_tipPhase == TipPhase.Hold)
        {
            if (elapsed >= tipHoldSeconds)
            {
                _tipPhase = TipPhase.FadeOut;
                _tipPhaseStart = Time.unscaledTime;
                _tipFadeFrom = tipGroup != null ? tipGroup.alpha : 1f;
                _tipFadeTo = 0f;
                _tipFading = tipGroup != null;
            }
            return;
        }

        if (_tipPhase == TipPhase.FadeOut)
        {
            float u = tipFadeSeconds <= 0.01f ? 1f : Mathf.Clamp01(elapsed / tipFadeSeconds);
            u = u * u * (3f - 2f * u);
            if (_tipFading)
                tipGroup.alpha = Mathf.Lerp(_tipFadeFrom, _tipFadeTo, u);

            if (u >= 1f)
            {
                _tipIndex = (_tipIndex + 1) % tips.Length;
                tipBodyText.text = tips[_tipIndex];
                if (tipEyebrowText != null)
                    tipEyebrowText.text = "İPUCU";

                _tipPhase = TipPhase.FadeIn;
                _tipPhaseStart = Time.unscaledTime;
                _tipFadeFrom = 0f;
                _tipFadeTo = 1f;
            }
            return;
        }

        if (_tipPhase == TipPhase.FadeIn)
        {
            float u = tipFadeSeconds <= 0.01f ? 1f : Mathf.Clamp01(elapsed / tipFadeSeconds);
            u = u * u * (3f - 2f * u);
            if (_tipFading)
                tipGroup.alpha = Mathf.Lerp(_tipFadeFrom, _tipFadeTo, u);

            if (u >= 1f)
            {
                if (tipGroup != null) tipGroup.alpha = 1f;
                _tipPhase = TipPhase.Hold;
                _tipPhaseStart = Time.unscaledTime;
            }
        }
    }
}
