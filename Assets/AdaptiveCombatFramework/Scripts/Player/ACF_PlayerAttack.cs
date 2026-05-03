using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System; 

namespace AdaptiveCombatFramework {
    [AddComponentMenu("Framework/Player/Player Attack")]
    public class PlayerAttack : MonoBehaviour
    {
        // --- Framework Events (Not visible in Inspector) ---
        public static event Action OnPlayerAttack;
        public static event Action OnPlayerHitEnemy;

        [Header("Attack Settings")]
        [Tooltip("The total length of the attack animation state.")]
        [SerializeField] float attackDuration = 0.8f;
        [Tooltip("Stamina consumed per swing.")]
        [SerializeField] float staminaCost = 20f;
        [Tooltip("Health damage dealt to hit entities.")]
        [SerializeField] int damageAmount = 15;

        [Header("Hitbox Configuration")]
        [Tooltip("The transform (usually on the weapon) where the overlap sphere originates.")]
        public Transform attackPoint;
        [Tooltip("The radius of the damage-dealing sphere.")]
        public float attackRange = 1.5f;
        [Tooltip("Which layers are checked for damageable entities.")]
        public LayerMask enemyLayers;

        [Header("Timing Logic")]
        [Tooltip("Delay in seconds from the start of animation until hit detection occurs.")]
        public float hitRegistrationDelay = 0.3f;

        private Animator _animator;
        private bool _isAttacking = false;
        private Walk _walkScript;
        private PlayerDodge _dodgeScript;
        private CharacterStats _stats;
        private PlayerBlock _blockScript;

        private static readonly int AttackTrigger = Animator.StringToHash("Attack");

        public float StaminaCost => staminaCost;

        void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            _walkScript = GetComponent<Walk>();
            _dodgeScript = GetComponent<PlayerDodge>();
            _stats = GetComponent<CharacterStats>();
            _blockScript = GetComponent<PlayerBlock>();
        }

        public void OnAttack(InputAction.CallbackContext context)
        {
            if (context.started && CanAttemptAttack())
            {
                AttemptAttack();
            }
        }

        public bool CanAttemptAttack()
        {
            if (_isAttacking) return false;
            if (_dodgeScript != null && _dodgeScript.IsDodging()) return false;
            if (_blockScript != null && _blockScript.IsBlocking) return false;
            return _stats != null && _stats.CanSpendStamina(staminaCost);
        }

        public void AttemptAttack()
        {
            if (_stats != null && _stats.UseStamina(staminaCost))
            {
                StartCoroutine(AttackSequence());
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
            
            float remaining = attackDuration - hitRegistrationDelay;
            if (remaining > 0) yield return new WaitForSeconds(remaining);

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
                if (enemyStats != null)
                {
                    enemyStats.TakeDamage(damageAmount);
                    OnPlayerHitEnemy?.Invoke();
                }

                ICombatant combatant = enemy.GetComponentInParent<ICombatant>();
                if (combatant != null) combatant.TakeHit();
            }
        }

        void OnDrawGizmosSelected()
        {
            if (attackPoint == null) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attackPoint.position, attackRange);
        }

        public bool IsAttacking() => _isAttacking;
    }
}