using System.Collections;
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
            Debug.LogError("<color=red>[Interaction]</color> InventoryManager bulunamadý! " +
                           "Player prefab/root veya child objelerinde InventoryManager var mý?");
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
            Debug.LogError("<color=red>[Interaction]</color> UI bulunamadý!");
            yield break;
        }

        Debug.Log("<color=green>[Interaction]</color> UI baðlandý -> " + interactionText.gameObject.name);
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

        // 2) InteractionText adlý
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

        // 3) InteractionUI tag altý
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

        if (Physics.Raycast(origin, direction, out RaycastHit hit, interactDistance, layerMask))
        {
            IInteractable interactable =
                hit.collider.GetComponent<IInteractable>() ??
                hit.collider.GetComponentInParent<IInteractable>();

            if (interactable != null)
            {
                interactionText.gameObject.SetActive(true);
                interactionText.text = "[E] " + interactable.GetInteractText();

                if (Input.GetKeyDown(KeyCode.E))
                {
                    // Spawn timing/child ihtimali için son bir garanti
                    if (myInventory == null)
                        myInventory = GetComponentInChildren<InventoryManager>(true);

                    if (myInventory == null)
                    {
                        Debug.LogError("<color=red>[Interaction]</color> InventoryManager hâlâ yok. Player'a InventoryManager eklemen lazým.");
                        return;
                    }

                    interactable.Interact(myInventory);
                }
                return;
            }
        }

        if (interactionText.gameObject.activeSelf)
            interactionText.gameObject.SetActive(false);
    }
}
