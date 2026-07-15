using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lookup table for ItemData by itemID (for networked held visuals on remote clients).
/// Place as Resources/ItemDatabase.asset
/// </summary>
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Envanter/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public ItemData[] items;

    private Dictionary<string, ItemData> _map;

    public ItemData Get(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        EnsureMap();
        return _map.TryGetValue(itemId, out var item) ? item : null;
    }

    private void EnsureMap()
    {
        if (_map != null) return;
        _map = new Dictionary<string, ItemData>();
        if (items == null) return;
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.itemID)) continue;
            _map[item.itemID] = item;
        }
    }

    public void Register(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemID)) return;
        EnsureMap();
        _map[item.itemID] = item;
    }
}
