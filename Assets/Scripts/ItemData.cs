using UnityEngine;

[CreateAssetMenu(fileName = "Yeni Esya", menuName = "Envanter/Esya")]
public class ItemData : ScriptableObject
{
    public string itemName;   // Ekranda görünecek isim (Örn: TRIPOD)
    public Sprite itemIcon;   // Slotun üstünde görünecek resim
    public string itemID;     // Kodun tanýyacaðý ID (Örn: tripod_01)
}