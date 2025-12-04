using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System; 

public class PlayerAttack : MonoBehaviour
{
    public static event Action OnPlayerAttack; 

    [Header("Attack Settings")]
    [SerializeField] float attackDuration = 0.8f;
    [SerializeField] float staminaCost = 20f; // New: Attack costs stamina
    [SerializeField] int damageAmount = 15;

    [Header("Hitbox Settings")]
    [Tooltip("Assign an empty GameObject placed at the tip of your sword/weapon.")]
    public Transform attackPoint;
    public float attackRange = 1.5f;
    public LayerMask enemyLayers; // Assign "Enemy" layer to the Boss

    [Header("Timing")]
    [Tooltip("Time into the animation when the hit actually registers.")]
    public float hitRegistrationDelay = 0.3f;

    private Animator _animator;
    private bool _isAttacking = false;

    private Walk _walkScript;
    private PlayerDodge _dodgeScript;
    private CharacterStats _stats; // Reference to stats

    private static readonly int AttackTrigger = Animator.StringToHash("Attack");

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        _walkScript = GetComponent<Walk>();
        _dodgeScript = GetComponent<PlayerDodge>();
        _stats = GetComponent<CharacterStats>();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        // Check input, state, and STAMINA
        if (context.started && !_isAttacking && (_dodgeScript == null || !_dodgeScript.IsDodging()))
        {
            if (_stats != null && _stats.UseStamina(staminaCost))
            {
                StartCoroutine(AttackSequence());
            }
            else
            {
                Debug.Log("Not enough stamina to attack!");
            }
        }
    }

    private IEnumerator AttackSequence()
    {
        _isAttacking = true;
        if (_walkScript != null) _walkScript.IsMovementLocked = true;

        _animator.SetTrigger(AttackTrigger);
        OnPlayerAttack?.Invoke(); 

        // --- HIT REGISTRATION LOGIC ---
        // Wait for the swing to reach the target point
        yield return new WaitForSeconds(hitRegistrationDelay);
        
        CheckForHit();
        // -----------------------------

        // Wait for the rest of the animation
        yield return new WaitForSeconds(attackDuration - hitRegistrationDelay);

        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        _isAttacking = false;
    }

    private void CheckForHit()
    {
        if (attackPoint == null) return;

        // Detect enemies in range of the attack point
        Collider[] hitEnemies = Physics.OverlapSphere(attackPoint.position, attackRange, enemyLayers);

        foreach (Collider enemy in hitEnemies)
        {
            Debug.Log("We hit " + enemy.name);

            // Try to find CharacterStats on the enemy (The Boss)
            CharacterStats enemyStats = enemy.GetComponent<CharacterStats>();
            if (enemyStats != null)
            {
                enemyStats.TakeDamage(damageAmount);
            }
            else
            {
                // Backup check: maybe the collider is on a child bone?
                CharacterStats parentStats = enemy.GetComponentInParent<CharacterStats>();
                if (parentStats != null) parentStats.TakeDamage(damageAmount);
            }
        }
    }

    // Debug visualization to see the attack range in the Scene view
    void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }

    public bool IsAttacking()
    {
        return _isAttacking;
    }
}