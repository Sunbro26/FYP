using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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
        public bool isParriable = true;
        
        [Header("Timing")]
        public float windUpTime;      
        public float damageDuration;  
        public float totalDuration;
        
        [Header("Logic")]
        public float cooldown = 4.0f; 
        [HideInInspector] public float lastTimeUsed = -999f; 

        [Header("Quirks")]
        public bool tracksPlayerDuringWindup = true;
        public bool useFootHitbox = false;

        [Header("Damage Stats")]
        public int damage = 15;
        public float blockStaminaCost = 20f;
    }   

    [Header("Configuration")]
    public AIPersona currentPersona;
    public float sensorRadius = 15f;
    public float circleSpeed = 2.5f;

    // --- PROXY AGENT MODIFICATION ---
    [Header("AI Control")]
    [Tooltip("If true, the internal heuristic logic is disabled, allowing a Proxy Agent to drive this character.")]
    public bool useExternalAI = false; 

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
    public AIState currentState;
    private NavMeshAgent _agent;
    private Animator _animator;
    public Transform _target;
    
    private EnemyAttack _plannedAttack; 
    private EnemyAttack _currentExecutingAttack; 
    
    private float _decisionTimer;
    private bool _isActionLocked = false;
    private int _retreatType = 0; 
    private float _strafeDirection = 1f;
    private float _strafeTimer = 0f;
    
    public bool canDealDamage = false;
    private Vector2 _smoothInputVector; 

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

        // --- PROXY CHECK ---
        if (useExternalAI) return; 

        if (_isActionLocked) return;

        float distance = Vector3.Distance(transform.position, _target.position);

        if (currentState == AIState.Strategizing)
        {
            _decisionTimer += Time.deltaTime;
            // Use the "Smart" Heuristic from the ML Branch
            RunHeuristicDecisionLogic();
        }
        else
        {
            ManageActiveState(distance);
        }

        ExecuteStateMovement(distance);
        if (currentState != AIState.Stunned) FaceTarget();
    }

    // --- HEURISTIC DECISION LOGIC (ML Branch) ---
    void RunHeuristicDecisionLogic()
    {
        if (_decisionTimer > currentPersona.decisionFrequency)
        {
            float dist = Vector3.Distance(transform.position, _target.position);
            
            // 1. Panic Check (Using Smart Random selection from ML Branch)
            if (dist < 1.5f && Random.value < currentPersona.aggression)
            {
                EnemyAttack panicAttack = GetRandomFastAttack();
                if (panicAttack != null)
                {
                    StartAttack(panicAttack);
                    return;
                }
            }

            // 2. Smart Choice (Utility System)
            EnemyAttack bestMove = ChooseSmartAttack();
            
            if (bestMove != null)
            {
                _plannedAttack = bestMove;
                SwitchState(AIState.Maneuvering);
            }
            else
            {
                SwitchState(AIState.Retreating);
            }
        }
    }

    // --- UTILITY LOGIC (ML Branch) ---
    EnemyAttack ChooseSmartAttack()
    {
        float currentDist = Vector3.Distance(transform.position, _target.position);
        
        List<EnemyAttack> validAttacks = new List<EnemyAttack>();
        List<float> scores = new List<float>();
        float totalScore = 0;

        foreach (var attack in availableAttacks)
        {
            // Cooldown Filter
            if (Time.time < attack.lastTimeUsed + attack.cooldown) continue;

            float score = CalculateAttackScore(attack, currentDist);
            
            if (score > 0)
            {
                float finalScore = Mathf.Pow(score, 2); 
                validAttacks.Add(attack);
                scores.Add(finalScore);
                totalScore += finalScore;
            }
        }

        if (validAttacks.Count > 0)
        {
            float randomValue = Random.Range(0, totalScore);
            float cursor = 0;
            for (int i = 0; i < validAttacks.Count; i++)
            {
                cursor += scores[i];
                if (cursor >= randomValue) return validAttacks[i];
            }
            return validAttacks[validAttacks.Count - 1]; 
        }
        return null;
    }


    float CalculateAttackScore(EnemyAttack attack, float dist)
    {
        float score = 10f; 
        float distDiff = Mathf.Abs(dist - attack.optimalRange);
        
        if (distDiff <= attack.rangeTolerance + 1.0f) 
        {
            score += 40f; 
            if (distDiff <= attack.rangeTolerance) score += 20f;
        }
        else score -= distDiff * 3f; 

        score += attack.weight * 5f; 
        return score;
    }

    EnemyAttack GetRandomFastAttack()
    {
        List<EnemyAttack> fastAttacks = availableAttacks.FindAll(a => a.windUpTime < 1.0f);
        if (fastAttacks.Count > 0) return fastAttacks[Random.Range(0, fastAttacks.Count)];
        return availableAttacks.Count > 0 ? availableAttacks[0] : null;
    }

    // --- CORE LOGIC & MOVEMENT ---
    void ManageActiveState(float dist)
    {
        switch (currentState)
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
        switch (currentState)
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

    // --- ATTACK EXECUTION (Merged from MultipleAttacks Branch) ---
    IEnumerator ExecuteAttackRoutine()
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);
        
        _currentExecutingAttack = _plannedAttack ?? availableAttacks[0];
        _currentExecutingAttack.lastTimeUsed = Time.time;
        
        OnEnemyAttackAttempt?.Invoke(_currentExecutingAttack.name);

        _animator.SetInteger(AttackIndex, _currentExecutingAttack.animationIndex);
        _animator.SetTrigger(TriggerAttack);

        // --- COMBO ATTACK LOGIC ---
        if (_currentExecutingAttack.name == "Combo Attack")
        {
            // Hit 1
            float windup1 = 0.7f; float duration1 = 0.85f;
            float windup2 = 0.1f; float duration2 = 0.85f;
            float windup3 = 0.1f; float duration3 = 0.85f;

            float timer = 0f;
            while (timer < windup1) 
            {
                FaceTarget(); 
                timer += Time.deltaTime;
                yield return null;
            }
            canDealDamage = true; yield return new WaitForSeconds(duration1); canDealDamage = false;
            yield return new WaitForSeconds(windup2); 
            canDealDamage = true; yield return new WaitForSeconds(duration2); canDealDamage = false;
            yield return new WaitForSeconds(windup3); 
            canDealDamage = true; yield return new WaitForSeconds(duration3); canDealDamage = false;

            float timeSpent = windup1 + duration1 + windup2 + duration2 + windup3 + duration3;
            float remaining = _currentExecutingAttack.totalDuration - timeSpent;
            if (remaining > 0) yield return new WaitForSeconds(remaining);
        }
        else 
        {
            // --- STANDARD ATTACK LOGIC ---
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

        float retreatChance = (1.0f - currentPersona.aggression) + currentPersona.fear;
        
        if (Random.value < retreatChance) 
        {
            SwitchState(AIState.Retreating);
        }
        else 
        {
            SwitchState(AIState.Strategizing);
            // Soft-reset timer so an aggressive AI can chain attacks faster
            _decisionTimer = currentPersona.decisionFrequency * 0.5f; 
        }

        _plannedAttack = null; 
        _isActionLocked = false;
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
        _animator.SetFloat(AttackSpeedHash, -1.5f); // Fast rebound
        yield return new WaitForSeconds(0.3f);
        _animator.SetFloat(AttackSpeedHash, 1f); 
        _animator.CrossFade("Stun", 0.15f);
        yield return new WaitForSeconds(1.1f);
        _animator.CrossFade("Locomotion", 0.25f);
        _isActionLocked = false; 
        SwitchState(AIState.Retreating);
    }

    // --- FLINCH LOGIC (From MultipleAttacks Branch) ---
    public void TakeHit()
    {
        // Hyper-Armor check
        if (_isActionLocked) return;

        StopAllCoroutines(); 
        _agent.isStopped = true;
        _isActionLocked = true; 

        //_animator.SetTrigger(HitTrigger);
        StartCoroutine(RecoverFromHit());
    }

    public void RegisterHit() { if (_currentExecutingAttack != null) OnEnemyAttackSuccess?.Invoke(_currentExecutingAttack.name); }
    
    private IEnumerator RecoverFromHit()
    {
        yield return new WaitForSeconds(0.5f);
        _isActionLocked = false;
        if (_agent.isOnNavMesh) _agent.isStopped = false;
        SwitchState(AIState.Retreating);
    }

    // --- PROXY METHODS (From ML Branch) ---
    public void SetMovementInput(float strafe, float forward)
    {
        if (_isActionLocked || currentState == AIState.Stunned) return;

        _smoothInputVector = Vector2.Lerp(_smoothInputVector, new Vector2(strafe, forward), Time.deltaTime * 10f);
        UpdateAnim(_smoothInputVector.x, _smoothInputVector.y);

        Vector3 toPlayer = (_target.position - transform.position).normalized;
        Vector3 tangent = Vector3.Cross(toPlayer, Vector3.up);
        Vector3 moveVec = (tangent * _smoothInputVector.x * circleSpeed) + (toPlayer * _smoothInputVector.y * circleSpeed);
        _agent.Move(moveVec * Time.deltaTime);
    }

    public void RequestAttack(int attackIndex)
    {
        if (_isActionLocked || currentState == AIState.Stunned) return;

        if (attackIndex >= 0 && attackIndex < availableAttacks.Count)
        {
            EnemyAttack attack = availableAttacks[attackIndex];
            if (Time.time >= attack.lastTimeUsed + attack.cooldown)
            {
                StartAttack(attack);
            }
        }
    }

    // --- HELPERS ---
    public EnemyAttack GetCurrentAttack() => _currentExecutingAttack;
    public float GetCurrentStrafe() => _animator.GetFloat(MoveX);
    public float GetCurrentForward() => _animator.GetFloat(MoveZ);
    public bool IsAttacking() => _isActionLocked;

    void SwitchState(AIState newState) { if (currentState != newState) { currentState = newState; _decisionTimer = 0; } }
    
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
    
    void OnDrawGizmos() { /* Gizmo logic can go here if needed */ }
}