using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class ItemUsageManager : NetworkBehaviour
{
    private InventoryManager inventory;
    private PlayerStats playerStats;
    
    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        
        inventory = GetComponentInChildren<InventoryManager>();
        playerStats = GetComponent<PlayerStats>();
        
        if (playerStats == null)
        {
            playerStats = GetComponentInParent<PlayerStats>();
        }
        
        if (inventory == null)
        {
            Debug.LogError("[ItemUsage] InventoryManager not found!");
        }
        
        if (playerStats == null)
        {
            Debug.LogError("[ItemUsage] PlayerStats not found!");
        }
    }
    
    public void UseConsumable(ItemData item)
    {
        if (item == null || !item.isConsumable || item.consumableEffects == null || item.consumableEffects.Count == 0)
        {
            Debug.LogWarning("[ItemUsage] Item is not consumable or has no effects!");
            return;
        }
        
        if (!IsOwner) return;
        
        Debug.Log($"<color=cyan>[ItemUsage]</color> Using consumable: {item.itemName}");
        
        foreach (var effect in item.consumableEffects)
        {
            ApplyEffect(effect);
        }
        
        // Item'ı envanterden kaldır
        inventory.RemoveItem(item.itemID, 1);
    }
    
    private void ApplyEffect(ConsumableEffect effect)
    {
        if (playerStats == null) return;
        
        switch (effect.effectType)
        {
            case ConsumableEffectType.Heal:
                if (effect.duration == 0)
                {
                    // Anında iyileştirme (yüzde veya sabit)
                    if (effect.value <= 1f)
                    {
                        // Yüzde olarak (0.2 = %20)
                        playerStats.HealPercentage(effect.value * 100f);
                    }
                    else
                    {
                        // Sabit değer
                        playerStats.Heal(effect.value);
                    }
                }
                else
                {
                    // Zamanla iyileştirme
                    float totalHeal = effect.value <= 1f 
                        ? playerStats.MaxHealth * effect.value 
                        : effect.value;
                    playerStats.HealOverTime(totalHeal, effect.duration);
                }
                break;
                
            case ConsumableEffectType.StaminaBoost:
                if (effect.duration == 0)
                {
                    // Anında stamina restore
                    if (effect.value >= 1f)
                    {
                        playerStats.RestoreStamina(effect.value);
                    }
                    else
                    {
                        playerStats.RestoreStaminaFull();
                    }
                }
                else
                {
                    // Stamina regen boost
                    playerStats.BoostStaminaRegen(effect.value, effect.duration);
                }
                break;
                
            case ConsumableEffectType.StatBoost:
                // Melee damage boost
                if (effect.value > 0)
                {
                    playerStats.BoostMeleeDamage(effect.value, effect.duration);
                }
                break;
        }
        
        // Yan etki kontrolü
        if (effect.sideEffectChance > 0 && Random.Range(0f, 1f) < effect.sideEffectChance)
        {
            ApplySideEffect(effect);
        }
    }
    
    private void ApplySideEffect(ConsumableEffect effect)
    {
        if (playerStats == null) return;
        
        Debug.Log($"<color=yellow>[ItemUsage]</color> Side effect applied!");
        
        // Yan etkileri uygula
        if (effect.sideEffectValue < 0)
        {
            // Negatif değer = yorgunluk/sersemlik
            playerStats.ApplyExhaustion(effect.sideEffectDuration, Mathf.Abs(effect.sideEffectValue));
        }
        else if (effect.sideEffectValue < 1f)
        {
            // 0-1 arası = hız azaltma
            playerStats.ApplySpeedModifier(effect.sideEffectValue, effect.sideEffectDuration);
        }
        else
        {
            // 1'den büyük = zehirlenme (yavaş can kaybı)
            StartCoroutine(PoisonCoroutine(effect.sideEffectValue, effect.sideEffectDuration));
        }
    }
    
    private IEnumerator PoisonCoroutine(float damagePerSecond, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && playerStats != null)
        {
            playerStats.TakeDamage(damagePerSecond * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}

