using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveCombatFramework {
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Framework/Player/Basic Movement")]
    public class Walk : MonoBehaviour
    {
        [Header("Movement Stats")]
        [Tooltip("Linear walking speed in meters per second.")]
        public float speed = 5f;
        [Tooltip("Rotation speed when not locked on.")]
        public float rotationSpeed = 720f;

        [Header("State Control")]
        [Tooltip("Used by combat modules to freeze movement during animations.")]
        public bool IsMovementLocked { get; set; }

        private CharacterController characterController;
        private Animator animator;
        private LockOn lockOn;
        private CharacterStats _stats;
        private Transform cameraTransform;
        private Vector2 moveInput;

        private static readonly int MovementDirection = Animator.StringToHash("MovementDirection");
        private static readonly int MovementX = Animator.StringToHash("MovementX");

        void Start()
        {
            characterController = GetComponent<CharacterController>();
            animator = GetComponentInChildren<Animator>();
            cameraTransform = Camera.main.transform;
            lockOn = GetComponent<LockOn>();
            _stats = GetComponent<CharacterStats>();
            IsMovementLocked = false;
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            moveInput = context.ReadValue<Vector2>();
        }

        void Update()
        {
            if (_stats != null && _stats.IsDead) return;

            if (IsMovementLocked)
            {
                animator.SetFloat(MovementDirection, 0f, 0.1f, Time.deltaTime);
                animator.SetFloat(MovementX, moveInput.x, 0.1f, Time.deltaTime);
                return;
            }

            Vector3 cameraForward = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z).normalized;
            Vector3 cameraRight = new Vector3(cameraTransform.right.x, 0, cameraTransform.right.z).normalized;
            Vector3 moveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;

            characterController.Move(moveDirection * speed * Time.deltaTime);

            if (!lockOn.isLockedOn)
            {
                if (moveInput != Vector2.zero)
                {
                    Vector3 lookDir = moveInput.y < -0.1f ? -cameraForward : moveDirection;
                    Quaternion targetRotation = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(cameraForward), rotationSpeed * Time.deltaTime);
            }

            UpdateAnimation();
        }

        private void UpdateAnimation()
        {
            if (animator == null) return;
            animator.SetFloat(MovementDirection, moveInput.y, 0.1f, Time.deltaTime);
            animator.SetFloat(MovementX, moveInput.x, 0.1f, Time.deltaTime);
        }

        public Vector2 GetMoveInput() => moveInput;
        public void SetInput(Vector2 input) => moveInput = input;
    }
}