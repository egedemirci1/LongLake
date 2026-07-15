using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Scene-hierarchy stamina bar binder. Assign Fill Image in the Inspector (CrashSite InteractionUI).
/// Shows the local player's PlayerStamina only.
/// Görsel stil (pill bar, renkler, otomatik gizlenme) runtime'da uygulanır.
/// </summary>
public class StaminaUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool hideUntilCharacterSelected = true;

    [Header("Style")]
    [SerializeField] private Vector2 barSize = new Vector2(240f, 12f);
    [SerializeField] private float fillPadding = 3f;

    private static readonly Color BackgroundColor = new Color(0.05f, 0.06f, 0.08f, 0.78f); // görev kartıyla aynı
    private static readonly Color FillColor = new Color(0.95f, 0.77f, 0.32f, 1f);          // kehribar vurgu rengi
    private static readonly Color LowColor = new Color(0.92f, 0.42f, 0.25f, 1f);
    private static readonly Color ExhaustedColor = new Color(0.85f, 0.25f, 0.22f, 1f);

    private const float FullHideDelay = 1f;    // full olduktan sonra gizlenmeye başlama süresi
    private const float IntroShowSeconds = 3f; // oyuna girişte barın kendini tanıtma süresi
    private const float FadeOutSpeed = 2.5f;
    private const float FadeInSpeed = 10f;
    private const float FillLerpSpeed = 10f;
    private const float LowThreshold = 0.35f;

    private PlayerStamina _bound;
    private PlayerController _boundController;
    private float _findCooldown;

    private float _targetFill = 1f;
    private float _displayFill = 1f;
    private bool _wasFull = true;
    private float _fullSince;
    private float _alpha;
    private float _introShownUntil = -1f;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (backgroundImage == null)
        {
            var bg = transform.Find("Background");
            if (bg != null)
                backgroundImage = bg.GetComponent<Image>();
        }

        ApplyStyle();

        _alpha = 0f;
        canvasGroup.alpha = 0f;
    }

    /// <summary>Barı ince, yuvarlak köşeli pill görünümüne getirir (diğer HUD panelleriyle uyumlu).</summary>
    private void ApplyStyle()
    {
        if (transform is RectTransform rt)
            rt.sizeDelta = barSize;

        if (backgroundImage != null)
        {
            backgroundImage.sprite = RuntimeUiSprites.GetRoundedSprite(6);
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = BackgroundColor;
            backgroundImage.raycastTarget = false;
        }

        if (fillImage != null)
        {
            fillImage.sprite = RuntimeUiSprites.GetRoundedSprite(3);
            // Sliced + anchorMax.x ile daralt: Filled modun aksine köşeler her genişlikte düzgün kalır.
            fillImage.type = Image.Type.Sliced;
            fillImage.color = FillColor;
            fillImage.raycastTarget = false;

            var frt = fillImage.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = new Vector2(fillPadding, fillPadding);
            frt.offsetMax = new Vector2(-fillPadding, -fillPadding);
        }
    }

    private void OnEnable()
    {
        TryBind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        if (_bound == null)
        {
            _findCooldown -= Time.deltaTime;
            if (_findCooldown <= 0f)
            {
                _findCooldown = 0.5f;
                TryBind();
            }
        }

        UpdateFill();
        UpdateColor();
        UpdateVisibility();
    }

    private void TryBind()
    {
        if (_bound != null) return;
        if (NetworkManager.Singleton == null) return;

        var local = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (local == null) return;

        var stamina = local.GetComponent<PlayerStamina>();
        if (stamina == null) return;

        _bound = stamina;
        _boundController = stamina.GetComponent<PlayerController>();
        _bound.OnStaminaChanged += OnStaminaChanged;
        OnStaminaChanged(_bound.CurrentStamina, _bound.MaxStamina);
        _displayFill = _targetFill;
    }

    private void Unbind()
    {
        if (_bound != null)
            _bound.OnStaminaChanged -= OnStaminaChanged;
        _bound = null;
        _boundController = null;
    }

    private void UpdateFill()
    {
        if (fillImage == null) return;

        _displayFill = Mathf.Lerp(_displayFill, _targetFill, Time.deltaTime * FillLerpSpeed);
        if (Mathf.Abs(_displayFill - _targetFill) < 0.002f)
            _displayFill = _targetFill;

        var frt = fillImage.rectTransform;
        frt.anchorMax = new Vector2(Mathf.Max(_displayFill, 0.001f), 1f);
    }

    private void UpdateColor()
    {
        if (fillImage == null || _bound == null) return;

        Color color;
        if (!_bound.CanSprint)
        {
            // Sprint kilitli: soluk kırmızı, hafif nabız efekti.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
            color = ExhaustedColor;
            color.a = Mathf.Lerp(0.45f, 1f, pulse);
        }
        else
        {
            color = Color.Lerp(LowColor, FillColor, Mathf.Clamp01(_targetFill / LowThreshold));
        }

        fillImage.color = color;
    }

    private void UpdateVisibility()
    {
        if (canvasGroup == null) return;

        bool characterReady = !hideUntilCharacterSelected ||
                              (_boundController != null && _boundController.HasSelectedCharacter);

        // Oyuna girişte barı bir kez kısaca göster ki oyuncu varlığını bilsin.
        if (characterReady && _bound != null && _introShownUntil < 0f)
            _introShownUntil = Time.time + IntroShowSeconds;

        bool isFull = _targetFill >= 0.999f && (_bound == null || _bound.CanSprint);
        if (isFull && !_wasFull)
            _fullSince = Time.time;
        _wasFull = isFull;

        bool wantVisible = _bound != null && characterReady &&
                           (!isFull || Time.time - _fullSince < FullHideDelay ||
                            Time.time < _introShownUntil);

        float speed = wantVisible ? FadeInSpeed : FadeOutSpeed;
        _alpha = Mathf.MoveTowards(_alpha, wantVisible ? 1f : 0f, Time.deltaTime * speed);

        canvasGroup.alpha = _alpha;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    private void OnStaminaChanged(float current, float max)
    {
        _targetFill = max > 0.0001f ? Mathf.Clamp01(current / max) : 0f;
    }
}
