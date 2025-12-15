using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

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
    private CharacterStats PlayerStats; 
    private CharacterStats EnemyStats; 

    // --- SECTION 2: Public Properties for ML-Agent ---

    [Header("Spatial Metrics")]
    public float PlayerEnemyDistance_Agent { get; private set; }
    public float PlayerEnemyDistanceChange_Agent { get; private set; } 

    [Header("Resource Metrics")]
    public float PlayerHealthPercentage_Agent { get; private set; }
    public float PlayerStaminaPercentage_Agent { get; private set; }
    public float StaminaUsageRate_Agent { get; private set; } 

    [Header("Combat Efficiency Metrics")]
    public float RecentDamageDealt_Agent { get; private set; }
    public float RecentDamageReceived_Agent { get; private set; }
    
    [Header("Success Rates (0.0 to 1.0)")]
    public float AttackSuccessRate_Agent { get; private set; }
    public float ParrySuccessRate_Agent { get; private set; }
    public float DodgeSuccessRate_Agent { get; private set; }
    public float BlockSuccessRate_Agent { get; private set; } // NEW

    [Header("Action Counts (Absolute)")]
    public int TotalAttacks_Agent { get; private set; }
    public int TotalParries_Agent { get; private set; }
    public int TotalDodges_Agent { get; private set; }
    public int TotalBlocks_Agent { get; private set; } // NEW

    // Enemy Attack Tracking
    private Dictionary<string, int> _enemyAttackAttempts = new Dictionary<string, int>();
    private Dictionary<string, int> _enemyAttackSuccesses = new Dictionary<string, int>();

    // --- SECTION 3: Internal State Management ---

    private float _historyWindow = 10f; 

    // Event Lists
    private List<float> _attackAttempts = new List<float>();
    private List<float> _attackSuccesses = new List<float>();
    private List<float> _parryAttempts = new List<float>();
    private List<float> _parrySuccesses = new List<float>();
    private List<float> _dodgeAttempts = new List<float>();
    private List<float> _dodgeSuccesses = new List<float>();
    private List<float> _blockSuccesses = new List<float>(); // NEW (Successful blocks only)
    
    // Accumulators
    private List<KeyValuePair<float, float>> _damageDealtHistory = new List<KeyValuePair<float, float>>();
    private List<KeyValuePair<float, float>> _damageReceivedHistory = new List<KeyValuePair<float, float>>();
    private List<KeyValuePair<float, float>> _staminaUsedHistory = new List<KeyValuePair<float, float>>();

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
        
         PlayerAttack.OnPlayerAttack += HandleAttackAttempt;
         PlayerAttack.OnPlayerHitEnemy += HandleAttackSuccess;

        PlayerParry.OnParryAttempt += HandleParryAttempt;
        SkeletonAI.OnParrySuccess += HandleParrySuccess;

        PlayerDodge.OnDodgeAttempt += HandleDodgeAttempt;
        PlayerDodge.OnDodgeSuccess += HandleDodgeSuccess;

        PlayerControl.OnBlockSuccess += HandleBlockSuccess; // NEW

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
        // Unsubscribe to prevent memory leaks!
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
        if (playerTransform == null || enemyTransform == null)
        {
            Debug.LogError("Telemetry: Missing Transforms!", this);
            return;
        }
        
        _lastPlayerPosition_ForAgent = playerTransform.position;
        _lastDistanceToEnemy_ForAgent = Vector3.Distance(playerTransform.position, enemyTransform.position);
        
        if (PlayerStats) _lastStamina = PlayerStats.currentStamina;
        _logTimer = logInterval;
    }

    void Update()
    {
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
        if (PlayerStats == null) return;

        PlayerHealthPercentage_Agent = (float)PlayerStats.currentHealth / PlayerStats.maxHealth;
        PlayerStaminaPercentage_Agent = PlayerStats.currentStamina / PlayerStats.maxStamina;

        if (PlayerStats.currentStamina < _lastStamina)
        {
            float used = _lastStamina - PlayerStats.currentStamina;
            _staminaUsedHistory.Add(new KeyValuePair<float, float>(Time.time, used));
        }
        _lastStamina = PlayerStats.currentStamina;

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
        CleanupList(_blockSuccesses); // NEW
        CleanupKVPList(_damageDealtHistory);
        CleanupKVPList(_damageReceivedHistory);
        CleanupKVPList(_staminaUsedHistory);

        // Update Counts
        TotalAttacks_Agent = _attackAttempts.Count;
        TotalParries_Agent = _parryAttempts.Count;
        TotalDodges_Agent = _dodgeAttempts.Count;
        TotalBlocks_Agent = _blockSuccesses.Count; // Total SUCCESSFUL blocks

        // Update Success Ratios
        AttackSuccessRate_Agent = TotalAttacks_Agent > 0 ? (float)_attackSuccesses.Count / TotalAttacks_Agent : 0f;
        ParrySuccessRate_Agent = TotalParries_Agent > 0 ? (float)_parrySuccesses.Count / TotalParries_Agent : 0f;
        DodgeSuccessRate_Agent = TotalDodges_Agent > 0 ? (float)_dodgeSuccesses.Count / TotalDodges_Agent : 0f;
        
        // For Block Rate, since we don't count "Block Attempts" (holding button), 
        // we can measure it as "Blocks / (Blocks + DamageTaken Events)".
        // This estimates: "Of all the times I got hit, how many did I block?"
        int totalDefenseEvents = TotalBlocks_Agent + _damageReceivedHistory.Count;
        BlockSuccessRate_Agent = totalDefenseEvents > 0 ? (float)TotalBlocks_Agent / totalDefenseEvents : 0f;

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

    // --- SECTION 6: Event Handlers ---

    public void HandleAttackAttempt() { _attackAttempts.Add(Time.time); }
    public void HandleAttackSuccess() { _attackSuccesses.Add(Time.time); }

    public void HandleParryAttempt() { _parryAttempts.Add(Time.time); }
    public void HandleParrySuccess() { _parrySuccesses.Add(Time.time); }

    public void HandleDodgeAttempt() { _dodgeAttempts.Add(Time.time); }
    public void HandleDodgeSuccess() { _dodgeSuccesses.Add(Time.time); }

    // NEW: Handle Block
    public void HandleBlockSuccess() { _blockSuccesses.Add(Time.time); }

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
        Debug.Log($"Block Rate: {BlockSuccessRate_Agent:P0} ({TotalBlocks_Agent} Blocks)");
        Debug.Log($"Parry Rate: {ParrySuccessRate_Agent:P0}");
        Debug.Log($"Dodge Rate: {DodgeSuccessRate_Agent:P0}");
        Debug.Log($"Attack Efficiency: {AttackSuccessRate_Agent:P0} ({_attackSuccesses.Count} Hits / {TotalAttacks_Agent} Attempts)");
        Debug.Log($"Recent Damage: Dealt {RecentDamageDealt_Agent} / Taken {RecentDamageReceived_Agent}");
        Debug.Log($"------------------------");
    }
}