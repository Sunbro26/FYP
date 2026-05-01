using UnityEngine;
using System.IO;
using System.Text;

/// <summary>
/// Logs one CSV row per fight session to persistent storage.
/// Triggers SurveyUI after each fight ends (with delay so You Died shows first).
/// Fight restart is independent of survey — system never gets stuck.
///
/// SETUP IN UNITY:
///  4. Set personaLabel to "Aggressive" or "Defensive" before each test session.
/// </summary>
public class FightLogger : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Telemetry telemetry;
    [SerializeField] private SkeletonAI bossAI;
    [SerializeField] private CharacterStats playerStats;
    [SerializeField] private CharacterStats bossStats;
    [SerializeField] private SurveyUI surveyUI;

    [Header("Persona Label — change this before each test session")]
    [Tooltip("Type exactly: Aggressive   or   Defensive")]
    [SerializeField] private string personaLabel = "Aggressive";

    [Header("Timing")]
    [Tooltip("Seconds after fight ends before survey appears. You Died screen shows during this time.")]
    [SerializeField] private float surveyDelay = 6f;

    // Internal fight state 
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

    private string _csvPath;

    private const string CSV_HEADER =
        "session,persona,aggression,fear," +
        "fight_duration,player_survived,player_survival_time," +
        "player_hp_pct,boss_hp_pct," +
        "avg_distance," +
        "boss_attack_count,boss_atk_per_sec," +
        "boss_retreat_count,boss_retreat_per_sec," +
        "player_attacks,player_dodges," +
        "player_parries,player_blocks," +
        "damage_dealt,damage_received";


    void Awake()
    {
        _csvPath = Path.Combine(Application.persistentDataPath, "fight_log.csv");
        if (!File.Exists(_csvPath))
        {
            File.WriteAllText(_csvPath, CSV_HEADER + "\n", Encoding.UTF8);
            Debug.Log($"[FightLogger] Created log at: {_csvPath}");
        }
        else
        {
            Debug.Log($"[FightLogger] Appending to existing log at: {_csvPath}");
        }
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
            {
                _bossRetreatCount++;
            }
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
        // Cancel any leftover pending Invokes from previous session
        CancelInvoke();

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

        Debug.Log($"[FightLogger] Fight {_sessionNumber} started — Persona: {personaLabel}");
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

        // Write CSV row immediately
        WriteRow(fightDuration, playerWon, playerHpPct, bossHpPct,
                 aggression, fear, avgDistance, bossRetreatFreq);

        Debug.Log($"[FightLogger] Fight {_sessionNumber} logged | " +
                  $"Duration:{fightDuration:F1}s | PlayerWon:{playerWon} | " +
                  $"Persona:{personaLabel} | AvgDist:{avgDistance:F1} | " +
                  $"BossAttacks:{bossAI?.totalAttacksThisFight} | " +
                  $"Retreats:{_bossRetreatCount}");

        // Show survey after delay — You Died screen shows during this window
        if (surveyUI != null)
            Invoke(nameof(ShowSurveyDelayed), surveyDelay);

        // Fight ALWAYS restarts on a long fallback timer — independent of survey
        // If player fills survey, OnSurveyComplete() cancels this and restarts sooner
        // 120 seconds gives plenty of time to fill the survey
        Invoke(nameof(BeginFight), surveyDelay + 120f);
    }

    private void ShowSurveyDelayed()
    {
        if (surveyUI != null)
            surveyUI.ShowSurvey();
    }

    /// <summary>
    /// Called by SurveyUI after player submits responses.
    /// Cancels the long fallback timer and restarts fight immediately.
    /// </summary>
    public void OnSurveyComplete()
    {
        // Cancel both the fallback BeginFight and any pending survey show
        CancelInvoke(nameof(BeginFight));
        CancelInvoke(nameof(ShowSurveyDelayed));

        // Restart after GameManager has finished its reset sequence
        Invoke(nameof(BeginFight), 3.5f);
    }

    //  CSV Write 

    private void WriteRow(
        float fightDuration, bool playerWon,
        float playerHpPct, float bossHpPct,
        float aggression, float fear,
        float avgDistance, float bossRetreatFreq)
    {
        if (telemetry == null)
        {
            Debug.LogWarning("[FightLogger] Telemetry null — row skipped.");
            return;
        }

        int bossAttacks = bossAI != null ? bossAI.totalAttacksThisFight : 0;
        float bossAtkPerSec = fightDuration > 0f
            ? (float)bossAttacks / fightDuration : 0f;

        string row = string.Format(
            "{0},{1},{2:F3},{3:F3}," +
            "{4:F2},{5},{6:F2}," +
            "{7:F3},{8:F3}," +
            "{9:F2}," +
            "{10},{11:F3}," +
            "{12},{13:F3}," +
            "{14},{15}," +
            "{16},{17}," +
            "{18:F2},{19:F2}",

            _sessionNumber, personaLabel, aggression, fear,
            fightDuration, playerWon ? 1 : 0, _playerSurvivalTime,
            playerHpPct, bossHpPct,
            avgDistance,
            bossAttacks, bossAtkPerSec,
            _bossRetreatCount, bossRetreatFreq,
            telemetry.LifetimeAttacks, telemetry.LifetimeDodges,
            telemetry.LifetimeParries, telemetry.LifetimeBlocks,
            telemetry.LifetimeDamageDealt, telemetry.LifetimeDamageReceived
        );

        File.AppendAllText(_csvPath, row + "\n", Encoding.UTF8);
    }

    //  Public getter used by SurveyUI 
    public string GetPersonaLabel() => personaLabel;
}