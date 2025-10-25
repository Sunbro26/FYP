using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System; // Required for Action

public class PlayerAttack : MonoBehaviour
{
    // --- NEW: Event Declaration ---
    public static event Action OnPlayerAttack; 

    [Header("Attack Settings")]
    [Tooltip("How long movement is locked during the attack. Time this to your animation.")]
    [SerializeField] float attackDuration = 0.8f;

    private Animator _animator;
    private bool _isAttacking = false;

    private Walk _walkScript;
    private PlayerDodge _dodgeScript;

    private static readonly int AttackTrigger = Animator.StringToHash("Attack");

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        
        _walkScript = GetComponent<Walk>();
        _dodgeScript = GetComponent<PlayerDodge>();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.started && !_isAttacking && (_dodgeScript == null || !_dodgeScript.IsDodging()))
        {
            StartCoroutine(AttackSequence());
        }
    }

    private IEnumerator AttackSequence()
    {
        _isAttacking = true;
        if (_walkScript != null) _walkScript.IsMovementLocked = true;

        _animator.SetTrigger(AttackTrigger);

        // --- NEW: Invoke the event when the attack animation starts ---
        OnPlayerAttack?.Invoke(); 

        yield return new WaitForSeconds(attackDuration);

        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        _isAttacking = false;
    }

    public bool IsAttacking()
    {
        return _isAttacking;
    }
}