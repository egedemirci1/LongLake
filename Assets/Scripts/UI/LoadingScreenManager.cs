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
public partial class LoadingScreenManager : MonoBehaviour
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
        "Koşma hakkın sınırlıdır; stamina bitince biraz beklemen gerekir.",
        "Envanter ve craft paneli I tuşu ile açılır.",
        "E tuşu ile kapı, çekmece ve eşyalara etkileşime girersin.",
        "Görev günlüğünü M tuşu ile açıp kapatabilirsin.",
        "Sırt çantası bulunca hotbar açılır; slotlar 1-8 ile seçilir.",
        "Kapıyı çalmak için iki oyuncu da kapının yakınında olmalı.",
        "Diyalogda her iki oyuncu da Devam demeli veya aynı seçimi onaylamalı.",
        "Not defterini L tuşu ile açabilirsin; ipuçları orada birikir."
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

}
