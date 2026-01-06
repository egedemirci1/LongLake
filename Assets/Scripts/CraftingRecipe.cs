using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "Yeni Recipe", menuName = "Envanter/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    public ItemData resultItem;
    public List<CraftingIngredient> requiredIngredients = new List<CraftingIngredient>();
    public bool requiresTool = false;
    public string requiredToolID; // Örn: "hammer_01" - çekiç gerekiyorsa
    public int toolDurabilityCost = 0; // Kullanılan aletin durability'si azalır
}

[System.Serializable]
public class CraftingIngredient
{
    public ItemData item;
    public int quantity = 1;
}

