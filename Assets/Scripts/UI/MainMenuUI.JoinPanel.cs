using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// Partial: split for maintainability. Type identity unchanged.
public partial class MainMenuUI : MonoBehaviour
{
    public void ShowJoinPanel()
    {
        if (joinPanel == null)
            BuildJoinPanel();

        if (joinPanel == null)
        {
            // Panel kurulamazsa eski davranış: direkt bağlan.
            StartClient();
            return;
        }

        // Öneri: transport'taki adres ya da defaultIp.
        if (joinIpInput != null && string.IsNullOrWhiteSpace(joinIpInput.text))
        {
            string suggested = unityTransport != null ? unityTransport.ConnectionData.Address : null;
            if (string.IsNullOrWhiteSpace(suggested) || suggested == "0.0.0.0")
                suggested = defaultIp;
            joinIpInput.text = suggested ?? "";
        }

        joinPanel.SetActive(true);
    }

    private void HideJoinPanel()
    {
        if (joinPanel != null)
            joinPanel.SetActive(false);
    }

    private void ConnectFromJoinPanel()
    {
        HideJoinPanel();
        StartClient();
    }

    private void BuildJoinPanel()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        joinPanel = new GameObject("JoinPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        joinPanel.transform.SetParent(canvas.transform, false);
        var rt = joinPanel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var bg = joinPanel.GetComponent<Image>();
        bg.color = new Color(0.02f, 0.05f, 0.06f, 0.72f);
        bg.raycastTarget = true;

        var box = new GameObject("Box", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        box.transform.SetParent(joinPanel.transform, false);
        var boxRt = box.GetComponent<RectTransform>();
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.anchoredPosition = Vector2.zero;
        boxRt.sizeDelta = new Vector2(520f, 260f);
        box.GetComponent<Image>().color = new Color(0.07f, 0.1f, 0.11f, 0.95f);

        CreateTmpText(box.transform, "Title", "Odaya Katıl", 30, FontStyles.Bold,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -60f), new Vector2(-24f, -16f), TextAlignmentOptions.Center);

        joinIpInput = CreateTmpInput(box.transform, "IpInput",
            new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(420f, 52f), "Host IP (örn. 10.171.156.166)");

        var connectBtn = CreateTmpButton(box.transform, "ConnectButton", "Bağlan",
            new Vector2(0.5f, 0f), new Vector2(-110f, 46f), new Vector2(200f, 52f));
        connectBtn.onClick.AddListener(PlayUiClick);
        connectBtn.onClick.AddListener(ConnectFromJoinPanel);

        var backBtn = CreateTmpButton(box.transform, "BackButton", "Geri",
            new Vector2(0.5f, 0f), new Vector2(110f, 46f), new Vector2(200f, 52f));
        backBtn.onClick.AddListener(PlayUiClick);
        backBtn.onClick.AddListener(HideJoinPanel);

        joinPanel.SetActive(false);
    }

    private static TextMeshProUGUI CreateTmpText(Transform parent, string name, string text, float size, FontStyles style,
        Vector2 aMin, Vector2 aMax, Vector2 offsetMin, Vector2 offsetMax, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = new Color(0.93f, 0.94f, 0.92f, 1f);
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Button CreateTmpButton(Transform parent, string name, string label, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.18f, 0.95f);

        CreateTmpText(go.transform, "Label", label, 24, FontStyles.Normal,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);

        return go.GetComponent<Button>();
    }

    private static TMP_InputField CreateTmpInput(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, string placeholder)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.16f, 0.22f, 0.22f, 1f);

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var areaRt = area.GetComponent<RectTransform>();
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(12f, 6f);
        areaRt.offsetMax = new Vector2(-12f, -6f);

        var placeholderTmp = CreateTmpText(area.transform, "Placeholder", placeholder, 22, FontStyles.Italic,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);
        placeholderTmp.color = new Color(0.7f, 0.74f, 0.73f, 0.6f);

        var textTmp = CreateTmpText(area.transform, "Text", "", 22, FontStyles.Normal,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TextAlignmentOptions.Left);

        var input = go.GetComponent<TMP_InputField>();
        input.textViewport = areaRt;
        input.textComponent = textTmp;
        input.placeholder = placeholderTmp;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 64;
        return input;
    }

}
