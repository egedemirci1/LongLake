using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.Netcode;

public class InventoryManager : NetworkBehaviour
{
    [Header("Player-specific UI (not assigned in prefab, bound at runtime)")]
    public GameObject hotbarObject;
    public TextMeshProUGUI[] nameTexts;
    public Image[] iconImages;

    public List<ItemData> items = new List<ItemData>();

    // UI path in scene (according to your hierarchy)
    private const string HotbarPath = "InteractionUI/Envanter_Sistemi/Hotbar";

    public bool HasKey(string keyID)
    {
        return items.Exists(item => item != null && item.itemID == keyID);
    }

    public override void OnNetworkSpawn()
    {
        // Only owner binds and sees their own UI
        if (!IsOwner)
        {
            // Don't open other players' UI (scene UI is single)
            return;
        }

        BindUIRuntime(); // BindUIRuntime already calls UpdateUI() at the end

        if (hotbarObject != null)
            hotbarObject.SetActive(true); // You can set to false and use I key to toggle
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Retry binding if UI not bound (safe for scene timing)
        if (hotbarObject == null || nameTexts == null || nameTexts.Length == 0 || iconImages == null || iconImages.Length == 0)
        {
            BindUIRuntime();
        }

        if (Input.GetKeyDown(KeyCode.I) && hotbarObject != null)
            hotbarObject.SetActive(!hotbarObject.activeSelf);
    }

    private void BindUIRuntime()
    {
        // 1) Find hotbar root
        GameObject hotbarGO = GameObject.Find(HotbarPath);

        if (hotbarGO == null)
        {
            // Alternative: find by "Hotbar" name only (worst case)
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
            // UI might not be loaded yet
            return;
        }

        hotbarObject = hotbarGO;

        // 2) Find slots in order: Slot_1..Slot_5
        nameTexts = new TextMeshProUGUI[5];
        iconImages = new Image[5];

        for (int i = 0; i < 5; i++)
        {
            string slotName = $"Slot_{i + 1}";
            Transform slot = hotbarObject.transform.Find(slotName);

            if (slot == null)
            {
                Debug.LogError($"[Inventory] '{slotName}' not found! Hotbar should have Slot_1..Slot_5 as children.");
                continue;
            }

            // Find child objects
            Transform nameT = slot.Find("ItemName");
            Transform iconT = slot.Find("ItemIcon");
            
            // Slot itself has Image component (frame/background - always visible)
            // ItemIcon child has Image component (item icon - only visible when item exists)
            // ItemName should be positioned inside ItemIcon (overlay text)
            
            if (nameT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemName not found!");

            if (iconT == null)
                Debug.LogError($"[Inventory] {slotName}/ItemIcon not found!");

            nameTexts[i] = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
            
            // Get Image component from ItemIcon child (not from Slot itself)
            iconImages[i] = iconT != null ? iconT.GetComponent<Image>() : null;
            
            if (iconImages[i] == null && iconT != null)
            {
                Debug.LogWarning($"[Inventory] {slotName}/ItemIcon found but Image component is missing. Adding Image component...");
                iconImages[i] = iconT.gameObject.AddComponent<Image>();
                if (iconImages[i] != null)
                {
                    Debug.Log($"<color=green>[Inventory]</color> Image component added to {slotName}/ItemIcon");
                }
            }

            // Initialize text and icon (will be updated by UpdateUI)
            if (nameTexts[i] != null) nameTexts[i].text = "";
            if (iconImages[i] != null)
            {
                iconImages[i].sprite = null;
                iconImages[i].color = new Color(1, 1, 1, 0); // icon hidden (transparent)
            }
            
            // Ensure Slot's Image component (frame/background) is always enabled and visible
            Image slotFrameImage = slot.GetComponent<Image>();
            if (slotFrameImage != null)
            {
                slotFrameImage.enabled = true; // Always keep frame/background visible
            }
        }

        Debug.Log("<color=green>[Inventory]</color> UI runtime bound.");
        
        // Update UI after binding to show current items
        UpdateUI();
    }

    public void AddItem(ItemData newItem)
    {
        if (!IsOwner) return; // only our own inventory
        if (newItem == null) return;

        if (items.Count < 5)
        {
            items.Add(newItem);
            
            // Bind UI if not bound
            if (nameTexts == null || iconImages == null || nameTexts.Length == 0 || iconImages.Length == 0)
            {
                BindUIRuntime();
            }
            
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        // Don't update if UI not bound
        if (nameTexts == null || iconImages == null || nameTexts.Length == 0 || iconImages.Length == 0)
        {
            Debug.LogWarning("[Inventory] UpdateUI called but UI not bound. BindUIRuntime() should be called.");
            return;
        }

        for (int i = 0; i < 5; i++)
        {

            if (i < items.Count && items[i] != null)
            {
                if (nameTexts[i] != null) nameTexts[i].text = items[i].itemName;

                if (iconImages[i] != null)
                {
                    if (items[i].itemIcon == null)
                    {
                        Debug.LogWarning($"[Inventory] Item '{items[i].itemName}' has null itemIcon!");
                    }
                    else
                    {
                        iconImages[i].sprite = items[i].itemIcon;
                        iconImages[i].color = Color.white; // visible
                        iconImages[i].enabled = true; // ensure enabled
                        
                        // Ensure ItemIcon GameObject is active
                        if (iconImages[i].gameObject != null)
                        {
                            iconImages[i].gameObject.SetActive(true);
                        }
                        
                        // Fix RectTransform size if it's 0
                        RectTransform iconRect = iconImages[i].GetComponent<RectTransform>();
                        if (iconRect != null)
                        {
                            if (iconRect.sizeDelta.x == 0 || iconRect.sizeDelta.y == 0)
                            {
                                // Try to get size from LayoutElement first
                                LayoutElement layoutElement = iconImages[i].GetComponent<LayoutElement>();
                                if (layoutElement != null && layoutElement.preferredWidth > 0 && layoutElement.preferredHeight > 0)
                                {
                                    iconRect.sizeDelta = new Vector2(layoutElement.preferredWidth, layoutElement.preferredHeight);
                                    Debug.Log($"[Inventory] Slot {i}: ItemIcon size set from LayoutElement: {iconRect.sizeDelta}");
                                }
                                else
                                {
                                    // Default size if LayoutElement doesn't have preferred size
                                    iconRect.sizeDelta = new Vector2(50, 50);
                                    Debug.Log($"[Inventory] Slot {i}: ItemIcon size set to default: {iconRect.sizeDelta}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[Inventory] Slot {i}: iconImages[{i}] is null! ItemIcon Image component missing?");
                }
            }
            else
            {
                // show empty slot
                if (nameTexts[i] != null) nameTexts[i].text = "Boş";
                if (iconImages[i] != null)
                {
                    iconImages[i].sprite = null;
                    iconImages[i].color = new Color(1, 1, 1, 0);
                }
            }
        }
    }
}
