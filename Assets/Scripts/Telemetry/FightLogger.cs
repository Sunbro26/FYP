using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Logs per-fight data to two CSV files:
///   fight_log.csv           — one row per fight, all summary metrics
///   attack_distribution.csv — one row per attack type per fight
///
/// Agent type is AUTO-DETECTED from bossAI.useExternalAI — no manual input needed.
///   useExternalAI = false  -->  logs as "Heuristic"
///   useExternalAI = true   -->  logs as "Trained"
///
/// Only personaLabel needs to be set manually in Inspector.
///
/// SETUP IN UNITY:
///   4. Set personaLabel to "Aggressive" or "Defensive" before each session.
///   5. Agent type is read automatically from boss — no other changes needed.
/// </summary>
public class FightLogger : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Telemetry telemetry;
    [SerializeField] private SkeletonAI bossAI;
    [SerializeField] private CharacterStats playerStats;
    [SerializeField] private CharacterStats bossStats;
    [SerializeField] private SurveyUI surveyUI;

    [Header("Persona Label — only thing to change manually")]
    [Tooltip("Type exactly: Aggressive   or   Defensive")]
    [SerializeField] private string personaLabel = "Aggressive";

    [Header("Timing")]
    [Tooltip("Seconds after fight ends before survey appears. You Died shows during this window.")]
    [SerializeField] private float surveyDelay = 6f;

    // Internal fight state 
    private string _agentType = "Heuristic"; // auto-detected
    private float _fightStartTime;
    private float _playerSurvivalTime;
    private bool _sessionActive = false;
    private bool _playerDiedThisFight = false;
    private bool _fightEndTriggered = false;
    private int _sessionNumber = 0;

    //  Sampling for fight-wide averages 
    private float _distanceSampleSum = 0f;
    private int _distanceSampleCount = 0;
    private int _bossRetreatCount = 0;
    private SkeletonAI.AIState _lastBossState = SkeletonAI.AIState.Idle;

    //  CSV paths 
    private string _fightCsvPath;
    private string _attackCsvPath;

    private const string FIGHT_CSV_HEADER =
        "session,agent_type,persona,aggression,fear," +
        "fight_duration,player_survived,player_survival_time," +
        "player_hp_pct,boss_hp_pct," +
        "avg_distance," +
        "boss_attack_count,boss_atk_per_sec," +
        "boss_retreat_count,boss_retreat_per_sec," +
        "player_attacks,player_dodges," +
        "player_parries,player_blocks," +
        "damage_dealt,damage_received";

    private const string ATTACK_CSV_HEADER =
        "session,agent_type,persona,attack_name,count,proportion";

    void Awake()
    {
        _fightCsvPath = Path.Combine(Application.persistentDataPath, "fight_log.csv");
        _attackCsvPath = Path.Combine(Application.persistentDataPath, "attack_distribution.csv");

        if (!File.Exists(_fightCsvPath))
        {
            File.WriteAllText(_fightCsvPath, FIGHT_CSV_HEADER + "\n", Encoding.UTF8);
            _sessionNumber = 0;
        }
        else
        {
            string[] lines = File.ReadAllLines(_fightCsvPath);
            _sessionNumber = Mathf.Max(0, lines.Length - 1);
        }

        if (!File.Exists(_attackCsvPath))
            File.WriteAllText(_attackCsvPath, ATTACK_CSV_HEADER + "\n", Encoding.UTF8);

        Debug.Log($"[FightLogger] Starting from fight #{_sessionNumber + 1}");
    }

    void Start()
    {
        BeginFight();
    }

    void Update()
    {
        if (!_sessionActive || _fightEndTriggered) return;

        // Track player first death moment for survival time
        if (playerStats != null && playerStats.IsDead && !_playerDiedThisFight)
        {
            _playerDiedThisFight = true;
            _playerSurvivalTime = Time.time - _fightStartTime;
        }

        // Sample distance every frame for true fight-wide average
        if (telemetry != null)
        {
            _distanceSampleSum += telemetry.PlayerEnemyDistance_Agent;
            _distanceSampleCount++;
        }

        // Count each new entry into Retreating state
        if (bossAI != null)
        {
            if (bossAI.currentState == SkeletonAI.AIState.Retreating
                && _lastBossState != SkeletonAI.AIState.Retreating)
                _bossRetreatCount++;
            _lastBossState = bossAI.currentState;
        }

        // Detect fight end
        bool playerDead = playerStats != null && playerStats.IsDead;
        bool bossDead = bossStats != null && bossStats.IsDead;

        if (playerDead || bossDead)
        {
            _fightEndTriggered = true;
            EndFight();
        }
    }

    //  Fight Session 

    private void BeginFight()
    {

        CancelInvoke();

        // Auto-detect agent type from Behaviour Parameters
        var behaviourParams = bossAI.GetComponent
            <Unity.MLAgents.Policies.BehaviorParameters>();

        if (behaviourParams != null &&
            behaviourParams.BehaviorType ==
            Unity.MLAgents.Policies.BehaviorType.HeuristicOnly)
        {
            _agentType = "Heuristic";
        }
        else
        {
            _agentType = "Trained";
        }

        _fightStartTime = Time.time;
        _playerDiedThisFight = false;
        _playerSurvivalTime = 0f;
        _fightEndTriggered = false;
        _sessionActive = true;
        _sessionNumber++;

        // Reset sampling counters
        _distanceSampleSum = 0f;
        _distanceSampleCount = 0;
        _bossRetreatCount = 0;
        _lastBossState = SkeletonAI.AIState.Idle;

        // Reset telemetry lifetime counters for clean per-fight data
        if (telemetry != null) telemetry.ResetLifetimeCounters();

        Debug.Log($"[FightLogger] Fight {_sessionNumber} started | " +
                  $"AgentType:{_agentType} | Persona:{personaLabel}");
    }

    private void EndFight()
    {
        _sessionActive = false;

        float fightDuration = Time.time - _fightStartTime;
        bool playerWon = bossStats != null && bossStats.IsDead
                              && (playerStats == null || !playerStats.IsDead);

        // If player never died, survival time equals full fight duration
        if (!_playerDiedThisFight)
            _playerSurvivalTime = fightDuration;

        // Health at fight end
        float playerHpPct = playerStats != null
            ? (float)playerStats.currentHealth / playerStats.maxHealth : 0f;
        float bossHpPct = bossStats != null
            ? (float)bossStats.currentHealth / bossStats.maxHealth : 0f;

        // Persona values live from boss AI
        float aggression = bossAI != null ? bossAI.currentPersona.aggression : 0f;
        float fear = bossAI != null ? bossAI.currentPersona.fear : 0f;

        // Compute fight-wide averages
        float avgDistance = _distanceSampleCount > 0
            ? _distanceSampleSum / _distanceSampleCount : 0f;
        float bossRetreatFreq = fightDuration > 0f
            ? (float)_bossRetreatCount / fightDuration : 0f;

        // Write both CSV rows
        // Write both CSV rows — wrapped in try-catch inside each method
        WriteFightRow(fightDuration, playerWon, playerHpPct, bossHpPct,
                      aggression, fear, avgDistance, bossRetreatFreq);
        WriteAttackDistribution();

        // These ALWAYS execute regardless of whether CSV write succeeded
        if (surveyUI != null)
            Invoke(nameof(ShowSurveyDelayed), surveyDelay);

        Invoke(nameof(BeginFight), surveyDelay + 120f);
    }

    private void ShowSurveyDelayed()
    {
        if (surveyUI != null)
            surveyUI.ShowSurvey();
    }

    /// <summary>
    /// Called by SurveyUI after player submits responses.
    /// Cancels the long fallback timer and restarts fight after short delay.
    /// </summary>
    public void OnSurveyComplete()
    {
        CancelInvoke(nameof(BeginFight));
        CancelInvoke(nameof(ShowSurveyDelayed));
        Invoke(nameof(BeginFight), 3.5f);
    }

    private void WriteFightRow(
        float fightDuration, bool playerWon,
        float playerHpPct, float bossHpPct,
        float aggression, float fear,
        float avgDistance, float bossRetreatFreq)
    {
        if (telemetry == null)
        {
            Debug.LogWarning("[FightLogger] Telemetry null — fight row skipped.");
            return;
        }

        int bossAttacks = bossAI != null ? bossAI.totalAttacksThisFight : 0;
        float bossAtkPerSec = fightDuration > 0f
            ? (float)bossAttacks / fightDuration : 0f;

        string row = string.Format(
            "{0},{1},{2},{3:F3},{4:F3}," +
            "{5:F2},{6},{7:F2}," +
            "{8:F3},{9:F3}," +
            "{10:F2}," +
            "{11},{12:F3}," +
            "{13},{14:F3}," +
            "{15},{16}," +
            "{17},{18}," +
            "{19:F2},{20:F2}",

            _sessionNumber, _agentType, personaLabel, aggression, fear,
            fightDuration, playerWon ? 1 : 0, _playerSurvivalTime,
            playerHpPct, bossHpPct,
            avgDistance,
            bossAttacks, bossAtkPerSec,
            _bossRetreatCount, bossRetreatFreq,
            telemetry.LifetimeAttacks, telemetry.LifetimeDodges,
            telemetry.LifetimeParries, telemetry.LifetimeBlocks,
            telemetry.LifetimeDamageDealt, telemetry.LifetimeDamageReceived
        );

        try
        {
            File.AppendAllText(_fightCsvPath, row + "\n", Encoding.UTF8);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FightLogger] Could not write fight row — is the CSV open in another program? Error: {e.Message}");
        }
    }

    private void WriteAttackDistribution()
    {
        if (telemetry == null) return;

        Dictionary<string, int> dist = telemetry.GetEnemyAttackDistribution();
        if (dist == null || dist.Count == 0)
        {
            Debug.LogWarning("[FightLogger] Attack distribution empty — skipping distribution log.");
            return;
        }

        // Compute total for proportions
        int total = 0;
        foreach (var kvp in dist) total += kvp.Value;
        if (total == 0) return;

        foreach (var kvp in dist)
        {
            float proportion = (float)kvp.Value / total;
            string row = string.Format(
                "{0},{1},{2},{3},{4},{5:F4}",
                _sessionNumber,
                _agentType,
                personaLabel,
                kvp.Key,
                kvp.Value,
                proportion
            );
            try
            {
                File.AppendAllText(_attackCsvPath, row + "\n", Encoding.UTF8);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FightLogger] Could not write attack distribution — is the CSV open? Error: {e.Message}");
            }
        }

        Debug.Log($"[FightLogger] Attack distribution logged — " +
                  $"{dist.Count} attack types, total:{total}");
    }

    //  Public getters used by SurveyUI 
    public string GetPersonaLabel() => personaLabel;
    public string GetAgentType() => _agentType;
}