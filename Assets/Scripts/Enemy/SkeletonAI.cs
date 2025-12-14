using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.AI;
// --- ML-AGENTS IMPORTS ---
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;

[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class SkeletonAI : Agent 
{
    // --- Definitions ---
    public enum AIState
    {
        Idle,
        Strategizing,
        Maneuvering,
        Attacking,
        Retreating,
        Stunned
    }

    [System.Serializable]   
    public class AIPersona
    {
        [Range(0, 1)] public float aggression = 0.7f;
        [Range(0, 1)] public float fear = 0.2f;
        public float decisionFrequency = 2.0f;
        public float preferredCombatRange = 2.5f;
    }

    [Serializable]
    public class EnemyAttack
    {
        public string name;             
        public int animationIndex;      
        public float optimalRange;      
        public float rangeTolerance = 0.5f; 
        public float weight = 1.0f;     
        
        [Header("Timing")]
        public float windUpTime;      
        public float damageDuration;  
        public float totalDuration;   

        [Header("Quirks")]
        [Tooltip("Uncheck this for 360 spins so the skeleton doesn't snap to player while spinning.")]
        public bool tracksPlayerDuringWindup = true;

        [Tooltip("Check this for Kicks to visualize the hitbox on the Foot instead of Sword.")]
        public bool useFootHitbox = false;
    }   

    [Header("Configuration")]
    public AIPersona currentPersona;
    public float sensorRadius = 15f;
    public float circleSpeed = 2.5f;

    [Header("Attack Library")]
    public List<EnemyAttack> availableAttacks; 

    [Header("ML Integrations")]
    public Telemetry telemetrySystem; 
    public MultiGAILManager multiGAILManager; 
    [Tooltip("If true, uses MultiGAIL reward signal. If false, uses standard sparse rewards.")]
    public bool useMultiGAILReward = true;

    // --- Visual Debugging ---
    [Header("Visual Debugging")]
    public Renderer swordMesh; 
    public Transform swordBone;
    public Transform footBone; 
    public bool showDebugGizmos = true;
    public float hitRadius = 0.5f;

    // --- Events for Telemetry ---
    public event System.Action<string> OnEnemyAttackAttempt; 
    public event System.Action<string> OnEnemyAttackSuccess;
    // Static event for when the PLAYER successfully parries THIS enemy
    public static event System.Action OnParrySuccess; 

    // --- Private Debug Variables ---
    private Color _originalSwordColor; 
    private Material _swordMaterialInstance; 
    private string _colorPropertyName; 

    // --- State Variables ---
    private AIState _currentState;
    private NavMeshAgent _agent;
    private Animator _animator;
    private Transform _target;
    
    private EnemyAttack _plannedAttack; 
    private EnemyAttack _currentExecutingAttack; 
    
    private float _decisionTimer;
    private bool _isActionLocked = false;
    private int _retreatType = 0; 

    private float _strafeDirection = 1f;
    private float _strafeTimer = 0f;

    public bool canDealDamage = false;

    // --- Hashes --
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveZ = Animator.StringToHash("MoveZ");
    private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
    private static readonly int TriggerAttack = Animator.StringToHash("TriggerAttack");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");

    // --- ML-AGENTS: INITIALIZATION ---
    public override void Initialize()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p) _target = p.transform;

        if (availableAttacks.Count == 0) Debug.LogError("Add Attacks to the List in Inspector!");
        
        // Safe Shader Setup
        // Safe Shader Setup
        if (swordMesh != null) 
        {
            _swordMaterialInstance = swordMesh.material;
            // Handle URP/Standard shader property names
            if (_swordMaterialInstance.HasProperty("_Color")) _colorPropertyName = "_Color";
            else if (_swordMaterialInstance.HasProperty("_BaseColor")) _colorPropertyName = "_BaseColor";
            else if (_swordMaterialInstance.HasProperty("_MainColor")) _colorPropertyName = "_MainColor";
            
            if (!string.IsNullOrEmpty(_colorPropertyName))
                _originalSwordColor = _swordMaterialInstance.GetColor(_colorPropertyName);
        }

        SwitchState(AIState.Idle);
    }

    // --- ML-AGENTS: OBSERVATIONS ---
    public override void CollectObservations(VectorSensor sensor)
    {
        if (_target == null || telemetrySystem == null)
        {
            // Fallback (20 floats total)
            for(int i=0; i<20; i++) sensor.AddObservation(0f);
            return;
        }

        // 1. Internal State (2)
        sensor.AddObservation((float)_currentState); 
        sensor.AddObservation(canDealDamage);

        // 2. Physical Observation (7)
        float distance = Vector3.Distance(transform.position, _target.position);
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward); 
        sensor.AddObservation((_target.position - transform.position).normalized);

        // 3. Telemetry Data (Player Modeling) (11 floats)
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistance_Agent);
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent);
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);
        sensor.AddObservation(telemetrySystem.StaminaUsageRate_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageDealt_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageReceived_Agent);
        sensor.AddObservation(telemetrySystem.AttackSuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.ParrySuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.DodgeSuccessRate_Agent);
        sensor.AddObservation((float)telemetrySystem.TotalAttacks_Agent);
                // Add Success Rate for EACH available attack in order
        foreach (var attack in availableAttacks)
        {
            float rate = telemetrySystem.GetEnemyAttackSuccessRate(attack.name);
            sensor.AddObservation(rate);
        }

    }

    // --- ML-AGENTS: ACTIONS ---
    public override void OnActionReceived(ActionBuffers actions)
    {
        int decision = actions.DiscreteActions[0];
        
        // --- Execute Logic based on Decision ---
        // 0 = Circle/Wait
        // 1..N = Attacks
        // Last = Retreat

        if (_currentState == AIState.Strategizing)
        {
            if (decision == 0)
            {
                // Keep Circling
            }
            else if (decision <= availableAttacks.Count)
            {
                // Plan Attack
                int attackIdx = decision - 1;
                _plannedAttack = availableAttacks[attackIdx];
                SwitchState(AIState.Maneuvering);
            }
            else
            {
                // Force Retreat
                SwitchState(AIState.Retreating);
            }
        }

        // --- MultiGAIL Reward ---
        if (useMultiGAILReward && multiGAILManager != null && _target != null)
        {
            // Reconstruct the observation list for the Critic
            // (In production, cache this list to avoid allocation)
            float distance = Vector3.Distance(transform.position, _target.position);
            List<float> currentObs = new List<float>
            {
                (float)_currentState,
                canDealDamage ? 1f : 0f,
                distance,
                transform.forward.x, transform.forward.y, transform.forward.z,
                (_target.position - transform.position).normalized.x,
                (_target.position - transform.position).normalized.y,
                (_target.position - transform.position).normalized.z,
                telemetrySystem.PlayerEnemyDistance_Agent,
                telemetrySystem.PlayerEnemyDistanceChange_Agent,
                telemetrySystem.PlayerHealthPercentage_Agent,
                telemetrySystem.PlayerStaminaPercentage_Agent,
                telemetrySystem.StaminaUsageRate_Agent,
                telemetrySystem.RecentDamageDealt_Agent,
                telemetrySystem.RecentDamageReceived_Agent,
                telemetrySystem.AttackSuccessRate_Agent,
                telemetrySystem.ParrySuccessRate_Agent,
                telemetrySystem.DodgeSuccessRate_Agent,
                (float)telemetrySystem.TotalAttacks_Agent
            };

            float styleReward = multiGAILManager.CalculateStyleReward(currentObs, decision);
            AddReward(styleReward);
        }
    }

    // --- ML-AGENTS: HEURISTIC (The Smart Teacher) ---
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = 0; // Default: Wait

        // Only decide if timer is ready
        // Only decide if timer is ready
        if (_currentState == AIState.Strategizing && _decisionTimer > currentPersona.decisionFrequency)
        {
            float dist = 0f;
            if (_target) dist = Vector3.Distance(transform.position, _target.position);
            
            // 1. Interrupt: Panic/Whiff Punish
            if (dist < 1.5f && UnityEngine.Random.value < currentPersona.aggression)
            {
                discreteActions[0] = 1; // Force Basic Attack (Assumed Index 0 + 1)
                return;
            }

            // 2. Utility-Based Choice (Smart Selection)
            EnemyAttack bestMove = ChooseSmartAttack();
            
            if (bestMove != null)
            {
                int listIndex = availableAttacks.IndexOf(bestMove);
                discreteActions[0] = listIndex + 1;
            }
            else
            {
                // Fallback or Retreat
                discreteActions[0] = availableAttacks.Count + 1; // Retreat Index
            }
        }
    }

    // --- UTILITY SYSTEM FOR HEURISTIC ---
    EnemyAttack ChooseSmartAttack()
    {
        float currentDist = Vector3.Distance(transform.position, _target.position);
        
        EnemyAttack bestAttack = null;
        float bestScore = -999f;

        foreach (var attack in availableAttacks)
        {
            float score = CalculateAttackScore(attack, currentDist);
            
            // Add Noise to prevent robotic perfection
            score += UnityEngine.Random.Range(-5f, 5f); 

            if (score > bestScore)
            {
                bestScore = score;
                bestAttack = attack;
            }
        }
        return bestAttack;
    }

    float CalculateAttackScore(EnemyAttack attack, float dist)
    {
        float score = 0f;

        // 1. Range Scoring
        float distDiff = Mathf.Abs(dist - attack.optimalRange);
        if (distDiff <= attack.rangeTolerance) score += 50f;
        else score -= distDiff * 2f; 

        // 2. Context Scoring (Kiting vs Brawling)
        if (dist > 5.0f && attack.optimalRange > 4.0f) score += 30f;
        if (dist < 2.0f && attack.optimalRange < 2.5f) score += 20f;

        // 3. Telemetry Reading (The "Intelligence")
        if (telemetrySystem != null)
        {
            // Punish Low Stamina
            if (telemetrySystem.PlayerStaminaPercentage_Agent < 0.3f && attack.windUpTime < 0.5f) 
                score += 15f;

            // Punish Parry Spam with Slow Attacks
            if (telemetrySystem.ParrySuccessRate_Agent < 0.3f && telemetrySystem.TotalParries_Agent > 3)
            {
                if (attack.damageDuration > 0.5f) score += 20f;
            }
        }

        score += attack.weight; // Designer bias
        return score;
    }

    // --- UNITY UPDATE LOOP ---
    void Update()
    {
        UpdateDebugVisuals(); 

        if (_target == null) return;
        if (_isActionLocked) return;

        float distance = Vector3.Distance(transform.position, _target.position);

        // 1. BRAIN
        if (_currentState == AIState.Strategizing)
        {
            _decisionTimer += Time.deltaTime;
            // Ask for a decision every frame while strategizing
            RequestDecision();
        }
        else
        {
            // For other states (Maneuvering, Retreating), run internal logic
            ManageActiveState(distance);
        }

        // 2. BODY
        ExecuteStateMovement(distance);

        if (_currentState != AIState.Stunned) FaceTarget();
    }

    // --- LOGIC: State Management ---
    void ManageActiveState(float dist)
    {
        switch (_currentState)
        {
            case AIState.Idle:
                if (dist < sensorRadius) SwitchState(AIState.Strategizing);
                break;

            case AIState.Maneuvering:
                if (IsPositionedForPlan(dist))
                {
                    StartCoroutine(ExecuteAttackRoutine());
                }
                // Failsafe
                _decisionTimer += Time.deltaTime;
                if (_decisionTimer > 5.0f)
                {
                    _plannedAttack = null;
                    SwitchState(AIState.Strategizing);
                }
                break;

            case AIState.Retreating:
                if (_retreatType == 0) // Bait
                {
                    if (dist < 2.0f) { StartCoroutine(ExecuteAttackRoutine()); return; }
                    if (dist > currentPersona.preferredCombatRange) SwitchState(AIState.Strategizing);
                }
                else if (_retreatType == 1) // Reset
                {
                    if (dist > 7.0f) SwitchState(AIState.Strategizing);
                }
                
                // Panic check for stuck retreating
                if (_decisionTimer > 3.0f) StartCoroutine(ExecuteAttackRoutine());
                _decisionTimer += Time.deltaTime;
                break;
        }
    }

    // --- THE BODY ---
    void ExecuteStateMovement(float dist)
    {
        switch (_currentState)
        {
            case AIState.Strategizing:
                _agent.isStopped = true;
                HandleCirclingMovement();
                break;

            case AIState.Maneuvering:
                if (_plannedAttack == null) return;
                _agent.isStopped = false;
                
                float targetRange = _plannedAttack.optimalRange;

                if (dist > targetRange + _plannedAttack.rangeTolerance)
                {
                    _agent.SetDestination(_target.position);
                    UpdateAnim(0, 1);
                }
                else if (dist < targetRange - _plannedAttack.rangeTolerance)
                {
                    Vector3 fleeDir = (transform.position - _target.position).normalized;
                    _agent.SetDestination(transform.position + fleeDir * 2f);
                    UpdateAnim(0, -1);
                }
                else
                {
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

    
    // --- ATTACK EXECUTION ---
    IEnumerator ExecuteAttackRoutine()
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);
        
        _currentExecutingAttack = _plannedAttack ?? availableAttacks[0];
        
        // Log Attempt Telemetry
        OnEnemyAttackAttempt?.Invoke(_currentExecutingAttack.name);

        _animator.SetInteger(AttackIndex, _currentExecutingAttack.animationIndex);
        _animator.SetTrigger(TriggerAttack);

        float currentWindUp = _currentExecutingAttack.windUpTime;
        float currentDamageWindow = _currentExecutingAttack.damageDuration;
        float currentTotalDuration = _currentExecutingAttack.totalDuration;

        // Windup Phase
        float timer = 0f;
        while (timer < currentWindUp) 
        {
            if (_currentExecutingAttack.tracksPlayerDuringWindup) FaceTarget();
            timer += Time.deltaTime;
            yield return null;
        }

        // Damage Phase
        canDealDamage = true;
        yield return new WaitForSeconds(currentDamageWindow);
        canDealDamage = false;

        // Recovery Phase
        float remaining = currentTotalDuration - currentWindUp - currentDamageWindow;
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        // Post-Attack Decision
        if (UnityEngine.Random.value < currentPersona.fear)
        {
            _retreatType = 1; 
            SwitchState(AIState.Retreating);
        }
        else
        {
            SwitchState(AIState.Strategizing);
        }

        _plannedAttack = null; 
        _currentExecutingAttack = null;
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
        OnParrySuccess?.Invoke();
        StartCoroutine(ParryReboundRoutine());
    }

    private IEnumerator ParryReboundRoutine()
    {
        _animator.ResetTrigger(TriggerAttack);
        _animator.SetFloat(AttackSpeedHash, -1.0f);
        yield return new WaitForSeconds(0.4f);
        _animator.SetFloat(AttackSpeedHash, 0f);
        yield return new WaitForSeconds(1.5f);
        _animator.SetFloat(AttackSpeedHash, 1.0f);
        _animator.CrossFade("Locomotion", 0.2f);
        _retreatType = 1; 
        SwitchState(AIState.Retreating); 
        _isActionLocked = false;
    }

    // --- HIT REGISTRATION ---
    public void RegisterHit()
    {
        if (_currentExecutingAttack != null)
        {
            OnEnemyAttackSuccess?.Invoke(_currentExecutingAttack.name);
        }
    }

    // --- HELPERS ---
    
    // Default fallback if logic fails
    EnemyAttack ChooseRandomAttack()
    {
        float totalWeight = 0;
        foreach (var atk in availableAttacks) totalWeight += atk.weight;

        float randomValue = UnityEngine.Random.Range(0, totalWeight);
        float cursor = 0;

        foreach (var atk in availableAttacks)
        {
            cursor += atk.weight;
            if (cursor >= randomValue) return atk;
        }
        return availableAttacks[0];
    }

    bool IsPositionedForPlan(float dist)
    {
        if (_plannedAttack == null) return false;
        return Mathf.Abs(dist - _plannedAttack.optimalRange) <= _plannedAttack.rangeTolerance;
    }

    void HandleCirclingMovement()
    {
        Vector3 toPlayer = (_target.position - transform.position).normalized;
        Vector3 tangent = Vector3.Cross(toPlayer, Vector3.up);

        _strafeTimer += Time.deltaTime;
        if (_strafeTimer > 3.0f)
        {
            _strafeDirection = (UnityEngine.Random.value > 0.5f) ? 1f : -1f;
            _strafeTimer = 0f;
        }

        Vector3 finalMove = tangent * _strafeDirection * circleSpeed * Time.deltaTime;
        float dist = Vector3.Distance(transform.position, _target.position);
        float error = dist - currentPersona.preferredCombatRange;
        Vector3 correction = toPlayer * error * 0.5f * Time.deltaTime; 

        _agent.Move(finalMove + correction);
        UpdateAnim(_strafeDirection, 0);
    }

    void SwitchState(AIState newState)
    {
        if (_currentState == newState) return;
        _currentState = newState;
        _decisionTimer = 0; 
        if (newState == AIState.Retreating) _retreatType = (UnityEngine.Random.value > 0.5f) ? 0 : 1; 
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

    void UpdateDebugVisuals()
    {
        if (_swordMaterialInstance == null || string.IsNullOrEmpty(_colorPropertyName)) return;
        _swordMaterialInstance.SetColor(_colorPropertyName, canDealDamage ? Color.red : _originalSwordColor);
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        Transform activeBone = swordBone; 
        if (_currentExecutingAttack != null && _currentExecutingAttack.useFootHitbox) activeBone = footBone;
        if (activeBone == null) return;

        Gizmos.color = canDealDamage ? new Color(1, 0, 0, 0.5f) : Color.yellow;
        Gizmos.DrawWireSphere(activeBone.position, hitRadius);
    }

}
