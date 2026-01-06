using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class CraftingManager : MonoBehaviour
{
    public static CraftingManager Instance;
    
    [SerializeField] private List<CraftingRecipe> allRecipes = new List<CraftingRecipe>();
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Sahne değişse bile kalır
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    public bool CanCraft(InventoryManager inventory, CraftingRecipe recipe)
    {
        if (recipe == null || inventory == null) 
        {
            Debug.LogWarning("[Crafting] Recipe or inventory is null!");
            return false;
        }
        
        // Gerekli malzemeleri kontrol et
        foreach (var ingredient in recipe.requiredIngredients)
        {
            if (ingredient.item == null) continue;
            
            int count = inventory.CountItem(ingredient.item.itemID);
            if (count < ingredient.quantity)
            {
                Debug.Log($"[Crafting] Not enough {ingredient.item.itemName}. Need {ingredient.quantity}, have {count}");
                return false;
            }
        }
        
        // Gerekli aleti kontrol et
        if (recipe.requiresTool && !inventory.HasItem(recipe.requiredToolID))
        {
            Debug.Log($"[Crafting] Required tool not found: {recipe.requiredToolID}");
            return false;
        }
        
        return true;
    }
    
    public void CraftItem(InventoryManager inventory, CraftingRecipe recipe)
    {
        if (!CanCraft(inventory, recipe))
        {
            Debug.LogWarning("[Crafting] Cannot craft item! Requirements not met.");
            return;
        }
        
        if (!inventory.IsOwner) return;
        
        // Malzemeleri kaldır
        foreach (var ingredient in recipe.requiredIngredients)
        {
            if (ingredient.item == null) continue;
            inventory.RemoveItem(ingredient.item.itemID, ingredient.quantity);
        }
        
        // Alet durability'si azalt
        if (recipe.requiresTool && recipe.toolDurabilityCost > 0)
        {
            inventory.ReduceItemDurability(recipe.requiredToolID, recipe.toolDurabilityCost);
        }
        
        // Yeni item ekle
        if (recipe.resultItem != null)
        {
            inventory.AddItem(recipe.resultItem);
            Debug.Log($"<color=green>[Crafting]</color> Successfully crafted {recipe.resultItem.itemName}!");
        }
    }
    
    public List<CraftingRecipe> GetAvailableRecipes(InventoryManager inventory)
    {
        List<CraftingRecipe> available = new List<CraftingRecipe>();
        
        foreach (var recipe in allRecipes)
        {
            if (CanCraft(inventory, recipe))
            {
                available.Add(recipe);
            }
        }
        
        return available;
    }
}

