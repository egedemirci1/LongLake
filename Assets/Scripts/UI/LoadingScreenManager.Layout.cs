using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// Partial: split for maintainability. Type identity unchanged.
public partial class LoadingScreenManager : MonoBehaviour
{
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
