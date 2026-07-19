using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Partial: split for maintainability. Type identity unchanged.
public partial class LobbyUI : MonoBehaviour
{
    private void RestyleCharacterButton(
        Button button, Transform parent, string displayName, string meta,
        Color tone, Sprite portrait,
        out Image cardBg, out Image ring, out Image portraitImg,
        out TextMeshProUGUI nameTmp, out TextMeshProUGUI metaTmp, out TextMeshProUGUI badgeTmp)
    {
        cardBg = null;
        ring = null;
        portraitImg = null;
        nameTmp = null;
        metaTmp = null;
        badgeTmp = null;
        if (button == null) return;

        // Eski child label'ları temizle (Destroy gecikmeli; Immediate gerekir)
        while (button.transform.childCount > 0)
            DestroyImmediate(button.transform.GetChild(0).gameObject);

        button.transform.SetParent(parent, false);
        button.gameObject.SetActive(true);

        var le = button.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = button.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;
        le.minWidth = 300f;
        le.minHeight = 240f;
        le.preferredWidth = 370f;
        le.preferredHeight = 240f;

        var btnRt = button.GetComponent<RectTransform>();
        btnRt.sizeDelta = new Vector2(370f, 240f);

        cardBg = button.GetComponent<Image>();
        if (cardBg == null) cardBg = button.gameObject.AddComponent<Image>();
        cardBg.sprite = RuntimeUiSprites.GetRoundedSprite(12);
        cardBg.type = Image.Type.Sliced;
        cardBg.color = CharCardIdle;
        button.targetGraphic = cardBg;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.85f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        // Selection ring
        var ringGo = new GameObject("SelectRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        ringGo.transform.SetParent(button.transform, false);
        var ringRt = ringGo.GetComponent<RectTransform>();
        StretchFull(ringRt);
        ringRt.offsetMin = new Vector2(-3f, -3f);
        ringRt.offsetMax = new Vector2(3f, 3f);
        ring = ringGo.GetComponent<Image>();
        ring.sprite = RuntimeUiSprites.GetRoundedSprite(14);
        ring.type = Image.Type.Sliced;
        ring.color = AccentColor;
        ring.raycastTarget = false;
        ring.enabled = false;

        // Portrait plate — sprite varsa portre, yoksa monogram
        var plate = new GameObject("PortraitPlate", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        plate.transform.SetParent(button.transform, false);
        var plateRt = plate.GetComponent<RectTransform>();
        plateRt.anchorMin = new Vector2(0.5f, 1f);
        plateRt.anchorMax = new Vector2(0.5f, 1f);
        plateRt.pivot = new Vector2(0.5f, 1f);
        plateRt.anchoredPosition = new Vector2(0f, -12f);
        plateRt.sizeDelta = new Vector2(300f, 130f);
        var plateImg = plate.GetComponent<Image>();
        plateImg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        plateImg.type = Image.Type.Sliced;
        plateImg.color = portrait != null
            ? new Color(0.08f, 0.09f, 0.10f, 1f)
            : new Color(tone.r * 0.35f, tone.g * 0.35f, tone.b * 0.35f, 1f);
        plateImg.raycastTarget = false;

        // Mask so portrait fits rounded plate
        var maskGo = new GameObject("Mask", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        maskGo.transform.SetParent(plate.transform, false);
        StretchFull(maskGo.GetComponent<RectTransform>());
        var maskImg = maskGo.GetComponent<Image>();
        maskImg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        maskImg.type = Image.Type.Sliced;
        maskImg.color = Color.white;
        maskImg.raycastTarget = false;
        maskGo.GetComponent<Mask>().showMaskGraphic = false;

        portraitImg = null;
        if (portrait != null)
        {
            var pGo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pGo.transform.SetParent(maskGo.transform, false);
            StretchFull(pGo.GetComponent<RectTransform>());
            portraitImg = pGo.GetComponent<Image>();
            portraitImg.sprite = portrait;
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;
            portraitImg.color = Color.white;
            // Hafif zoom — yüz kartta daha büyük dursun
            var prt = pGo.GetComponent<RectTransform>();
            prt.localScale = new Vector3(1.15f, 1.15f, 1f);
        }
        else
        {
            var mono = CreateTmp(plate.transform, "Monogram", 72f, FontStyles.Bold, tone);
            StretchFull(mono.rectTransform);
            mono.alignment = TextAlignmentOptions.Center;
            mono.text = displayName.Length > 0 ? displayName.Substring(0, 1).ToUpperInvariant() : "?";
            mono.characterSpacing = 4f;
        }

        // Soft bottom fade on plate
        var fade = new GameObject("Fade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fade.transform.SetParent(plate.transform, false);
        var fadeRt = fade.GetComponent<RectTransform>();
        fadeRt.anchorMin = new Vector2(0f, 0f);
        fadeRt.anchorMax = new Vector2(1f, 0.35f);
        fadeRt.offsetMin = Vector2.zero;
        fadeRt.offsetMax = Vector2.zero;
        var fadeImg = fade.GetComponent<Image>();
        fadeImg.color = new Color(0.06f, 0.07f, 0.09f, 0.45f);
        fadeImg.raycastTarget = false;

        nameTmp = CreateTmp(button.transform, "Name", 28f, FontStyles.Bold, BodyText);
        var nameRt = nameTmp.rectTransform;
        nameRt.anchorMin = new Vector2(0f, 0f);
        nameRt.anchorMax = new Vector2(1f, 0f);
        nameRt.pivot = new Vector2(0.5f, 0f);
        nameRt.anchoredPosition = new Vector2(0f, 68f);
        nameRt.sizeDelta = new Vector2(-32f, 32f);
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.text = displayName;
        nameTmp.fontSize = 26f;

        metaTmp = CreateTmp(button.transform, "Meta", 15f, FontStyles.Bold, MutedText);
        var metaRt = metaTmp.rectTransform;
        metaRt.anchorMin = new Vector2(0f, 0f);
        metaRt.anchorMax = new Vector2(1f, 0f);
        metaRt.pivot = new Vector2(0.5f, 0f);
        metaRt.anchoredPosition = new Vector2(0f, 42f);
        metaRt.sizeDelta = new Vector2(-24f, 24f);
        metaTmp.alignment = TextAlignmentOptions.Center;
        metaTmp.text = meta;

        badgeTmp = CreateTmp(button.transform, "Badge", 12f, FontStyles.Bold, tone);
        var badgeRt = badgeTmp.rectTransform;
        badgeRt.anchorMin = new Vector2(0f, 0f);
        badgeRt.anchorMax = new Vector2(1f, 0f);
        badgeRt.pivot = new Vector2(0.5f, 0f);
        badgeRt.anchoredPosition = new Vector2(0f, 14f);
        badgeRt.sizeDelta = new Vector2(-32f, 20f);
        badgeTmp.alignment = TextAlignmentOptions.Center;
        badgeTmp.characterSpacing = 3f;
        badgeTmp.text = "SEÇ";
    }

    private void RestyleActionButton(
        Button button, Transform parent, TextMeshProUGUI existingLabel, bool isStart, out Image bg)
    {
        bg = null;
        if (button == null) return;

        button.transform.SetParent(parent, false);
        button.gameObject.SetActive(true);

        var le = button.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = button.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;
        le.minHeight = 48f;
        le.preferredHeight = 48f;
        le.minWidth = 200f;

        var btnRt = button.GetComponent<RectTransform>();
        btnRt.localScale = Vector3.one;
        btnRt.sizeDelta = new Vector2(360f, 48f);

        bg = button.GetComponent<Image>();
        if (bg == null) bg = button.gameObject.AddComponent<Image>();
        bg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        bg.type = Image.Type.Sliced;
        bg.color = isStart ? StartAccent : ButtonIdle;
        button.targetGraphic = bg;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        colors.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.7f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI label = existingLabel;
        if (label == null)
            label = button.GetComponentInChildren<TextMeshProUGUI>(true);

        if (label != null)
        {
            label.transform.SetParent(button.transform, false);
            StretchFull(label.rectTransform);
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.color = isStart ? new Color(0.08f, 0.14f, 0.10f, 1f) : BodyText;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
        else
        {
            label = CreateTmp(button.transform, "Label", 18f, FontStyles.Bold,
                isStart ? new Color(0.08f, 0.14f, 0.10f, 1f) : BodyText);
            StretchFull(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            if (isStart) label.text = "Oyunu Başlat";
        }

        if (!isStart && readyButtonLabel == null)
            readyButtonLabel = label;
    }

}
