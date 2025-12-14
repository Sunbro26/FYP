using System.Collections;
using System.Collections.Generic;
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

    // --- Visual Debugging ---
    [Header("Visual Debugging")]
    [Tooltip("Drag the MeshRenderer of the Sword here to see it flash Red.")]
    public Renderer swordMesh; 
    
    [Tooltip("Drag the Sword Bone (Hand) here.")]
    public Transform swordBone;
    
    [Tooltip("Drag the Foot Bone here (For Kicks).")]
    public Transform footBone; 

    [Tooltip("Check this to see the hitbox wireframe.")]
    public bool showDebugGizmos = true;
    public float hitRadius = 0.5f;

    // --- Private Debug Variables ---
    private Color _originalSwordColor; 
    private Material _swordMaterialInstance; // Cache the material
    private string _colorPropertyName; // To store the correct shader property name

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

    // --- Hashes ---
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
        
        // --- NEW: SAFE SHADER SETUP ---
        if (swordMesh != null) 
        {
            // Creates a temporary instance of the material so we don't change the asset file
            _swordMaterialInstance = swordMesh.material;
            
            // Try to find the correct color property name
            if (_swordMaterialInstance.HasProperty("_Color")) _colorPropertyName = "_Color";
            else if (_swordMaterialInstance.HasProperty("_BaseColor")) _colorPropertyName = "_BaseColor";
            else if (_swordMaterialInstance.HasProperty("_MainColor")) _colorPropertyName = "_MainColor";
            
            // If we found a valid property, save the original color
            if (!string.IsNullOrEmpty(_colorPropertyName))
            {
                _originalSwordColor = _swordMaterialInstance.GetColor(_colorPropertyName);
            }
            else
            {
                Debug.LogWarning("Could not find a Color property on the sword shader. Red flash will be disabled.");
            }
        }

        SwitchState(AIState.Idle);
    }

    // --- ML-AGENTS: OBSERVATIONS (The Eyes) ---
    public override void CollectObservations(VectorSensor sensor)
    {
        if (_target == null || telemetrySystem == null)
        {
            // FALLBACK BLOCK (Must match the total count below: 20 floats)
            sensor.AddObservation(0f); // State
            sensor.AddObservation(0f); // Dmg
            sensor.AddObservation(0f); // Dist
            sensor.AddObservation(Vector3.zero); // Facing (3)
            sensor.AddObservation(Vector3.zero); // Dir (3)
            
            // Telemetry Fallbacks (11 floats)
            for(int i=0; i<11; i++) sensor.AddObservation(0f);
            return;
        }

        // 1. Internal State (2 floats)
        sensor.AddObservation((float)_currentState); 
        sensor.AddObservation(canDealDamage);

        // 2. Physical Observation (7 floats)
        float distance = Vector3.Distance(transform.position, _target.position);
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward); 
        sensor.AddObservation((_target.position - transform.position).normalized);

        // 3. Telemetry Data (Player Modeling) (11 floats)
        // Spatial
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistance_Agent);
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent);
        
        // Resources
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);
        sensor.AddObservation(telemetrySystem.StaminaUsageRate_Agent);

        // Performance / Skill
        sensor.AddObservation(telemetrySystem.RecentDamageDealt_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageReceived_Agent);
        sensor.AddObservation(telemetrySystem.AttackSuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.ParrySuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.DodgeSuccessRate_Agent);
        
        // General Activity
        sensor.AddObservation(telemetrySystem.TotalAttacks_Agent); // Or TotalAPM, depending on preference
    }

    // --- ML-AGENTS: ACTIONS (The Brain's Decision) ---
    public override void OnActionReceived(ActionBuffers actions)
    {
        int decision = actions.DiscreteActions[0];
        float distance = 0f;
        if (_target != null) distance = Vector3.Distance(transform.position, _target.position);

        // --- Execute Logic based on ML Decision ---
        if (_currentState == AIState.Strategizing)
        {
            if (decision == 0)
            {
                // Wait/Circle
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
                // Retreat
                SwitchState(AIState.Retreating);
            }
        }

        // --- REWARD CALCULATION (MultiGAIL) ---
        if (useMultiGAILReward && multiGAILManager != null)
        {
            // Reconstruct observations list manually for the Critic
            List<float> currentObs = new List<float>();
            
            // 1. Internal
            currentObs.Add((float)_currentState);
            currentObs.Add(canDealDamage ? 1f : 0f);
            
            // 2. Physical
            currentObs.Add(distance);
            currentObs.Add(transform.forward.x);
            currentObs.Add(transform.forward.y);
            currentObs.Add(transform.forward.z);
            Vector3 dir = (_target.position - transform.position).normalized;
            currentObs.Add(dir.x);
            currentObs.Add(dir.y);
            currentObs.Add(dir.z);

            // 3. Telemetry (Must match CollectObservations order)
            currentObs.Add(telemetrySystem.PlayerEnemyDistance_Agent);
            currentObs.Add(telemetrySystem.PlayerEnemyDistanceChange_Agent);
            currentObs.Add(telemetrySystem.PlayerHealthPercentage_Agent);
            currentObs.Add(telemetrySystem.PlayerStaminaPercentage_Agent);
            currentObs.Add(telemetrySystem.StaminaUsageRate_Agent);
            currentObs.Add(telemetrySystem.RecentDamageDealt_Agent);
            currentObs.Add(telemetrySystem.RecentDamageReceived_Agent);
            currentObs.Add(telemetrySystem.AttackSuccessRate_Agent);
            currentObs.Add(telemetrySystem.ParrySuccessRate_Agent);
            currentObs.Add(telemetrySystem.DodgeSuccessRate_Agent);
            currentObs.Add((float)telemetrySystem.TotalAttacks_Agent);

            // Calculate Style Reward
            float styleReward = multiGAILManager.CalculateStyleReward(currentObs, decision);
            AddReward(styleReward);
        }
    }

    // --- ML-AGENTS: HEURISTIC (The "Old" Logic) ---
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActions = actionsOut.DiscreteActions;
        discreteActions[0] = 0; // Default: Wait

        // Only make a decision if the timer is ready
        if (_currentState == AIState.Strategizing && _decisionTimer > currentPersona.decisionFrequency)
        {
            // Logic 1: Panic Attack check
            float dist = 0f;
            if (_target) dist = Vector3.Distance(transform.position, _target.position);
            
            if (dist < 1.5f && Random.value < currentPersona.aggression)
            {
                // Force Attack 0 (Basic Slash) -> In our mapping, this is Action 1
                discreteActions[0] = 1;
                return;
            }

            // Logic 2: Standard Weighted Choice
            EnemyAttack chosen = ChooseNextAttackStrategy_Heuristic();
            int listIndex = availableAttacks.IndexOf(chosen);

            // Map list index to Action Index (Action 0 is wait, so we add 1)
            discreteActions[0] = listIndex + 1;
        }
    }

    // --- UNITY UPDATE LOOP ---
    void Update()
    {
        UpdateDebugVisuals(); 

        if (_target == null) return;
        if (_isActionLocked) return;

        float distance = Vector3.Distance(transform.position, _target.position);
        DecideStrategy(distance);
        ExecuteStateMovement(distance);

        if (_currentState != AIState.Stunned) FaceTarget();
    }

    // --- THE BRAIN ---
    void DecideStrategy(float dist)
    {
        switch (_currentState)
        {
            case AIState.Idle:
                if (dist < sensorRadius) SwitchState(AIState.Strategizing);
                break;

            case AIState.Strategizing:
                _decisionTimer += Time.deltaTime;
                if (dist < 1.5f && Random.value < currentPersona.aggression)
                {
                    _plannedAttack = availableAttacks[0]; 
                    StartCoroutine(ExecuteAttackRoutine());
                    return;
                }

                if (_decisionTimer > currentPersona.decisionFrequency)
                {
                    _plannedAttack = ChooseNextAttackStrategy();
                    SwitchState(AIState.Maneuvering);
                }
                break;

            case AIState.Maneuvering:
                if (IsPositionedForPlan(dist))
                {
                    StartCoroutine(ExecuteAttackRoutine());
                }
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
                    if (dist < 2.0f) { StartCoroutine(ExecuteAttackRoutine()); return; }
                    if (dist > currentPersona.preferredCombatRange) SwitchState(AIState.Strategizing);
                }
                else if (_retreatType == 1) // Reset
                {
                    if (dist > 7.0f) SwitchState(AIState.Strategizing);
                }
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
        
        _animator.SetInteger(AttackIndex, _currentExecutingAttack.animationIndex);
        _animator.SetTrigger(TriggerAttack);

        float currentWindUp = _currentExecutingAttack.windUpTime;
        float currentDamageWindow = _currentExecutingAttack.damageDuration;
        float currentTotalDuration = _currentExecutingAttack.totalDuration;

        float timer = 0f;
        while (timer < currentWindUp) 
        {
            if (_currentExecutingAttack.tracksPlayerDuringWindup) FaceTarget();
            timer += Time.deltaTime;
            yield return null;
            yield return null;
        }

        canDealDamage = true;
        yield return new WaitForSeconds(currentDamageWindow);
        canDealDamage = false;

        float remaining = currentTotalDuration - currentWindUp - currentDamageWindow;
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        if (Random.value < currentPersona.fear)
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

    // --- HELPERS ---
    void UpdateDebugVisuals()
    {
        // Safe check: If we never found a valid color property, do nothing.
        if (_swordMaterialInstance == null || string.IsNullOrEmpty(_colorPropertyName)) return;

        if (canDealDamage)
        {
            _swordMaterialInstance.SetColor(_colorPropertyName, Color.red);
        }
        else
        {
            _swordMaterialInstance.SetColor(_colorPropertyName, _originalSwordColor);
        }
    }

    EnemyAttack ChooseNextAttackStrategy()
    {
        float totalWeight = 0;
        foreach (var atk in availableAttacks) totalWeight += atk.weight;

        float randomValue = Random.Range(0, totalWeight);
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
            _strafeDirection = (Random.value > 0.5f) ? 1f : -1f;
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
        
        if (newState == AIState.Retreating)
            _retreatType = (Random.value > 0.5f) ? 0 : 1; 
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

    void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        Transform activeBone = swordBone; 
        if (_currentExecutingAttack != null && _currentExecutingAttack.useFootHitbox) activeBone = footBone;
        if (activeBone == null) return;

        if (canDealDamage)
        {
            Gizmos.color = new Color(1, 0, 0, 0.5f); 
            Gizmos.DrawSphere(activeBone.position, hitRadius);
        }
        else
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(activeBone.position, hitRadius);
        }
    }
}