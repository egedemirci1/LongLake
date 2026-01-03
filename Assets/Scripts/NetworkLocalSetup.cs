using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;
using StarterAssets;

public class NetworkLocalSetup : NetworkBehaviour
{
    [Header("Temel Bileþenler")]
    [SerializeField] private FirstPersonController firstPersonController;
    [SerializeField] private StarterAssetsInputs starterAssetsInputs;
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private PlayerInteraction playerInteraction; // Yeni eklediðimiz etkileþim scripti

    [Header("Kamera ve Model Ayarlarý")]
    [SerializeField] private CinemachineVirtualCamera vcam;
    [SerializeField] private GameObject modelParent; // Karakter modellerinin (Ahu/Yaman) ana objesi

    // Baþlangýç koordinatlarý (Zemine gömülmemesi için Y=102 yapýldý)
    private readonly Vector3 startPosition = new Vector3(1427.68f, 102.0f, 960.8365f);

    public override void OnNetworkSpawn()
    {
        // SAHÝBÝ DEÐÝLSEK: Kontrolleri kapat ve diðer oyuncuyu sadece izle
        if (!IsOwner)
        {
            DisableControls();
            return;
        }

        // --- 1. KARAKTERÝ IÞINLA ---
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false; // Iþýnlanma için CC geçici kapatýlýr
        transform.position = startPosition;
        if (cc != null) cc.enabled = true;

        // --- 2. MODELÝ MERKEZLE ---
        // Modellerin kapsül dýþýna kaymasýný önler
        if (modelParent != null)
        {
            modelParent.transform.localPosition = Vector3.zero;
            modelParent.transform.localRotation = Quaternion.identity;
        }

        // --- 3. KAMERA VE BAKIÞ SÝSTEMÝNÝ KUR ---
        if (firstPersonController != null && firstPersonController.CinemachineCameraTarget != null)
        {
            Transform cameraRoot = firstPersonController.CinemachineCameraTarget.transform;

            // Kafa objesini (Root) göz hizasýna al
            cameraRoot.SetParent(this.transform);
            cameraRoot.localPosition = new Vector3(0f, 1.375f, 0f);
            cameraRoot.localRotation = Quaternion.identity;

            // Sanal Kamerayý (VCAM) kafa içine hapset (Yukarý-aþaðý bakýþ için)
            if (vcam != null)
            {
                vcam.transform.SetParent(cameraRoot);
                vcam.transform.localPosition = Vector3.zero;
                vcam.transform.localRotation = Quaternion.identity;
                vcam.enabled = true;
                vcam.Priority = 100;
                vcam.Follow = cameraRoot;
                vcam.LookAt = cameraRoot;
            }
        }

        // --- 4. YEREL KONTROLLERÝ AKTÝF ET ---
        EnableControls();
    }

    private void EnableControls()
    {
        if (firstPersonController) firstPersonController.enabled = true;
        if (starterAssetsInputs) starterAssetsInputs.enabled = true;
        if (playerInput) playerInput.enabled = true;
        if (playerInteraction) playerInteraction.enabled = true; // Etkileþim ve UI metni aktif olur
    }

    private void DisableControls()
    {
        if (firstPersonController) firstPersonController.enabled = false;
        if (starterAssetsInputs) starterAssetsInputs.enabled = false;
        if (playerInput) playerInput.enabled = false;
        if (playerInteraction) playerInteraction.enabled = false; // Diðer oyuncularýn UI'ý görünmez
        if (vcam) vcam.enabled = false;
    }

    // Inspector'da sað týklayýp Reset derseniz bileþenleri otomatik bulur
    private void Reset()
    {
        firstPersonController = GetComponent<FirstPersonController>();
        starterAssetsInputs = GetComponent<StarterAssetsInputs>();
        playerInput = GetComponent<PlayerInput>();
        playerInteraction = GetComponent<PlayerInteraction>();
        vcam = GetComponentInChildren<CinemachineVirtualCamera>(true);
    }
}