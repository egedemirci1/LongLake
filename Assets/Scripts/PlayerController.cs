using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.5f;
    public float gravity = -15f;
    public float speedChangeRate = 10f;

    [Header("Camera")]
    public Transform cameraTransform; // NetworkLocalSetup set ediyorsa kalsin
    public GameObject cinemachineCameraTarget;
    public float rotationSpeed = 2f;
    public float topClamp = 90.0f;
    public float bottomClamp = -90.0f;

    [Header("Ground Check")]
    public float groundedOffset = -0.1f;
    public float groundedRadius = 0.28f;
    public LayerMask groundLayers;

    [Header("Models & Character Selection")]
    public GameObject modelA;
    public GameObject modelB;

    // Animator
    private Animator _animator;

    // BlendTree param adi "Speed" olmali
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    // Netcode
    public NetworkVariable<int> characterIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    // Remote yurume/kosma icin speed sync
    public NetworkVariable<float> netSpeed = new NetworkVariable<float>(
        0f,
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
        UpdateCharacterModel(characterIndex.Value);
    }

    public override void OnNetworkSpawn()
    {
        characterIndex.OnValueChanged += OnCharacterIndexChanged;
        netSpeed.OnValueChanged += OnNetSpeedChanged;

        UpdateCharacterModel(characterIndex.Value);

        // Remote tarafta ilk frame idle takilmasin
        if (!IsOwner && _animator != null)
            _animator.SetFloat(SpeedHash, netSpeed.Value);
    }

    public override void OnNetworkDespawn()
    {
        characterIndex.OnValueChanged -= OnCharacterIndexChanged;
        netSpeed.OnValueChanged -= OnNetSpeedChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;

        GroundedCheck();
        JumpAndGravity();
        Move();
        if (IsOwner && Time.frameCount % 15 == 0) // ~ saniyede 3-4 kez
        {
            Debug.Log($"[SpeedDBG] _speed={_speed:F2} target={(Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed):F2} animSpeed={_animator.GetFloat("Speed"):F2}");
        }
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;
        CameraRotation();
    }

    private void GroundedCheck()
    {
        Vector3 spherePosition = new Vector3(
            transform.position.x,
            transform.position.y + groundedOffset,
            transform.position.z
        );

        _grounded = Physics.CheckSphere(
            spherePosition,
            groundedRadius,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );
    }

    private void CameraRotation()
    {
        if (cinemachineCameraTarget == null) return;

        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");

        _cinemachineTargetPitch -= mouseY * rotationSpeed;
        _cinemachineTargetPitch = Mathf.Clamp(_cinemachineTargetPitch, bottomClamp, topClamp);

        cinemachineCameraTarget.transform.localRotation =
            Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

        transform.Rotate(Vector3.up * mouseX * rotationSpeed);
    }

    private void Move()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        bool isSprinting = Input.GetKey(KeyCode.LeftShift);

        Vector3 inputDir = transform.right * h + transform.forward * v;

        float targetSpeed = (h == 0f && v == 0f) ? 0.0f : (isSprinting ? sprintSpeed : walkSpeed);
        _speed = Mathf.Lerp(_speed, targetSpeed, Time.deltaTime * speedChangeRate);

        Vector3 move = inputDir.normalized * (_speed * Time.deltaTime);
        move.y = _verticalVelocity * Time.deltaTime;

        _controller.Move(move);

        // Local anim
        if (_animator != null)
            _animator.SetFloat(SpeedHash, _speed);

        // Sync speed so others see walk/run
        netSpeed.Value = _speed;
    }

    private void JumpAndGravity()
    {
        if (_grounded)
        {
            if (_verticalVelocity < 0.0f) _verticalVelocity = -2f;

            if (Input.GetKeyDown(KeyCode.Space))
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        if (_verticalVelocity < 53f)
            _verticalVelocity += gravity * Time.deltaTime;
    }

    private void OnNetSpeedChanged(float oldVal, float newVal)
    {
        if (IsOwner) return;
        if (_animator != null)
            _animator.SetFloat(SpeedHash, newVal);
    }

    private void OnCharacterIndexChanged(int oldVal, int newVal)
    {
        UpdateCharacterModel(newVal);
    }

    private void UpdateCharacterModel(int index)
    {
        if (modelA != null) modelA.SetActive(index == 0);
        if (modelB != null) modelB.SetActive(index == 1);

        GameObject activeModel = (index == 0) ? modelA : modelB;
        _animator = (activeModel != null) ? activeModel.GetComponent<Animator>() : null;

        if (_animator != null)
            _animator.SetFloat(SpeedHash, netSpeed.Value);
    }

    // UI'dan cagirilir
    public void SelectCharacter(int index)
    {
        if (!IsOwner) return;
        characterIndex.Value = index;
    }
}
