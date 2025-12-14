using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Expanded Telemetry system.
/// Tracks Performance, Resource Management, and Success Rates.
/// </summary>
public class Telemetry : MonoBehaviour
{
    // --- SECTION 1: Configuration ---

    [Header("Console Logging")]
    [SerializeField] private float logInterval = 5f;

    [Header("Object References")]
    [Tooltip("The Transform of the player character.")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("The Transform of the enemy agent.")]
    [SerializeField] private Transform enemyTransform;
    private SkeletonAI enemyAI;
    
    private CharacterStats PlayerStats; // REPLACE with your actual Stats script type
    private CharacterStats EnemyStats; // REPLACE with your actual Stats script type

    // --- SECTION 2: Public Properties for ML-Agent (Observation Vector) ---
    // These are updated every frame.

    [Header("Spatial Metrics")]
    public float PlayerEnemyDistance_Agent { get; private set; }
    public float PlayerEnemyDistanceChange_Agent { get; private set; } // + = Retreating, - = Closing

    [Header("Resource Metrics")]
    public float PlayerHealthPercentage_Agent { get; private set; }
    public float PlayerStaminaPercentage_Agent { get; private set; }
    public float StaminaUsageRate_Agent { get; private set; } // Stamina used per second

    [Header("Combat Efficiency Metrics")]
    public float RecentDamageDealt_Agent { get; private set; }
    public float RecentDamageReceived_Agent { get; private set; }
    
    [Header("Success Rates (0.0 to 1.0)")]
    public float AttackSuccessRate_Agent { get; private set; }
    public float ParrySuccessRate_Agent { get; private set; }
    public float DodgeSuccessRate_Agent { get; private set; }

    [Header("Action Counts (Absolute)")]
    public int TotalAttacks_Agent { get; private set; }
    public int TotalParries_Agent { get; private set; }
    public int TotalDodges_Agent { get; private set; }

// Store counts for each attack name
    private Dictionary<string, int> _enemyAttackAttempts = new Dictionary<string, int>();
    private Dictionary<string, int> _enemyAttackSuccesses = new Dictionary<string, int>();
    

    // --- SECTION 3: Internal State Management ---

    // Rolling Windows (for calculating rates over the last X seconds)
    private float _historyWindow = 10f; // Look back 10 seconds for "Recent" metrics

    // Event Timestamp Lists (To calculate frequency/rates)
    private List<float> _attackAttempts = new List<float>();
    private List<float> _attackSuccesses = new List<float>();
    private List<float> _parryAttempts = new List<float>();
    private List<float> _parrySuccesses = new List<float>();
    private List<float> _dodgeAttempts = new List<float>();
    private List<float> _dodgeSuccesses = new List<float>();
    
    // Damage Accumulators
    private List<KeyValuePair<float, float>> _damageDealtHistory = new List<KeyValuePair<float, float>>();
    private List<KeyValuePair<float, float>> _damageReceivedHistory = new List<KeyValuePair<float, float>>();
    private List<KeyValuePair<float, float>> _staminaUsedHistory = new List<KeyValuePair<float, float>>();

    // State for Calculations
    private Vector3 _lastPlayerPosition_ForAgent;
    private float _lastDistanceToEnemy_ForAgent;
    private float _lastStamina;
    private float _logTimer;

    // --- SECTION 4: Unity Lifecycle & Events ---

    void OnEnable()
    {
        enemyAI = enemyTransform != null ? enemyTransform.GetComponent<SkeletonAI>() : null;
        PlayerStats = playerTransform != null ? playerTransform.GetComponent<CharacterStats>() : null;
        EnemyStats = enemyTransform != null ? enemyTransform.GetComponent<CharacterStats>() : null;

        PlayerParry.OnParryAttempt += HandleParryAttempt;
        SkeletonAI.OnParrySuccess += HandleParrySuccess; // static reference as refers to player parry success

        PlayerDodge.OnDodgeAttempt += HandleDodgeAttempt;
        PlayerDodge.OnDodgeSuccess += HandleDodgeSuccess;

        PlayerStats.OnTakeDamage += HandleDamageReceived;
        EnemyStats.OnTakeDamage += HandleDamageDealt;

        enemyAI.OnEnemyAttackAttempt += HandleEnemyAttackAttempt; // non-static reference as refers to individual enemy damage
        enemyAI.OnEnemyAttackSuccess += HandleEnemyAttackSuccess;
    }

    void OnDisable()
    {
        // PlayerAttack.OnPlayerAttack -= HandleAttackAttempt;
        // ... (Unsubscribe from all)
    }

    void Start()
    {
        _lastPlayerPosition_ForAgent = playerTransform.position;
        _lastDistanceToEnemy_ForAgent = Vector3.Distance(playerTransform.position, enemyTransform.position);
        _lastStamina = PlayerStats.currentStamina;
        _logTimer = logInterval;
    }

    void Update()
    {
        // 1. Process High-Frequency Data
        UpdateSpatialMetrics();
        UpdateResourceMetrics();
        UpdateCombatRates();

        // 2. Periodic Console Log (Optional Debugging)
        _logTimer -= Time.deltaTime;
        if (_logTimer <= 0f)
        {
            LogSummaryReport();
            _logTimer = logInterval;
        }
    }

    // --- SECTION 5: Core Logic ---

    private void UpdateSpatialMetrics()
    {
        Vector3 currentPos = playerTransform.position;
        float currentDist = Vector3.Distance(currentPos, enemyTransform.position);

        PlayerEnemyDistance_Agent = currentDist;
        PlayerEnemyDistanceChange_Agent = currentDist - _lastDistanceToEnemy_ForAgent;

        _lastPlayerPosition_ForAgent = currentPos;
        _lastDistanceToEnemy_ForAgent = currentDist;
    }

