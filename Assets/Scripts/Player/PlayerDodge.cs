using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class PlayerDodge : MonoBehaviour
{
    // --- EVENTS ---
    public static event Action OnDodgeAttempt; // Fired when button pressed
    public static event Action OnDodgeSuccess; // Fired when I-Frame absorbs a hit

    [Header("Dodge Settings")]
    public float dodgeDistance = 5f;
    public float dodgeDuration = 0.6f;
    public float dodgeRotationSpeed = 15f;

    [Header("I-Frame Settings")]
    public float iFrameStartDelay = 0.2f; 
    public float iFrameDuration = 0.3f;

    public bool IsInvincible { get; private set; }

    // References
    private CharacterController _characterController;
    private Animator _animator;
    private Walk _walkScript;
    private PlayerAttack _attackScript;
    private Transform _cameraTransform;

    private bool _isDodging = false;
    private static readonly int DodgeTrigger = Animator.StringToHash("Dodge");

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponentInChildren<Animator>();
        _walkScript = GetComponent<Walk>();
        _attackScript = GetComponent<PlayerAttack>();
        _cameraTransform = Camera.main.transform;
    }

    public bool IsDodging() => _isDodging;

    public void OnDodge(InputAction.CallbackContext context)
    {
        if (context.started && !_isDodging && (_attackScript == null || !_attackScript.IsAttacking()))
        {
            // Check stamina here if you have it
            // if (!_stats.UseStamina(cost)) return;

            if (_walkScript != null) _walkScript.IsMovementLocked = true;
            
            // Fire Attempt Telemetry
            OnDodgeAttempt?.Invoke();
            
            StartCoroutine(DodgeSequence());
        }
    }

    // --- NEW: CALL THIS FROM YOUR HEALTH SCRIPT ---
    public void RegisterPerfectDodge()
    {
        // Only count it if we are actually currently invincible
        if (IsInvincible)
        {
            Debug.Log("PERFECT DODGE! Event Fired.");
            OnDodgeSuccess?.Invoke();
            
            // Optional: Add "Time Slow" or "Flash" effect here for game feel
        }
    }

    private IEnumerator DodgeSequence()
    {
        _isDodging = true;
        if (_attackScript != null) _attackScript.enabled = false;

        _animator.SetTrigger(DodgeTrigger);

        // Start I-Frames
        StartCoroutine(HandleIFrames());

        // Calculate Direction
        Vector2 moveInput = _walkScript.GetMoveInput();
        Vector3 dodgeDirection;
        Vector3 cameraForward = new Vector3(_cameraTransform.forward.x, 0, _cameraTransform.forward.z).normalized;
        Vector3 cameraRight = new Vector3(_cameraTransform.right.x, 0, _cameraTransform.right.z).normalized;

        if (moveInput.magnitude > 0.1f)
            dodgeDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;
        else
            dodgeDirection = cameraForward; // Backstep or forward dash if no input?

        Quaternion targetRotation = Quaternion.LookRotation(dodgeDirection);

        // Movement Loop
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
        IsInvincible = false; // Safety Reset

        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        if (_attackScript != null) _attackScript.enabled = true;

        _isDodging = false;
    }

    private IEnumerator HandleIFrames()
    {
        yield return new WaitForSeconds(iFrameStartDelay);
        IsInvincible = true;
        yield return new WaitForSeconds(iFrameDuration);
        IsInvincible = false;
    }
    
// Inside PlayerDodge.cs
public void AttemptDodge()
{
    if (!_isDodging && (_attackScript == null || !_attackScript.IsAttacking()))
    {
        // ... (copy rest of OnDodge logic) ...
        if (_walkScript != null) _walkScript.IsMovementLocked = true;
        OnDodgeAttempt?.Invoke();
        StartCoroutine(DodgeSequence());
    }
}
}