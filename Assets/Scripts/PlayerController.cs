using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float rotationSpeed = 10f;
    public Transform cameraTransform;

    [Header("Jump & Gravity")]
    public float gravity = -9.81f;
    public float jumpHeight = 1.6f; // metre cinsinden zýplama yüksekliði
    public float groundedStickForce = -2f; // yerde "yapýþma" için

    [Header("Animation (Optional)")]
    public Animator animator;

    private CharacterController controller;
    private Vector3 velocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Eðer elle atamadýysan otomatik bulmaya çalýþsýn
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // --- Ground check ---
        bool isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0f)
            velocity.y = groundedStickForce;

        // --- Input ---
        float h = Input.GetAxis("Horizontal");   // A-D
        float v = Input.GetAxis("Vertical");     // W-S

        // Kamera bazlý yön
        Vector3 camForward = cameraTransform.forward;
        Vector3 camRight = cameraTransform.right;

        camForward.y = 0f;
        camRight.y = 0f;

        camForward.Normalize();
        camRight.Normalize();

        Vector3 moveDir = (camForward * v + camRight * h);
        float moveMagnitude = Mathf.Clamp01(moveDir.magnitude);
        if (moveDir.sqrMagnitude > 0.001f)
            moveDir.Normalize();

        // --- Move & Rotate ---
        if (moveMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
        }

        controller.Move(moveDir * (speed * moveMagnitude) * Time.deltaTime);

        // --- Jump ---
        if (isGrounded && Input.GetButtonDown("Jump")) // default: Space
        {
            // jumpHeight kadar zýplatacak ilk hýz:
            // v = sqrt(h * -2 * g)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            // Anim trigger
            if (animator != null)
                animator.SetTrigger("Jump");
        }

        // --- Gravity ---
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // --- Animator params (recommended) ---
        if (animator != null)
        {
            animator.SetFloat("Speed", moveMagnitude);           // 0-1
            animator.SetBool("IsGrounded", controller.isGrounded);
            animator.SetFloat("VerticalVelocity", velocity.y);   // düþüþ/zýplayýþ blend için iyi
        }
    }
}
