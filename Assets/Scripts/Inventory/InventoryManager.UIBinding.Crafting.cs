using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using System;

// Partial: split for maintainability. Type identity unchanged.
public partial class InventoryManager : NetworkBehaviour
{
    private void BindCraftingUI()
    {
        // Crafting input slot'ları için UI binding (3 slot)
        craftingInputIcons = new Image[3];
        craftingInputNames = new TextMeshProUGUI[3];
        craftingInputFrames = new Image[3];

        // CraftSite'i bul (MainInventory > CraftPanel > CraftSite)
        Transform craftSite = mainInventoryObject.transform.Find("CraftPanel/CraftSite");
        if (craftSite == null)
        {
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (var tr in all)
            {
                if (tr != null && tr.name == "CraftSite")
                {
                    craftSite = tr;
                    break;
                }
            }
        }

        if (craftSite == null)
        {
            return;
        }

        // Input slot'ları bul
        for (int i = 0; i < 3; i++)
        {
            string slotName = $"InputSlot_{i + 1}";
            Transform slot = craftSite.Find(slotName);

            if (slot == null && i < craftSite.childCount)
            {
                slot = craftSite.GetChild(i);
                if (slot.name.Contains("Output")) continue;
            }

            if (slot != null && !slot.name.Contains("Output"))
            {
                Transform iconT = slot.Find("ItemIcon");
                if (iconT == null)
                {
                    // Fallback: Slot'un kendisinde Image varsa onu kullan
                    if (slot.GetComponent<Image>() != null)
                    {
                        iconT = slot;
                    }
                    else
                    {
                        // Tüm child'ları kontrol et
                        for (int j = 0; j < slot.childCount; j++)
                        {
                            Transform child = slot.GetChild(j);
                            if (child.GetComponent<Image>() != null && !child.name.Contains("ItemName"))
                            {
                                iconT = child;
                                break;
                            }
                        }
                    }
                }

                Transform nameT = slot.Find("ItemName_Text");
                if (nameT == null) nameT = slot.Find("ItemName_Tex");
                if (nameT == null) nameT = slot.Find("ItemName");
                
                if (nameT == null)
                {
                    for (int j = 0; j < slot.childCount; j++)
                    {
                        Transform child = slot.GetChild(j);
                        if (child.GetComponent<TextMeshProUGUI>() != null)
                        {
                            nameT = child;
                            break;
                        }
                    }
                }

                craftingInputIcons[i] = iconT != null ? iconT.GetComponent<Image>() : null;
                craftingInputNames[i] = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
                
                // Slot frame'ini al (boş slot görseli için)
                craftingInputFrames[i] = slot.GetComponent<Image>();
                
                // Bulunamazsa child'larda "Frame" veya "Background" ara
                if (craftingInputFrames[i] == null)
                {
                    Transform frameT = slot.Find("Frame") ?? slot.Find("Background") ?? slot.Find("SlotFrame");
                    if (frameT != null)
                    {
                        craftingInputFrames[i] = frameT.GetComponent<Image>();
                    }
                }
                
                // Hala bulunamazsa, slot'un kendisinde Image oluştur
                if (craftingInputFrames[i] == null)
                {
                    Image frameImage = slot.gameObject.GetComponent<Image>();
                    if (frameImage == null)
                    {
                        frameImage = slot.gameObject.AddComponent<Image>();
                        frameImage.color = FrameEmptyColor;
                    }
                    craftingInputFrames[i] = frameImage;
                }
                
                // Frame'i her zaman görünür tut (envanterdeki gibi)
                if (craftingInputFrames[i] != null)
                {
                    craftingInputFrames[i].enabled = true;
                    craftingInputFrames[i].color = FrameEmptyColor;
                    if (craftingInputFrames[i].gameObject != null)
                    {
                        craftingInputFrames[i].gameObject.SetActive(true);
                    }
                }
            }
        }

        // Output slot'u bul
        Transform outputSlot = craftSite.Find("OutputSlot");
        if (outputSlot == null && craftSite.childCount > 0)
        {
            outputSlot = craftSite.GetChild(craftSite.childCount - 1);
            if (!outputSlot.name.Contains("Output"))
            {
                outputSlot = null;
            }
        }

        if (outputSlot != null)
        {
            Transform iconT = outputSlot.Find("ItemIcon");
            if (iconT == null)
            {
                // Çerçeve ile ikon aynı Image olursa boş ikon gizlenirken çerçeve de kaybolur.
                GameObject iconGO = new GameObject("ItemIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGO.transform.SetParent(outputSlot, false);
                RectTransform iconRect = iconGO.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(48f, 48f);
                iconRect.anchoredPosition = Vector2.zero;
                Image iconImage = iconGO.GetComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconT = iconGO.transform;
            }

            Transform nameT = outputSlot.Find("ItemName_Text");
            if (nameT == null) nameT = outputSlot.Find("ItemName_Tex");
            if (nameT == null) nameT = outputSlot.Find("ItemName");
            
            if (nameT == null)
            {
                GameObject nameGO = new GameObject("ItemName", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                nameGO.transform.SetParent(outputSlot, false);
                RectTransform nameRect = nameGO.GetComponent<RectTransform>();
                nameRect.anchorMin = new Vector2(0f, 0f);
                nameRect.anchorMax = new Vector2(1f, 0f);
                nameRect.pivot = new Vector2(0.5f, 0f);
                nameRect.anchoredPosition = new Vector2(0f, 3f);
                nameRect.sizeDelta = new Vector2(0f, 16f);
                TextMeshProUGUI nameText = nameGO.GetComponent<TextMeshProUGUI>();
                nameText.fontSize = 10f;
                nameText.fontStyle = FontStyles.Bold;
                nameText.alignment = TextAlignmentOptions.Center;
                nameText.color = Color.white;
                nameText.raycastTarget = false;
                nameT = nameGO.transform;
            }

            craftingOutputIcon = iconT != null ? iconT.GetComponent<Image>() : null;
            craftingOutputName = nameT != null ? nameT.GetComponent<TextMeshProUGUI>() : null;
            
            // Output slot frame'ini al (boş slot görseli için)
            craftingOutputFrame = outputSlot.GetComponent<Image>();
            
            // Bulunamazsa child'larda "Frame" veya "Background" ara
            if (craftingOutputFrame == null)
            {
                Transform frameT = outputSlot.Find("Frame") ?? outputSlot.Find("Background") ?? outputSlot.Find("SlotFrame");
                if (frameT != null)
                {
                    craftingOutputFrame = frameT.GetComponent<Image>();
                }
            }
            
            // Hala bulunamazsa, slot'un kendisinde Image oluştur
            if (craftingOutputFrame == null)
            {
                Image frameImage = outputSlot.gameObject.GetComponent<Image>();
                if (frameImage == null)
                {
                    frameImage = outputSlot.gameObject.AddComponent<Image>();
                    frameImage.color = FrameEmptyColor;
                }
                craftingOutputFrame = frameImage;
            }
            
            // Frame'i her zaman görünür tut (envanterdeki gibi)
            if (craftingOutputFrame != null)
            {
                craftingOutputFrame.enabled = true;
                craftingOutputFrame.color = CraftOutputEmptyColor;
                craftingOutputFrame.raycastTarget = false;
                if (craftingOutputFrame.gameObject != null)
                {
                    craftingOutputFrame.gameObject.SetActive(true);
                }

                Outline outline = craftingOutputFrame.GetComponent<Outline>();
                if (outline == null)
                    outline = craftingOutputFrame.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(CraftAccentColor.r, CraftAccentColor.g, CraftAccentColor.b, 0.72f);
                outline.effectDistance = new Vector2(2f, -2f);
                outline.useGraphicAlpha = false;
            }
        }

        // CancelButton'ı bul ve bağla
        Transform cancelButtonT = mainInventoryObject.transform.Find("CraftPanel/CancelButton");
        if (cancelButtonT == null)
        {
            // Alternatif path'ler dene
            cancelButtonT = craftSite.Find("CancelButton");
            if (cancelButtonT == null)
            {
                Transform craftPanel = mainInventoryObject.transform.Find("CraftPanel");
                if (craftPanel != null)
                {
                    cancelButtonT = craftPanel.Find("CancelButton");
                }
                if (cancelButtonT == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<Transform>();
                    foreach (var tr in all)
                    {
                        if (tr != null && tr.name == "CancelButton")
                        {
                            cancelButtonT = tr;
                            break;
                        }
                    }
                }
            }
        }
        
        if (cancelButtonT != null)
        {
            cancelCraftButton = cancelButtonT.GetComponent<Button>();
            if (cancelCraftButton != null)
            {
                cancelCraftButton.onClick.RemoveAllListeners();
                cancelCraftButton.onClick.AddListener(CancelCrafting);
                StyleCraftButton(cancelCraftButton, false);
            }
        }

        // CraftButton'ı bul ve ses bağla
        Transform craftButtonT = mainInventoryObject.transform.Find("CraftPanel/CraftButton");
        if (craftButtonT == null)
        {
            craftButtonT = craftSite.Find("CraftButton");
            if (craftButtonT == null)
            {
                Transform craftPanel = mainInventoryObject.transform.Find("CraftPanel");
                if (craftPanel != null)
                {
                    craftButtonT = craftPanel.Find("CraftButton");
                }
                if (craftButtonT == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<Transform>();
                    foreach (var tr in all)
                    {
                        if (tr != null && tr.name == "CraftButton")
                        {
                            craftButtonT = tr;
                            break;
                        }
                    }
                }
            }
        }

        if (craftButtonT != null)
        {
            Button craftButton = craftButtonT.GetComponent<Button>();
            if (craftButton != null)
            {
                craftButton.onClick.RemoveListener(PlayCraftSound);
                craftButton.onClick.AddListener(PlayCraftSound);
                StyleCraftButton(craftButton, true);
            }
        }
    }

    private static void StyleCraftButton(Button button, bool primary)
    {
        if (button == null) return;

        Image background = button.GetComponent<Image>();
        if (background != null)
        {
            background.color = primary
                ? new Color(0.82f, 0.52f, 0.16f, 1f)
                : new Color(0.12f, 0.14f, 0.17f, 0.98f);
        }

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = primary
            ? new Color(1f, 0.88f, 0.68f, 1f)
            : new Color(1.18f, 1.18f, 1.18f, 1f);
        colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        RectTransform rect = button.GetComponent<RectTransform>();
        if (rect != null)
            rect.sizeDelta = new Vector2(160f, 38f);

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = primary ? new Color(0.08f, 0.07f, 0.05f, 1f) : new Color(0.9f, 0.92f, 0.95f, 1f);
            label.text = primary ? "BİRLEŞTİR" : "İPTAL";
        }

        Outline outline = button.GetComponent<Outline>();
        if (outline == null)
            outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = primary
            ? new Color(1f, 0.76f, 0.34f, 0.65f)
            : new Color(0.42f, 0.47f, 0.54f, 0.65f);
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = false;
    }

    private void PlayCraftSound()
    {
        GameAudio.PlayCraft();
    }

    private string GetChildrenNames(Transform parent)
    {
        if (parent == null || parent.childCount == 0) return "None";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < parent.childCount; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(parent.GetChild(i).name);
        }
        return sb.ToString();
    }

}
