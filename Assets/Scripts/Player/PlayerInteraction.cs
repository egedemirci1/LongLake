using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;

public class PlayerInteraction : NetworkBehaviour
{
    [Header("Ayarlar")]
    [SerializeField] private float interactDistance = 4.0f;
    [SerializeField] private Transform cameraRoot;

    private TMP_Text interactionText;
    private InventoryManager myInventory;

    // Prompt paneli (tuş rozeti + metin). Yoksa çıplak metne düşer.
    private GameObject promptRoot;
    private CanvasGroup promptGroup;
    private RectTransform promptRect;
    private Vector2 promptBasePosition;
    private const float FadeDuration = 0.12f;
    private const float SlideOffset = 10f;
    private float showTime = -1f;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        // Inventory root'ta olmayabilir -> child dahil ara
        myInventory = GetComponentInChildren<InventoryManager>(true);

        if (myInventory == null)
        {
            Debug.LogError("<color=red>[Interaction]</color> InventoryManager bulunamadı! " +
                           "Player prefab/root veya child objelerinde InventoryManager var mı?");
        }
    }

    private void Start()
    {
        if (!IsOwner) return;
        StartCoroutine(BindUIUntilFound());
    }

    private IEnumerator BindUIUntilFound()
    {
        float timeout = 5f;
        float t = 0f;

        while (interactionText == null && t < timeout)
        {
            TryBindUI();
            if (interactionText != null) break;

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (interactionText == null)
        {
            Debug.LogError("<color=red>[Interaction]</color> UI bulunamadı!");
            yield break;
        }

        // Panel varsa (InteractionPrompt) onu yönet; yoksa çıplak metni.
        promptGroup = interactionText.GetComponentInParent<CanvasGroup>(true);
        promptRoot = promptGroup != null ? promptGroup.gameObject : interactionText.gameObject;
        promptRect = promptRoot.GetComponent<RectTransform>();
        if (promptRect != null)
            promptBasePosition = promptRect.anchoredPosition;

        interactionText.text = "";
        promptRoot.SetActive(false);
    }

    private void ShowPrompt(string text)
    {
        interactionText.text = text;

        if (!promptRoot.activeSelf)
        {
            promptRoot.SetActive(true);
            showTime = Time.unscaledTime;
        }
    }

    private void HidePrompt()
    {
        if (promptRoot != null && promptRoot.activeSelf)
            promptRoot.SetActive(false);
        showTime = -1f;
    }

    /// <summary>Prompt açılırken kısa fade-in ve hafif yukarı kayma.</summary>
    private void AnimatePrompt()
    {
        if (promptGroup == null || showTime < 0f || !promptRoot.activeSelf)
            return;

        float t = Mathf.Clamp01((Time.unscaledTime - showTime) / FadeDuration);
        promptGroup.alpha = t;
        if (promptRect != null)
            promptRect.anchoredPosition = promptBasePosition + Vector2.down * (SlideOffset * (1f - t));
    }

    private void TryBindUI()
    {
        var all = Resources.FindObjectsOfTypeAll<TMP_Text>();

        // 1) Deneme yazan
        foreach (var txt in all)
        {
            if (txt == null) continue;
            if (!txt.gameObject.scene.IsValid()) continue;

            if (!string.IsNullOrEmpty(txt.text) && txt.text.ToLower().Contains("deneme"))
            {
                interactionText = txt;
                return;
            }
        }

        // 2) InteractionText adlı
        foreach (var txt in all)
        {
            if (txt == null) continue;
            if (!txt.gameObject.scene.IsValid()) continue;

            if (txt.gameObject.name == "InteractionText")
            {
                interactionText = txt;
                return;
            }
        }

        // 3) InteractionUI tag altı
        GameObject uiRoot = GameObject.FindWithTag("InteractionUI");
        if (uiRoot != null)
            interactionText = uiRoot.GetComponentInChildren<TMP_Text>(true);
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (interactionText == null) return;

        Vector3 origin = cameraRoot != null ? cameraRoot.position : transform.position + Vector3.up * 1.6f;
        Vector3 direction = cameraRoot != null ? cameraRoot.forward : transform.forward;

        Debug.DrawRay(origin, direction * interactDistance, Color.magenta);
        CheckInteraction(origin, direction);
        AnimatePrompt();
    }

    private void CheckInteraction(Vector3 origin, Vector3 direction)
    {
        if (DialogueManager.IsDialogueOpen)
        {
            HidePrompt();
            return;
        }

        int layerMask = ~(1 << gameObject.layer);

        // RaycastAll kullan - tüm collider'ları kontrol et (çekmece + içindeki kitap için)
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, interactDistance, layerMask);
        
        if (hits.Length == 0)
        {
            HidePrompt();
            return;
        }
        
        // Önce tüm IInteractable'ları bul
        List<(RaycastHit hit, IInteractable interactable, float priority)> interactables = new List<(RaycastHit, IInteractable, float)>();
        
        foreach (RaycastHit hit in hits)
        {
            IInteractable interactable =
                hit.collider.GetComponent<IInteractable>() ??
                hit.collider.GetComponentInParent<IInteractable>();

            if (interactable != null)
            {
                // Linecast ile arada duvar/engel olup olmadığını kontrol et
                if (Physics.Linecast(origin, hit.point, out RaycastHit wallHit, layerMask, QueryTriggerInteraction.Ignore))
                {
                    if (IsBlockingObstacle(wallHit.collider, hit.collider, interactable))
                        continue; // Arada engel var, etkileşimi engelle
                }

                float priority = 0f; // Varsayılan öncelik
                
                // ItemPickUp'lara öncelik ver, AMA sadece çekmece açıksa
                if (interactable is ItemPickUp)
                {
                    // Bu hit'in parent'ında CabinetController var mı kontrol et
                    CabinetController cabinet = hit.collider.GetComponentInParent<CabinetController>();
                    if (cabinet != null)
                    {
                        // Çekmece açıksa kitaba öncelik ver
                        if (cabinet.IsOpenState)
                        {
                            priority = 1f; // Yüksek öncelik (çekmece açık, kitap alınabilir)
                        }
                        // Çekmece kapalıysa öncelik 0 (çekmece önce seçilmeli)
                    }
                    else
                    {
                        // Çekmecenin içinde değilse direkt öncelik ver
                        priority = 1f;
                    }
                }
                
                interactables.Add((hit, interactable, priority));
            }
        }
        
        if (interactables.Count == 0)
        {
            HidePrompt();
            return;
        }
        
        // Önce önceliğe göre, sonra mesafeye göre sırala
        interactables.Sort((x, y) => 
        {
            int priorityCompare = y.priority.CompareTo(x.priority); // Yüksek öncelik önce
            if (priorityCompare != 0) return priorityCompare;
            return x.hit.distance.CompareTo(y.hit.distance); // Aynı öncelikte mesafeye göre
        });
        
        // İlk (en yüksek öncelikli) IInteractable'ı kullan
        var (selectedHit, selectedInteractable, _) = interactables[0];

        string prompt = selectedInteractable.GetInteractText();
        if (string.IsNullOrEmpty(prompt))
        {
            HidePrompt();
            return;
        }

        ShowPrompt(prompt);

        if (Input.GetKeyDown(KeyCode.E))
        {
            // Spawn timing/child ihtimali için son bir garanti
            if (myInventory == null)
                myInventory = GetComponentInChildren<InventoryManager>(true);

            if (myInventory == null)
            {
                Debug.LogError("<color=red>[Interaction]</color> InventoryManager hâlâ yok. Player'a InventoryManager eklemen lazım.");
                return;
            }

            selectedInteractable.Interact(myInventory);
        }
    }

    /// <summary>
    /// Hedefle aramızdaki collider gerçek bir engel mi?
    /// Etkileşimsiz her collider (duvar, bina gövdesi) engeldir — hedefin ebeveyni olsa bile.
    /// Etkileşimli bir collider ise yalnızca hedefle ilişkiliyse (aynı obje ya da
    /// çekmece-kitap gibi ebeveyn/çocuk etkileşimlisi) engel sayılmaz.
    /// </summary>
    private static bool IsBlockingObstacle(Collider obstacle, Collider target, IInteractable targetInteractable)
    {
        if (obstacle == target)
            return false;

        IInteractable obstacleInteractable =
            obstacle.GetComponent<IInteractable>() ??
            obstacle.GetComponentInParent<IInteractable>();

        // Duvar/bina gibi etkileşimsiz collider'lar her zaman engeller.
        if (obstacleInteractable == null)
            return true;

        // Aynı etkileşimli objenin başka bir collider'ı.
        if (ReferenceEquals(obstacleInteractable, targetInteractable))
            return false;

        // Çekmece içindeki kitap gibi hiyerarşik olarak ilişkili etkileşimliler engellemez.
        if (obstacle.transform.IsChildOf(target.transform) || target.transform.IsChildOf(obstacle.transform))
            return false;

        return true;
    }
}
