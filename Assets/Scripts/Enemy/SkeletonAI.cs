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
        public float cooldown = 4.0f; // NEW: How long before using this again?
        [HideInInspector] public float lastTimeUsed = -999f; // Track usage

        [Header("Quirks")]
        public bool tracksPlayerDuringWindup = true;
        public bool useFootHitbox = false;
        [Header("Damage Stats")]
        [Tooltip("How much HP this attack takes if it hits.")]
        public int damage = 15;
        [Tooltip("How much Player Stamina this drains if Blocked.")]
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
        // --- PROXY AGENT MODIFICATION: Logic Guard ---
        // If an external agent is controlling us, we skip the internal decision-making and movement logic.
        if (useExternalAI) return; 
        if (_isActionLocked) return;

        float distance = Vector3.Distance(transform.position, _target.position);

        if (currentState == AIState.Strategizing)
        {
            _decisionTimer += Time.deltaTime;
            RunHeuristicDecisionLogic();
        }
        else
        {
            ManageActiveState(distance);
        }

        ExecuteStateMovement(distance);
        if (currentState != AIState.Stunned) FaceTarget();
    }

    // --- HEURISTIC DECISION LOGIC ---
    void RunHeuristicDecisionLogic()
    {
        if (_decisionTimer > currentPersona.decisionFrequency)
        {
            float dist = 0f;
            if (_target) dist = Vector3.Distance(transform.position, _target.position);
            
            // 1. Panic Check (FIXED)
            // Instead of forcing Attack[0], find a valid fast attack
            if (dist < 1.5f && Random.value < currentPersona.aggression)
            {
                EnemyAttack panicAttack = GetRandomFastAttack();
                if (panicAttack != null)
                {
                    StartAttack(panicAttack);
                    return;
                }
            }

            // 2. Smart Choice
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

    // --- UTILITY LOGIC ---
// --- UTILITY LOGIC (Updated for Variety) ---
    EnemyAttack ChooseSmartAttack()
    {
        float currentDist = Vector3.Distance(transform.position, _target.position);
        
        // Use a list to store all "Decent" options
        List<EnemyAttack> validAttacks = new List<EnemyAttack>();
        List<float> scores = new List<float>();
        float totalScore = 0;

        foreach (var attack in availableAttacks)
        {
            // Filter: Cooldown
            if (Time.time < attack.lastTimeUsed + attack.cooldown) continue;

            float score = CalculateAttackScore(attack, currentDist);
            
            // Only consider attacks that make sense (Score > 0)
            if (score > 0)
            {
                // Cube the score to emphasize good moves, but keep bad ones possible
                // e.g. Score 10 -> 1000. Score 5 -> 125. 
                // The 10 is much more likely, but 5 is still possible.
                float finalScore = Mathf.Pow(score, 2); 
                
                validAttacks.Add(attack);
                scores.Add(finalScore);
                totalScore += finalScore;
            }
        }

        // Weighted Random Selection (The Lottery)
        if (validAttacks.Count > 0)
        {
            float randomValue = Random.Range(0, totalScore);
            float cursor = 0;
            for (int i = 0; i < validAttacks.Count; i++)
            {
                cursor += scores[i];
                if (cursor >= randomValue) return validAttacks[i];
            }
            return validAttacks[validAttacks.Count - 1]; // Fallback to last
        }

        return null;
    }

    float CalculateAttackScore(EnemyAttack attack, float dist)
    {
        float score = 10f; // Base score so we don't hit 0 easily

        // 1. Range Logic (Widened tolerance)
        float distDiff = Mathf.Abs(dist - attack.optimalRange);
        
        // If within range + 1 meter, it's a good candidate
        if (distDiff <= attack.rangeTolerance + 1.0f) 
        {
            score += 40f; 
            // Bonus points for being in PERFECT range
            if (distDiff <= attack.rangeTolerance) score += 20f;
        }
        else 
        {
            // Penalty for distance, but not as harsh
            score -= distDiff * 3f; 
        }

        // 2. Add Weight (Inspector Bias)
        score += attack.weight * 5f; // Multiplied to make Inspector slider impactful

        return score;
    }

    EnemyAttack GetRandomFastAttack()
    {
        // INCREASED THRESHOLD: Now includes attacks up to 1.0s windup
        // This allows Basic Slash / Horizontal to be used in "Panic"
        List<EnemyAttack> fastAttacks = availableAttacks.FindAll(a => a.windUpTime < 1.0f);
        
        if (fastAttacks.Count > 0)
        {
            // Pick random from valid fast attacks
            return fastAttacks[Random.Range(0, fastAttacks.Count)];
        }
        
        // Fallback: Just return the first available attack (ignoring windup)
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

    IEnumerator ExecuteAttackRoutine()
    {
        _isActionLocked = true;
        _agent.isStopped = true;
        SwitchState(AIState.Attacking);
        
        _currentExecutingAttack = _plannedAttack ?? availableAttacks[0];
        
        // --- NEW: Mark as used for Cooldowns ---
        _currentExecutingAttack.lastTimeUsed = Time.time;

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
    IEnumerator ParryRoutine() 
    {
        // 1. Clear any pending triggers so they don't fire late
        _animator.ResetTrigger(TriggerAttack);

        // 2. THE REBOUND (Reverse the swing quickly)
        // We set it to -1.5f so it violently bounces backward, rather than just playing in slow reverse.
        _animator.SetFloat(AttackSpeedHash, -1.5f); 
        
        // Wait for just 0.3 seconds. This only rewinds the very end of the swing.
        yield return new WaitForSeconds(0.3f);

        // 3. RESET ATTACK SPEED
        // Vital so future attacks don't stay broken
        _animator.SetFloat(AttackSpeedHash, 1f); 

        // 4. THE STUN
        // We force the animator to abort the attack and smoothly blend into the Stun animation.
        _animator.CrossFade("Stun", 0.15f);

        // Wait for the duration of your Stun animation (Adjust this 1.5f to match your actual clip length)
        yield return new WaitForSeconds(1.1f);

        // 5. RECOVERY
        // Smoothly blend back into the moving/idle blend tree. This prevents the old attack from continuing!
        _animator.CrossFade("Locomotion", 0.25f);
        
        // Unlock AI logic
        _isActionLocked = false; 

        // Force the boss to back off after being humiliated
        SwitchState(AIState.Retreating);
    }

    void SwitchState(AIState newState) { if (currentState != newState) { currentState = newState; _decisionTimer = 0; } }
    
    // --- CIRCLING LOGIC ---
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
    
    // --- PROXY AGENT MODIFICATION: Remote Control Methods ---

    /// <summary>
    /// Allows an external script (like the Proxy Agent) to drive movement.
    /// </summary>
    private Vector2 _smoothInputVector; 
    public void SetMovementInput(float strafe, float forward)
    {
        if (_isActionLocked || currentState == AIState.Stunned) return;

        // --- THE FIX: SMOOTHING ---
        // Lerp from current value to target value over time.
        // 10f is the speed. Lower = Smoother/Sluggish. Higher = Snappier/Jittery.
        _smoothInputVector = Vector2.Lerp(_smoothInputVector, new Vector2(strafe, forward), Time.deltaTime * 10f);

        // 1. Update Animator parameters using smoothed values
        UpdateAnim(_smoothInputVector.x, _smoothInputVector.y);

        // 2. Physical Movement logic
        Vector3 toPlayer = (_target.position - transform.position).normalized;
        Vector3 tangent = Vector3.Cross(toPlayer, Vector3.up);

        // Use smoothed values for movement too
        Vector3 moveVec = (tangent * _smoothInputVector.x * circleSpeed) + (toPlayer * _smoothInputVector.y * circleSpeed);
        _agent.Move(moveVec * Time.deltaTime);
    }
    /// <summary>
    /// Allows an external script to trigger a specific attack by its list index.
    /// </summary>
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
    // Add these public getters
    public float GetCurrentStrafe() => _animator.GetFloat(MoveX);
    public float GetCurrentForward() => _animator.GetFloat(MoveZ);
    public bool IsAttacking() => _isActionLocked; // Or check state
    // --- Helper for PlayerControl to get stats ---
    public EnemyAttack GetCurrentAttack()
    {
        return _currentExecutingAttack;
    }
}
