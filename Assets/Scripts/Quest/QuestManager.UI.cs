using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

// Partial: split for maintainability. Type identity unchanged.
public partial class QuestManager : NetworkBehaviour
{
    private void ToggleQuestPanel()
    {
        EnsureUiExists();
        if (questPanelObject == null) return;

        if (isPanelOpen) CloseQuestPanel();
        else OpenQuestPanel();
    }

    private void OpenQuestPanel()
    {
        EnsureUiExists();
        if (questPanelObject == null) return;

        isPanelOpen = true;
        questPanelObject.SetActive(true);
        RefreshAllUi();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseQuestPanel()
    {
        isPanelOpen = false;
        if (questPanelObject != null)
            questPanelObject.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnCurrentQuestChanged(int oldValue, int newValue)
    {
        RefreshAllUi();

        // Yeni görev başladığında kart soldan kayarak belirsin
        // Eğer bu ilk görevse (oldValue == -1) ses ve intro hemen çalınır.
        // Eğer bir önceki görev tamamlandığı için buraya geldiysek (oldValue >= 0),
        // ses ve intro PlayQuestCompletedFlash coroutine'i tarafından gecikmeli çalınacak.
        if (newValue >= 0 && oldValue == -1 && !hudFlashActive)
        {
            PlayHudIntro();
            GameAudio.PlayQStart();
        }

        OnQuestStateChanged?.Invoke();
    }

    private void OnBagsCollectedChanged(int oldValue, int newValue)
    {
        RefreshAllUi();
        OnQuestStateChanged?.Invoke();
    }

    private void OnCompletedQuestsChanged(NetworkListEvent<int> changeEvent)
    {
        // Tamamlanan görevin adını yakalayıp kısa bir yeşil "tamamlandı" flaşı göster.
        if (changeEvent.Type == NetworkListEvent<int>.EventType.Add &&
            availableQuests != null &&
            changeEvent.Value >= 0 && changeEvent.Value < availableQuests.Length &&
            availableQuests[changeEvent.Value] != null &&
            isActiveAndEnabled)
        {
            StartCoroutine(PlayQuestCompletedFlash(availableQuests[changeEvent.Value]));
            GameAudio.PlayQFinish();
        }

        RefreshAllUi();
        OnQuestStateChanged?.Invoke();
    }

    private void RefreshAllUi()
    {
        EnsureUiExists();
        UpdateCurrentQuestDisplay();
        UpdateHud();
    }

    private string BuildQuestDescription(QuestData quest)
    {
        return quest == null ? string.Empty : quest.questDescription;
    }

    /// <summary>İlerleme bilgisini açıklamadan ayrı, kendi satırı olarak üretir (yoksa boş).</summary>
    /// <remarks>Açılış görevinde çanta sayısı bilerek gösterilmez; oyuncu görevi keşfederek öğrenmelidir.</remarks>
    private string BuildProgressText(QuestData quest)
    {
        return string.Empty;
    }

    private void UpdateCurrentQuestDisplay()
    {
        QuestData quest = GetCurrentQuest();

        if (quest == null)
        {
            if (currentQuestTitleText != null)
                currentQuestTitleText.text = "Güncel Görev Yok";
            if (currentQuestDescriptionText != null)
                currentQuestDescriptionText.text = "Aktif bir görev bulunmuyor.";
            return;
        }

        if (currentQuestTitleText != null)
            currentQuestTitleText.text = quest.questTitle;

        if (currentQuestDescriptionText != null)
        {
            string desc = BuildQuestDescription(quest);
            string progress = BuildProgressText(quest);
            if (!string.IsNullOrEmpty(progress))
                desc += "\n\n" + progress;
            currentQuestDescriptionText.text = desc;
        }
    }

    private void UpdateHud()
    {
        // Tamamlanma flaşı sırasında kartı coroutine yönetir.
        if (hudFlashActive) return;

        QuestData quest = GetCurrentQuest();
        bool show = quest != null;

        if (runtimeHudRoot != null)
            runtimeHudRoot.SetActive(show);

        if (!show) return;

        if (hudEyebrowText != null)
        {
            hudEyebrowText.text = "GÖREV";
            hudEyebrowText.color = AccentColor;
        }

        if (hudAccentBar != null)
            hudAccentBar.color = AccentColor;

        if (hudTitleText != null)
            hudTitleText.text = quest.questTitle;
        if (hudDescriptionText != null)
            hudDescriptionText.text = BuildQuestDescription(quest);

        if (hudProgressText != null)
        {
            string progress = BuildProgressText(quest);
            hudProgressText.gameObject.SetActive(!string.IsNullOrEmpty(progress));
            hudProgressText.text = progress;
        }
    }

    private void PlayHudIntro()
    {
        if (hudGroup == null) return;
        hudIntroStart = Time.unscaledTime;
        hudGroup.alpha = 0f;
    }

    private void AnimateHudIntro()
    {
        if (hudGroup == null || hudIntroStart < 0f) return;

        float t = Mathf.Clamp01((Time.unscaledTime - hudIntroStart) / HudIntroDuration);
        float eased = 1f - (1f - t) * (1f - t);

        hudGroup.alpha = eased;
        if (hudRect != null)
            hudRect.anchoredPosition = hudBasePosition + Vector2.left * (HudIntroSlide * (1f - eased));

        if (t >= 1f)
            hudIntroStart = -1f;
    }

    private System.Collections.IEnumerator PlayQuestCompletedFlash(QuestData completedQuest)
    {
        EnsureUiExists();
        if (runtimeHudRoot == null || completedQuest == null) yield break;

        hudFlashActive = true;
        hudIntroStart = -1f;

        runtimeHudRoot.SetActive(true);
        if (hudGroup != null) hudGroup.alpha = 1f;
        if (hudRect != null) hudRect.anchoredPosition = hudBasePosition;

        if (hudEyebrowText != null)
        {
            hudEyebrowText.text = "GÖREV TAMAMLANDI";
            hudEyebrowText.color = CompleteColor;
        }
        if (hudAccentBar != null)
            hudAccentBar.color = CompleteColor;
        if (hudTitleText != null)
            hudTitleText.text = completedQuest.questTitle;
        if (hudDescriptionText != null)
            hudDescriptionText.text = string.Empty;
        if (hudProgressText != null)
            hudProgressText.gameObject.SetActive(false);

        float waitTime = GameAudio.GetQFinishDuration();
        yield return new WaitForSeconds(waitTime);

        hudFlashActive = false;
        UpdateHud();

        if (GetCurrentQuest() != null)
        {
            PlayHudIntro();
            GameAudio.PlayQStart();
        }
    }

    private void EnsureUiExists()
    {
        if (hudTitleText != null && questPanelObject != null && currentQuestTitleText != null)
            return;

        Canvas canvas = FindInteractionCanvas();
        if (canvas == null) return;

        if (hudTitleText == null)
            CreateRuntimeHud(canvas);

        if (questPanelObject == null)
            CreateRuntimePanel(canvas);
    }

    private static Canvas FindInteractionCanvas()
    {
        var tagged = GameObject.FindGameObjectWithTag("InteractionUI");
        if (tagged != null)
        {
            var c = tagged.GetComponent<Canvas>();
            if (c != null) return c;
        }

        return FindFirstObjectByType<Canvas>();
    }

    private void CreateRuntimeHud(Canvas canvas)
    {
        if (runtimeHudRoot != null) return;

        EnsureCanvasScalesWithScreen(canvas);

        runtimeHudRoot = new GameObject("ActiveQuestHud", typeof(RectTransform));
        runtimeHudRoot.transform.SetParent(canvas.transform, false);

        hudRect = runtimeHudRoot.GetComponent<RectTransform>();
        hudRect.anchorMin = new Vector2(0f, 1f);
        hudRect.anchorMax = new Vector2(0f, 1f);
        hudRect.pivot = new Vector2(0f, 1f);
        hudRect.anchoredPosition = new Vector2(20f, -20f);
        hudRect.sizeDelta = new Vector2(340f, 100f); // yükseklik ContentSizeFitter ile içerige uyar
        hudBasePosition = hudRect.anchoredPosition;

        var bg = runtimeHudRoot.AddComponent<Image>();
        bg.sprite = GetRoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.05f, 0.06f, 0.08f, 0.78f);
        bg.raycastTarget = false;

        var layout = runtimeHudRoot.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 14, 10, 12);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = runtimeHudRoot.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        hudGroup = runtimeHudRoot.AddComponent<CanvasGroup>();
        hudGroup.blocksRaycasts = false;
        hudGroup.interactable = false;

        hudAccentBar = CreateAccentBar(runtimeHudRoot.transform);

        hudEyebrowText = CreateLayoutTmp(runtimeHudRoot.transform, "HudEyebrow", 11, FontStyles.Bold, AccentColor);
        hudEyebrowText.characterSpacing = 10f;
        hudEyebrowText.text = "GÖREV";

        hudTitleText = CreateLayoutTmp(runtimeHudRoot.transform, "HudTitle", 20, FontStyles.Bold, Color.white);
        hudDescriptionText = CreateLayoutTmp(runtimeHudRoot.transform, "HudDesc", 14, FontStyles.Normal, new Color(1f, 1f, 1f, 0.72f));
        hudProgressText = CreateLayoutTmp(runtimeHudRoot.transform, "HudProgress", 14, FontStyles.Bold, new Color(1f, 1f, 1f, 0.9f));
        hudProgressText.gameObject.SetActive(false);

        runtimeHudRoot.SetActive(false);
    }

