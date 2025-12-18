using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Pure MonoBehaviour. No ML-Agents inheritance.
[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class SkeletonAI : MonoBehaviour 
{
    // --- Definitions ---
    public enum AIState { Idle, Strategizing, Maneuvering, Attacking, Retreating, Stunned }

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
        public bool tracksPlayerDuringWindup = true;
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
    public Renderer swordMesh; 
    public Transform swordBone;
    public Transform footBone; 
    public bool showDebugGizmos = true;
    public float hitRadius = 0.5f;

    // --- Events for Telemetry ---
    public event System.Action<string> OnEnemyAttackAttempt; 
    public event System.Action<string> OnEnemyAttackSuccess;
    public static event System.Action OnParrySuccess; 

    // --- Internals ---
    private Color _originalSwordColor; 
    private Material _swordMaterialInstance; 
    private string _colorPropertyName; 
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

    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponent<Animator>();
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p) _target = p.transform;

        if (availableAttacks.Count == 0) Debug.LogError("Add Attacks to the List in Inspector!");
        
        // Shader Setup
        if (swordMesh != null) 
        {
            _swordMaterialInstance = swordMesh.material;
            if (_swordMaterialInstance.HasProperty("_Color")) _colorPropertyName = "_Color";
            else if (_swordMaterialInstance.HasProperty("_BaseColor")) _colorPropertyName = "_BaseColor";
            else if (_swordMaterialInstance.HasProperty("_MainColor")) _colorPropertyName = "_MainColor";
            if (!string.IsNullOrEmpty(_colorPropertyName))
                _originalSwordColor = _swordMaterialInstance.GetColor(_colorPropertyName);
        }
        SwitchState(AIState.Idle);
    }

    void Update()
    {
        UpdateDebugVisuals(); 
        if (_target == null) return;
        if (_isActionLocked) return;

        float distance = Vector3.Distance(transform.position, _target.position);

        if (_currentState == AIState.Strategizing)
        {
            _decisionTimer += Time.deltaTime;
            // The Heuristic Decision Logic runs every frame to check timer
            RunHeuristicDecisionLogic();
        }
        else
        {
            ManageActiveState(distance);
        }

        ExecuteStateMovement(distance);
        if (_currentState != AIState.Stunned) FaceTarget();
    }

    // --- HEURISTIC DECISION LOGIC (Formerly "Heuristic()" in Agent) ---
    void RunHeuristicDecisionLogic()
    {
        if (_decisionTimer > currentPersona.decisionFrequency)
        {
            float dist = 0f;
            if (_target) dist = Vector3.Distance(transform.position, _target.position);
            
            // Panic Check
            if (dist < 1.5f && Random.value < currentPersona.aggression)
            {
                // Force Attack 0
                StartAttack(availableAttacks[0]);
                return;
            }

            // Smart Choice
            EnemyAttack bestMove = ChooseSmartAttack();
            
            if (bestMove != null)
            {
                // Plan the attack
                _plannedAttack = bestMove;
                SwitchState(AIState.Maneuvering);
            }
            else
            {
                // Retreat
                SwitchState(AIState.Retreating);
            }
        }
    }

    // --- UTILITY LOGIC ---
    EnemyAttack ChooseSmartAttack()
    {
        float currentDist = Vector3.Distance(transform.position, _target.position);
        EnemyAttack bestAttack = null;
        float bestScore = -999f;

        foreach (var attack in availableAttacks)
        {
            float score = CalculateAttackScore(attack, currentDist);
            score += Random.Range(-5f, 5f); 
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
        float distDiff = Mathf.Abs(dist - attack.optimalRange);
        if (distDiff <= attack.rangeTolerance) score += 50f; 
        else score -= distDiff * 10f; 

        if (dist > 5.0f && attack.optimalRange > 4.0f) score += 30f;
        if (dist < 2.0f && attack.optimalRange < 2.5f) score += 20f;

        score += attack.weight; 
        return score;
    }

    // --- CORE LOGIC & MOVEMENT ---
    void ManageActiveState(float dist)
    {
        switch (_currentState)
        {
            case AIState.Idle:
                if (dist < sensorRadius) SwitchState(AIState.Strategizing);
                break;
            case AIState.Maneuvering:
                if (IsPositionedForPlan(dist)) StartCoroutine(ExecuteAttackRoutine());
                _decisionTimer += Time.deltaTime;
                if (_decisionTimer > 5.0f) { _plannedAttack = null; SwitchState(AIState.Strategizing); }
                break;
            case AIState.Retreating:
                if (dist > currentPersona.preferredCombatRange + 2f) SwitchState(AIState.Strategizing);
                if (_decisionTimer > 2.5f) SwitchState(AIState.Strategizing); 
                _decisionTimer += Time.deltaTime;
                break;
        }
    }

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
                if (dist > _plannedAttack.optimalRange + 0.5f) { _agent.SetDestination(_target.position); UpdateAnim(0, 1); }
                else if (dist < _plannedAttack.optimalRange - 0.5f) { 
                    Vector3 flee = (transform.position - _target.position).normalized;
                    _agent.SetDestination(transform.position + flee); 
                    UpdateAnim(0, -1); 
                }
                else { _agent.isStopped = true; UpdateAnim(0, 0); }
                break;
            case AIState.Retreating:
                _agent.isStopped = false;
                Vector3 ret = transform.position + (transform.position - _target.position).normalized * 3f;
                _agent.SetDestination(ret);
                UpdateAnim(0, -1);
                break;
        }
    }

    void StartAttack(EnemyAttack attack)
    {
        _plannedAttack = attack;
        StartCoroutine(ExecuteAttackRoutine());
    }

    IEnumerator ExecuteAttackRoutine()
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);
        
        _currentExecutingAttack = _plannedAttack ?? availableAttacks[0];
        OnEnemyAttackAttempt?.Invoke(_currentExecutingAttack.name);

        _animator.SetInteger(AttackIndex, _currentExecutingAttack.animationIndex);
        _animator.SetTrigger(TriggerAttack);

        float timer = 0f;
        while (timer < _currentExecutingAttack.windUpTime) 
        {
            if (_currentExecutingAttack.tracksPlayerDuringWindup) FaceTarget();
            timer += Time.deltaTime;
            yield return null;
        }

        canDealDamage = true;
        yield return new WaitForSeconds(_currentExecutingAttack.damageDuration);
        canDealDamage = false;

        float remaining = _currentExecutingAttack.totalDuration - _currentExecutingAttack.windUpTime - _currentExecutingAttack.damageDuration;
        if (remaining > 0) yield return new WaitForSeconds(remaining);

        _plannedAttack = null; 
        _isActionLocked = false;
        
        if (Random.value < currentPersona.fear) SwitchState(AIState.Retreating);
        else SwitchState(AIState.Strategizing);
    }

    public void RegisterHit() { if (_currentExecutingAttack != null) OnEnemyAttackSuccess?.Invoke(_currentExecutingAttack.name); }

    public void GetParried() {
        StopAllCoroutines(); _isActionLocked = true; canDealDamage = false;
        SwitchState(AIState.Stunned); OnParrySuccess?.Invoke();
        StartCoroutine(ParryRoutine());
    }
    IEnumerator ParryRoutine() {
        _animator.ResetTrigger(TriggerAttack);
        _animator.SetFloat(AttackSpeedHash, -1f); yield return new WaitForSeconds(0.4f);
        _animator.SetFloat(AttackSpeedHash, 0f); yield return new WaitForSeconds(1.5f);
        _animator.SetFloat(AttackSpeedHash, 1f); _isActionLocked = false; SwitchState(AIState.Retreating);
    }

    // Helpers
    void SwitchState(AIState newState) { if (_currentState != newState) { _currentState = newState; _decisionTimer = 0; } }
    void HandleCirclingMovement() { /* Tangent Logic */ } // (Keeping brief, same as before)
    void UpdateAnim(float x, float z) { _animator.SetFloat(MoveX, x, 0.1f, Time.deltaTime); _animator.SetFloat(MoveZ, z, 0.1f, Time.deltaTime); }
    void FaceTarget() { 
        Vector3 d = (_target.position - transform.position).normalized; d.y=0; 
        if(d != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Time.deltaTime * 10f); 
    }
    bool IsPositionedForPlan(float d) { 
        if(_plannedAttack == null) return false; 
        return Mathf.Abs(d - _plannedAttack.optimalRange) <= _plannedAttack.rangeTolerance; 
    }
    void UpdateDebugVisuals() { if (_swordMaterialInstance) _swordMaterialInstance.SetColor(_colorPropertyName, canDealDamage ? Color.red : _originalSwordColor); }
    void OnDrawGizmos() { /* Gizmo logic */ }
}