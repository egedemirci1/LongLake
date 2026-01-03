using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.Netcode;

public class InventoryManager : NetworkBehaviour
{
    [Header("Bu Oyuncuya Özel UI (Prefab içine sahne objesi sürüklenmez, runtime baðlanýr)")]
    public GameObject hotbarObject;
    public TextMeshProUGUI[] nameTexts;
    public Image[] iconImages;

    public List<ItemData> items = new List<ItemData>();

    // Sahnedeki UI yolu (senin hiyerarþine göre)
    private const string HotbarPath = "InteractionUI/Envanter_Sistemi/Hotbar";

    public bool HasKey(string keyID)
    {
        return items.Exists(item => item != null && item.itemID == keyID);
    }

    public override void OnNetworkSpawn()
    {
        // Sadece owner kendi UI'sýný baðlar ve görür
        if (!IsOwner)
        {
            // Diðer oyuncularýn UI'sý açýlmasýn (zaten scene UI tek)
            return;
        }

        BindUIRuntime();

        if (hotbarObject != null)
            hotbarObject.SetActive(true); // Ýstersen false yapýp I ile aç-kapa kullan
    }

    private void Update()
    {
        if (!IsOwner) return;

        // UI baðlanmadýysa arada bir tekrar dene (scene timing için güvenli)
        if (hotbarObject == null || nameTexts == null || nameTexts.Length == 0 || iconImages == null || iconImages.Length == 0)
        {
            BindUIRuntime();
        }

        if (Input.GetKeyDown(KeyCode.I) && hotbarObject != null)
            hotbarObject.SetActive(!hotbarObject.activeSelf);
    }

    private void BindUIRuntime()
    {
        // 1) Hotbar root'u bul
        GameObject hotbarGO = GameObject.Find(HotbarPath);

        if (hotbarGO == null)
        {
            // Alternatif: sadece "Hotbar" adýna göre de bul (en kötü ihtimal)
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var tr in all)
            {
                if (tr != null && tr.name == "Hotbar")
                {
                    hotbarGO = tr.gameObject;
                    break;
                }
            }
        }

        if (hotbarGO == null)
        {
            // UI daha yüklenmemiþ olabilir
            return;
        }

        hotbarObject = hotbarGO;

        // 2) Slotlarý sýrayla bul: Slot_1..Slot_5
        nameTexts = new TextMeshProUGUI[5];
        iconImages = new Image[5];

        for (int i = 0; i < 5; i++)
        {
            string slotName = $"Slot_{i + 1}";
            Transform slot = hotbarObject.transform.Find(slotName);

            if (slot == null)
            {
                Debug.LogError($"[Inventory] '{slotName}' bulunamadý! Hotbar altýnda Slot_1..Slot_5 olmalý.");
                continue;
            }

            Transform nameT = slot.Find("ItemName_Text");
            Transform iconT = slot.Find("ItemIcon");

            if (nameT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemName_Text bulunamadý!");

            if (iconT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemIcon bulunamadý!");

            nameTexts[i] = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
            iconImages[i] = iconT != null ? iconT.GetComponent<Image>() : null;

            // Baþlangýçta temizle
            if (nameTexts[i] != null) nameTexts[i].text = "";
            if (iconImages[i] != null) iconImages[i].color = new Color(1, 1, 1, 0); // ikon gizli
        }

        Debug.Log("<color=green>[Inventory]</color> UI runtime baðlandý.");
    }

    public void AddItem(ItemData newItem)
    {
        if (!IsOwner) return; // sadece kendi envanterimiz
        if (newItem == null) return;

        if (items.Count < 5)
        {
            items.Add(newItem);
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        for (int i = 0; i < 5; i++)
        {
            if (nameTexts == null || iconImages == null) return;

            if (i < items.Count && items[i] != null)
            {
                if (nameTexts[i] != null) nameTexts[i].text = items[i].itemName;

                if (iconImages[i] != null)
                {
                    iconImages[i].sprite = items[i].itemIcon;
                    iconImages[i].color = Color.white; // görünür
                }
            }
            else
            {
                // boþ slot temizle
                if (nameTexts[i] != null) nameTexts[i].text = "";
                if (iconImages[i] != null)
                {
                    iconImages[i].sprite = null;
                    iconImages[i].color = new Color(1, 1, 1, 0);
                }
            }
        }
    }
}