    /// <summary>Panelin sol kenarına layout dışı ince vurgu şeridi ekler.</summary>
    private static Image CreateAccentBar(Transform parent)
    {
        var go = new GameObject("Accent", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(6f, 0f);
        rt.sizeDelta = new Vector2(4f, -20f);

        var image = go.AddComponent<Image>();
        image.sprite = GetRoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = AccentColor;
        image.raycastTarget = false;

        go.AddComponent<LayoutElement>().ignoreLayout = true;
        return image;
    }

    private static void EnsureCanvasScalesWithScreen(Canvas canvas)
    {
        if (canvas == null) return;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void CreateRuntimePanel(Canvas canvas)
    {
        if (runtimePanelRoot != null) return;

        EnsureCanvasScalesWithScreen(canvas);

        runtimePanelRoot = new GameObject("QuestPanel", typeof(RectTransform));
        runtimePanelRoot.transform.SetParent(canvas.transform, false);

        var root = runtimePanelRoot.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(560f, 250f);

        var bg = runtimePanelRoot.AddComponent<Image>();
        bg.sprite = GetRoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);

        CreateAccentBar(runtimePanelRoot.transform);

        var eyebrow = CreateTmp(runtimePanelRoot.transform, "PanelEyebrow", new Vector2(28f, -18f), 12, FontStyles.Bold, 20f);
        eyebrow.text = "GÖREV GÜNLÜĞÜ";
        eyebrow.color = AccentColor;
        eyebrow.characterSpacing = 10f;

        currentQuestTitleText = CreateTmp(runtimePanelRoot.transform, "PanelTitle", new Vector2(28f, -42f), 26, FontStyles.Bold);
        currentQuestDescriptionText = CreateTmp(runtimePanelRoot.transform, "PanelDesc", new Vector2(28f, -88f), 16, FontStyles.Normal, 110f);
        currentQuestDescriptionText.color = new Color(1f, 1f, 1f, 0.8f);

        var hint = CreateTmp(runtimePanelRoot.transform, "Hint", new Vector2(28f, -212f), 13, FontStyles.Italic, 24f);
        hint.text = "Kapatmak için M veya ESC";
        hint.color = new Color(1f, 1f, 1f, 0.45f);

        questPanelObject = runtimePanelRoot;
        questPanelObject.SetActive(false);
    }

    private static TextMeshProUGUI CreateTmp(Transform parent, string name, Vector2 anchoredPos, float fontSize, FontStyles style, float height = 40f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(-24f, height);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>VerticalLayoutGroup içinde boyutu layout tarafından yönetilen TMP oluşturur.</summary>
    private static TextMeshProUGUI CreateLayoutTmp(Transform parent, string name, float fontSize, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Sprite GetRoundedSprite() => RuntimeUiSprites.GetRoundedSprite(10);

}
