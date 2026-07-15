using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    [Header("Crosshair Settings")]
    [SerializeField] private Image crosshairImage;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color interactColor = Color.green;
    [SerializeField] private float normalSize = 10f;
    [SerializeField] private float interactSize = 15f;
    
    [Header("UI Path")]
    [SerializeField] private string crosshairPath = "InteractionUI/Crosshair";
    
    private PlayerInteraction playerInteraction;
    private InventoryManager inventoryManager;
    private RectTransform crosshairRect;
    private bool isInteracting = false;

    private void Start()
    {
        BindCrosshairUI();
        FindPlayerComponents();
        
        if (crosshairImage != null)
        {
            crosshairRect = crosshairImage.GetComponent<RectTransform>();
        }
    }

    private void FindPlayerComponents()
    {
        // Local player'ı bul
        GameObject localPlayer = null;
        
        // NetworkManager'dan local player'ı bul
        if (Unity.Netcode.NetworkManager.Singleton != null && 
            Unity.Netcode.NetworkManager.Singleton.LocalClient != null &&
            Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            localPlayer = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.gameObject;
        }
        
        // Bulunamazsa tag ile dene
        if (localPlayer == null)
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (GameObject player in players)
            {
                // NetworkBehaviour kontrolü yap (IsOwner kontrolü için)
                var networkObj = player.GetComponent<Unity.Netcode.NetworkObject>();
                if (networkObj != null && networkObj.IsOwner)
                {
                    localPlayer = player;
                    break;
                }
            }
        }
        
        if (localPlayer != null)
        {
            playerInteraction = localPlayer.GetComponent<PlayerInteraction>();
            inventoryManager = localPlayer.GetComponent<InventoryManager>();
        }
        else
        {
            // Retry later
            Invoke(nameof(FindPlayerComponents), 0.5f);
        }
    }

    private void BindCrosshairUI()
    {
        if (crosshairImage != null) return;
        
        GameObject crosshairObj = GameObject.Find(crosshairPath);
        if (crosshairObj == null)
        {
            // Alternatif: Tag ile bul
            GameObject uiRoot = GameObject.FindWithTag("InteractionUI");
            if (uiRoot != null)
            {
                Transform crosshairT = uiRoot.transform.Find("Crosshair");
                if (crosshairT != null)
                {
                    crosshairObj = crosshairT.gameObject;
                }
            }
        }
        
        if (crosshairObj != null)
        {
            crosshairImage = crosshairObj.GetComponent<Image>();
            if (crosshairImage == null)
            {
                Debug.LogWarning("[Crosshair] Crosshair GameObject found but Image component is missing!");
            }
            else
            {
                Debug.Log("<color=green>[Crosshair]</color> Crosshair UI bound successfully");
            }
        }
        else
        {
            Debug.LogWarning("[Crosshair] Crosshair UI not found at path: " + crosshairPath);
        }
    }

    private void Update()
    {
        if (crosshairImage == null) return;
        
        // Local player'ı tekrar kontrol et (eğer bulunamadıysa)
        if (playerInteraction == null || inventoryManager == null)
        {
            FindPlayerComponents();
            return; // Bu frame'de çık, sonraki frame'de tekrar dene
        }
        
        // Envanter açık mı kontrol et
        bool inventoryOpen = inventoryManager != null && 
                            inventoryManager.mainInventoryObject != null && 
                            inventoryManager.mainInventoryObject.activeSelf;
        
        // Envanter açıksa crosshair'ı gizle
        crosshairImage.enabled = !inventoryOpen;
        
        if (inventoryOpen) return; // Envanter açıksa etkileşim kontrolü yapma
        
        // Etkileşim var mı kontrol et
        bool canInteract = CheckIfCanInteract();
        
        if (canInteract && !isInteracting)
        {
            // Etkileşilebilir nesne üzerinde
            crosshairImage.color = interactColor;
            if (crosshairRect != null)
            {
                crosshairRect.sizeDelta = new Vector2(interactSize, interactSize);
            }
            isInteracting = true;
        }
        else if (!canInteract && isInteracting)
        {
            // Normal mod
            crosshairImage.color = normalColor;
            if (crosshairRect != null)
            {
                crosshairRect.sizeDelta = new Vector2(normalSize, normalSize);
            }
            isInteracting = false;
        }
    }

    private bool CheckIfCanInteract()
    {
        if (playerInteraction == null) return false;
        
        // Camera'yı bul
        Camera cam = Camera.main;
        if (cam == null)
        {
            // Local player'dan camera'yı bul
            if (playerInteraction != null)
            {
                GameObject playerObj = playerInteraction.gameObject;
                cam = playerObj.GetComponentInChildren<Camera>();
                
                if (cam == null)
                {
                    // Cinemachine camera'yı bul
                    var cinemachineCams = FindObjectsByType<Cinemachine.CinemachineVirtualCamera>(FindObjectsSortMode.None);
                    foreach (var vcam in cinemachineCams)
                    {
                        if (vcam != null && vcam.enabled && vcam.gameObject.activeInHierarchy)
                        {
                            if (vcam.VirtualCameraGameObject != null)
                            {
                                cam = vcam.VirtualCameraGameObject.GetComponent<Camera>();
                            }
                            if (cam == null)
                            {
                                cam = Camera.main;
                            }
                            break;
                        }
                    }
                }
            }
        }
        
        if (cam == null) return false;
        
        // Raycast yap
        RaycastHit hit;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, 
            out hit, 4f))
        {
            IInteractable interactable = hit.collider.GetComponent<IInteractable>() ?? 
                                       hit.collider.GetComponentInParent<IInteractable>();
            return interactable != null;
        }
        
        return false;
    }
}
