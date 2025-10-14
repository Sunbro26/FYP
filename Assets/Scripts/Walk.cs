using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class Walk : MonoBehaviour
{
    public float speed = 5f;
    public float rotationSpeed = 720f;

    private CharacterController characterController;
    private Animator animator;
    private Vector2 moveInput;

    // A unique ID for the "MovementDirection" parameter to improve performance.
    private static readonly int MovementDirection = Animator.StringToHash("MovementDirection");

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
    }

    // This method is called by the Player Input component when the "Move" action is triggered.
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    void Update()
    {
        // We only care about forward/backward input for movement.
        float verticalInput = moveInput.y;

        // The move direction is always based on the character's forward vector.
        Vector3 moveDirection = transform.forward * verticalInput;

        // Move the character.
        characterController.Move(moveDirection * speed * Time.deltaTime);

        // Handle rotation using the horizontal input.
        float horizontalInput = moveInput.x;
        transform.Rotate(Vector3.up, horizontalInput * rotationSpeed * Time.deltaTime);

        // Update the Animator.
        UpdateAnimation(verticalInput);
    }

    private void UpdateAnimation(float verticalInput)
    {
        if (animator == null) return;

        // Determine the direction and set the parameter.
        if (verticalInput > 0.1f)
        {
            // Moving forward
            animator.SetFloat(MovementDirection, 1f, 0.1f, Time.deltaTime);
        }
        else if (verticalInput < -0.1f)
        {
            // Moving backward
            animator.SetFloat(MovementDirection, -1f, 0.1f, Time.deltaTime);
        }
        else
        {
            // Not moving (Idle)
            animator.SetFloat(MovementDirection, 0f, 0.1f, Time.deltaTime);
        }
    }
}