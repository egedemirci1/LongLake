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

    [Header("Held / Equip Visual")]
    [Tooltip("Optional. If empty, worldPrefab mesh is reused in-hand (pickup scripts stripped).")]
    public GameObject heldPrefab;

    [Header("Held Pose — Yaman (characterIndex 1)")]
    public Vector3 heldLocalPosition = new Vector3(0.05f, -0.02f, 0.02f);
    public Vector3 heldLocalEulerAngles = new Vector3(0f, 90f, -90f);
    public Vector3 heldLocalScale = Vector3.one;

    [Header("Held Pose — Ahu (characterIndex 0)")]
    [Tooltip("Calibrate while playing as Ahu. Until set, falls back to Yaman pose.")]
    public bool useSeparateAhuHeldPose = false;
    public Vector3 heldLocalPositionAhu;
    public Vector3 heldLocalEulerAnglesAhu;
    public Vector3 heldLocalScaleAhu = Vector3.one;

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

    public GameObject GetHeldVisualPrefab()
    {
        if (heldPrefab != null) return heldPrefab;
        return worldPrefab;
    }

    public bool CanHoldInHand =>
        itemType == ItemType.MeleeWeapon && GetHeldVisualPrefab() != null;

    /// <summary>characterIndex: 0 = Ahu, 1 = Yaman (and any other uses Yaman pose).</summary>
    public void GetHeldPose(int characterIndex, out Vector3 position, out Vector3 eulerAngles, out Vector3 scale)
    {
        bool ahu = characterIndex == 0 && useSeparateAhuHeldPose;
        if (ahu)
        {
            position = heldLocalPositionAhu;
            eulerAngles = heldLocalEulerAnglesAhu;
            scale = heldLocalScaleAhu;
        }
        else
        {
            position = heldLocalPosition;
            eulerAngles = heldLocalEulerAngles;
            scale = heldLocalScale;
        }

        if (scale.sqrMagnitude < 0.0001f)
            scale = Vector3.one;
    }
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
