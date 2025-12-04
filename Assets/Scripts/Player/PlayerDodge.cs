using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class PlayerDodge : MonoBehaviour
{
    public static event Action OnPlayerDodge;

    [Header("Dodge Settings")]
    public float dodgeDistance = 5f;
    public float dodgeDuration = 0.6f;
    public float dodgeRotationSpeed = 15f;

    [Header("I-Frame Settings")]
    [Tooltip("How long to wait after dodge starts before invincibility kicks in.")]
    public float iFrameStartDelay = 0.2f; 
    [Tooltip("How long the player stays invincible.")]
    public float iFrameDuration = 0.3f;

    // Public property for other scripts to check
    public bool IsInvincible { get; private set; }

    // References
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

    public bool IsDodging()
    {
        return _isDodging;
    }

    public void OnDodge(InputAction.CallbackContext context)
    {
        if (context.started && !_isDodging && (_attackScript == null || !_attackScript.IsAttacking()))
        {
            if (_walkScript != null) _walkScript.IsMovementLocked = true;
            StartCoroutine(DodgeSequence());
        }
    }

    private IEnumerator DodgeSequence()
    {
        _isDodging = true;
        
        if (_attackScript != null) _attackScript.enabled = false;

        _animator.SetTrigger(DodgeTrigger);

        OnPlayerDodge?.Invoke(); 

        // --- I-FRAME LOGIC STARTS HERE ---
        // We start the I-Frame timer in parallel so it doesn't stop the movement loop
        StartCoroutine(HandleIFrames());

        // --- MOVEMENT CALCULATION ---
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

        // --- MOVEMENT LOOP ---
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

        // Safety reset to ensure player isn't invincible if logic desyncs
        IsInvincible = false;

        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        if (_attackScript != null) _attackScript.enabled = true;

        _isDodging = false;
    }

    // This runs purely to toggle the boolean at the right times
    private IEnumerator HandleIFrames()
    {
        // 1. Wait for the start delay (vulnerability at start of dodge)
        yield return new WaitForSeconds(iFrameStartDelay);

        // 2. Turn on Invincibility
        IsInvincible = true;

        // 3. Wait for the i-frame duration
        yield return new WaitForSeconds(iFrameDuration);

        // 4. Turn off Invincibility (vulnerability at end of dodge)
        IsInvincible = false;
    }
}