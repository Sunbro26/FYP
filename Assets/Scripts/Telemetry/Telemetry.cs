using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Centralized Telemetry system.
/// Monitors Player and Enemy interaction to provide features for ML models.
/// </summary>
public class Telemetry : MonoBehaviour
{
    // --- SECTION 1: Configuration ---

    [Header("Console Logging")]
    [SerializeField] private float logInterval = 5f;

    [Header("Object References")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform enemyTransform;
    
    private SkeletonAI enemyAI;
    private CharacterStats PlayerStats; 
    private CharacterStats EnemyStats; 

    // --- SECTION 2: Public Properties for ML-Agent (The Observation Vector) ---

    [Header("Spatial & Relational Metrics")]
    public float PlayerEnemyDistance_Agent { get; private set; }
    public float PlayerEnemyDistanceChange_Agent { get; private set; } 
    public float RelativeFacing_Agent { get; private set; } // 1.0 = Face to Face, -1.0 = Back to Back

    [Header("Player Status")]
    public float PlayerHealthPercentage_Agent { get; private set; }
    public float PlayerStaminaPercentage_Agent { get; private set; }
    public float PlayerStaminaUsageRate_Agent { get; private set; } 

    [Header("Enemy Status")]
    public float EnemyHealthPercentage_Agent { get; private set; }
    public float EnemyFSMState_Agent { get; private set; } // Normalized State
    public float IsEnemyAttacking_Agent { get; private set; } // Binary trigger

    [Header("Combat Efficiency")]
    public float RecentDamageDealtByPlayer_Agent { get; private set; }
    public float RecentDamageReceivedByPlayer_Agent { get; private set; }
    
    [Header("Player Proficiency (Success Rates)")]
    public float AttackSuccessRate_Agent { get; private set; }
    public float ParrySuccessRate_Agent { get; private set; }
    public float DodgeSuccessRate_Agent { get; private set; }
    public float BlockSuccessRate_Agent { get; private set; } 

    [Header("Absolute Action Counts")]
    public int TotalAttacks_Agent { get; private set; }
    public int TotalParries_Agent { get; private set; }
    public int TotalDodges_Agent { get; private set; }
    public int TotalBlocks_Agent { get; private set; }

    // FOR QUANTITATIVE DATA (ADDITIONAL, MIGHT REMOVE AFTER CHECKING IF THIS IS CORRECT)
    // Lifetime counters — full fight totals, used by FightLogger
    public int LifetimeAttacks { get; private set; }
    public int LifetimeParries { get; private set; }
    public int LifetimeDodges { get; private set; }
    public int LifetimeBlocks { get; private set; }
    public float LifetimeDamageDealt { get; private set; }
    public float LifetimeDamageReceived { get; private set; }



    public float EnemyAttackID_Agent { get; private set; }
    public float EnemyAttackProgress_Agent { get; private set; }

    // Enemy Attack Type Tracking
    private Dictionary<string, int> _enemyAttackAttempts = new Dictionary<string, int>();
    private Dictionary<string, int> _enemyAttackSuccesses = new Dictionary<string, int>();

    // --- SECTION 3: Internal History Management ---

    private float _historyWindow = 10f; 

    private List<float> _attackAttempts = new List<float>();
    private List<float> _attackSuccesses = new List<float>();
    private List<float> _parryAttempts = new List<float>();
    private List<float> _parrySuccesses = new List<float>();
    private List<float> _dodgeAttempts = new List<float>();
    private List<float> _dodgeSuccesses = new List<float>();
    private List<float> _blockSuccesses = new List<float>();
    
    private List<KeyValuePair<float, float>> _damageDealtHistory = new List<KeyValuePair<float, float>>();
    private List<KeyValuePair<float, float>> _damageReceivedHistory = new List<KeyValuePair<float, float>>();
    private List<KeyValuePair<float, float>> _staminaUsedHistory = new List<KeyValuePair<float, float>>();

    private Vector3 _lastPlayerPosition;
    private float _lastDistanceToEnemy;
    private float _lastStamina;
    private float _logTimer;

    // --- SECTION 4: Unity Lifecycle & Events ---

    void OnEnable()
    {
        // Cache Component References
        enemyAI = enemyTransform != null ? enemyTransform.GetComponent<SkeletonAI>() : null;
        PlayerStats = playerTransform != null ? playerTransform.GetComponent<CharacterStats>() : null;
        EnemyStats = enemyTransform != null ? enemyTransform.GetComponent<CharacterStats>() : null;

        // --- Event Subscriptions ---
        PlayerAttack.OnPlayerAttack += HandleAttackAttempt;
        PlayerAttack.OnPlayerHitEnemy += HandleAttackSuccess;

        PlayerParry.OnParryAttempt += HandleParryAttempt;
        SkeletonAI.OnParrySuccess += HandleParrySuccess; // Static event from Boss script

        PlayerDodge.OnDodgeAttempt += HandleDodgeAttempt;
        PlayerDodge.OnDodgeSuccess += HandleDodgeSuccess;

        PlayerControl.OnBlockSuccess += HandleBlockSuccess;

        if (PlayerStats) PlayerStats.OnTakeDamage += HandleDamageReceived;
        if (EnemyStats) EnemyStats.OnTakeDamage += HandleDamageDealt;

        if (enemyAI)
        {
            enemyAI.OnEnemyAttackAttempt += HandleEnemyAttackAttempt;
            enemyAI.OnEnemyAttackSuccess += HandleEnemyAttackSuccess;
        }
    }

    void OnDisable()
    {
        // Prevent Memory Leaks
        PlayerAttack.OnPlayerAttack -= HandleAttackAttempt;
        PlayerAttack.OnPlayerHitEnemy -= HandleAttackSuccess;
        PlayerParry.OnParryAttempt -= HandleParryAttempt;
        SkeletonAI.OnParrySuccess -= HandleParrySuccess;
        PlayerDodge.OnDodgeAttempt -= HandleDodgeAttempt;
        PlayerDodge.OnDodgeSuccess -= HandleDodgeSuccess;
        PlayerControl.OnBlockSuccess -= HandleBlockSuccess;
        if (PlayerStats) PlayerStats.OnTakeDamage -= HandleDamageReceived;
        if (EnemyStats) EnemyStats.OnTakeDamage -= HandleDamageDealt;
        if (enemyAI)
        {
            enemyAI.OnEnemyAttackAttempt -= HandleEnemyAttackAttempt;
            enemyAI.OnEnemyAttackSuccess -= HandleEnemyAttackSuccess;
        }
    }

    void Start()
    {
        if (playerTransform == null || enemyTransform == null) return;
        _lastPlayerPosition = playerTransform.position;
        _lastDistanceToEnemy = Vector3.Distance(playerTransform.position, enemyTransform.position);
        if (PlayerStats) _lastStamina = PlayerStats.currentStamina;
        _logTimer = logInterval;
    }

    void Update()
    {
        if (playerTransform == null || enemyTransform == null) return;

        UpdateSpatialMetrics();
        UpdateResourceMetrics();
        UpdateCombatRates();

        _logTimer -= Time.deltaTime;
        if (_logTimer <= 0f)
        {
            LogSummaryReport();
            _logTimer = logInterval;
        }
    }

    // --- SECTION 5: Logic Calculations ---

    private void UpdateSpatialMetrics()
    {
        float currentDist = Vector3.Distance(playerTransform.position, enemyTransform.position);
        PlayerEnemyDistance_Agent = currentDist;
        PlayerEnemyDistanceChange_Agent = currentDist - _lastDistanceToEnemy;
        _lastDistanceToEnemy = currentDist;

        // Relative Facing (Dot Product)
        // 1.0 means player is looking directly at enemy
        RelativeFacing_Agent = Vector3.Dot(playerTransform.forward, (enemyTransform.position - playerTransform.position).normalized);
    }

    private void UpdateResourceMetrics()
    {
        if (PlayerStats)
        {
            PlayerHealthPercentage_Agent = (float)PlayerStats.currentHealth / PlayerStats.maxHealth;
            PlayerStaminaPercentage_Agent = PlayerStats.currentStamina / PlayerStats.maxStamina;

            if (PlayerStats.currentStamina < _lastStamina)
            {
                _staminaUsedHistory.Add(new KeyValuePair<float, float>(Time.time, _lastStamina - PlayerStats.currentStamina));
            }
            _lastStamina = PlayerStats.currentStamina;
            PlayerStaminaUsageRate_Agent = CalculateAccumulatedValue(_staminaUsedHistory);
        }

        if (EnemyStats)
        {
            EnemyHealthPercentage_Agent = (float)EnemyStats.currentHealth / EnemyStats.maxHealth;
        }
    }

    private void UpdateCombatRates()
    {
        // Cleanup Window
        CleanupList(_attackAttempts); CleanupList(_attackSuccesses);
        CleanupList(_parryAttempts); CleanupList(_parrySuccesses);
        CleanupList(_dodgeAttempts); CleanupList(_dodgeSuccesses);
        CleanupList(_blockSuccesses);
        CleanupKVPList(_damageDealtHistory); CleanupKVPList(_damageReceivedHistory);
        CleanupKVPList(_staminaUsedHistory);

        // Tactical Context
                if (enemyAI != null)
        {
            EnemyFSMState_Agent = (float)enemyAI.currentState / 5f;
            
            if (enemyAI.IsAttacking()) // Uses the getter we added to SkeletonAI
            {
                IsEnemyAttacking_Agent = 1f;
                
                // --- THE NEW CRITICAL DATA ---
                var currentAtk = enemyAI.GetCurrentAttack();
                if (currentAtk != null)
                {
                    // 1. Identify which attack is happening (Normalized 0-1)
                    EnemyAttackID_Agent = (float)enemyAI.availableAttacks.IndexOf(currentAtk) / enemyAI.availableAttacks.Count;
                    
                    // 2. Calculate the progress (How far into the animation are we?)
                    // This is (CurrentTimeInState / TotalDuration)
                    // You'll need to expose a timer from SkeletonAI or use Animator.GetCurrentAnimatorStateInfo
                    EnemyAttackProgress_Agent = enemyAI.GetAttackProgress(); 
                }
            }
            else
            {
                IsEnemyAttacking_Agent = 0f;
                EnemyAttackID_Agent = 0f;
                EnemyAttackProgress_Agent = 0f;
            }
        }

        // Skill Ratios
        TotalAttacks_Agent = _attackAttempts.Count;
        TotalParries_Agent = _parryAttempts.Count;
        TotalDodges_Agent = _dodgeAttempts.Count;
        TotalBlocks_Agent = _blockSuccesses.Count;

        AttackSuccessRate_Agent = TotalAttacks_Agent > 0 ? (float)_attackSuccesses.Count / TotalAttacks_Agent : 0f;
        ParrySuccessRate_Agent = TotalParries_Agent > 0 ? (float)_parrySuccesses.Count / TotalParries_Agent : 0f;
        DodgeSuccessRate_Agent = TotalDodges_Agent > 0 ? (float)_dodgeSuccesses.Count / TotalDodges_Agent : 0f;
        
        int totalIncomingHits = TotalBlocks_Agent + _damageReceivedHistory.Count;
        BlockSuccessRate_Agent = totalIncomingHits > 0 ? (float)TotalBlocks_Agent / totalIncomingHits : 0f;

        RecentDamageDealtByPlayer_Agent = CalculateAccumulatedValue(_damageDealtHistory);
        RecentDamageReceivedByPlayer_Agent = CalculateAccumulatedValue(_damageReceivedHistory);
    }

    // --- SECTION 6: Helpers & Handlers ---

    public float GetEnemyAttackSuccessRate(string attackName)
    {
        if (_enemyAttackAttempts.TryGetValue(attackName, out int attempts) && attempts > 0)
        {
            int successes = _enemyAttackSuccesses.ContainsKey(attackName) ? _enemyAttackSuccesses[attackName] : 0;
            return (float)successes / attempts;
        }
        return 0f;
    }

    private void HandleAttackAttempt() { _attackAttempts.Add(Time.time); LifetimeAttacks++; }
    private void HandleAttackSuccess() { _attackSuccesses.Add(Time.time); }
    private void HandleParryAttempt() { _parryAttempts.Add(Time.time); LifetimeParries++;  }
    private void HandleParrySuccess() { _parrySuccesses.Add(Time.time); }
    private void HandleDodgeAttempt() { _dodgeAttempts.Add(Time.time); LifetimeDodges++;  }
    private void HandleDodgeSuccess() { _dodgeSuccesses.Add(Time.time); }
    private void HandleBlockSuccess() { _blockSuccesses.Add(Time.time); LifetimeBlocks++; }
    private void HandleDamageDealt(int amount) { _damageDealtHistory.Add(new KeyValuePair<float, float>(Time.time, amount)); LifetimeDamageDealt += amount; }
    private void HandleDamageReceived(int amount) { _damageReceivedHistory.Add(new KeyValuePair<float, float>(Time.time, amount)); LifetimeDamageReceived += amount;  }

    private void HandleEnemyAttackAttempt(string name) { if (!_enemyAttackAttempts.ContainsKey(name)) _enemyAttackAttempts[name] = 0; _enemyAttackAttempts[name]++; }
    private void HandleEnemyAttackSuccess(string name) { if (!_enemyAttackSuccesses.ContainsKey(name)) _enemyAttackSuccesses[name] = 0; _enemyAttackSuccesses[name]++; }

    private void CleanupList(List<float> list) { list.RemoveAll(t => t < Time.time - _historyWindow); }
    private void CleanupKVPList(List<KeyValuePair<float, float>> list) { list.RemoveAll(kvp => kvp.Key < Time.time - _historyWindow); }
    private float CalculateAccumulatedValue(List<KeyValuePair<float, float>> list) { float t = 0; foreach (var e in list) t += e.Value; return t; }

    private void LogSummaryReport()
    {
        Debug.Log($"[TELEMETRY] P-HP: {PlayerHealthPercentage_Agent:P0} | E-HP: {EnemyHealthPercentage_Agent:P0}");
        Debug.Log($"[SKILL] Parry: {ParrySuccessRate_Agent:P0} | Dodge: {DodgeSuccessRate_Agent:P0} | Block: {BlockSuccessRate_Agent:P0}");
        Debug.Log($"[TACTICAL] Dist: {PlayerEnemyDistance_Agent:F1} | Facing: {RelativeFacing_Agent:F2} | EnemyState: {enemyAI?.currentState}");
    }

    public void ResetLifetimeCounters()
    {
        LifetimeAttacks = 0;
        LifetimeParries = 0;
        LifetimeDodges = 0;
        LifetimeBlocks = 0;
        LifetimeDamageDealt = 0f;
        LifetimeDamageReceived = 0f;
    }

}