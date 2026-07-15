using UnityEngine;
using System.Collections.Generic;

public class PlayerInventory : MonoBehaviour
{
    public static PlayerInventory Instance; // Diðer scriptlerden kolay eriþim için (Singleton)
    public List<string> keys = new List<string>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    // Anahtar var mý kontrolü
    public bool HasKey(string keyName)
    {
        return keys.Contains(keyName);
    }
}