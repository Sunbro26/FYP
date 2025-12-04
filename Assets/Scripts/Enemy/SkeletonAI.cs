using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class SkeletonAI : MonoBehaviour
{
    // --- Definitions ---
    public enum AIState
    {
        Idle,           // Waiting/Ambush
        Chasing,        // Closing distance directly
        Circling,       // Combat stance, strafing
        Retreating,     // Short-term backing off (creating space)
        Regrouping,     // Long-term running away (fear/low health)
        Attacking       // Locked in animation
    }

    // --- Persona Settings ---
    [System.Serializable]
    public class AIPersona
    {
        [Range(0, 1)] public float aggression = 0.5f; 
        [Range(0, 1)] public float caution = 0.5f;    
        [Range(0, 1)] public float fear = 0.2f;       
        public float preferredCombatRange = 3.0f;
    }

    [Header("Configuration")]
    public AIPersona currentPersona;
    public float sensorRadius = 15f;
    
    [Header("Movement Stats")]
    public float circleSpeed = 2.5f;
    public float retreatDistance = 5.0f;

    // --- NEW: Combat Timing Settings (From Script 2) ---
    [Header("Combat Timing & Hitboxes")]
    [Tooltip("Total length of the animation clip.")]
    public float attackAnimationDuration = 1.2f;
    
    [Tooltip("Time to wait AFTER animation starts before damage is enabled (Wind-up).")]
    public float damageStartDelay = 0.5f; 

    [Tooltip("How long the damage stays active (The actual Swing).")]
    public float damageWindowDuration = 0.2f;

    // --- State Variables ---
    private AIState _currentState;
    private NavMeshAgent _agent;
    private Animator _animator;
    private Transform _target;
    private float _stateTimer; 
    private bool _isActionLocked = false;
    
    // Public flag for the PlayerControl script to check
    public bool canDealDamage = false;

    // --- Hashes ---
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveZ = Animator.StringToHash("MoveZ");
    private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
    // Note: Make sure your Trigger in the Animator is named "TriggerAttack"
    private static readonly int TriggerAttack = Animator.StringToHash("TriggerAttack"); 

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _target = playerObj.transform;
        
        // Start passive
        SwitchState(AIState.Idle);
    }

    void Update()
    {
        if (_target == null) return;
        if (_isActionLocked) return; // Don't move if mid-attack

        // 1. SENSORY UPDATE
        float distance = Vector3.Distance(transform.position, _target.position);
        float healthPct = 1.0f; 

        // 2. LOGIC UPDATE
        DecideNextState(distance, healthPct);

        // 3. EXECUTION UPDATE
        ExecuteStateLogic(distance);
        
        // 4. ROTATION 
        if (_currentState != AIState.Regrouping) FaceTarget();
    }

    // --- THE BRAIN ---
    void DecideNextState(float dist, float health)
    {
        // Fear/Regroup Override
        if (health < 0.3f && Random.value < currentPersona.fear)
        {
            if (_currentState != AIState.Regrouping) SwitchState(AIState.Regrouping);
            return;
        }

        switch (_currentState)
        {
            case AIState.Idle:
                if (dist < sensorRadius) SwitchState(AIState.Chasing);
                break;

            case AIState.Chasing:
                if (dist <= currentPersona.preferredCombatRange) SwitchState(AIState.Circling);
                break;

            case AIState.Circling:
                // Random Attack
                if (_stateTimer > 1.0f && Random.value < (currentPersona.aggression * Time.deltaTime))
                {
                    StartCoroutine(PerformAttackLogic());
                }
                // Random Retreat
                else if (dist < currentPersona.preferredCombatRange * 0.5f)
                {
                    SwitchState(AIState.Retreating);
                }
                break;

            case AIState.Retreating:
                if (dist > currentPersona.preferredCombatRange) SwitchState(AIState.Circling);
                break;
        }
        
        _stateTimer += Time.deltaTime;
    }

    // --- THE BODY ---
    void ExecuteStateLogic(float dist)
    {
        switch (_currentState)
        {
            case AIState.Chasing:
                _agent.isStopped = false;
                _agent.SetDestination(_target.position);
                UpdateAnim(0, 1); // Run Forward
                break;

            case AIState.Circling:
                _agent.isStopped = true;
                Vector3 strafeDir = transform.right * Mathf.Sin(Time.time * circleSpeed);
                _agent.Move(strafeDir * Time.deltaTime);
                UpdateAnim(Mathf.Sin(Time.time), 0); 
                break;

            case AIState.Retreating:
                _agent.isStopped = false;
                Vector3 fleeDir = (transform.position - _target.position).normalized;
                _agent.SetDestination(transform.position + fleeDir * 2f);
                UpdateAnim(0, -1); // Walk Backwards
                break;

            case AIState.Regrouping:
                _agent.isStopped = false;
                Vector3 runAwayPos = transform.position + (transform.position - _target.position).normalized * 10f;
                _agent.SetDestination(runAwayPos);
                UpdateAnim(0, 1); // Run Forward
                break;
        }
    }

    // --- ATTACK SYSTEM (MERGED LOGIC) ---
    IEnumerator PerformAttackLogic()
    {
        // 1. Setup State
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);
        
        // Ensure we face the player before swinging
        FaceTarget();

        // 2. Trigger Animation
        int attackID = ChooseAttackBasedOnContext();
        _animator.SetInteger(AttackIndex, attackID);
        _animator.SetTrigger(TriggerAttack);

        // --- TIMING LOGIC FROM SCRIPT 2 STARTS HERE ---

        // 3. WAIT for wind-up (Damage is OFF)
        yield return new WaitForSeconds(damageStartDelay);

        // 4. ENABLE Damage (The Swing)
        canDealDamage = true;

        // 5. WAIT for swing duration
        yield return new WaitForSeconds(damageWindowDuration);

        // 6. DISABLE Damage (The Recovery)
        // This ensures no damage happens at the end of the animation
        canDealDamage = false;

        // 7. WAIT for remainder of animation
        float remainingTime = attackAnimationDuration - damageStartDelay - damageWindowDuration;
        if (remainingTime > 0)
        {
            yield return new WaitForSeconds(remainingTime);
        }

        // --- TIMING LOGIC ENDS ---

        // 8. Decide next move (Persona Logic)
        if (Random.value < currentPersona.caution)
        {
            SwitchState(AIState.Retreating);
        }
        else
        {
            SwitchState(AIState.Circling);
        }

        if (_agent.isOnNavMesh) _agent.isStopped = false;
        _isActionLocked = false;
    }

    int ChooseAttackBasedOnContext()
    {
        float dist = Vector3.Distance(transform.position, _target.position);
        
        if (dist > 4.0f) return 1; // Dash attack if far
        if (dist < 1.5f) return 2; // AOE if very close
        return 0; // Basic slash otherwise
    }

    // --- Helpers ---
    void SwitchState(AIState newState)
    {
        _currentState = newState;
        _stateTimer = 0;
    }

    void UpdateAnim(float x, float z)
    {
        _animator.SetFloat(MoveX, x, 0.1f, Time.deltaTime);
        _animator.SetFloat(MoveZ, z, 0.1f, Time.deltaTime);
    }

    void FaceTarget()
    {
        Vector3 dir = (_target.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
        }
    }
}