using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float gravity = -9.81f;
    public Transform cameraTransform;

    [Header("Models & Character Selection")]
    public GameObject ahuModel;
    public GameObject yamanModel;

    // Owner seçer, herkes görür
    public NetworkVariable<int> characterIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private CharacterController controller;
    private Vector3 velocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        characterIndex.OnValueChanged += OnCharacterIndexChanged;
        UpdateCharacterModel(characterIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        characterIndex.OnValueChanged -= OnCharacterIndexChanged;
    }

    private void OnCharacterIndexChanged(int oldVal, int newVal)
    {
        UpdateCharacterModel(newVal);
    }

    private void Update()
    {
        // Sadece local player input okusun
        if (!IsOwner) return;

        if (controller == null) controller = GetComponent<CharacterController>();

        float h = Input.GetAxis("Horizontal");   // A-D
        float v = Input.GetAxis("Vertical");     // W-S

        Vector3 moveDir;

        if (cameraTransform != null)
        {
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;

            camForward.y = 0f;
            camRight.y = 0f;

            camForward.Normalize();
            camRight.Normalize();

            moveDir = (camForward * v + camRight * h);
        }
        else
        {
            // Kamera atanmamýþsa dünya ekseninde yürü
            moveDir = new Vector3(h, 0f, v);
        }

        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(moveDir),
                Time.deltaTime * 10f
            );

            controller.Move(moveDir * speed * Time.deltaTime);
        }

        ApplyGravity();
    }

    private void ApplyGravity()
    {
        if (controller.isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void UpdateCharacterModel(int index)
    {
        if (ahuModel) ahuModel.SetActive(index == 0);
        if (yamanModel) yamanModel.SetActive(index == 1);
    }

    public void SelectCharacter(int index)
    {
        if (IsOwner)
            characterIndex.Value = index;
    }
}
