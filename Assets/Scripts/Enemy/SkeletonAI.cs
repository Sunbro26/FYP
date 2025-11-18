using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class SkeletonAI : MonoBehaviour
{
    [Header("AI Settings")]
    public float attackRange = 2f;
    public float attackCooldown = 3f;
    
    [Header("Animation Timing")]
    [Tooltip("Total length of the animation clip.")]
    public float attackAnimationDuration = 1.2f;
    
    [Tooltip("Time to wait AFTER animation starts before damage is enabled (Wind-up).")]
    public float damageStartDelay = 0.5f; 

    [Tooltip("How long the damage stays active (The actual Swing).")]
    public float damageWindowDuration = 0.2f;

    // Private references
    private NavMeshAgent _navAgent;
    private Animator _animator;
    private Transform _playerTarget;

    // State variables
    private float _timeSinceLastAttack = 0f;
    public bool _isAttacking = false; 
    public bool canDealDamage = false;

    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int MovementDirection = Animator.StringToHash("MovementDirection");

    void Start()
    {
        _navAgent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _playerTarget = playerObj.transform;

        _timeSinceLastAttack = attackCooldown;
    }

    void Update()
    {
        if (_playerTarget == null) return;
        if (_isAttacking) return;
        
        _timeSinceLastAttack += Time.deltaTime;

        float distanceToPlayer = Vector3.Distance(transform.position, _playerTarget.position);

        if (distanceToPlayer <= attackRange && _timeSinceLastAttack >= attackCooldown)
        {
            StartCoroutine(AttackSequence());
        }
        else
        {
            _navAgent.SetDestination(_playerTarget.position);
            
            if (_navAgent.remainingDistance > _navAgent.stoppingDistance)
            {
                _animator.SetFloat(MovementDirection, 1f, 0.1f, Time.deltaTime);
                
                Vector3 direction = _navAgent.velocity.normalized;
                if (direction != Vector3.zero)
                {
                    Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
                    transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
                }
            }
            else 
            {
                _animator.SetFloat(MovementDirection, 0f, 0.1f, Time.deltaTime);
                FaceTarget();
            }
        }
    }

    private IEnumerator AttackSequence()
    {
        _isAttacking = true;
        _timeSinceLastAttack = 0f;
        
        // 1. Stop moving and start animation
        _navAgent.isStopped = true;
        FaceTarget();
        _animator.SetTrigger(AttackTrigger);
        
        // 2. WAIT for the wind-up (Sword is harmless here)
        yield return new WaitForSeconds(damageStartDelay);

        // 3. ENABLE damage (Sword is now "Sharp")
        canDealDamage = true;

        // 4. WAIT for the swing duration
        yield return new WaitForSeconds(damageWindowDuration);

        // 5. DISABLE damage (Sword is harmless again during recovery)
        canDealDamage = false;

        // 6. WAIT for the remainder of the animation to finish
        // We calculate how much time is left so the AI doesn't move too early
        float remainingTime = attackAnimationDuration - damageStartDelay - damageWindowDuration;
        if (remainingTime > 0)
        {
            yield return new WaitForSeconds(remainingTime);
        }

        if (_navAgent.isOnNavMesh) 
        {
            _navAgent.isStopped = false;
        }

        _isAttacking = false;
    }
    
    void FaceTarget()
    {
        Vector3 direction = (_playerTarget.position - transform.position).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
    }
}