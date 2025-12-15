using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System; 

public class PlayerAttack : MonoBehaviour
{
    public static event Action OnPlayerAttack; 
    public static event Action OnPlayerHitEnemy; 

    [Header("Attack Settings")]
    [SerializeField] float attackDuration = 0.8f;
    [SerializeField] float staminaCost = 20f; 
    [SerializeField] int damageAmount = 15;

    [Header("Hitbox Settings")]
    [Tooltip("Assign an empty GameObject placed at the tip of your sword/weapon.")]
    public Transform attackPoint;
    public float attackRange = 1.5f;
    public LayerMask enemyLayers; 

    [Header("Timing")]
    [Tooltip("Time into the animation when the hit actually registers.")]
    public float hitRegistrationDelay = 0.3f;

    private Animator _animator;
    private bool _isAttacking = false;

    private Walk _walkScript;
    private PlayerDodge _dodgeScript;
    private CharacterStats _stats; 
    
    // --- EDIT 1: Add reference variable ---
    private PlayerBlock _blockScript; 

    private static readonly int AttackTrigger = Animator.StringToHash("Attack");

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        _walkScript = GetComponent<Walk>();
        _dodgeScript = GetComponent<PlayerDodge>();
        _stats = GetComponent<CharacterStats>();
        
        // --- EDIT 2: Get the component ---
        _blockScript = GetComponent<PlayerBlock>();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        // --- EDIT 3: Add the check (!IsBlocking) to the condition ---
        // We check if _blockScript is null first to prevent errors if you remove the script later
        if (context.started && !_isAttacking 
            && (_dodgeScript == null || !_dodgeScript.IsDodging()) 
            && (_blockScript == null || !_blockScript.IsBlocking)) 
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

        yield return new WaitForSeconds(hitRegistrationDelay);
        
        CheckForHit();

        yield return new WaitForSeconds(attackDuration - hitRegistrationDelay);

        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        _isAttacking = false;
    }

    private void CheckForHit()
    {
        if (attackPoint == null) return;

        Collider[] hitEnemies = Physics.OverlapSphere(attackPoint.position, attackRange, enemyLayers);

        foreach (Collider enemy in hitEnemies)
        {
            CharacterStats enemyStats = enemy.GetComponent<CharacterStats>();
            if (enemyStats == null)
            {
                CharacterStats parentStats = enemy.GetComponentInParent<CharacterStats>();
                if (parentStats != null) enemyStats = parentStats;
            }

            if (enemyStats != null)
            {
                enemyStats.TakeDamage(damageAmount);
                
                // --- FIRE SUCCESS EVENT HERE ---
                OnPlayerHitEnemy?.Invoke();
            }
        }
    }
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