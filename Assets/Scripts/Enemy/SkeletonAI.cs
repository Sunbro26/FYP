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
        Retreating,     // Backing off
        Attacking,      // Locked in animation
        Stunned         // Parried/Hit
    }

    [System.Serializable]
    public class AIPersona
    {
        [Range(0, 1)] public float aggression = 0.7f; // High = Attack when close. Low = Run when close.
        public float preferredCombatRange = 2.5f;     // The "Sweet Spot"
    }

    [Header("Configuration")]
    public AIPersona currentPersona;
    public float sensorRadius = 15f;
    
    [Header("Movement Physics")]
    public float circleSpeed = 2.5f;
    public float retreatDistance = 1.5f; // If closer than this, we react
    public float stateCommitmentTime = 0.5f; // Min time to stay in a state (Prevents jitter)

    [Header("Combat Hitboxes")]
    public float damageStartDelay = 0.4f; 
    public float damageWindowDuration = 0.2f;
    public float attackAnimDuration = 1.2f;

    // --- Internal State ---
    private AIState _currentState;
    private NavMeshAgent _agent;
    private Animator _animator;
    private Transform _target;
    private float _stateTimer; 
    private float _cooldownTimer;
    private bool _isActionLocked = false;
    private int _retreatType = 0; // 0=Bait, 1=Reset, 2=RangedSetup
    
    // Public flag for Player damage script
    public bool canDealDamage = false;

    // Circling variables
    private float _strafeDirection = 1f; // 1 = Right, -1 = Left
    private float _strafeTimer = 0f;
    private float _strafeChangeInterval = 3.0f; // How often we switch direction

    // --- Hashes ---
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveZ = Animator.StringToHash("MoveZ");
    private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
    private static readonly int TriggerAttack = Animator.StringToHash("TriggerAttack");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed"); // For Parry

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p) _target = p.transform;
        
        SwitchState(AIState.Idle);
    }

    void Update()
    {
        if (_target == null) return;
        
        // Cooldowns tick down constantly
        if (_cooldownTimer > 0) _cooldownTimer -= Time.deltaTime;
        _stateTimer += Time.deltaTime;

        // 1. If we are locked in an attack or stunned, do nothing else.
        if (_isActionLocked) return;

        float distance = Vector3.Distance(transform.position, _target.position);

        // 2. DECISION LOGIC
        if (_stateTimer > stateCommitmentTime) 
        {
            EvaluateState(distance);
        }

        // 3. EXECUTION LOGIC
        ExecuteStateMovement(distance);
        
        // 4. ROTATION
        if (_currentState != AIState.Stunned) FaceTarget();
    }

    // --- THE UNIFIED BRAIN ---
    void EvaluateState(float dist)
    {
        switch (_currentState)
        {
            case AIState.Idle:
                if (dist < sensorRadius) SwitchState(AIState.Chasing);
                break;

            case AIState.Chasing:
                if (dist <= currentPersona.preferredCombatRange) SwitchState(AIState.Circling);
                break;

            case AIState.Circling:
                // Player too close?
                if (dist < retreatDistance)
                {
                    // Aggressive = Punish. Passive = Retreat.
                    if (_cooldownTimer <= 0 && Random.value < currentPersona.aggression)
                    {
                        StartCoroutine(ExecuteAttackRoutine(0)); // 0 = Close Range Slash
                    }
                    else
                    {
                        SwitchState(AIState.Retreating);
                    }
                }
                // Random Attack from neutral
                else if (_cooldownTimer <= 0 && Random.value < (currentPersona.aggression * Time.deltaTime))
                {
                    int atkID = (dist > 3.5f) ? 1 : 0; // 1 = Dash, 0 = Slash
                    StartCoroutine(ExecuteAttackRoutine(atkID));
                }
                break;

            case AIState.Retreating:
                _retreatType = Random.Range(0, 2); // Pick a random retreat behavior Change later
                // --- SUB-BEHAVIOR 1: THE BAIT (33% Chance) ---
                if (_retreatType == 0)
                {
                    // If player bites the bait (chases close), PUNISH.
                    if (dist < 2.0f && _cooldownTimer <= 0)
                    {
                        StartCoroutine(ExecuteAttackRoutine(0)); // Fast Slash
                        return;
                    }
                    // Exit condition: Standard range
                    if (dist > currentPersona.preferredCombatRange) SwitchState(AIState.Circling);
                }

                // --- SUB-BEHAVIOR 2: THE RESET (33% Chance) ---
                else if (_retreatType == 1)
                {
                    // Ignore player proximity! Keep running until FAR away.
                    // This forces a complete break in combat.
                    if (dist > 6.0f) 
                    {
                        // Once we are far, we don't just circle... we switch to IDLE or CHASING
                        // to trigger a "fresh" start to the fight (e.g., a Dash Attack in Chasing)
                        SwitchState(AIState.Chasing); 
                    }
                }

                // // --- SUB-BEHAVIOR 3: RANGED SETUP (33% Chance) ---
                // else if (_retreatType == 2)
                // {
                //     // As soon as we have a TINY bit of breathing room (4m)...
                //     if (dist > 4.0f && _cooldownTimer <= 0)
                //     {
                //         // ... Launch a projectile or Dash Attack immediately!
                //         // Assuming AttackID 2 is a Ranged/Gap Closer move
                //         StartCoroutine(ExecuteAttackRoutine(2)); 
                //         return;
                //     }
                // }

                // Failsafe for all types: If stuck, panic attack
                if (_stateTimer > 3.0f && _cooldownTimer <= 0)
                {
                    StartCoroutine(ExecuteAttackRoutine(0));
                }
                break;
        }
    }

    // --- THE BODY ---
    void ExecuteStateMovement(float dist)
    {
        switch (_currentState)
        {
            case AIState.Chasing:
                _agent.isStopped = false;
                _agent.SetDestination(_target.position);
                UpdateAnim(0, 1);
                break;

            case AIState.Circling:
                _agent.isStopped = true;

                // 1. Calculate the vector pointing from Enemy -> Player
                Vector3 toPlayer = (_target.position - transform.position).normalized;

                // 2. Calculate the Tangent (The vector perpendicular to the look direction)
                // Cross Product of "Forward" and "Up" gives us "Right" relative to the connection line.
                Vector3 tangent = Vector3.Cross(toPlayer, Vector3.up);

                // 3. Handle Direction Switching (So we don't circle forever in one way)
                _strafeTimer += Time.deltaTime;
                if (_strafeTimer > _strafeChangeInterval)
                {
                    // Pick a random new direction (Left or Right)
                    _strafeDirection = (Random.value > 0.5f) ? 1f : -1f;
                    
                    // Randomize how long we circle this way (2 to 5 seconds)
                    _strafeChangeInterval = Random.Range(2.0f, 5.0f);
                    _strafeTimer = 0f;
                }

                // 4. Move along the tangent
                // Note: We use _strafeDirection to flip the vector for left/right
                Vector3 finalMove = tangent * _strafeDirection * circleSpeed * Time.deltaTime;
                
                // 5. Optional: Slight Drift Correction
                // If we are too far, blend slightly forward. If too close, blend slightly back.
                float error = dist - currentPersona.preferredCombatRange;
                
                // Add a tiny bit of forward/backward movement to correct the radius
                Vector3 correction = toPlayer * error * 0.5f * Time.deltaTime; 

                _agent.Move(finalMove + correction);
                UpdateAnim(_strafeDirection, 0); 
                break;

            case AIState.Retreating:
                _agent.isStopped = false;
                Vector3 retreatPos = transform.position + (transform.position - _target.position).normalized * 2f;
                _agent.SetDestination(retreatPos);
                UpdateAnim(0, -1);
                break;
        }
    }

    // --- COMBAT EXECUTION ---
    IEnumerator ExecuteAttackRoutine(int attackID)
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);

        _animator.SetInteger(AttackIndex, attackID);
        _animator.SetTrigger(TriggerAttack);

        yield return new WaitForSeconds(damageStartDelay);
        canDealDamage = true;
        yield return new WaitForSeconds(damageWindowDuration);
        canDealDamage = false;

        float remaining = attackAnimDuration - damageStartDelay - damageWindowDuration;
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        _cooldownTimer = Mathf.Lerp(2.0f, 0.5f, currentPersona.aggression);
        SwitchState(AIState.Circling);
        _isActionLocked = false;
    }

    // --- PARRY LOGIC ---
    public void GetParried()
    {
        // 1. Interrupt EVERYTHING
        StopAllCoroutines(); 
        
        _isActionLocked = true;
        _agent.isStopped = true;
        canDealDamage = false; // Sword is harmless immediately
        
        SwitchState(AIState.Stunned); // Important for Logic to know we are stunned

        // 2. Start the Stun Routine
        StartCoroutine(ParryReboundRoutine());
    }

    private IEnumerator ParryReboundRoutine()
    {
        // 3. REVERSE THE ANIMATION (Visual Feedback)
        // Ensure you have added the 'AttackSpeed' float parameter to your Animator!
        // Link it to the Multiplier of your Attack State in the Animator graph.
        _animator.SetFloat(AttackSpeedHash, -1.0f);

        // 4. Wait for bounce back
        yield return new WaitForSeconds(0.4f);

        // 5. FREEZE (Stun Frame)
        _animator.SetFloat(AttackSpeedHash, 0f);
        
        // 6. Stun Duration (Free hits for player)
        yield return new WaitForSeconds(1.5f);

        // 7. RECOVER
        _animator.SetFloat(AttackSpeedHash, 1.0f); // Reset speed
        _cooldownTimer = 1.0f; // Brief pause before attacking again
        
        SwitchState(AIState.Retreating); // Back off after getting wrecked
        _isActionLocked = false;
    }

    // --- HELPERS ---
    void SwitchState(AIState newState)
    {
        if (_currentState == newState) return;
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
        if (dir != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
    }
}