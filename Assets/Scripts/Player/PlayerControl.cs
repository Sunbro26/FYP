using UnityEngine;
using System;
using System.Collections; 

public class PlayerControl : MonoBehaviour
{
    [Header("Block Settings")]
    public float blockAngle = 60f;
    
    [Tooltip("The particle effect prefab to spawn on a successful block.")]
    public GameObject blockSparksPrefab;
    
    [Tooltip("The transform where the sparks will appear.")]
    public Transform blockEffectSpawnPoint;

    [Header("Guard Break")]
    [Tooltip("How long the player is stunned when stamina runs out.")]
    public float guardBreakStunDuration = 3.0f; // Increased default to 3.0

    // --- References ---
    private CharacterStats _stats;
    private PlayerBlock _blockScript;
    private PlayerDodge _dodgeScript;
    private PlayerAttack _attackScript; 
    private Animator _animator;         

    public static event Action OnBlockSuccess;
    
    private static readonly int HitTrigger = Animator.StringToHash("Hit");
    private static readonly int GuardBreakTrigger = Animator.StringToHash("GuardBreak");

    void Start()
    {
        _stats = GetComponent<CharacterStats>();
        _blockScript = GetComponent<PlayerBlock>();
        _dodgeScript = GetComponent<PlayerDodge>();
        _attackScript = GetComponent<PlayerAttack>();
        _animator = GetComponentInChildren<Animator>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("sword"))
        {
            SkeletonAI enemyScript = other.GetComponentInParent<SkeletonAI>();
            if (enemyScript == null || enemyScript.canDealDamage == false) return;

            var incomingAttack = enemyScript.GetCurrentAttack();
            int incomingDamage = 10;
            float incomingStaminaCost = 10f;
            bool isParriable = true; 

            if (incomingAttack != null)
            {
                incomingDamage = incomingAttack.damage; 
                incomingStaminaCost = incomingAttack.blockStaminaCost;
                isParriable = incomingAttack.isParriable; 
            }

            // I-Frames Check
            if (_dodgeScript != null && _dodgeScript.IsInvincible) 
            {
                _dodgeScript.RegisterPerfectDodge(); 
                return;
            }

            // Parry Check
            PlayerParry parryScript = GetComponent<PlayerParry>();
            if (parryScript != null && parryScript.IsParryWindowActive)
            {
                if (isParriable) 
                {
                    Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                    float angle = Vector3.Angle(transform.forward, directionToEnemy);

                    if (angle <= 60f) 
                    {
                        enemyScript.GetParried(); 
                        if (blockSparksPrefab != null && blockEffectSpawnPoint != null)
                            Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, Quaternion.identity);
                        return; 
                    }
                }
            }

            bool isBlockingSuccessfully = false;

            if (_blockScript != null && _blockScript.IsBlocking)
            {
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                if (angle <= blockAngle / 2)
                {
                    if (_stats.UseStamina(incomingStaminaCost))
                    {
                        isBlockingSuccessfully = true;
                    }
                    else
                    {
                        Debug.Log("GUARD BROKEN! Stamina Depleted.");
                        StartCoroutine(GuardBreakSequence(enemyScript));
                        return; 
                    }
                }
            }

            if (isBlockingSuccessfully)
            {
                OnBlockSuccess?.Invoke(); 
                if (blockSparksPrefab != null && blockEffectSpawnPoint != null)
                {
                    Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                    Quaternion sparkRotation = Quaternion.LookRotation(directionToEnemy);
                    GameObject sparks = Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, sparkRotation);
                    Destroy(sparks, 1.0f);
                }
                enemyScript.canDealDamage = false; 
                return; 
            }
            else
            {
                if (_stats != null) _stats.TakeDamage(incomingDamage); 
                
                if (_attackScript != null && !_attackScript.IsAttacking())
                {
                    if (_animator != null) _animator.SetTrigger(HitTrigger);
                }

                enemyScript.RegisterHit(); 
                enemyScript.canDealDamage = false;
            }
        }
    }

    // --- GUARD BREAK COROUTINE ---
    private IEnumerator GuardBreakSequence(SkeletonAI enemyScript)
    {
        // 1. Drop Shield
        if (_blockScript != null) _blockScript.ForceDropShield();

        // 2. PAUSE STAMINA REGEN
        // We pause it for the stun duration + a little extra (e.g. 0.5s) so you don't recover immediately
        if (_stats != null) _stats.PauseStaminaRegen(guardBreakStunDuration + 0.5f);

        // 3. Lock controls
        Walk walkScript = GetComponent<Walk>(); // Assuming you have Walk script
        if (walkScript != null) walkScript.IsMovementLocked = true;
        if (_attackScript != null) _attackScript.enabled = false;
        if (_dodgeScript != null) _dodgeScript.enabled = false;

        // 4. Animation
        if (_animator != null) _animator.SetTrigger(GuardBreakTrigger);

        // 5. Disable enemy damage for this specific swing so we don't take HP damage instantly
        if (enemyScript != null) enemyScript.canDealDamage = false;

        // 6. Wait (The Stun)
        // This is where the player is helpless
        yield return new WaitForSeconds(guardBreakStunDuration);

        // 7. Restore Controls
        if (walkScript != null) walkScript.IsMovementLocked = false;
        if (_attackScript != null) _attackScript.enabled = true;
        if (_dodgeScript != null) _dodgeScript.enabled = true;
    }
}