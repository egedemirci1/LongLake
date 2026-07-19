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
    private void EnsureProfessionalLayout()
    {
        if (_layoutReady || lobbyPanel == null) return;
        _layoutReady = true;

        var panelRt = lobbyPanel.GetComponent<RectTransform>();
        var panelImg = lobbyPanel.GetComponent<Image>();
        if (panelImg != null)
        {
            panelImg.color = ScrimColor;
            panelImg.raycastTarget = true;
        }

        // Eski çocukları gizle — referanslar korunur, yeni karta taşınır.
        for (int i = 0; i < lobbyPanel.transform.childCount; i++)
            lobbyPanel.transform.GetChild(i).gameObject.SetActive(false);

        var root = new GameObject("LobbyRoot", typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(lobbyPanel.transform, false);
        var rootRt = root.GetComponent<RectTransform>();
        StretchFull(rootRt);
        _rootGroup = root.GetComponent<CanvasGroup>();

        var card = new GameObject("LobbyCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        card.transform.SetParent(root.transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(860f, 680f);
        cardRt.anchoredPosition = new Vector2(0f, 0f);

        var cardImg = card.GetComponent<Image>();
        cardImg.sprite = RuntimeUiSprites.GetRoundedSprite(14);
        cardImg.type = Image.Type.Sliced;
        cardImg.color = CardColor;
        cardImg.raycastTarget = true;

        // Sol üstte ikincil geri aksiyonu; ana CTA'lardan ayrı tutulur.
        var backGo = new GameObject(
            "BackToMainMenuButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        backGo.transform.SetParent(card.transform, false);
        var backRt = backGo.GetComponent<RectTransform>();
        backRt.anchorMin = new Vector2(0f, 1f);
        backRt.anchorMax = new Vector2(0f, 1f);
        backRt.pivot = new Vector2(0f, 1f);
        backRt.anchoredPosition = new Vector2(32f, -22f);
        backRt.sizeDelta = new Vector2(124f, 30f);

        var backImg = backGo.GetComponent<Image>();
        backImg.sprite = RuntimeUiSprites.GetRoundedSprite(8);
        backImg.type = Image.Type.Sliced;
        backImg.color = new Color(0.12f, 0.13f, 0.15f, 0.92f);

        var backButton = backGo.GetComponent<Button>();
        backButton.targetGraphic = backImg;
        backButton.onClick.AddListener(ReturnToMainMenu);

        var backLabel = CreateTmp(
            backGo.transform, "Label", 13f, FontStyles.Bold, MutedText);
        StretchFull(backLabel.rectTransform);
        backLabel.alignment = TextAlignmentOptions.Center;
        backLabel.text = "‹  ANA MENÜ";

        var accent = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        accent.transform.SetParent(card.transform, false);
        var aRt = accent.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(0f, 0f);
        aRt.anchorMax = new Vector2(0f, 1f);
        aRt.pivot = new Vector2(0f, 0.5f);
        aRt.anchoredPosition = new Vector2(10f, 0f);
        aRt.sizeDelta = new Vector2(4f, -36f);
        var aImg = accent.GetComponent<Image>();
        aImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        aImg.type = Image.Type.Sliced;
        aImg.color = AccentColor;
        aImg.raycastTarget = false;

        // Header
        var eyebrow = CreateTmp(card.transform, "Eyebrow", 14f, FontStyles.Bold, AccentColor);
        Place(eyebrow.rectTransform, 172f, -28f, 36f, 22f);
        eyebrow.characterSpacing = 8f;
        eyebrow.alignment = TextAlignmentOptions.MidlineLeft;
        eyebrow.text = "LOBI";

        var title = CreateTmp(card.transform, "Title", 34f, FontStyles.Bold, BodyText);
        Place(title.rectTransform, 36f, -52f, 36f, 44f);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.text = "Karakterini Seç";

        var subtitle = CreateTmp(card.transform, "Subtitle", 15f, FontStyles.Normal, MutedText);
        Place(subtitle.rectTransform, 36f, -96f, 36f, 24f);
        subtitle.alignment = TextAlignmentOptions.MidlineLeft;
        subtitle.text = "Her oyuncu farklı bir karakter alır. Seçimin kalıcıdır.";

        // Bağlı oyuncular — host + misafir her iki tarafta da görünür
        BuildPlayerRoster(card.transform);

        var rule = new GameObject("Rule", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rule.transform.SetParent(card.transform, false);
        var ruleRt = rule.GetComponent<RectTransform>();
        ruleRt.anchorMin = new Vector2(0f, 1f);
        ruleRt.anchorMax = new Vector2(0f, 1f);
        ruleRt.pivot = new Vector2(0f, 0.5f);
        ruleRt.anchoredPosition = new Vector2(36f, -210f);
        ruleRt.sizeDelta = new Vector2(72f, 2f);
        var ruleImg = rule.GetComponent<Image>();
        ruleImg.sprite = RuntimeUiSprites.GetRoundedSprite(2);
        ruleImg.type = Image.Type.Sliced;
        ruleImg.color = AccentColor;
        ruleImg.raycastTarget = false;

        // Character row — altta aksiyon butonlarına boşluk bırak
        var row = new GameObject("CharacterRow", typeof(RectTransform));
        row.transform.SetParent(card.transform, false);
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 1f);
        rowRt.anchorMax = new Vector2(0.5f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, -220f);
        rowRt.sizeDelta = new Vector2(780f, 240f);

        var h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 28f;
        h.childAlignment = TextAnchor.UpperCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        h.padding = new RectOffset(0, 0, 0, 0);

        RestyleCharacterButton(
            ahuButton, row.transform, "Ahu", "Meraklı · Gözlemci", AhuTone, ahuPortrait,
            out _ahuCardBg, out _ahuRing, out _ahuPortraitImg, out _ahuName, out _ahuMeta, out _ahuBadge);

        RestyleCharacterButton(
            yamanButton, row.transform, "Yaman", "Kararlı · Koruyucu", YamanTone, yamanPortrait,
            out _yamanCardBg, out _yamanRing, out _yamanPortraitImg, out _yamanName, out _yamanMeta, out _yamanBadge);

        // Actions — kartların ALTINDA (bottom-anchored, status/IP üstünde)
        var actions = new GameObject("Actions", typeof(RectTransform));
        actions.transform.SetParent(card.transform, false);
        actions.transform.SetAsLastSibling();
        var actRt = actions.GetComponent<RectTransform>();
        actRt.anchorMin = new Vector2(0.5f, 0f);
        actRt.anchorMax = new Vector2(0.5f, 0f);
        actRt.pivot = new Vector2(0.5f, 0f);
        actRt.anchoredPosition = new Vector2(0f, 78f);
        actRt.sizeDelta = new Vector2(780f, 48f);

        var actH = actions.AddComponent<HorizontalLayoutGroup>();
        actH.spacing = 16f;
        actH.childAlignment = TextAnchor.MiddleCenter;
        actH.childControlWidth = true;
        actH.childControlHeight = true;
        actH.childForceExpandWidth = true;
        actH.childForceExpandHeight = false;
        actH.padding = new RectOffset(0, 0, 0, 0);

        RestyleActionButton(readyButton, actions.transform, readyButtonLabel, false, out _readyBg);
        if (readyButtonLabel != null)
            readyButtonLabel.text = "Hazırım";

        RestyleActionButton(startGameButton, actions.transform, null, true, out _startBg);
        var startLabel = startGameButton != null
            ? startGameButton.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (startLabel != null)
        {
            startLabel.text = "Oyunu Başlat";
            startLabel.fontSize = 18f;
            startLabel.fontStyle = FontStyles.Bold;
            startLabel.color = BodyText;
            startLabel.alignment = TextAlignmentOptions.Center;
        }

        // Oda durumu sağ üstte; rol ve hazır sayısı iki ayrı satırda.
        if (statusText != null)
        {
            statusText.transform.SetParent(card.transform, false);
            statusText.transform.SetAsLastSibling();
            statusText.gameObject.SetActive(true);
            Place(statusText.rectTransform, 600f, -28f, 36f, 48f);
            statusText.fontSize = 14f;
            statusText.fontStyle = FontStyles.Bold;
            statusText.color = MutedText;
            statusText.alignment = TextAlignmentOptions.TopRight;
            statusText.lineSpacing = 4f;
            statusText.raycastTarget = false;
        }

        // Oda IP'si alt merkezdeki sabit konumunu korur.
        if (hostIpText != null)
        {
            hostIpText.transform.SetParent(card.transform, false);
            hostIpText.transform.SetAsLastSibling();
            hostIpText.gameObject.SetActive(true);
            Place(hostIpText.rectTransform, 36f, 18f, 36f, 20f, fromBottom: true);
            hostIpText.fontSize = 17f;
            hostIpText.fontStyle = FontStyles.Bold;
            hostIpText.color = new Color(MutedText.r, MutedText.g, MutedText.b, 0.85f);
            hostIpText.alignment = TextAlignmentOptions.Center;
            hostIpText.characterSpacing = 1f;
            hostIpText.raycastTarget = false;
        }
    }

    private void BuildPlayerRoster(Transform card)
    {
        _playersEyebrow = CreateTmp(card, "PlayersEyebrow", 12f, FontStyles.Bold, AccentColor);
        Place(_playersEyebrow.rectTransform, 36f, -128f, 36f, 18f);
        _playersEyebrow.characterSpacing = 4f;
        _playersEyebrow.alignment = TextAlignmentOptions.MidlineLeft;
        _playersEyebrow.text = "OYUNCULAR  ·  1/2";

        var roster = new GameObject("PlayerRoster", typeof(RectTransform));
        roster.transform.SetParent(card, false);
        var rosterRt = roster.GetComponent<RectTransform>();
        rosterRt.anchorMin = new Vector2(0.5f, 1f);
        rosterRt.anchorMax = new Vector2(0.5f, 1f);
        rosterRt.pivot = new Vector2(0.5f, 1f);
        rosterRt.anchoredPosition = new Vector2(0f, -150f);
        rosterRt.sizeDelta = new Vector2(780f, 52f);

        var h = roster.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 14f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;

        for (int i = 0; i < 2; i++)
            _playerSlots[i] = CreatePlayerSlot(roster.transform, i);
    }

    private PlayerSlotUi CreatePlayerSlot(Transform parent, int index)
    {
        var go = new GameObject($"PlayerSlot{index}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var bg = go.GetComponent<Image>();
        bg.sprite = RuntimeUiSprites.GetRoundedSprite(10);
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.08f, 0.09f, 0.10f, 0.85f);
        bg.raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.minHeight = 52f;
        le.preferredHeight = 52f;

        var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dotGo.transform.SetParent(go.transform, false);
        var dotRt = dotGo.GetComponent<RectTransform>();
        dotRt.anchorMin = new Vector2(0f, 0.5f);
        dotRt.anchorMax = new Vector2(0f, 0.5f);
        dotRt.pivot = new Vector2(0.5f, 0.5f);
        dotRt.anchoredPosition = new Vector2(22f, 0f);
        dotRt.sizeDelta = new Vector2(10f, 10f);
        var dot = dotGo.GetComponent<Image>();
        dot.sprite = RuntimeUiSprites.GetRoundedSprite(8);
        dot.type = Image.Type.Sliced;
        dot.color = new Color(0.35f, 0.36f, 0.34f, 1f);
        dot.raycastTarget = false;

        var title = CreateTmp(go.transform, "Title", 15f, FontStyles.Bold, BodyText);
        var titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0f, 0.5f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(40f, 2f);
        titleRt.offsetMax = new Vector2(-14f, -4f);
        title.alignment = TextAlignmentOptions.BottomLeft;
        title.text = index == 0 ? "Oda sahibi" : "2. Oyuncu";

        var detail = CreateTmp(go.transform, "Detail", 12f, FontStyles.Normal, MutedText);
        var detailRt = detail.rectTransform;
        detailRt.anchorMin = new Vector2(0f, 0f);
        detailRt.anchorMax = new Vector2(1f, 0.5f);
        detailRt.offsetMin = new Vector2(40f, 6f);
        detailRt.offsetMax = new Vector2(-14f, -2f);
        detail.alignment = TextAlignmentOptions.TopLeft;
        detail.text = "Bağlantı bekleniyor…";

        return new PlayerSlotUi
        {
            Root = go,
            Dot = dot,
            Bg = bg,
            Title = title,
            Detail = detail
        };
    }

    private static TextMeshProUGUI CreateTmp(
        Transform parent, string name, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Place(RectTransform rt, float left, float yFromTop, float right, float height, bool fromBottom = false)
    {
        if (fromBottom)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, yFromTop);
            rt.offsetMax = new Vector2(-right, yFromTop + height);
        }
        else
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, yFromTop - height);
            rt.offsetMax = new Vector2(-right, yFromTop);
        }
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

}
