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
    private static readonly int HitTrigger = Animator.StringToHash("Hit");

    void Start()
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

    // --- ATTACK EXECUTION (UPDATED FOR COMBO) ---
    IEnumerator ExecuteAttackRoutine()
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);
        
        // Use planned attack or fallback
        _currentExecutingAttack = _plannedAttack ?? availableAttacks[0];
        
        // Trigger Animation
        _animator.SetInteger(AttackIndex, _currentExecutingAttack.animationIndex);
        _animator.SetTrigger(TriggerAttack);

        // --- SPECIAL LOGIC: CHECK FOR COMBO ATTACK ---
        if (_currentExecutingAttack.name == "Combo Attack")
        {
            // === HARDCODED COMBO TIMING ===
            // Tweak these numbers to match your specific animation visual
            
            // HIT 1
            float windup1 = 0.7f;
            float duration1 = 0.85f;
            
            // HIT 2 (Time between end of Hit 1 and start of Hit 2)
            float windup2 = 0.1f; 
            float duration2 = 0.85f;

            // HIT 3 (Time between end of Hit 2 and start of Hit 3)
            float windup3 = 0.1f;
            float duration3 = 0.85f;

            // --- EXECUTE HIT 1 ---
            float timer = 0f;
            while (timer < windup1) 
            {
                FaceTarget(); // Track player for first hit
                timer += Time.deltaTime;
                yield return null;
            }
            canDealDamage = true;
            yield return new WaitForSeconds(duration1);
            canDealDamage = false;

            // --- EXECUTE HIT 2 ---
            // Optional: FaceTarget() here if you want tracking on second swing
            yield return new WaitForSeconds(windup2); 
            canDealDamage = true;
            yield return new WaitForSeconds(duration2);
            canDealDamage = false;

            // --- EXECUTE HIT 3 ---
            // Optional: FaceTarget() here if you want tracking on third swing
            yield return new WaitForSeconds(windup3); 
            canDealDamage = true;
            yield return new WaitForSeconds(duration3);
            canDealDamage = false;

            // --- CALCULATE REMAINING TIME ---
            // Total time spent so far
            float timeSpent = windup1 + duration1 + windup2 + duration2 + windup3 + duration3;
            float remaining = _currentExecutingAttack.totalDuration - timeSpent;
            
            if (remaining > 0) yield return new WaitForSeconds(remaining);
        }
        else 
        {
            // === STANDARD SINGLE-HIT LOGIC (Your existing code) ===
            float currentWindUp = _currentExecutingAttack.windUpTime;
            float currentDamageWindow = _currentExecutingAttack.damageDuration;
            float currentTotalDuration = _currentExecutingAttack.totalDuration;

            float timer = 0f;
            while (timer < currentWindUp) 
            {
                if (_currentExecutingAttack.tracksPlayerDuringWindup) FaceTarget();
                timer += Time.deltaTime;
                yield return null;
            }

            canDealDamage = true;
            yield return new WaitForSeconds(currentDamageWindow);
            canDealDamage = false;

            float remaining = currentTotalDuration - currentWindUp - currentDamageWindow;
            if (remaining > 0) yield return new WaitForSeconds(remaining);
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

    // --- NEW: Public Method called by PlayerAttack.cs ---
    public void TakeHit()
    {
        // HYPER-ARMOR CHECK:
        // If we are locked in an action (Attacking, Stunned, etc), DO NOT FLINCH.
        // We still take HP damage (handled by CharacterStats), but animation continues.
        if (_isActionLocked) return;

        // If we are just walking/idling/maneuvering, we get interrupted.
        StopAllCoroutines(); // Stop any movement/strategy logic
        _agent.isStopped = true;
        _isActionLocked = true; // Lock briefly for the flinch duration

        // Trigger Animation
        _animator.SetTrigger(HitTrigger);

        // Start Recovery
        StartCoroutine(RecoverFromHit());
    }

    private IEnumerator RecoverFromHit()
    {
        // Wait for length of flinch animation (approx 0.5s)
        yield return new WaitForSeconds(0.5f);

        // Reset
        _isActionLocked = false;
        if (_agent.isOnNavMesh) _agent.isStopped = false;

        // Force a tactical retreat after getting hit
        SwitchState(AIState.Retreating);
    }
}