using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Player")]
        public float MoveSpeed = 4.0f;
        public float SprintSpeed = 6.0f;
        public float RotationSpeed = 1.0f;
        public float SpeedChangeRate = 10.0f;

        [Header("Jump & Gravity")]
        public float JumpHeight = 1.6f;          // metre
        public float Gravity = -15.0f;           // -9.81 de olur, biraz daha “oyun gibi” için -15 iyi
        public float GroundedStick = -2.0f;      // yerde yapışma

        [Header("Ground Check")]
        public float GroundedOffset = -0.1f;
        public float GroundedRadius = 0.28f;
        public LayerMask GroundLayers;

        [Header("Cinemachine")]
        public GameObject CinemachineCameraTarget;
        public float TopClamp = 90.0f;
        public float BottomClamp = -90.0f;

        // --- ANIMASYON ---
        private Animator _animator;
        private bool _hasAnimator;
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDVerticalVel;
        private int _animIDJump;

        private float _cinemachineTargetPitch;

        private float _speed;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private const float _terminalVelocity = 53.0f;

        private bool _grounded;

        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private const float _threshold = 0.01f;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
        private bool IsCurrentDeviceMouse => _playerInput.currentControlScheme == "KeyboardMouse";
#endif

        private void Start()
        {
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();

#if ENABLE_INPUT_SYSTEM
            _playerInput = GetComponent<PlayerInput>();
#endif

            _animator = GetComponentInChildren<Animator>();
            _hasAnimator = _animator != null;

            if (_hasAnimator)
            {
                _animIDSpeed = Animator.StringToHash("Speed");
                _animIDGrounded = Animator.StringToHash("IsGrounded");
                _animIDVerticalVel = Animator.StringToHash("VerticalVelocity");
                _animIDJump = Animator.StringToHash("Jump");
            }
        }

        private void Update()
        {
            GroundedCheck();
            JumpAndGravity();
            Move();
        }

        private void LateUpdate()
        {
            CameraRotation();
        }

        private void GroundedCheck()
        {
            // CharacterController'ın altından sphere check
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y + GroundedOffset, transform.position.z);
            _grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

            if (_hasAnimator)
                _animator.SetBool(_animIDGrounded, _grounded);
        }

        private void JumpAndGravity()
        {
            if (_grounded)
            {
                // yerdeyken düşüş hızını sabitle
                if (_verticalVelocity < 0.0f)
                    _verticalVelocity = GroundedStick;

                // Jump (consume mantığı ile)
                if (_input.jump)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    if (_hasAnimator)
                        _animator.SetTrigger(_animIDJump);

                    _input.ConsumeJump();
                }
            }

            // Terminal velocity clamp
            if (_verticalVelocity < _terminalVelocity)
                _verticalVelocity += Gravity * Time.deltaTime;

            if (_hasAnimator)
                _animator.SetFloat(_animIDVerticalVel, _verticalVelocity);
        }

        private void CameraRotation()
        {
            if (_input.look.sqrMagnitude >= _threshold)
            {
#if ENABLE_INPUT_SYSTEM
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
#else
                float deltaTimeMultiplier = Time.deltaTime;
#endif
                _cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
                _rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

                _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

                CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);
                transform.Rotate(Vector3.up * _rotationVelocity);
            }
        }

        private void Move()
        {
            float targetSpeed = (_input.move == Vector2.zero) ? 0.0f : (_input.sprint ? SprintSpeed : MoveSpeed);

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            if (currentHorizontalSpeed < targetSpeed - 0.1f || currentHorizontalSpeed > targetSpeed + 0.1f)
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed, Time.deltaTime * SpeedChangeRate);
            else
                _speed = targetSpeed;

            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            if (_input.move != Vector2.zero)
                inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;

            // Final move: yatay + dikey
            Vector3 move = inputDirection.normalized * (_speed * Time.deltaTime);
            move.y = _verticalVelocity * Time.deltaTime;

            _controller.Move(move);

            if (_hasAnimator)
                _animator.SetFloat(_animIDSpeed, _speed);
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            // Ground check görsün diye
            Gizmos.DrawWireSphere(new Vector3(transform.position.x, transform.position.y + GroundedOffset, transform.position.z), GroundedRadius);
        }
    }
}
