using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerStats : NetworkBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    private NetworkVariable<float> currentHealth = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaRegenRate = 10f; // Saniyede
    [SerializeField] private float staminaDrainRate = 15f; // Koşarken saniyede
    private NetworkVariable<float> currentStamina = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    [Header("Combat Stats")]
    private float baseMeleeDamage = 10f;
    private float currentMeleeDamageMultiplier = 1f;
    private float meleeDamageBoostEndTime = 0f;

    // Status effects
    private bool isExhausted = false;
    private float exhaustionEndTime = 0f;
    private float speedModifier = 1f;
    private float speedModifierEndTime = 0f;

    public float CurrentHealth => currentHealth.Value;
    public float MaxHealth => maxHealth;
    public float CurrentStamina => currentStamina.Value;
    public float MaxStamina => maxStamina;
    public float CurrentMeleeDamage => baseMeleeDamage * currentMeleeDamageMultiplier;
    public float SpeedModifier => speedModifier;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            currentHealth.Value = maxHealth;
            currentStamina.Value = maxStamina;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Stamina regeneration/drain
        UpdateStamina();
        
        // Status effect timers
        UpdateStatusEffects();
    }

    private void UpdateStamina()
    {
        bool isSprinting = Input.GetKey(KeyCode.LeftShift) && (Input.GetAxis("Horizontal") != 0 || Input.GetAxis("Vertical") != 0);
        
        if (isSprinting && currentStamina.Value > 0)
        {
            currentStamina.Value = Mathf.Max(0, currentStamina.Value - staminaDrainRate * Time.deltaTime);
        }
        else if (!isSprinting && currentStamina.Value < maxStamina)
        {
            float regenRate = staminaRegenRate;
            if (isExhausted && Time.time < exhaustionEndTime)
            {
                regenRate *= 0.3f; // Yorgunlukta %30 daha yavaş
            }
            currentStamina.Value = Mathf.Min(maxStamina, currentStamina.Value + regenRate * Time.deltaTime);
        }
    }

    private void UpdateStatusEffects()
    {
        if (Time.time >= meleeDamageBoostEndTime && currentMeleeDamageMultiplier != 1f)
        {
            currentMeleeDamageMultiplier = 1f;
        }

        if (Time.time >= exhaustionEndTime && isExhausted)
        {
            isExhausted = false;
        }

        if (Time.time >= speedModifierEndTime && speedModifier != 1f)
        {
            speedModifier = 1f;
        }
    }

    // Healing methods
    public void Heal(float amount)
    {
        if (!IsOwner) return;
        currentHealth.Value = Mathf.Min(maxHealth, currentHealth.Value + amount);
        Debug.Log($"[PlayerStats] Healed {amount}. Current HP: {currentHealth.Value}/{maxHealth}");
    }

    public void HealPercentage(float percentage)
    {
        if (!IsOwner) return;
        float healAmount = maxHealth * (percentage / 100f);
        Heal(healAmount);
    }

    public void HealOverTime(float totalAmount, float duration)
    {
        if (!IsOwner) return;
        StartCoroutine(HealOverTimeCoroutine(totalAmount, duration));
    }

    private IEnumerator HealOverTimeCoroutine(float totalAmount, float duration)
    {
        float healPerSecond = totalAmount / duration;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            Heal(healPerSecond * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // Stamina methods
    public void RestoreStamina(float amount)
    {
        if (!IsOwner) return;
        currentStamina.Value = Mathf.Min(maxStamina, currentStamina.Value + amount);
    }

    public void RestoreStaminaFull()
    {
        if (!IsOwner) return;
        currentStamina.Value = maxStamina;
    }

    public void BoostStaminaRegen(float multiplier, float duration)
    {
        if (!IsOwner) return;
        StartCoroutine(BoostStaminaRegenCoroutine(multiplier, duration));
    }

    private IEnumerator BoostStaminaRegenCoroutine(float multiplier, float duration)
    {
        float originalRate = staminaRegenRate;
        staminaRegenRate *= multiplier;
        yield return new WaitForSeconds(duration);
        staminaRegenRate = originalRate;
    }

    public void ReduceStaminaDrain(float multiplier, float duration)
    {
        if (!IsOwner) return;
        StartCoroutine(ReduceStaminaDrainCoroutine(multiplier, duration));
    }

    private IEnumerator ReduceStaminaDrainCoroutine(float multiplier, float duration)
    {
        float originalRate = staminaDrainRate;
        staminaDrainRate *= (1f - multiplier); // %25 daha yavaş = 0.75x
        yield return new WaitForSeconds(duration);
        staminaDrainRate = originalRate;
    }

    // Combat stat methods
    public void BoostMeleeDamage(float multiplier, float duration)
    {
        if (!IsOwner) return;
        currentMeleeDamageMultiplier = multiplier;
        meleeDamageBoostEndTime = Time.time + duration;
        Debug.Log($"[PlayerStats] Melee damage boosted by {multiplier}x for {duration} seconds");
    }

    // Status effect methods
    public void ApplyExhaustion(float duration, float staminaReduction = 0.3f)
    {
        if (!IsOwner) return;
        isExhausted = true;
        exhaustionEndTime = Time.time + duration;
        Debug.Log($"[PlayerStats] Exhausted for {duration} seconds (stamina regen reduced)");
    }

    public void ApplySpeedModifier(float modifier, float duration)
    {
        if (!IsOwner) return;
        speedModifier = modifier;
        speedModifierEndTime = Time.time + duration;
        Debug.Log($"[PlayerStats] Speed modifier: {modifier}x for {duration} seconds");
    }

    // Damage methods
    public void TakeDamage(float amount)
    {
        if (!IsOwner) return;
        currentHealth.Value = Mathf.Max(0, currentHealth.Value - amount);
        Debug.Log($"[PlayerStats] Took {amount} damage. Current HP: {currentHealth.Value}/{maxHealth}");
        
        if (currentHealth.Value <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log("[PlayerStats] Player died!");
        // Death logic here
    }
}

