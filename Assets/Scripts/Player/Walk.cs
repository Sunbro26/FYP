using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class Walk : MonoBehaviour
{
    private CharacterStats _stats;

    public float speed = 5f;
    public float rotationSpeed = 720f;

    // --- NEW: This property lets other scripts lock/unlock movement ---
    public bool IsMovementLocked { get; set; }

    private CharacterController characterController;
    private Animator animator;
    private LockOn lockOn;
    private Vector2 moveInput;
    private Transform cameraTransform;

    private static readonly int MovementDirection = Animator.StringToHash("MovementDirection");
    private static readonly int MovementX = Animator.StringToHash("MovementX");


    void Start()
    {
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        cameraTransform = Camera.main.transform;
        IsMovementLocked = false; // Player can move at the start
        lockOn = GetComponent<LockOn>();
        _stats = GetComponent<CharacterStats>();
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    void Update()
    {
        if (_stats != null && _stats.IsDead) return;
        // --- NEW: If movement is locked, do nothing and exit the Update loop early ---
        if (IsMovementLocked)
        {
            // Set animator values to idle when locked to prevent sliding animation
            animator.SetFloat(MovementDirection, 0f, 0.1f, Time.deltaTime);
            animator.SetFloat(MovementX, moveInput.x, 0.1f, Time.deltaTime); // X axis
            return;
        }

        Vector3 cameraForward = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z).normalized;
        Vector3 cameraRight = new Vector3(cameraTransform.right.x, 0, cameraTransform.right.z).normalized;
        Vector3 moveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;

        characterController.Move(moveDirection * speed * Time.deltaTime);
        Quaternion targetRotation;
        if (!lockOn.isLockedOn)
        {
        if (moveInput != Vector2.zero)
        {
            if (Mathf.Abs(moveInput.y) < -0.1f)
                targetRotation = Quaternion.LookRotation(-cameraForward);
            else
                targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
        }
        else
        {
            targetRotation = cameraTransform.rotation;
        }
        UpdateAnimation();
    }

private void UpdateAnimation()
{
    if (animator == null) return;

    // ----- Z (forward/back) -----
    if (moveInput.y < -0.1f)
        animator.SetFloat(MovementDirection, -1f, 0.1f, Time.deltaTime);
    else if (moveInput.y > 0.1f)
        animator.SetFloat(MovementDirection, 1f, 0.1f, Time.deltaTime);
    else
        animator.SetFloat(MovementDirection, 0f, 0.1f, Time.deltaTime);

    if (moveInput.x < -0.1f)
        animator.SetFloat(MovementX, -1f, 0.1f, Time.deltaTime);
    else if (moveInput.x > 0.1f)
        animator.SetFloat(MovementX, 1f, 0.1f, Time.deltaTime);
    else
        animator.SetFloat(MovementX, 0f, 0.1f, Time.deltaTime);
}


    public Vector2 GetMoveInput()
    {
        return moveInput;
    }
    public void SetInput(Vector2 input)
{
    moveInput = input; // Use this instead of reading Context in Update
}

}