using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "Yeni Crafting Tarifi", menuName = "Envanter/Crafting Tarifi")]
public class CraftingRecipe : ScriptableObject
{
    public ItemData resultItem;
    public List<CraftingIngredient> requiredIngredients = new List<CraftingIngredient>();

    [Header("Tool Requirements (Optional)")]
    public bool requiresTool = false;
    public string requiredToolID; // e.g., "hammer_01"
    public int toolDurabilityCost = 0;
}

[System.Serializable]
public class CraftingIngredient
{
    public ItemData item;
    public int quantity;
}

