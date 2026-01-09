using UnityEngine;
using System.Collections.Generic;

public enum ItemType
{
    Generic,
    MeleeWeapon,
    Consumable,
    Material,
    Key
}

public enum WeaponCategory
{
    None,
    Blunt,
    Sharp,
    Tool
}

public enum ConsumableEffectType
{
    None,
    Heal,
    StaminaBoost,
    PowerBoost,
    NightVision,
    SpeedBoost,
    Poison,
    Fatigue,
    Dizziness,
    Burp
}

[CreateAssetMenu(fileName = "Yeni Esya", menuName = "Envanter/Esya")]
public class ItemData : ScriptableObject
{
    [Header("Basic Info")]
    public string itemName;   // Ekranda görünecek isim (örn: TRIPOD)
    public Sprite itemIcon;   // Slotun üstünde görünecek resim
    public string itemID;     // Kodun tanıyacağı ID (örn: tripod_01)
    public ItemType itemType;
    
    [Header("World Representation")]
    [Tooltip("Yere atıldığında görünecek 3D prefab (ItemPickUp component'li olmalı). Eğer boşsa, fallback prefab kullanılır.")]
    public GameObject worldPrefab; // Yere atıldığında spawn edilecek prefab

    [Header("Weapon Properties")]
    public WeaponCategory weaponCategory;
    public int tier; // Tier 1, 2, 3
    public float meleeDamage;
    public int durability = -1; // -1 means infinite durability

    [Header("Crafting")]
    public bool isCraftable = false;
    public CraftingRecipe craftingRecipe; // ScriptableObject
    
    [Header("Consumable Properties")]
    public bool isConsumable = false;
    public List<ConsumableEffect> consumableEffects = new List<ConsumableEffect>();
    
    [Header("Material Properties")]
    public bool isMaterial = false;

    [Header("Stack Properties")]
    public int maxStackSize = 1; // 1 = stack edilemez, >1 = stack edilebilir (örn: Çivi için 99)
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
