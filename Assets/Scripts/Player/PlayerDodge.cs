using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class PlayerDodge : MonoBehaviour
{
    public static event Action OnDodgeAttempt;
    public static event Action OnDodgeSuccess;

    [Header("Dodge Settings")]
    public float dodgeDistance = 5f;
    public float dodgeDuration = 0.6f;
    public float dodgeRotationSpeed = 15f;

    [Header("Stamina Settings")]
    [Tooltip("How much stamina it costs to perform a dodge roll.")]
    public float staminaCost = 15f;

    [Header("I-Frame Settings")]
    public float iFrameStartDelay = 0.2f;
    public float iFrameDuration = 0.3f;

    public bool IsInvincible { get; private set; }

    private CharacterController _characterController;
    private Animator _animator;
    private Walk _walkScript;
    private PlayerAttack _attackScript;
    private Transform _cameraTransform;
    private CharacterStats _stats;

    private bool _isDodging = false;
    private static readonly int DodgeTrigger = Animator.StringToHash("Dodge");

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponentInChildren<Animator>();
        _walkScript = GetComponent<Walk>();
        _attackScript = GetComponent<PlayerAttack>();
        _cameraTransform = Camera.main.transform;
        _stats = GetComponent<CharacterStats>();
    }

    public bool IsDodging() => _isDodging;

    public bool CanAttemptDodge()
    {
        if (_isDodging) return false;
        if (_attackScript != null && _attackScript.IsAttacking()) return false;
        return _stats != null && _stats.CanSpendStamina(staminaCost);
    }

    public void OnDodge(InputAction.CallbackContext context)
    {
        if (context.started && CanAttemptDodge())
        {
            if (_stats != null && !_stats.UseStamina(staminaCost))
            {
                Debug.Log("Not enough stamina to dodge!");
                return;
            }

            if (_walkScript != null) _walkScript.IsMovementLocked = true;
            OnDodgeAttempt?.Invoke();
            StartCoroutine(DodgeSequence());
        }
    }

    public void RegisterPerfectDodge()
    {
        if (IsInvincible)
        {
            Debug.Log("PERFECT DODGE! Event Fired.");
            OnDodgeSuccess?.Invoke();
        }
    }

    private IEnumerator DodgeSequence()
    {
        _isDodging = true;
        if (_attackScript != null) _attackScript.enabled = false;

        _animator.SetTrigger(DodgeTrigger);
        StartCoroutine(HandleIFrames());

        Vector2 moveInput = _walkScript.GetMoveInput();
        Vector3 dodgeDirection;
        Vector3 cameraForward = new Vector3(_cameraTransform.forward.x, 0, _cameraTransform.forward.z).normalized;
        Vector3 cameraRight = new Vector3(_cameraTransform.right.x, 0, _cameraTransform.right.z).normalized;

        if (moveInput.magnitude > 0.1f)
            dodgeDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;
        else
            dodgeDirection = cameraForward;

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
        IsInvincible = false;

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

    public void AttemptDodge()
    {
        if (CanAttemptDodge())
        {
            if (_stats != null && !_stats.UseStamina(staminaCost))
            {
                Debug.Log("Not enough stamina to dodge!");
                return;
            }

            if (_walkScript != null) _walkScript.IsMovementLocked = true;
            OnDodgeAttempt?.Invoke();
            StartCoroutine(DodgeSequence());
        }
    }
}
