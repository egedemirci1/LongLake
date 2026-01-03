using UnityEngine;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
<<<<<<< Updated upstream
    public float speed = 5f;
    public float gravity = -9.81f;
    public Transform cameraTransform;

    private CharacterController controller;
    private Vector3 velocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
=======
    [Header("Models & Character Selection")]
    public GameObject ahuModel;
    public GameObject yamanModel;

    public NetworkVariable<int> characterIndex = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        characterIndex.OnValueChanged += (_, newVal) => UpdateCharacterModel(newVal);
        UpdateCharacterModel(characterIndex.Value);
>>>>>>> Stashed changes
    }

    private void UpdateCharacterModel(int index)
    {
<<<<<<< Updated upstream
        float h = Input.GetAxis("Horizontal");   // A-D
        float v = Input.GetAxis("Vertical");     // W-S

        Vector3 inputDir = new Vector3(h, 0f, v).normalized;

        // Kameraya göre yön hesaplama
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;

        camForward.y = 0f;
        camRight.y = 0f;

        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDir = camForward * v + camRight * h;

        if (moveDir.sqrMagnitude > 0.01f)
        {
            // Karakteri hareket yönüne döndür
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(moveDir),
                Time.deltaTime * 10f);

            controller.Move(moveDir * speed * Time.deltaTime);
        }

        // Yer çekimi
        if (controller.isGrounded && velocity.y < 0)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
=======
        if (ahuModel) ahuModel.SetActive(index == 0);
        if (yamanModel) yamanModel.SetActive(index == 1);
    }

    public void SelectCharacter(int index)
    {
        if (IsOwner) characterIndex.Value = index;
>>>>>>> Stashed changes
    }
}
