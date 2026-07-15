using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Scene-hierarchy stamina bar binder. Assign Fill Image in the Inspector (CrashSite InteractionUI).
/// Shows the local player's PlayerStamina only.
/// </summary>
public class StaminaUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool hideUntilCharacterSelected = true;

    private PlayerStamina _bound;
    private float _findCooldown;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
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

        RefreshVisibility();
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
        _bound.OnStaminaChanged += OnStaminaChanged;
        OnStaminaChanged(_bound.CurrentStamina, _bound.MaxStamina);
    }

    private void Unbind()
    {
        if (_bound != null)
            _bound.OnStaminaChanged -= OnStaminaChanged;
        _bound = null;
    }

    private void RefreshVisibility()
    {
        if (canvasGroup == null) return;

        bool show = true;
        if (hideUntilCharacterSelected)
        {
            show = false;
            if (_bound != null)
            {
                var pc = _bound.GetComponent<PlayerController>();
                show = pc != null && pc.HasSelectedCharacter;
            }
        }

        canvasGroup.alpha = show ? 1f : 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    private void OnStaminaChanged(float current, float max)
    {
        if (fillImage == null) return;
        fillImage.fillAmount = max > 0.0001f ? Mathf.Clamp01(current / max) : 0f;
    }
}
