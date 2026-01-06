using UnityEngine;
using System.Collections.Generic;

public enum ItemType
{
    Weapon,
    Consumable,
    Material,
    Tool
}

public enum WeaponCategory
{
    Melee,
    Ranged
}

public enum ConsumableEffectType
{
    Heal,
    StaminaBoost,
    StatBoost,
    StatusEffect
}

[CreateAssetMenu(fileName = "Yeni Esya", menuName = "Envanter/Esya")]
public class ItemData : ScriptableObject
{
    [Header("Basic Info")]
    public string itemName;   // Ekranda görünecek isim (örn: TRIPOD)
    public Sprite itemIcon;   // Slotun üstünde görünecek resim
    public string itemID;     // Kodun tanıyacağı ID (örn: tripod_01)
    public ItemType itemType = ItemType.Material;
    
    [Header("Weapon Properties")]
    public WeaponCategory weaponCategory = WeaponCategory.Melee;
    public int tier = 1; // 1, 2, 3 veya 0 (tier yok)
    public float meleeDamage = 0f;
    public int durability = -1; // -1 = sınırsız
    
    [Header("Crafting")]
    public bool isCraftable = false;
    public CraftingRecipe craftingRecipe; // ScriptableObject
    
    [Header("Consumable Properties")]
    public bool isConsumable = false;
    public List<ConsumableEffect> consumableEffects = new List<ConsumableEffect>();
    
    [Header("Material Properties")]
    public bool isMaterial = false;
}

[System.Serializable]
public class ConsumableEffect
{
    public ConsumableEffectType effectType;
    public float value; // Yüzde veya sabit değer
    public float duration; // Saniye cinsinden (0 = anında)
    public float sideEffectValue; // Yan etki değeri
    public float sideEffectDuration; // Yan etki süresi
    public float sideEffectChance; // Yan etki ihtimali (0-1)
}
