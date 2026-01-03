using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components; // NetworkAnimator için gerekli

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.5f;
    public float gravity = -15f;
    public float rotationSpeed = 2f;

    public Transform cameraTransform;

    [Header("Cinemachine")]
    public GameObject cinemachineCameraTarget;
    public float topClamp = 90.0f;
    public float bottomClamp = -90.0f;

    [Header("Ground Check")]
    public float groundedOffset = -0.1f;
    public float groundedRadius = 0.28f;
    public LayerMask groundLayers;

    [Header("Models & Character Selection")]
    public GameObject ahuModel;
    public GameObject yamanModel;

    // Animasyon Senkronizasyonu
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public NetworkVariable<int> characterIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    private CharacterController _controller;
    private float _cinemachineTargetPitch;
    private float _verticalVelocity;
    private bool _grounded;
    private float _speed;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        UpdateCharacterModel(characterIndex.Value);
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

    private void OnCharacterIndexChanged(int oldVal, int newVal) => UpdateCharacterModel(newVal);

    private void Update()
    {
        if (!IsOwner) return;

        GroundedCheck();
        JumpAndGravity();
        Move();
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;
        CameraRotation();
    }

    private void GroundedCheck()
    {
        Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y + groundedOffset, transform.position.z);
        _grounded = Physics.CheckSphere(spherePosition, groundedRadius, groundLayers, QueryTriggerInteraction.Ignore);
    }

    private void CameraRotation()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        if (cinemachineCameraTarget != null)
        {
            _cinemachineTargetPitch -= mouseY * rotationSpeed;
            _cinemachineTargetPitch = Mathf.Clamp(_cinemachineTargetPitch, bottomClamp, topClamp);

            cinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);
            transform.Rotate(Vector3.up * mouseX * rotationSpeed);
        }
    }

    private void Move()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        // Hareket girdisi varsa hýzý belirle, yoksa 0
        float targetSpeed = (h == 0 && v == 0) ? 0.0f : (isSprinting ? sprintSpeed : walkSpeed);
        _speed = Mathf.Lerp(_speed, targetSpeed, Time.deltaTime * 10f);

        Vector3 inputDir = transform.right * h + transform.forward * v;
        Vector3 finalMove = inputDir.normalized * (_speed * Time.deltaTime);
        finalMove.y = _verticalVelocity * Time.deltaTime;

        _controller.Move(finalMove);

        // --- ANIMASYON GÜNCELLEME ---
        if (_animator != null)
        {
            // Hareket girdisi yoksa animasyon hýzýný hemen 0'a çekmek daha temiz durur
            float animValue = (h == 0 && v == 0) ? 0f : _speed;
            _animator.SetFloat(SpeedHash, animValue);
        }
    }

    private void JumpAndGravity()
    {
        if (_grounded)
        {
            if (_verticalVelocity < 0.0f) _verticalVelocity = -2f;
            if (Input.GetKeyDown(KeyCode.Space))
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        if (_verticalVelocity < 53f) _verticalVelocity += gravity * Time.deltaTime;
    }

    private void UpdateCharacterModel(int index)
    {
        if (ahuModel) ahuModel.SetActive(index == 0);
        if (yamanModel) yamanModel.SetActive(index == 1);

        // Aktif olan modeldeki Animator'ý bul ve NetworkAnimator'a tanýt
        _animator = GetComponentInChildren<Animator>();

        // Eðer NetworkAnimator varsa, runtime'da doðru animatörü besle
        if (_networkAnimator != null && _animator != null)
        {
            _networkAnimator.Animator = _animator;
        }
    }

    public void SelectCharacter(int index)
    {
        if (IsSpawned && IsOwner)
            characterIndex.Value = index;
    }
}