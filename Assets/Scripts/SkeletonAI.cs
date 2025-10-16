using System.Collections;
using UnityEngine;
using UnityEngine.AI; // Important: We need this for the NavMeshAgent

[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class SkeletonAI : MonoBehaviour
{
    [Header("AI Settings")]
    [Tooltip("The range at which the skeleton will start its attack.")]
    public float attackRange = 2f;
    [Tooltip("How often the skeleton can perform an attack (in seconds).")]
    public float attackCooldown = 3f;
    [Tooltip("How long the attack animation lasts. Movement is locked during this time.")]
    public float attackAnimationDuration = 1.2f;

    // Private references
    private NavMeshAgent _navAgent;
    private Animator _animator;
    private Transform _playerTarget;

    // State variables
    private float _timeSinceLastAttack = 0f;
    private bool _isAttacking = false;

    // Animator parameter hashes for performance
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");
    private static readonly int MovementDirection = Animator.StringToHash("MovementDirection");

    void Start()
    {
        // Get the components attached to this GameObject
        _navAgent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();

        // Automatically find the player by their tag
        _playerTarget = GameObject.FindGameObjectWithTag("Player").transform;

        // Allow the skeleton to attack as soon as the game starts if the player is in range
        _timeSinceLastAttack = attackCooldown;
    }

    void Update()
    {
        // If we don't have a target, do nothing
        if (_playerTarget == null) return;

        // If we are in the middle of an attack animation, do nothing
        if (_isAttacking) return;
        
        // Always increment the attack timer
        _timeSinceLastAttack += Time.deltaTime;

        // Calculate the distance to the player
        float distanceToPlayer = Vector3.Distance(transform.position, _playerTarget.position);

        // --- Decision Making Logic ---

        // Condition to ATTACK
        if (distanceToPlayer <= attackRange && _timeSinceLastAttack >= attackCooldown)
        {
            StartCoroutine(AttackSequence());
        }
        // Condition to CHASE
        else
        {
            // Set the player as the destination for the NavMeshAgent
            _navAgent.SetDestination(_playerTarget.position);
            
            // If the agent is moving (i.e., its path is not complete)
            if (_navAgent.remainingDistance > _navAgent.stoppingDistance)
            {
                // Play the forward walk animation
                _animator.SetFloat(MovementDirection, 1f, 0.1f, Time.deltaTime);
                
                // --- Face the direction of movement (optional but looks better) ---
                Vector3 direction = _navAgent.velocity.normalized;
                if (direction != Vector3.zero)
                {
                    Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
                    transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);
                }
            }
            else // If the agent has reached its destination (is close to the player but on cooldown)
            {
                // Play the idle animation
                _animator.SetFloat(MovementDirection, 0f, 0.1f, Time.deltaTime);
                // Face the player
                FaceTarget();
            }
        }
    }

    private IEnumerator AttackSequence()
    {
        _isAttacking = true;
        _timeSinceLastAttack = 0f;

        // Stop the agent from moving during the attack
        _navAgent.isStopped = true;

        // Turn to face the player before attacking
        FaceTarget();

        // Trigger the attack animation
        _animator.SetTrigger(AttackTrigger);
        
        // Wait for the duration of the animation
        yield return new WaitForSeconds(attackAnimationDuration);

        // Resume movement after the attack is finished
        if (_navAgent.isOnNavMesh) // Safety check in case the agent was destroyed
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