using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerAttack : MonoBehaviour
{
    [Header("Attack Settings")]
    [Tooltip("How long movement is locked during the attack. Time this to your animation.")]
    public float attackDuration = 0.8f;

    private Animator _animator;
    private bool _isAttacking = false;

    // --- NEW: References to other scripts for state checking and locking ---
    private Walk _walkScript;
    private PlayerDodge _dodgeScript;

    private static readonly int AttackTrigger = Animator.StringToHash("Attack");

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        // --- NEW: Get references to the other scripts on this GameObject ---
        _walkScript = GetComponent<Walk>();
        _dodgeScript = GetComponent<PlayerDodge>();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        // --- NEW: Added checks to prevent attacking while already attacking or dodging ---
        if (context.started && !_isAttacking && (_dodgeScript == null || !_dodgeScript.IsDodging()))
        {
            StartCoroutine(AttackSequence());
        }
    }

    // --- NEW: Coroutine to handle the full attack sequence ---
    private IEnumerator AttackSequence()
    {
        _isAttacking = true;
        // Lock the player's movement
        if (_walkScript != null) _walkScript.IsMovementLocked = true;

        // Trigger the animation
        _animator.SetTrigger(AttackTrigger);

        // Wait for the duration of the attack animation
        yield return new WaitForSeconds(attackDuration);

        // Unlock the player's movement
        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        _isAttacking = false;
    }

    public bool IsAttacking()
    {
        return _isAttacking;
    }
}