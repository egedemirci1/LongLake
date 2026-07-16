using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Yükleme ekranı: loop video arka plan, ortada bölüm başlığı, altta dönen ipuçları.
/// Video RenderTexture + RawImage ile oynar (main menu CameraFarPlane ile çakışmaz).
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
    [SerializeField] private float tipHoldSeconds = 4.2f;
    [SerializeField] private float tipFadeSeconds = 0.45f;

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

    private Coroutine _tipRoutine;
    private Coroutine _showRoutine;
    private int _tipIndex;
    private bool _layoutReady;
    private bool _isShowing;

    private VideoPlayer _videoPlayer;
    private RawImage _videoRaw;
    private Image _posterImage;
    private RenderTexture _videoRt;
    private bool _videoHooks;
    private bool _videoWarmStarted;

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
        PersistLoadingCanvas();

        if (loadingScreenPanel != null)
            loadingScreenPanel.SetActive(false);
    }

    private void Start()
    {
        // Panel kapalıyken layout + video hazırla — Show anında eski UI flash etmesin.
        WarmUp();
    }

    private void OnDestroy()
    {
        TeardownVideo();
        if (instance == this)
            instance = null;
    }

    /// <summary>Panel canvas'ını DDOL yap — aksi halde MainMenu unload olunca panel yok olur.</summary>
    private void PersistLoadingCanvas()
    {
        if (loadingScreenPanel == null) return;

        Canvas canvas = loadingScreenPanel.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.gameObject != gameObject)
            DontDestroyOnLoad(canvas.gameObject);
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

        if (_showRoutine != null)
            StopCoroutine(_showRoutine);
        _showRoutine = StartCoroutine(ShowWhenReady());
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
        if (_showRoutine != null)
        {
            StopCoroutine(_showRoutine);
            _showRoutine = null;
        }

        StopTipRotation();
        StopLoadingVideo();
        if (loadingScreenPanel != null)
            loadingScreenPanel.SetActive(false);
    }

    private IEnumerator ShowWhenReady()
    {
        BeginVideoWarmup();

        // Eski footer flash etmesin: önce yeni layout + poster ile paneli aç.
        RevealPanel();
        StartTipRotation();

        float timeout = 2f;
        float t = 0f;
        while (loadingVideoClip != null && _videoPlayer != null && !_videoPlayer.isPrepared && t < timeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_videoPlayer != null && _videoPlayer.isPrepared && !_videoPlayer.isPlaying)
            _videoPlayer.Play();

        _showRoutine = null;
    }

    private void RevealPanel()
    {
        if (loadingScreenPanel == null) return;

        HideLegacyChrome();
        loadingScreenPanel.transform.SetAsLastSibling();
        loadingScreenPanel.SetActive(true);
        _isShowing = true;

        Canvas canvas = loadingScreenPanel.GetComponentInParent<Canvas>();
        if (canvas != null)
            canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, 500);
    }

    /// <summary>Eski prefab "Oyun Yükleniyor" footer / background — flash kaynağı.</summary>
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
        {
            Debug.LogWarning("[LoadingScreenManager] loadingVideoClip atanmamış — sadece poster kullanılır.");
            return;
        }

        _videoRt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
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
        _videoPlayer.audioOutputMode = muteVideo
            ? VideoAudioOutputMode.None
            : VideoAudioOutputMode.Direct;
        _videoPlayer.skipOnDrop = true;

        if (!_videoHooks)
        {
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.started += OnVideoStarted;
            _videoPlayer.errorReceived += OnVideoError;
            _videoHooks = true;
        }
    }

    private void BeginVideoWarmup()
    {
        if (_videoPlayer == null || loadingVideoClip == null) return;
        if (_videoPlayer.isPrepared || _videoPlayer.isPlaying) return;

        _videoWarmStarted = true;
        _videoPlayer.Prepare();
    }

    private void StopLoadingVideo()
    {
        if (_videoPlayer == null) return;
        if (_videoPlayer.isPlaying)
            _videoPlayer.Pause();
        _videoPlayer.time = 0;
        _videoWarmStarted = false;
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        if (_isShowing || (loadingScreenPanel != null && loadingScreenPanel.activeInHierarchy))
            source.Play();
    }

    private void OnVideoStarted(VideoPlayer source)
    {
        if (_posterImage != null)
            _posterImage.enabled = false;
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

        // topInset pozitif pixel — kart üstünden aşağı (negatif verilirse yazı kartın üstüne taşar).
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

    /// <summary>Kart içinde üstten inset ile TMP label. topInset her zaman pozitif olmalı.</summary>
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
        StopTipRotation();
        if (tips == null || tips.Length == 0 || tipBodyText == null) return;

        _tipIndex = Random.Range(0, tips.Length);
        tipBodyText.text = tips[_tipIndex];
        if (tipGroup != null) tipGroup.alpha = 1f;
        _tipRoutine = StartCoroutine(TipRotationRoutine());
    }

    private void StopTipRotation()
    {
        if (_tipRoutine != null)
        {
            StopCoroutine(_tipRoutine);
            _tipRoutine = null;
        }
    }

    private IEnumerator TipRotationRoutine()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(tipHoldSeconds);

            if (tipGroup != null)
                yield return FadeTip(1f, 0f);

            _tipIndex = (_tipIndex + 1) % tips.Length;
            tipBodyText.text = tips[_tipIndex];
            if (tipEyebrowText != null)
                tipEyebrowText.text = "İPUCU";

            if (tipGroup != null)
                yield return FadeTip(0f, 1f);
        }
    }

    private IEnumerator FadeTip(float from, float to)
    {
        float t = 0f;
        while (t < tipFadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / tipFadeSeconds);
            u = u * u * (3f - 2f * u);
            tipGroup.alpha = Mathf.Lerp(from, to, u);
            yield return null;
        }
        tipGroup.alpha = to;
    }
}
