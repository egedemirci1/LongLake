using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using UnityEngine.SceneManagement; // Sahne geçiþi için gerekli

public class MainMenuUI : MonoBehaviour
{
    [Header("Butonlar")]
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button clientBtn;

    [Header("Ayarlar")]
    [SerializeField] private string gameSceneName = "CrashSite_Main"; // Oyun sahnesinin tam adý

    private void Awake()
    {
        Debug.Log("MainMenuUI Awake çalýþtý");
        // Host butonuna týklanýnca
        hostBtn.onClick.AddListener(() => {
            // Önce Host olarak aðý baþlat
            if (NetworkManager.Singleton.StartHost())
            {
                // Sonra herkesi oyun sahnesine taþý (Sadece Host bunu yapabilir)
                NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            }
        });

        // Client butonuna týklanýnca
        clientBtn.onClick.AddListener(() => {
            // Sadece Client olarak baðlan, sahne deðiþimini Host yapacak
            NetworkManager.Singleton.StartClient();
        });
    }
}