using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Settings")]
    [SerializeField] private float interactDistance = 5f;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    // Bu mask, oyuncunun kendi layer'ýný hariç tutar
    private int interactionMask;

    private void Awake()
    {
        // Kamera atanmadýysa, önce child'larda ara, sonra Camera.main dene
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();

            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        if (playerCamera == null)
        {
            Debug.LogWarning($"{nameof(PlayerInteraction)} on {name}: Kamera bulunamadý. Inspector'dan atamayý unutma.");
        }

        // Sadece kendi layer'ýný ignore et, diðer tüm layer'lar raycast'e dahil
        interactionMask = ~(1 << gameObject.layer);
    }

    private void Update()
    {
        if (Input.GetKeyDown(interactKey))
        {
            TryInteract();
        }
    }

    private void TryInteract()
    {
        if (playerCamera == null)
        {
            // Awake'de de uyarý veriyoruz ama burasý ekstra güvenlik
            Debug.LogWarning($"{nameof(PlayerInteraction)} on {name}: playerCamera atanmadýðý için etkileþim çalýþmadý.");
            return;
        }

        // Ekranýn tam ortasýndan ray at
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        // Ýstersen debug için açabilirsin:
        // Debug.DrawRay(ray.origin, ray.direction * interactDistance, Color.red, 1f);

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactionMask, QueryTriggerInteraction.Collide))
        {
            // Vurduðu objede PickupItem var mý diye bak
            PickupItem pickup = hit.collider.GetComponent<PickupItem>();

            if (pickup != null)
            {
                pickup.OnPickup();
            }
            // Ýstersen burada baþka interface/interaction tiplerini de kontrol edebilirsin
        }
    }
}
