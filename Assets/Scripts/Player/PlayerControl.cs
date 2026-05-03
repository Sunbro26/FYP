using UnityEngine;
using System;
using System.Collections;
using AdaptiveCombatFramework; // --- NEW: Tells the script to use the framework ---

public class PlayerControl : MonoBehaviour
{
    [Header("Block Settings")]
    public float blockAngle = 60f;
    public GameObject blockSparksPrefab;
    public Transform blockEffectSpawnPoint;

    [Header("Guard Break")]
    public float guardBreakStunDuration = 3.0f; 

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
        if (_stats != null && _stats.IsDead) return;

        if (other.gameObject.CompareTag("sword"))
        {
            // --- DECOUPLING FIX: Look for the Interface, not the Skeleton ---
            ICombatant enemyScript = other.GetComponentInParent<ICombatant>();
            if (enemyScript == null || enemyScript.CanDealDamage == false) return;

            // Get clean attack data from the interface
            int incomingDamage = enemyScript.GetIncomingDamage();
            float incomingStaminaCost = enemyScript.GetIncomingStaminaCost();
            bool isParriable = enemyScript.IsIncomingAttackParriable();

            if (_dodgeScript != null && _dodgeScript.IsInvincible) 
            {
                _dodgeScript.RegisterPerfectDodge(); 
                return;
            }

            PlayerParry parryScript = GetComponent<PlayerParry>();
            if (parryScript != null && parryScript.IsParryWindowActive)
            {
                if (isParriable) 
                {
                    // Use GetTransform() because interfaces don't inherently know about Unity Transforms
                    Vector3 directionToEnemy = (enemyScript.GetTransform().position - transform.position).normalized;
                    float angle = Vector3.Angle(transform.forward, directionToEnemy);

                    if (angle <= 60f) 
                    {
                        Debug.Log("SUCCESSFUL PARRY!");
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
                Vector3 directionToEnemy = (enemyScript.GetTransform().position - transform.position).normalized;
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
                        if (_stats != null) _stats.UseStamina(_stats.currentStamina);
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
                    Vector3 directionToEnemy = (enemyScript.GetTransform().position - transform.position).normalized;
                    Quaternion sparkRotation = Quaternion.LookRotation(directionToEnemy);
                    GameObject sparks = Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, sparkRotation);
                    Destroy(sparks, 1.0f);
                }
                enemyScript.CanDealDamage = false; 
                return; 
            }
            else
            {
                if (_stats != null) _stats.TakeDamage(incomingDamage); 

                if (_stats != null && _stats.IsDead)
                {
                    enemyScript.CanDealDamage = false;
                    return; 
                }

                if (_attackScript != null && !_attackScript.IsAttacking())
                {
                    if (_animator != null) _animator.SetTrigger(HitTrigger);
                }

                enemyScript.RegisterHit(); 
                enemyScript.CanDealDamage = false;
            }
        }
    }

    private IEnumerator GuardBreakSequence(ICombatant enemyScript)
    {
        if (_blockScript != null) _blockScript.ForceDropShield();
        if (_stats != null) _stats.PauseStaminaRegen(guardBreakStunDuration + 0.5f);

        Walk walkScript = GetComponent<Walk>(); 
        if (walkScript != null) walkScript.IsMovementLocked = true;
        if (_attackScript != null) _attackScript.enabled = false;
        if (_dodgeScript != null) _dodgeScript.enabled = false;

        if (_animator != null) _animator.SetTrigger(GuardBreakTrigger);

        if (enemyScript != null) enemyScript.CanDealDamage = false;

        yield return new WaitForSeconds(guardBreakStunDuration);

        if (walkScript != null) walkScript.IsMovementLocked = false;
        if (_attackScript != null) _attackScript.enabled = true;
        if (_dodgeScript != null) _dodgeScript.enabled = true;
    }
}