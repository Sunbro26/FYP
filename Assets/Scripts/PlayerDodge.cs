using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDodge : MonoBehaviour
{
    [Header("Dodge Settings")]
    [Tooltip("How far the character will dodge.")]
    public float dodgeDistance = 5f;
    [Tooltip("The duration of the dodge. This should be slightly less than the animation's length.")]
    public float dodgeDuration = 0.6f;
    [Tooltip("How quickly the character turns to face the dodge direction.")]
    public float dodgeRotationSpeed = 15f;

    // References to other components
    private CharacterController _characterController;
    private Animator _animator;
    private Walk _walkScript;
    private PlayerAttack _attackScript;
    private Transform _cameraTransform;

    // State flag
    private bool _isDodging = false;

    // Animator trigger hash
    private static readonly int DodgeTrigger = Animator.StringToHash("Dodge");

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponentInChildren<Animator>();
        _walkScript = GetComponent<Walk>();
        _attackScript = GetComponent<PlayerAttack>();
        _cameraTransform = Camera.main.transform;
    }

    public void OnDodge(InputAction.CallbackContext context)
    {
        if (context.started && !_isDodging && (_attackScript == null || !_attackScript.IsAttacking()))
        {
            // --- THE FIX (PART 1) ---
            // Lock movement IMMEDIATELY to prevent the Walk script from causing a slide this frame.
            if (_walkScript != null) _walkScript.IsMovementLocked = true;

            // Now start the coroutine.
            StartCoroutine(DodgeSequence());
        }
    }

    private IEnumerator DodgeSequence()
    {
        _isDodging = true;

        // --- THE FIX (PART 2) ---
        // The movement lock is now handled in OnDodge(), so we can remove this line.
        // if (_walkScript != null) _walkScript.IsMovementLocked = true; 
        
        // We still disable attacking to prevent a dodge-attack combo.
        if (_attackScript != null) _attackScript.enabled = false;

        _animator.SetTrigger(DodgeTrigger);

        // --- All the dodge direction and movement logic remains the same ---
        Vector2 moveInput = _walkScript.GetMoveInput();
        Vector3 dodgeDirection;
        Vector3 cameraForward = new Vector3(_cameraTransform.forward.x, 0, _cameraTransform.forward.z).normalized;
        Vector3 cameraRight = new Vector3(_cameraTransform.right.x, 0, _cameraTransform.right.z).normalized;

        if (moveInput.magnitude > 0.1f)
        {
            dodgeDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;
        }
        else
        {
            dodgeDirection = cameraForward;
        }

        Quaternion targetRotation = Quaternion.LookRotation(dodgeDirection);

        float timer = 0f;
        while (timer < dodgeDuration)
        {
            float speed = dodgeDistance / dodgeDuration;
            _characterController.Move(dodgeDirection * speed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, dodgeRotationSpeed * Time.deltaTime);
            timer += Time.deltaTime;
            yield return null;
        }
        
        transform.rotation = targetRotation;

        // Unlock movement and re-enable attacking at the end.
        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        if (_attackScript != null) _attackScript.enabled = true;

        _isDodging = false;
    }

    // Public method for other scripts to check the dodge state
    public bool IsDodging()
    {
        return _isDodging;
    }
}