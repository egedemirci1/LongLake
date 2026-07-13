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

    // Netcode — -1 = henüz seçilmedi; server yazar (aynı karakter iki kez alınmasın)
    public NetworkVariable<int> characterIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>Local owner: (success, characterIndex). Fired after server accepts/rejects select.</summary>
    public static event System.Action<bool, int> OnLocalCharacterSelectResult;

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

        // Envanter açıksa kamera dönmesin
        InventoryManager inventoryManager = GetComponent<InventoryManager>();
        if (inventoryManager != null && inventoryManager.mainInventoryObject != null && inventoryManager.mainInventoryObject.activeSelf)
        {
            return;
        }

        // Görev paneli açıksa kamera dönmesin (shared QuestManager)
        if (QuestManager.Instance != null && QuestManager.Instance.IsPanelOpen)
        {
            return;
        }
        
        // Not defteri açıksa kamera dönmesin
        NotebookUI notebookUI = FindFirstObjectByType<NotebookUI>();
        if (notebookUI != null && notebookUI.IsNotebookOpen)
        {
            return;
        }

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

        GameObject activeModel = (index == 0) ? modelA : (index == 1 ? modelB : null);
        _animator = (activeModel != null) ? activeModel.GetComponent<Animator>() : null;

        if (_animator != null)
            _animator.SetFloat(SpeedHash, netSpeed.Value);
    }

    public bool HasSelectedCharacter => characterIndex.Value >= 0;

    /// <summary>True if any spawned player already owns this character index (selected = value &gt;= 0).</summary>
    public static bool IsCharacterIndexTaken(int index, ulong exceptClientId = ulong.MaxValue)
    {
        if (index < 0) return false;
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
            return false;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId == exceptClientId) continue;

            var playerObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            if (playerObject == null) continue;

            var pc = playerObject.GetComponent<PlayerController>();
            // -1 = henüz seçilmedi; sadece gerçek seçimler "taken" sayılır
            if (pc != null && pc.characterIndex.Value >= 0 && pc.characterIndex.Value == index)
                return true;
        }

        return false;
    }

    // UI'dan cagirilir — server onaylar
    public void SelectCharacter(int index)
    {
        if (!IsOwner) return;
        if (index < 0 || index > 1) return;
        RequestSelectCharacterServerRpc(index);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestSelectCharacterServerRpc(int index)
    {
        if (index < 0 || index > 1) return;

        // Already selected this character — treat as success (idempotent)
        if (characterIndex.Value == index)
        {
            NotifySelectResultClientRpc(true, index);
            return;
        }

        if (IsCharacterIndexTaken(index, OwnerClientId))
        {
            Debug.LogWarning($"[PlayerController] Character {index} already taken. Denied for client {OwnerClientId}.");
            NotifySelectResultClientRpc(false, index);
            return;
        }

        characterIndex.Value = index;
        NotifySelectResultClientRpc(true, index);
    }

    [Rpc(SendTo.Owner)]
    private void NotifySelectResultClientRpc(bool success, int index)
    {
        OnLocalCharacterSelectResult?.Invoke(success, index);
    }
}
