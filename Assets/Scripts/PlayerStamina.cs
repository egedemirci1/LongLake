using System;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Owner-only sprint stamina with exhausted-state machine (no CanSprint flicker).
/// PlayerController is the single source of truth for sprint via SetSprinting(bool).
/// </summary>
public class PlayerStamina : NetworkBehaviour
{
    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float drainPerSecond = 22f;
    [SerializeField] private float regenPerSecond = 10f;
    [SerializeField] private float regenDelayAfterEmpty = 0.75f;
    [SerializeField] private float recoverSprintThreshold = 5f;

    private float _current;
    private bool _sprinting;
    private bool _exhausted;
    private float _regenDelayTimer;

    public float MaxStamina => maxStamina;
    public float CurrentStamina => _current;
    public float Normalized => maxStamina > 0.0001f ? Mathf.Clamp01(_current / maxStamina) : 0f;

    /// <summary>False while exhausted / recovering past empty. Not a raw Current &gt; 0 check.</summary>
    public bool CanSprint => !_exhausted;

    public event Action<float, float> OnStaminaChanged;

    public override void OnNetworkSpawn()
    {
        _current = maxStamina;
        _exhausted = false;
        _regenDelayTimer = 0f;
        _sprinting = false;
        NotifyChanged();
    }

    public void SetSprinting(bool sprinting)
    {
        if (!IsOwner) return;
        _sprinting = sprinting;
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        float before = _current;

        if (_sprinting && !_exhausted)
        {
            _current = Mathf.Max(0f, _current - drainPerSecond * Time.deltaTime);
            if (_current <= 0.0001f)
            {
                _current = 0f;
                _exhausted = true;
                _regenDelayTimer = regenDelayAfterEmpty;
                _sprinting = false;
            }
        }
        else
        {
            // Regen when not sprinting (includes exhausted recovery).
            if (_exhausted)
            {
                if (_regenDelayTimer > 0f)
                    _regenDelayTimer -= Time.deltaTime;
                else
                    _current = Mathf.Min(maxStamina, _current + regenPerSecond * Time.deltaTime);

                if (_regenDelayTimer <= 0f && _current >= recoverSprintThreshold)
                    _exhausted = false;
            }
            else if (_current < maxStamina)
            {
                _current = Mathf.Min(maxStamina, _current + regenPerSecond * Time.deltaTime);
            }
        }

        if (!Mathf.Approximately(before, _current))
            NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnStaminaChanged?.Invoke(_current, maxStamina);
    }
}