    private void UpdateResourceMetrics()
    {
        // 1. Health & Stamina %
        PlayerHealthPercentage_Agent = (float)PlayerStats.currentHealth / PlayerStats.maxHealth;
        PlayerStaminaPercentage_Agent = PlayerStats.currentStamina / PlayerStats.maxStamina;

        // 2. Track Stamina Usage (If stamina went down, log usage)
        if (PlayerStats.currentStamina < _lastStamina)
        {
            float used = _lastStamina - PlayerStats.currentStamina;
            _staminaUsedHistory.Add(new KeyValuePair<float, float>(Time.time, used));
        }
        _lastStamina = PlayerStats.currentStamina;

        // 3. Calculate Usage Rate (Units per second over history window)
        StaminaUsageRate_Agent = CalculateAccumulatedValue(_staminaUsedHistory);
    }

    private void UpdateCombatRates()
    {
        // Cleanup old data
        CleanupList(_attackAttempts);
        CleanupList(_attackSuccesses);
        CleanupList(_parryAttempts);
        CleanupList(_parrySuccesses);
        CleanupList(_dodgeAttempts);
        CleanupList(_dodgeSuccesses);
        CleanupKVPList(_damageDealtHistory);
        CleanupKVPList(_damageReceivedHistory);
        CleanupKVPList(_staminaUsedHistory);

        // Update Counts
        TotalAttacks_Agent = _attackAttempts.Count;
        TotalParries_Agent = _parryAttempts.Count;
        TotalDodges_Agent = _dodgeAttempts.Count;

        // Update Success Ratios (Safety check for divide by zero)
        AttackSuccessRate_Agent = TotalAttacks_Agent > 0 ? (float)_attackSuccesses.Count / TotalAttacks_Agent : 0f;
        ParrySuccessRate_Agent = TotalParries_Agent > 0 ? (float)_parrySuccesses.Count / TotalParries_Agent : 0f;
        DodgeSuccessRate_Agent = TotalDodges_Agent > 0 ? (float)_dodgeSuccesses.Count / TotalDodges_Agent : 0f;

        // Update Damage Totals
        RecentDamageDealt_Agent = CalculateAccumulatedValue(_damageDealtHistory);
        RecentDamageReceived_Agent = CalculateAccumulatedValue(_damageReceivedHistory);
    }

    // --- Helpers ---

    private void CleanupList(List<float> timestamps)
    {
        float threshold = Time.time - _historyWindow;
        timestamps.RemoveAll(t => t < threshold);
    }

    private void CleanupKVPList(List<KeyValuePair<float, float>> history)
    {
        float threshold = Time.time - _historyWindow;
        history.RemoveAll(kvp => kvp.Key < threshold);
    }

    private float CalculateAccumulatedValue(List<KeyValuePair<float, float>> history)
    {
        float total = 0;
        foreach (var entry in history) total += entry.Value;
        return total;
    }

    // --- SECTION 6: Event Handlers (Call these from your other scripts) ---

    // Attacks
    public void HandleAttackAttempt() { _attackAttempts.Add(Time.time); }
    public void HandleAttackSuccess() { _attackSuccesses.Add(Time.time); }

    // Parries
    public void HandleParryAttempt() { _parryAttempts.Add(Time.time); }
    public void HandleParrySuccess() { _parrySuccesses.Add(Time.time); }

    // Dodges
    public void HandleDodgeAttempt() { _dodgeAttempts.Add(Time.time); }
    public void HandleDodgeSuccess() { _dodgeSuccesses.Add(Time.time); }

    // Damage (Pass the amount of damage taken/dealt)
    public void HandleDamageDealt(int amount) 
    { 
        _damageDealtHistory.Add(new KeyValuePair<float, float>(Time.time, amount)); 
    }
    public void HandleDamageReceived(int amount) 
    { 
        _damageReceivedHistory.Add(new KeyValuePair<float, float>(Time.time, amount)); 
    }

private void HandleEnemyAttackAttempt(string attackName)
    {
        if (!_enemyAttackAttempts.ContainsKey(attackName)) _enemyAttackAttempts[attackName] = 0;
        _enemyAttackAttempts[attackName]++;
    }

    private void HandleEnemyAttackSuccess(string attackName)
    {
        if (!_enemyAttackSuccesses.ContainsKey(attackName)) _enemyAttackSuccesses[attackName] = 0;
        _enemyAttackSuccesses[attackName]++;
    }

// Helper to get success rate for a specific attack
    public float GetEnemyAttackSuccessRate(string attackName)
    {
        if (_enemyAttackAttempts.TryGetValue(attackName, out int attempts) && attempts > 0)
        {
            int successes = _enemyAttackSuccesses.ContainsKey(attackName) ? _enemyAttackSuccesses[attackName] : 0;
            return (float)successes / attempts;
        }
        return 0f;
    }

    // --- SECTION 7: Debugging ---

    private void LogSummaryReport()
    {
        Debug.Log($"--- Telemetry Update ---");
        Debug.Log($"Health: {PlayerHealthPercentage_Agent:P0} | Stamina: {PlayerStaminaPercentage_Agent:P0}");
        Debug.Log($"Parry Efficiency: {ParrySuccessRate_Agent:P0} ({_parrySuccesses.Count}/{TotalParries_Agent})");
        Debug.Log($"Dodge Efficiency: {DodgeSuccessRate_Agent:P0} ({_dodgeSuccesses.Count}/{TotalDodges_Agent})");
        Debug.Log($"Recent Damage: Dealt {RecentDamageDealt_Agent} / Taken {RecentDamageReceived_Agent}");
        Debug.Log($"------------------------");
    }
}