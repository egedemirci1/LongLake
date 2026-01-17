using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class LoadingScreenManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject loadingScreenPanel;
    [SerializeField] private Image backgroundImage; // Background Image (screenshot için)
    
    [Header("Footer Text")]
    [SerializeField] private TextMeshProUGUI baslikText; // "Oyun Yükleniyor"
    [SerializeField] private TextMeshProUGUI aciklamaText; // "Bölüm 1: Uzungöl Tatili"

    [Header("Screenshot (Optional)")]
    [SerializeField] private Sprite[] screenshotSprites; // Farklı sahneler için

    [Header("Settings")]
    [SerializeField] private string defaultBaslik = "Oyun Yükleniyor";
    [SerializeField] private string defaultAciklama = "";
    
    private static LoadingScreenManager instance;
    public static LoadingScreenManager Instance => instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.gameObject != gameObject)
            {
                DontDestroyOnLoad(canvas.gameObject);
            }
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (loadingScreenPanel != null)
        {
            loadingScreenPanel.SetActive(false);
        }
    }

    public void ShowLoadingScreen(string customBaslik = null, string customAciklama = null, Sprite screenshot = null)
    {
        if (loadingScreenPanel != null)
        {
            loadingScreenPanel.SetActive(true);
        }

        // Baslik text
        if (baslikText != null)
        {
            baslikText.text = customBaslik ?? defaultBaslik;
        }

        // Aciklama text
        if (aciklamaText != null)
        {
            aciklamaText.text = customAciklama ?? defaultAciklama;
        }

        // Background/Screenshot
        if (backgroundImage != null)
        {
            if (screenshot != null)
            {
                backgroundImage.sprite = screenshot;
                backgroundImage.gameObject.SetActive(true);
            }
            else if (screenshotSprites != null && screenshotSprites.Length > 0)
            {
                backgroundImage.sprite = screenshotSprites[0];
                backgroundImage.gameObject.SetActive(true);
            }
        }
    }

    public void ShowLoadingScreenForScene(string sceneName, string customAciklama = null)
    {
        // Sahne ismine göre screenshot ve açıklama seç
        Sprite selectedScreenshot = null;
        string aciklama = customAciklama;

        if (screenshotSprites != null && screenshotSprites.Length > 0)
        {
            string sceneLower = sceneName.ToLower();
            
            if (sceneLower.Contains("crashsite") || sceneLower.Contains("gameplay"))
            {
                selectedScreenshot = screenshotSprites[0];
                if (string.IsNullOrEmpty(aciklama))
                    aciklama = "Bölüm 1: Uzungöl Tatili";
            }
            // Diğer sahneler için else-if eklenebilir
        }

        ShowLoadingScreen("Oyun Yükleniyor", aciklama, selectedScreenshot);
    }

    public void HideLoadingScreen()
    {
        if (loadingScreenPanel != null)
            loadingScreenPanel.SetActive(false);
    }
}
