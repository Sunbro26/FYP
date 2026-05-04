using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AdaptiveCombatFramework 
{
    [RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
    public class SkeletonAI : MonoBehaviour, ICombatant
    {
        // --- Definitions ---
        public enum AIState { Idle, Strategizing, Maneuvering, Attacking, Retreating, Stunned }

        [System.Serializable]   
        public class AIPersona
        {
            [Tooltip("How likely the AI is to choose an attack over circling.")]
            [Range(0, 1)] public float aggression = 0.7f;

            [Tooltip("How likely the AI is to retreat when things go wrong.")]
            [Range(0, 1)] public float fear = 0.2f;

            [Tooltip("How often (in seconds) the AI re-evaluates its current strategy.")]
            public float decisionFrequency = 2.0f;

            [Tooltip("The ideal distance the AI tries to maintain when not attacking.")]
            public float preferredCombatRange = 2.5f;
        }

        [System.Serializable]
        public class EnemyAttack
        {
            [Header("Basic Info")]
            public string name;             
            [Tooltip("Matches the AttackIndex integer in the Animator Controller.")]
            public int animationIndex;      

            [Header("Range Logic")]
            [Tooltip("The distance required to trigger this attack.")]
            public float optimalRange;      
            [Tooltip("Allowable distance error for this attack.")]
            public float rangeTolerance = 0.5f; 
            [Tooltip("Priority weight for the random selector (higher = more frequent).")]
            public float weight = 1.0f;
            [Tooltip("Can the player parry this specific move?")]
            public bool isParriable = true;
            
            [Header("Timing (Seconds)")]
            public float windUpTime;      
            public float damageDuration;  
            public float totalDuration;

            [Header("Logic")]
            [Tooltip("Cooldown period before this specific attack can be used again.")]
            public float cooldown = 4.0f; 
            [HideInInspector] public float lastTimeUsed = -999f; 

            [Header("Quirks")]
            [Tooltip("If true, AI keeps rotating toward player during the wind-up.")]
            public bool tracksPlayerDuringWindup = true;
            [Tooltip("If true, the damage hitbox originates from the foot instead of the weapon.")]
            public bool useFootHitbox = false;

            [Header("Damage Stats")]
            [Tooltip("HP damage dealt to the player.")]
            public int damage = 15;
            [Tooltip("Stamina drain if the player blocks this attack.")]
            public float blockStaminaCost = 20f;
        }

        [Header("AI Configuration")]
        [Tooltip("The 'Personality' traits governing this specific entity's behavior.")]
        public AIPersona currentPersona;

        [Header("Detection & Movement")]
        [Tooltip("Distance at which the AI becomes aware of the player.")]
        public float sensorRadius = 15f;
        [Tooltip("Speed of the circling/strafing movement.")]
        public float circleSpeed = 2.5f;

        [Header("External Control (ML-Agents)")]
        [Tooltip("If true, internal logic is disabled so a Proxy Agent (ML) can drive the character.")]
        public bool useExternalAI = false; 

        [Header("Attack Library")]
        [Tooltip("The list of all available moves for this character.")]
        public List<EnemyAttack> availableAttacks; 

        [Header("Visual Debugging")]
        [Tooltip("The mesh that will flash red during the active damage window.")]
        public Renderer swordMesh; 
        [Tooltip("The transform representing the weapon hitbox center.")]
        public Transform swordBone;
        [Tooltip("The transform representing the foot hitbox center.")]
        public Transform footBone; 
        [Tooltip("Shows the hitbox sphere in the Scene view while playing.")]
        public bool showDebugGizmos = true;
        [Tooltip("Radius of the damage-dealing sphere.")]
        public float hitRadius = 0.5f;

        // --- Events for Telemetry ---
        public event System.Action<string> OnEnemyAttackAttempt; 
        public event System.Action<string> OnEnemyAttackSuccess;
        public static event System.Action OnParrySuccess; 

        // --- Internals (Hidden from Inspector) ---
        private CharacterStats _myStats;
        private Color _originalSwordColor; 
        private Material _swordMaterialInstance; 
        private string _colorPropertyName; 
        [HideInInspector] public AIState currentState;
        private NavMeshAgent _agent;
        private Animator _animator;
        [HideInInspector] public Transform _target;
        
        private EnemyAttack _plannedAttack; 
        private EnemyAttack _currentExecutingAttack; 
        
        private float _decisionTimer;
        private bool _isActionLocked = false;
        private float _strafeDirection = 1f;
        private float _strafeTimer = 0f;
        
        public bool CanDealDamage { get; set; } = false;
        private Vector2 _smoothInputVector; 

        // --- Hashes ---
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveZ = Animator.StringToHash("MoveZ");
        private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
        private static readonly int TriggerAttack = Animator.StringToHash("TriggerAttack");
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
        private static readonly int HitTrigger = Animator.StringToHash("Hit");

        private float _attackTimer = 0f;
        [HideInInspector] public int totalAttacksThisFight = 0;
        private int _skippedAttackWindows = 0;

        // Start() follows here...
        void Start()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponent<Animator>();
            _myStats = GetComponent<CharacterStats>();

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
            if (_myStats != null && _myStats.IsDead) return;

            UpdateDebugVisuals(); 
            if (_target == null) return;

            // --- PROXY CHECK ---
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

        // --- HEURISTIC DECISION LOGIC (ML Branch) ---
        void RunHeuristicDecisionLogic()
        {
            float adjustedFrequency = currentPersona.decisionFrequency * Mathf.Lerp(1.1f, 0.65f, currentPersona.aggression);
            if (_decisionTimer <= adjustedFrequency)
            {
                return;
            }

            float dist = Vector3.Distance(transform.position, _target.position);
            EnemyAttack bestMove = ChooseSmartAttack();

            if (bestMove == null)
            {
                _skippedAttackWindows = 0;
                SwitchState(AIState.Retreating);
                return;
            }

            float rangeWindow = bestMove.rangeTolerance + 1.0f;
            float rangeScore = 1f - Mathf.Clamp01(Mathf.Abs(dist - bestMove.optimalRange) / Mathf.Max(0.1f, rangeWindow));
            float commitScore = currentPersona.aggression;
            commitScore += rangeScore * 0.35f;
            commitScore += Mathf.Min(_skippedAttackWindows, 3) * 0.2f;
            commitScore -= currentPersona.fear * (dist < bestMove.optimalRange ? 0.2f : 0.1f);

            bool forcedCommit = _skippedAttackWindows >= 2;
            if (forcedCommit || commitScore >= 0.5f)
            {
                _plannedAttack = bestMove;
                _skippedAttackWindows = 0;
                SwitchState(AIState.Maneuvering);
                return;
            }

            _skippedAttackWindows++;
            _decisionTimer = 0f;

            bool shouldRetreat = currentPersona.fear > 0.6f && dist < currentPersona.preferredCombatRange;
            if (shouldRetreat)
            {
                SwitchState(AIState.Retreating);
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
            if (dist < 2.0f)
            {
                score -= currentPersona.fear * 50f; 
            }
            float penaltyMultiplier = Mathf.Lerp(10f, 2f, currentPersona.aggression);
            score -= distDiff * penaltyMultiplier;

            score += (attack.weight * 5f) * currentPersona.aggression;
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
                    float exitThreshold = currentPersona.preferredCombatRange + Mathf.Lerp(2.0f, 0.5f, currentPersona.aggression);
                    if (dist > exitThreshold) SwitchState(AIState.Strategizing);
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
        IEnumerator WaitWithAttackTimer(float duration, bool trackTarget = false)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (trackTarget)
                {
                    FaceTarget();
                }

                elapsed += Time.deltaTime;
                _attackTimer += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator ExecuteAttackRoutine()
        {
            _isActionLocked = true;
            _agent.isStopped = true;
            SwitchState(AIState.Attacking);

            _currentExecutingAttack = _plannedAttack ?? availableAttacks[0];
            _currentExecutingAttack.lastTimeUsed = Time.time;
            _attackTimer = 0f;

            OnEnemyAttackAttempt?.Invoke(_currentExecutingAttack.name);
            totalAttacksThisFight++;

            _animator.SetInteger(AttackIndex, _currentExecutingAttack.animationIndex);
            _animator.SetTrigger(TriggerAttack);

            if (_currentExecutingAttack.name == "Combo Attack")
            {
                float windup1 = 0.7f; float duration1 = 0.85f;
                float windup2 = 0.1f; float duration2 = 0.85f;
                float windup3 = 0.1f; float duration3 = 0.85f;

                yield return StartCoroutine(WaitWithAttackTimer(windup1, true));
                CanDealDamage = true;
                yield return StartCoroutine(WaitWithAttackTimer(duration1));
                CanDealDamage = false;

                yield return StartCoroutine(WaitWithAttackTimer(windup2));
                CanDealDamage = true;
                yield return StartCoroutine(WaitWithAttackTimer(duration2));
                CanDealDamage = false;

                yield return StartCoroutine(WaitWithAttackTimer(windup3));
                CanDealDamage = true;
                yield return StartCoroutine(WaitWithAttackTimer(duration3));
                CanDealDamage = false;

                float timeSpent = windup1 + duration1 + windup2 + duration2 + windup3 + duration3;
                float remaining = _currentExecutingAttack.totalDuration - timeSpent;
                if (remaining > 0f)
                {
                    yield return StartCoroutine(WaitWithAttackTimer(remaining));
                }
            }
            else
            {
                float currentWindUp = _currentExecutingAttack.windUpTime;
                float currentDamageWindow = _currentExecutingAttack.damageDuration;
                float currentTotalDuration = _currentExecutingAttack.totalDuration;

                yield return StartCoroutine(WaitWithAttackTimer(currentWindUp, _currentExecutingAttack.tracksPlayerDuringWindup));
                CanDealDamage = true;
                yield return StartCoroutine(WaitWithAttackTimer(currentDamageWindow));
                CanDealDamage = false;

                float remaining = currentTotalDuration - currentWindUp - currentDamageWindow;
                if (remaining > 0f)
                {
                    yield return StartCoroutine(WaitWithAttackTimer(remaining));
                }
            }

            float retreatChance = (1.0f - currentPersona.aggression) + currentPersona.fear;
            if (Random.value < retreatChance)
            {
                SwitchState(AIState.Retreating);
            }
            else
            {
                SwitchState(AIState.Strategizing);
                _decisionTimer = currentPersona.decisionFrequency * 0.5f;
            }

            _plannedAttack = null;
            _currentExecutingAttack = null;
            _attackTimer = 0f;
            _isActionLocked = false;
        }

        public float GetAttackProgress()
        {
            if (_currentExecutingAttack == null || _currentExecutingAttack.totalDuration <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(_attackTimer / _currentExecutingAttack.totalDuration);
        }

        public float GetAttackElapsedTime() => _attackTimer;

        // --- PARRY LOGIC ---
        public void GetParried()
        {
            StopAllCoroutines();
            _isActionLocked = true;
            _agent.isStopped = true;
            CanDealDamage = false;
            _attackTimer = 0f;
            _currentExecutingAttack = null;
            _plannedAttack = null; 
            SwitchState(AIState.Stunned); 
            OnParrySuccess?.Invoke();
            StartCoroutine(ParryReboundRoutine());
        }

        private IEnumerator ParryReboundRoutine()
        {
            _animator.ResetTrigger(TriggerAttack);
            _animator.SetFloat(AttackSpeedHash, -1.5f); 
            yield return new WaitForSeconds(0.3f);
            _animator.SetFloat(AttackSpeedHash, 1f); 
            _animator.CrossFade("Stun", 0.15f);
            yield return new WaitForSeconds(1.1f);
            _animator.CrossFade("Locomotion", 0.25f);
            _isActionLocked = false; 
            SwitchState(AIState.Retreating);
        }

        // --- FLINCH LOGIC ---
        public void TakeHit()
        {
            if (_myStats != null && _myStats.IsDead) return;

            if (_isActionLocked) return;

            StopAllCoroutines();
            _agent.isStopped = true;
            _isActionLocked = true;
            CanDealDamage = false;
            _attackTimer = 0f;
            _currentExecutingAttack = null;
            _plannedAttack = null; 

            _animator.SetTrigger(HitTrigger);
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
            if ((_myStats != null && _myStats.IsDead) || _isActionLocked || currentState == AIState.Stunned) return;

            _smoothInputVector = Vector2.Lerp(_smoothInputVector, new Vector2(strafe, forward), Time.deltaTime * 10f);
            UpdateAnim(_smoothInputVector.x, _smoothInputVector.y);

            Vector3 toPlayer = (_target.position - transform.position).normalized;
            Vector3 tangent = Vector3.Cross(toPlayer, Vector3.up);
            Vector3 moveVec = (tangent * _smoothInputVector.x * circleSpeed) + (toPlayer * _smoothInputVector.y * circleSpeed);
            _agent.Move(moveVec * Time.deltaTime);
        }

        public void RequestAttack(int attackIndex)
        {
            if ((_myStats != null && _myStats.IsDead) || _isActionLocked || currentState == AIState.Stunned) return;

            if (attackIndex >= 0 && attackIndex < availableAttacks.Count)
            {
                EnemyAttack attack = availableAttacks[attackIndex];
                if (Time.time >= attack.lastTimeUsed + attack.cooldown)
                {
                    StartAttack(attack);
                }
            }
        }

        // --- PHASE 1: ICombatant Interface Implementation ---
        public Transform GetTransform() => transform;
        public int GetIncomingDamage() => _currentExecutingAttack != null ? _currentExecutingAttack.damage : 10;
        public float GetIncomingStaminaCost() => _currentExecutingAttack != null ? _currentExecutingAttack.blockStaminaCost : 10f;
        public bool IsIncomingAttackParriable() => _currentExecutingAttack != null ? _currentExecutingAttack.isParriable : true;
        public EnemyAttack GetCurrentAttack() => _currentExecutingAttack;

        // --- HELPERS ---
        public float GetCurrentStrafe() => _animator.GetFloat(MoveX);
        public float GetCurrentForward() => _animator.GetFloat(MoveZ);
        public bool IsAttacking() => currentState == AIState.Attacking;

        void SwitchState(AIState newState) {
            if (currentState != newState) {
                currentState = newState;
                _decisionTimer = 0; } }
        
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

            float fearGap = currentPersona.fear * 3.0f;
            float targetDist = currentPersona.preferredCombatRange + fearGap;

            if (currentPersona.aggression > 0.7f) targetDist -= 0.5f;

            float dist = Vector3.Distance(transform.position, _target.position);
            float error = dist - targetDist;
            Vector3 correction = toPlayer * error * 0.5f * Time.deltaTime; 

            _agent.Move(finalMove + correction);
            UpdateAnim(_strafeDirection, 0);
        }

        void UpdateAnim(float targetX, float targetZ) 
        { 
            float currentX = _animator.GetFloat(MoveX);
            float currentZ = _animator.GetFloat(MoveZ);

            float smoothedX = Mathf.MoveTowards(currentX, targetX, Time.deltaTime * 5f);
            float smoothedZ = Mathf.MoveTowards(currentZ, targetZ, Time.deltaTime * 5f);

            _animator.SetFloat(MoveX, smoothedX); 
            _animator.SetFloat(MoveZ, smoothedZ); 
        }
        
        void FaceTarget() { 
            Vector3 d = (_target.position - transform.position).normalized; d.y=0; 
            if(d != Vector3.zero) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), Time.deltaTime * 10f); 
        }
        
        bool IsPositionedForPlan(float d) { 
            if(_plannedAttack == null) return false; 
            return Mathf.Abs(d - _plannedAttack.optimalRange) <= _plannedAttack.rangeTolerance; 
        }
        
        void UpdateDebugVisuals() { if (_swordMaterialInstance) _swordMaterialInstance.SetColor(_colorPropertyName, CanDealDamage ? Color.red : _originalSwordColor); }
        
        void OnDrawGizmos() { /* Gizmo logic can go here if needed */ }

        // --- RESET LOGIC ---
        public void ResetAI()
        {
            StopAllCoroutines();
            _isActionLocked = false;
            CanDealDamage = false;
            totalAttacksThisFight = 0;
            _currentExecutingAttack = null;
            _plannedAttack = null;
            _attackTimer = 0f;
            _decisionTimer = 0f;
            _skippedAttackWindows = 0;
            _smoothInputVector = Vector2.zero;

            UpdateAnim(0f, 0f);
            UpdateDebugVisuals();
            SwitchState(AIState.Idle);
        }
    }
}