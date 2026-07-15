using UnityEngine;
using Unity.Netcode;

public enum CabinetType
{
    Slide,   // Push/pull (position change)
    Rotate   // Rotation (rotation change)
}

public enum SlideDirection
{
    Forward,   // Forward (transform.forward)
    Backward,  // Backward (-transform.forward)
    Right,     // Right (transform.right)
    Left,      // Left (-transform.right)
    Up,        // Up (transform.up)
    Down       // Down (-transform.up)
}

public class CabinetController : NetworkBehaviour, IInteractable
{
    [Header("Cabinet Type")]
    [SerializeField] private CabinetType cabinetType = CabinetType.Slide;

    [Header("Initial State")]
    [SerializeField] private bool startOpen = false;
    [SerializeField] private bool startLocked = false;
    public string requiredKeyName = "";

    [Header("Slide Settings (for Slide type)")]
    [SerializeField] private SlideDirection slideDirection = SlideDirection.Forward;
    [SerializeField] private float slideDistance = 0.5f;
    [SerializeField] private float slideSpeed = 2.0f;

    [Header("Rotate Settings (for Rotate type)")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up; // Which axis to rotate around
    [SerializeField] private float openRotation = 90.0f;
    [SerializeField] private float rotateSpeed = 5.0f;

    [Header("Collider Settings")]
    [SerializeField] private Collider interactionCollider; // Auto-found if null

    // Network-synchronized states
    private NetworkVariable<bool> IsOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> IsLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // For slide: store initial position
    private Vector3 closedPosition;
    private Vector3 openPosition;

    // For rotate: store initial rotation
    private Quaternion closedRotation;
    private Quaternion openRotationQuat;

    private void Awake()
    {
        // Store initial positions/rotations
        closedPosition = transform.localPosition;
        closedRotation = transform.localRotation;

        // Calculate open position for slide
        Vector3 direction = GetSlideDirection();
        openPosition = closedPosition + direction * slideDistance;

        // Calculate open rotation for rotate
        openRotationQuat = closedRotation * Quaternion.AngleAxis(openRotation, rotationAxis);

        // Find collider if not assigned
        if (interactionCollider == null)
        {
            interactionCollider = GetComponent<Collider>();
            if (interactionCollider == null)
            {
                interactionCollider = GetComponentInChildren<Collider>();
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        // Server sets initial states
        if (IsServer)
        {
            IsOpen.Value = startOpen;
            IsLocked.Value = startLocked;
        }
    }

    private void Update()
    {
        // All clients animate based on network state
        if (cabinetType == CabinetType.Slide)
        {
            Vector3 targetPosition = IsOpen.Value ? openPosition : closedPosition;
            transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, Time.deltaTime * slideSpeed);
        }
        else // Rotate
        {
            Quaternion targetRotation = IsOpen.Value ? openRotationQuat : closedRotation;
            transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, Time.deltaTime * rotateSpeed);
        }
    }

    private Vector3 GetSlideDirection()
    {
        // Local space direction (transform.localPosition kullandığımız için local space'de kalmalı)
        // Sadece Z ekseninde hareket (X ve Y bileşenleri 0) - çapraz hareket olmaz
        switch (slideDirection)
        {
            case SlideDirection.Forward:
                // Local Z+ ekseni (mavi ok yönü)
                return new Vector3(0, 0, 1);
            case SlideDirection.Backward:
                // Local Z- ekseni (mavi okun tersi)
                return new Vector3(0, 0, -1);
            case SlideDirection.Right:
                // Local X+ ekseni (kırmızı ok yönü)
                return new Vector3(1, 0, 0);
            case SlideDirection.Left:
                // Local X- ekseni (kırmızı okun tersi)
                return new Vector3(-1, 0, 0);
            case SlideDirection.Up:
                // Local Y+ ekseni (yeşil ok yönü)
                return new Vector3(0, 1, 0);
            case SlideDirection.Down:
                // Local Y- ekseni (yeşil okun tersi)
                return new Vector3(0, -1, 0);
            default:
                return new Vector3(0, 0, 1);
        }
    }

    // IInteractable
    public void Interact(InventoryManager interactorInventory)
    {
        if (!IsSpawned) return;

        bool hasKey = string.IsNullOrEmpty(requiredKeyName) || 
                     (interactorInventory != null && interactorInventory.HasKey(requiredKeyName));
        
        TryToggleCabinetServerRpc(hasKey);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TryToggleCabinetServerRpc(bool clientClaimsHasKey)
    {
        // If locked, check key
        if (IsLocked.Value)
        {
            if (clientClaimsHasKey)
            {
                IsLocked.Value = false;
                IsOpen.Value = true;
                Debug.Log($"<b>[CABINET]</b> {requiredKeyName} used, cabinet opened! (Server)");
            }
            else
            {
                Debug.Log($"<b>[CABINET]</b> Locked! Required key: {requiredKeyName} (Server)");
            }
            return;
        }

        // If not locked, toggle
        IsOpen.Value = !IsOpen.Value;
    }

    public string GetInteractText()
    {
        if (IsLocked.Value && !string.IsNullOrEmpty(requiredKeyName))
            return $"Çekmece Kilitli ({requiredKeyName} Gerekli)";
        
        return IsOpen.Value ? "Çekmeceyi Kapat" : "Çekmeceyi Aç";
    }
    
    // Public getter for IsOpen state (for other scripts to check)
    public bool IsOpenState => IsOpen.Value;
}

