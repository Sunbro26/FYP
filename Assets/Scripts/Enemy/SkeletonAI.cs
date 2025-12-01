using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class SkeletonAI_MultiGAIL : MonoBehaviour
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

    // --- Persona Settings (Crucial for MultiGAIL Data Generation) ---
    [System.Serializable]
    public class AIPersona
    {
        [Range(0, 1)] public float aggression = 0.5f; // Chance to attack vs circle
        [Range(0, 1)] public float caution = 0.5f;    // Chance to retreat after attacking
        [Range(0, 1)] public float fear = 0.2f;       // Chance to regroup at low health
        public float preferredCombatRange = 3.0f;
    }

    [Header("Configuration")]
    public AIPersona currentPersona;
    public float sensorRadius = 15f;
    
    [Header("Movement Stats")]
    public float circleSpeed = 2.5f;
    public float retreatDistance = 5.0f;

    // --- State Variables ---
    private AIState _currentState;
    private NavMeshAgent _agent;
    private Animator _animator;
    private Transform _target;
    private float _stateTimer; // How long we've been in the current state
    private bool _isActionLocked = false;

    // --- Hashes ---
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveZ = Animator.StringToHash("MoveZ");
    private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
    private static readonly int TriggerAttack = Animator.StringToHash("TriggerAttack");

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        _target = GameObject.FindGameObjectWithTag("Player").transform;
        
        // Start passive
        SwitchState(AIState.Idle);
    }

    void Update()
    {
        if (_isActionLocked) return; // Don't move if mid-attack

        // 1. SENSORY UPDATE (Input for Logic)
        float distance = Vector3.Distance(transform.position, _target.position);
        float healthPct = 1.0f; // Replace with actual Health component linkage later

        // 2. LOGIC UPDATE (The "Brain" - To be replaced by ML later)
        // In ML version, this function would be replaced by RequestDecision()
        DecideNextState(distance, healthPct);

        // 3. EXECUTION UPDATE (The "Body")
        ExecuteStateLogic(distance);
        
        // 4. ROTATION (Always face target unless fleeing)
        if (_currentState != AIState.Regrouping) FaceTarget();
    }

    // --- THE BRAIN (Heuristic Logic for Data Generation) ---
    void DecideNextState(float dist, float health)
    {
        // Global Override: Fear/Regroup
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
                // Randomly decide to Attack based on Aggression
                if (_stateTimer > 1.0f && Random.value < (currentPersona.aggression * Time.deltaTime))
                {
                    StartCoroutine(PerformAttackLogic());
                }
                // Randomly decide to Retreat if too close
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

    // --- THE BODY (Movement Execution) ---
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
                // Complex Strafing Logic
                Vector3 strafeDir = transform.right * Mathf.Sin(Time.time * circleSpeed); // Simple oscillating strafe
                _agent.Move(strafeDir * Time.deltaTime);
                UpdateAnim(Mathf.Sin(Time.time), 0); 
                break;

            case AIState.Retreating:
                _agent.isStopped = false;
                // Calculate position away from player
                Vector3 fleeDir = (transform.position - _target.position).normalized;
                _agent.SetDestination(transform.position + fleeDir * 2f);
                UpdateAnim(0, -1); // Walk Backwards
                break;

            case AIState.Regrouping:
                _agent.isStopped = false;
                // Run far away
                Vector3 runAwayPos = transform.position + (transform.position - _target.position).normalized * 10f;
                _agent.SetDestination(runAwayPos);
                UpdateAnim(0, 1); // Run Forward (away)
                break;
        }
    }

    // --- ATTACK SYSTEM (The Complex Patterns) ---
    IEnumerator PerformAttackLogic()
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);

        // Select Attack based on Persona & Distance
        // This is where your list of 16 attacks goes
        int attackID = ChooseAttackBasedOnContext();

        _animator.SetInteger(AttackIndex, attackID);
        _animator.SetTrigger(TriggerAttack);

        // Wait for animation to finish (simulated here)
        float animLength = 1.5f; // Replace with actual clip length query
        yield return new WaitForSeconds(animLength);

        // Decision after attack: Retreat or stay?
        if (Random.value < currentPersona.caution)
        {
            SwitchState(AIState.Retreating);
        }
        else
        {
            SwitchState(AIState.Circling);
        }

        _isActionLocked = false;
    }

    int ChooseAttackBasedOnContext()
    {
        float dist = Vector3.Distance(transform.position, _target.position);
        
        // Example Logic:
        // 0: Basic Slash, 1: Dash Attack, 2: Heavy AOE
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
        Quaternion lookRot = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 5f);
    }
}