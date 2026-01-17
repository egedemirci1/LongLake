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

        Debug.Log("<color=green>[Interaction]</color> UI bağlandı -> " + interactionText.gameObject.name);
        interactionText.text = "";
        interactionText.gameObject.SetActive(false);
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
    }

    private void CheckInteraction(Vector3 origin, Vector3 direction)
    {
        int layerMask = ~(1 << gameObject.layer);

        // RaycastAll kullan - tüm collider'ları kontrol et (çekmece + içindeki kitap için)
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, interactDistance, layerMask);
        
        if (hits.Length == 0)
        {
            if (interactionText.gameObject.activeSelf)
                interactionText.gameObject.SetActive(false);
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
            if (interactionText.gameObject.activeSelf)
                interactionText.gameObject.SetActive(false);
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
        
        interactionText.gameObject.SetActive(true);
        interactionText.text = "[E] " + selectedInteractable.GetInteractText();

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
}
