using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class SkeletonAI : MonoBehaviour
{
    // --- Definitions ---
    public enum AIState
    {
        Idle,           // Waiting for player
        Strategizing,   // Circling while deciding on a plan
        Maneuvering,    // Moving to the specific range required by the plan
        Attacking,      // Locked in animation
        Retreating,     // Defensive retreat (Bait/Reset)
        Stunned         // Parried
    }

[System.Serializable]
    public class AIPersona
    {
        [Range(0, 1)] public float aggression = 0.7f; 
        [Range(0, 1)] public float fear = 0.2f;       
        public float decisionFrequency = 2.0f; 
        public float preferredCombatRange = 2.5f; 
    }

    [System.Serializable]
    public class EnemyAttack
    {
        public string name;             // e.g. "Dash Attack"
        public int animationIndex;      // Matches Animator
        public float optimalRange;      // e.g. 6.0 for Dash, 1.5 for Slash
        public float rangeTolerance = 0.5f; // Precision needed
        public float weight = 1.0f;     // Likelihood of picking
        public bool requiresLineOfSight = true;

        // --- NEW: Per-Attack Timing ---
        public float windUpTime;      // How long before the hit happens?
        public float damageDuration;  // How long is the hitbox active?
        public float totalDuration;   // Total clip length
    }   

    [Header("Configuration")]
    public AIPersona currentPersona;
    public float sensorRadius = 15f;
    public float circleSpeed = 2.5f;

    [Header("Attack Library")]
    public List<EnemyAttack> availableAttacks; // POPULATE THIS IN INSPECTOR!

    // --- State Variables ---
    private AIState _currentState;
    private NavMeshAgent _agent;
    private Animator _animator;
    private Transform _target;
    private EnemyAttack _plannedAttack; // The GOAL
    private float _decisionTimer;
    private bool _isActionLocked = false;
    private int _retreatType = 0; // 0=Bait, 1=Reset
    [Header("Combat Timing")]
    public float damageStartDelay = 0.4f;
    public float damageWindowDuration = 0.2f;
    public float attackAnimDuration = 1.2f;

    // Circling vars
    private float _strafeDirection = 1f;
    private float _strafeTimer = 0f;

    // Components
    public bool canDealDamage = false;

    // --- Hashes ---
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveZ = Animator.StringToHash("MoveZ");
    private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
    private static readonly int TriggerAttack = Animator.StringToHash("TriggerAttack");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p) _target = p.transform;

        // Default setup
        if(availableAttacks.Count == 0) Debug.LogError("Add Attacks to the List in Inspector!");
        
        SwitchState(AIState.Idle);
    }

    void Update()
    {
        if (_target == null) return;
        if (_isActionLocked) return; 

        float distance = Vector3.Distance(transform.position, _target.position);

        // 1. BRAIN: Decide Strategy
        DecideStrategy(distance);

        // 2. BODY: Execute Movement based on Strategy
        ExecuteStateMovement(distance);

        if (_currentState != AIState.Stunned) FaceTarget();
    }

    // --- THE GOAL-ORIENTED BRAIN ---
    void DecideStrategy(float dist)
    {
        switch (_currentState)
        {
            case AIState.Idle:
                if (dist < sensorRadius) SwitchState(AIState.Strategizing);
                break;

            case AIState.Strategizing:
                // We are circling, looking for an opening.
                _decisionTimer += Time.deltaTime;
                // 1. Interrupt: Too Close?
                if (dist < 1.5f && Random.value < currentPersona.aggression)
                {
                    // Panic/Punish Attack (Force Basic Slash)
                    _plannedAttack = availableAttacks[0]; // Assuming 0 is fast slash
                    StartCoroutine(ExecuteAttackRoutine());
                    return;
                }

                // 2. Make a Plan
                if (_decisionTimer > currentPersona.decisionFrequency)
                {
                    _plannedAttack = ChooseNextAttackStrategy();
                    Debug.Log($"Plan Formulated: Perform {_plannedAttack.name} at range {_plannedAttack.optimalRange}");
                    SwitchState(AIState.Maneuvering);
                }
                break;

            case AIState.Maneuvering:
                // We have a plan. Are we in position?
                if (IsPositionedForPlan(dist))
                {
                    StartCoroutine(ExecuteAttackRoutine());
                }
                
                // Failsafe: If maneuvering takes too long (stuck?), give up
                _decisionTimer += Time.deltaTime;
                if (_decisionTimer > 5.0f)
                {
                    Debug.Log("Plan Aborted: Took too long.");
                    _plannedAttack = null;
                    SwitchState(AIState.Strategizing);
                }
                break;

            case AIState.Retreating:
                // Logic for Baits/Resets (Same as before)
                if (_retreatType == 0) // Bait
                {
                    if (dist < 2.0f) { StartCoroutine(ExecuteAttackRoutine()); return; } // Punish
                    if (dist > currentPersona.preferredCombatRange) SwitchState(AIState.Strategizing);
                }
                else if (_retreatType == 1) // Reset
                {
                    if (dist > 7.0f) SwitchState(AIState.Strategizing);
                }
                break;
        }
    }

    // --- THE BODY (Context-Aware Movement) ---
    void ExecuteStateMovement(float dist)
    {
        switch (_currentState)
        {
            case AIState.Strategizing:
                // Just circle/strafe menacingly
                _agent.isStopped = true;
                HandleCirclingMovement();
                break;

            case AIState.Maneuvering:
                // GOAL MOVEMENT: Move specifically to satisfy the plan
                if (_plannedAttack == null) return;

                _agent.isStopped = false;
                float targetRange = _plannedAttack.optimalRange;

                // Logic: How do I get to optimal range?
                if (dist > targetRange + _plannedAttack.rangeTolerance)
                {
                    // Too far? Chase.
                    _agent.SetDestination(_target.position);
                    UpdateAnim(0, 1); 
                }
                else if (dist < targetRange - _plannedAttack.rangeTolerance)
                {
                    // Too close? Back up (Tactical Retreat)
                    Vector3 fleeDir = (transform.position - _target.position).normalized;
                    _agent.SetDestination(transform.position + fleeDir * 2f);
                    UpdateAnim(0, -1);
                }
                else
                {
                    // In position! Stop.
                    _agent.isStopped = true;
                    UpdateAnim(0, 0);
                }
                break;

            case AIState.Retreating:
                _agent.isStopped = false;
                Vector3 retreatPos = transform.position + (transform.position - _target.position).normalized * 4f;
                _agent.SetDestination(retreatPos);
                UpdateAnim(0, -1);
                break;
        }
    }

    // --- HELPER LOGIC ---

    EnemyAttack ChooseNextAttackStrategy()
    {
        // Weighted Random Choice
        float totalWeight = 0;
        foreach (var atk in availableAttacks) totalWeight += atk.weight;

        float randomValue = Random.Range(0, totalWeight);
        float cursor = 0;

        foreach (var atk in availableAttacks)
        {
            cursor += atk.weight;
            if (cursor >= randomValue) return atk;
        }
        return availableAttacks[0]; // Fallback
    }

    bool IsPositionedForPlan(float dist)
    {
        if (_plannedAttack == null) return false;
        // Check if distance is within tolerance of optimal range
        return Mathf.Abs(dist - _plannedAttack.optimalRange) <= _plannedAttack.rangeTolerance;
    }

    void HandleCirclingMovement()
    {
        // Tangent Circling Logic
        Vector3 toPlayer = (_target.position - transform.position).normalized;
        Vector3 tangent = Vector3.Cross(toPlayer, Vector3.up);

        _strafeTimer += Time.deltaTime;
        if (_strafeTimer > 3.0f)
        {
            _strafeDirection = (Random.value > 0.5f) ? 1f : -1f;
            _strafeTimer = 0f;
        }

        Vector3 finalMove = tangent * _strafeDirection * circleSpeed * Time.deltaTime;
        
        // Drift Correction (Pull to preferred range)
        float dist = Vector3.Distance(transform.position, _target.position);
        float error = dist - currentPersona.preferredCombatRange;
        Vector3 correction = toPlayer * error * 0.5f * Time.deltaTime; 

        _agent.Move(finalMove + correction);
        
        // Smooth Anim
        UpdateAnim(_strafeDirection, 0);
    }

    // --- ATTACK EXECUTION ---
    // --- ATTACK EXECUTION ---
    IEnumerator ExecuteAttackRoutine()
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);
        
        int animIndex = (_plannedAttack != null) ? _plannedAttack.animationIndex : 0;

        _animator.SetInteger(AttackIndex, animIndex);
        _animator.SetTrigger(TriggerAttack);

        // --- DEFINE TIMING VALUES ---
        // Safety: If _plannedAttack is null (fallback), use global defaults. 
        // Otherwise, use the specific data from the Inspector list.
        float currentWindUp = (_plannedAttack != null) ? _plannedAttack.windUpTime : damageStartDelay;
        float currentDamageWindow = (_plannedAttack != null) ? _plannedAttack.damageDuration : damageWindowDuration;
        float currentTotalDuration = (_plannedAttack != null) ? _plannedAttack.totalDuration : attackAnimDuration;

        // 1. WIND UP PHASE (Tracking Player)
        // We loop here so we can keep facing the target during the windup
        float timer = 0f;
        while (timer < currentWindUp) 
        {
            FaceTarget();
            timer += Time.deltaTime;
            yield return null;
        }

        // 2. SWING PHASE (Damage ON)
        // We stop facing the target so the player can dodge sideways
        canDealDamage = true;
        yield return new WaitForSeconds(currentDamageWindow);
        canDealDamage = false;

        // 3. RECOVERY PHASE (The Fix)
        // FIXED: We now use the 'current' variables, not the global ones.
        float remaining = currentTotalDuration - currentWindUp - currentDamageWindow;
        
        if (remaining > 0) 
        {
            yield return new WaitForSeconds(remaining);
        }

        // --- POST-ATTACK DECISION ---
        if (Random.value < currentPersona.fear)
        {
            _retreatType = 1; // Reset
            SwitchState(AIState.Retreating);
        }
        else
        {
            SwitchState(AIState.Strategizing);
        }

        _plannedAttack = null; 
        _isActionLocked = false;
        _decisionTimer = 0;
    }
    // --- PARRY LOGIC ---
    public void GetParried()
    {
        StopAllCoroutines(); 
        _isActionLocked = true;
        _agent.isStopped = true;
        canDealDamage = false; 
        SwitchState(AIState.Stunned); 
        StartCoroutine(ParryReboundRoutine());
    }

    private IEnumerator ParryReboundRoutine()
    {
        _animator.SetFloat(AttackSpeedHash, -1.0f);
        yield return new WaitForSeconds(0.4f);
        _animator.SetFloat(AttackSpeedHash, 0f);
        yield return new WaitForSeconds(1.5f);
        _animator.SetFloat(AttackSpeedHash, 1.0f);
        
        _retreatType = 1; // Always reset after stun
        SwitchState(AIState.Retreating); 
        _isActionLocked = false;
    }

    // --- HELPERS ---
    void SwitchState(AIState newState)
    {
        if (_currentState == newState) return;
        _currentState = newState;
        _decisionTimer = 0; // Reset timer on state change
        
        // Init Retreat logic
        if (newState == AIState.Retreating)
        {
            _retreatType = (Random.value > 0.5f) ? 0 : 1; 
        }
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
        if (dir != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
    }
}