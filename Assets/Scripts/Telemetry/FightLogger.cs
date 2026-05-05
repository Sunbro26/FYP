using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Networking;
using Unity.MLAgents.Policies;

public class FightLogger : MonoBehaviour
{
    private const string LogPrefix = "[FightLogger]";

    [Header("Cloud Logging WebGL")]
    [Tooltip("The Google Apps Script Web App URL to send data to.")]
    [SerializeField] private string cloudWebhookURL = "YOUR_GOOGLE_SCRIPT_URL_HERE";

    [Tooltip("Toggle to enable/disable sending data to the cloud.")]
    [SerializeField] private bool enableCloudLogging = true;

    // [Header("CSV File Configuration")]
    // [SerializeField] private string fightLogFileName = "fight_log.csv";
    // [SerializeField] private string attackDistFileName = "attack_distribution.csv";

    [Header("Experiment Identity")]
    [SerializeField] private string personaLabel = "Aggressive";

    [Header("Timed Experiment Logging")]
    [Tooltip("Usually false now. SurveyUI should start/stop logging phases.")]
    [SerializeField] private bool autoStartLoggingOnStart = false;

    [Tooltip("If true, each death/victory inside the 3-minute phase is logged as one fight.")]
    [SerializeField] private bool logDeathsDuringTimedSegment = true;

    [Tooltip("After a death/victory, start logging the next fight after the GameManager reset.")]
    [SerializeField] private bool autoRestartLoggingAfterDeath = true;

    [SerializeField] private float restartLoggingDelayAfterDeath = 4f;

    [Header("Required Framework References")]
    [SerializeField] private Telemetry telemetry;
    [SerializeField] private SkeletonAI bossAI;
    [SerializeField] private CharacterStats playerStats;
    [SerializeField] private CharacterStats bossStats;

    private string _agentType = "Unknown";
    private string _forcedSegmentAgentType = null;

    private float _fightStartTime;
    private float _playerSurvivalTime;

    private bool _segmentLoggingEnabled;
    private bool _sessionActive;
    private bool _playerDiedThisFight;
    private bool _fightEndTriggered;

    private int _sessionNumber;

    private float _distanceSampleSum;
    private int _distanceSampleCount;
    private int _bossRetreatCount;
    private SkeletonAI.AIState _lastBossState = SkeletonAI.AIState.Idle;

    //private string _fightCsvPath;
    //private string _attackCsvPath;

    private Coroutine _restartRoutine;

    private const string FIGHT_CSV_HEADER =
        "session,agent_type,persona,aggression,fear," +
        "fight_duration,player_survived,player_survival_time," +
        "player_hp_pct,boss_hp_pct,avg_distance," +
        "boss_attack_count,boss_atk_per_sec," +
        "boss_retreat_count,boss_retreat_per_sec," +
        "player_attacks,player_dodges,player_parries,player_blocks," +
        "damage_dealt,damage_received,end_reason";

    private const string ATTACK_CSV_HEADER =
        "session,agent_type,persona,attack_name,count,proportion";

    private void Awake()
    {
        // _fightCsvPath = Path.Combine(Application.persistentDataPath, fightLogFileName);
        // _attackCsvPath = Path.Combine(Application.persistentDataPath, attackDistFileName);

    //    EnsureCsvFilesExist();

        Debug.Log($"{LogPrefix} Initialized.");
     //   Debug.Log($"{LogPrefix} Fight CSV: {_fightCsvPath}");
    //    Debug.Log($"{LogPrefix} Attack CSV: {_attackCsvPath}");
        Debug.Log($"{LogPrefix} Starting from fight #{_sessionNumber + 1}");
    }

    private void Start()
    {
        if (autoStartLoggingOnStart)
        {
            Debug.LogWarning($"{LogPrefix} Auto-start is enabled. This is usually disabled for the timed survey experiment.");
            StartLoggingSegment(DetectCurrentAgentType());
        }
    }

    private void Update()
    {
        if (!_segmentLoggingEnabled || !_sessionActive || _fightEndTriggered)
            return;

        TrackPlayerDeathMoment();
        SampleTelemetry();
        TrackBossRetreats();

        bool playerDead = playerStats != null && playerStats.IsDead;
        bool bossDead = bossStats != null && bossStats.IsDead;

        if (logDeathsDuringTimedSegment && (playerDead || bossDead))
        {
            string reason = playerDead ? "PlayerDead" : "BossDead";
            EndCurrentFightAndLog(reason);

            if (autoRestartLoggingAfterDeath && _segmentLoggingEnabled)
            {
                if (_restartRoutine != null)
                    StopCoroutine(_restartRoutine);

                _restartRoutine = StartCoroutine(RestartLoggingAfterDeath());
            }
        }
    }

    public void StartLoggingSegment(string agentType)
    {
        Debug.Log($"{LogPrefix} StartLoggingSegment invoked. AgentType: {agentType}");

        _segmentLoggingEnabled = true;
        _forcedSegmentAgentType = string.IsNullOrWhiteSpace(agentType)
            ? DetectCurrentAgentType()
            : agentType;

        if (_restartRoutine != null)
        {
            StopCoroutine(_restartRoutine);
            _restartRoutine = null;
        }

        BeginFight();
    }

    public void StopLoggingSegment(string endReason)
    {
        Debug.Log($"{LogPrefix} StopLoggingSegment invoked. Reason: {endReason}");

        if (_restartRoutine != null)
        {
            StopCoroutine(_restartRoutine);
            _restartRoutine = null;
        }

        if (_sessionActive && !_fightEndTriggered)
        {
            EndCurrentFightAndLog(endReason);
        }
        else
        {
            Debug.Log($"{LogPrefix} No active fight to close for this segment.");
        }

        _segmentLoggingEnabled = false;
        _sessionActive = false;
    }

    private IEnumerator RestartLoggingAfterDeath()
    {
        Debug.Log($"{LogPrefix} Waiting {restartLoggingDelayAfterDeath} real seconds before next fight log starts.");

        yield return new WaitForSecondsRealtime(restartLoggingDelayAfterDeath);

        if (_segmentLoggingEnabled)
        {
            Debug.Log($"{LogPrefix} Restarting fight logging inside current timed segment.");
            BeginFight();
        }

        _restartRoutine = null;
    }

    private void BeginFight()
    {
        if (!_segmentLoggingEnabled)
        {
            Debug.LogWarning($"{LogPrefix} BeginFight ignored because segment logging is disabled.");
            return;
        }

        _agentType = !string.IsNullOrWhiteSpace(_forcedSegmentAgentType)
            ? _forcedSegmentAgentType
            : DetectCurrentAgentType();

        _fightStartTime = Time.time;
        _playerSurvivalTime = 0f;

        _playerDiedThisFight = false;
        _fightEndTriggered = false;
        _sessionActive = true;

        _sessionNumber++;

        _distanceSampleSum = 0f;
        _distanceSampleCount = 0;
        _bossRetreatCount = 0;
        _lastBossState = SkeletonAI.AIState.Idle;

        if (telemetry != null)
        {
            telemetry.ResetLifetimeCounters();
        }
        else
        {
            Debug.LogWarning($"{LogPrefix} telemetry reference is missing.");
        }

        if (bossAI != null)
        {
            bossAI.totalAttacksThisFight = 0;
        }

        Debug.Log($"{LogPrefix} Fight {_sessionNumber} started. AgentType: {_agentType}, Persona: {personaLabel}");
    }

    private void EndCurrentFightAndLog(string endReason)
    {
        if (!_sessionActive)
        {
            Debug.LogWarning($"{LogPrefix} EndCurrentFightAndLog ignored because no session is active.");
            return;
        }

        _fightEndTriggered = true;
        _sessionActive = false;

        float fightDuration = Time.time - _fightStartTime;

        bool playerDead = playerStats != null && playerStats.IsDead;
        bool bossDead = bossStats != null && bossStats.IsDead;
        bool playerWon = bossDead && !playerDead;

        if (playerDead && !_playerDiedThisFight)
        {
            _playerDiedThisFight = true;
            _playerSurvivalTime = fightDuration;
        }

        if (!_playerDiedThisFight)
        {
            _playerSurvivalTime = fightDuration;
        }

        float playerHpPct = GetHealthPct(playerStats);
        float bossHpPct = GetHealthPct(bossStats);

        float aggression = bossAI != null && bossAI.currentPersona != null
            ? bossAI.currentPersona.aggression
            : 0f;

        float fear = bossAI != null && bossAI.currentPersona != null
            ? bossAI.currentPersona.fear
            : 0f;

        float avgDistance = _distanceSampleCount > 0
            ? _distanceSampleSum / _distanceSampleCount
            : 0f;

        float bossRetreatFreq = fightDuration > 0f
            ? _bossRetreatCount / fightDuration
            : 0f;

        Debug.Log(
            $"{LogPrefix} Fight {_sessionNumber} ended. " +
            $"Reason:{endReason}, AgentType:{_agentType}, Duration:{fightDuration:F2}, " +
            $"PlayerWon:{playerWon}, PlayerHP:{playerHpPct:F2}, BossHP:{bossHpPct:F2}"
        );

        WriteFightRow(
            fightDuration,
            playerWon,
            playerHpPct,
            bossHpPct,
            aggression,
            fear,
            avgDistance,
            bossRetreatFreq,
            endReason
        );

        WriteAttackDistribution();
    }

    private void TrackPlayerDeathMoment()
    {
        if (playerStats != null && playerStats.IsDead && !_playerDiedThisFight)
        {
            _playerDiedThisFight = true;
            _playerSurvivalTime = Time.time - _fightStartTime;

            Debug.Log($"{LogPrefix} Player death detected. Survival time: {_playerSurvivalTime:F2}");
        }
    }

    private void SampleTelemetry()
    {
        if (telemetry == null)
            return;

        _distanceSampleSum += telemetry.PlayerEnemyDistance_Agent;
        _distanceSampleCount++;
    }

    private void TrackBossRetreats()
    {
        if (bossAI == null)
            return;

        if (bossAI.currentState == SkeletonAI.AIState.Retreating &&
            _lastBossState != SkeletonAI.AIState.Retreating)
        {
            _bossRetreatCount++;
        }

        _lastBossState = bossAI.currentState;
    }

    private string DetectCurrentAgentType()
    {
        if (bossAI == null)
            return "Unknown";

        BehaviorParameters behaviorParameters = bossAI.GetComponent<BehaviorParameters>();

        if (behaviorParameters == null)
        {
            return bossAI.useExternalAI ? "InferenceOnly" : "HeuristicOnly";
        }

        return behaviorParameters.BehaviorType switch
        {
            BehaviorType.HeuristicOnly => "HeuristicOnly",
            BehaviorType.InferenceOnly => "InferenceOnly",
            BehaviorType.Default => "Default",
            _ => behaviorParameters.BehaviorType.ToString()
        };
    }

    private float GetHealthPct(CharacterStats stats)
    {
        if (stats == null || stats.maxHealth <= 0)
            return 0f;

        return Mathf.Clamp01((float)stats.currentHealth / stats.maxHealth);
    }

    private void WriteFightRow(
        float duration,
        bool playerWon,
        float playerHpPct,
        float bossHpPct,
        float aggression,
        float fear,
        float avgDistance,
        float bossRetreatFreq,
        string endReason)
    {
        if (telemetry == null)
        {
            Debug.LogWarning($"{LogPrefix} Fight row skipped because telemetry is missing.");
            return;
        }

        int bossAttacks = bossAI != null ? bossAI.totalAttacksThisFight : 0;

        float bossAtkPerSec = duration > 0f
            ? bossAttacks / duration
            : 0f;

        string row = string.Format(
            "{0},{1},{2},{3:F3},{4:F3}," +
            "{5:F2},{6},{7:F2}," +
            "{8:F3},{9:F3},{10:F2}," +
            "{11},{12:F3}," +
            "{13},{14:F3}," +
            "{15},{16},{17},{18}," +
            "{19:F2},{20:F2},{21}",
            _sessionNumber,
            EscapeCsv(_agentType),
            EscapeCsv(personaLabel),
            aggression,
            fear,
            duration,
            playerWon ? 1 : 0,
            _playerSurvivalTime,
            playerHpPct,
            bossHpPct,
            avgDistance,
            bossAttacks,
            bossAtkPerSec,
            _bossRetreatCount,
            bossRetreatFreq,
            telemetry.LifetimeAttacks,
            telemetry.LifetimeDodges,
            telemetry.LifetimeParries,
            telemetry.LifetimeBlocks,
            telemetry.LifetimeDamageDealt,
            telemetry.LifetimeDamageReceived,
            EscapeCsv(endReason)
        );

        StartCoroutine(PostDataToCloud("FightLog", row));

        // try
        // {
        //     File.AppendAllText(_fightCsvPath, row + "\n", Encoding.UTF8);
        //     Debug.Log($"{LogPrefix} Fight row saved locally.");
        // }
        // catch (Exception ex)
        // {
        //     Debug.LogWarning($"{LogPrefix} Local fight row save skipped/failed. This is expected in WebGL. Error: {ex.Message}");
        // }
    }

    private void WriteAttackDistribution()
    {
        if (telemetry == null)
        {
            Debug.LogWarning($"{LogPrefix} Attack distribution skipped because telemetry is missing.");
            return;
        }

        Dictionary<string, int> distribution = telemetry.GetEnemyAttackDistribution();

        if (distribution == null || distribution.Count == 0)
        {
            Debug.Log($"{LogPrefix} Attack distribution empty. Nothing to write.");
            return;
        }

        int total = 0;
        foreach (KeyValuePair<string, int> kvp in distribution)
            total += kvp.Value;

        if (total <= 0)
            return;

        foreach (KeyValuePair<string, int> kvp in distribution)
        {
            float proportion = (float)kvp.Value / total;

            string row = string.Format(
                "{0},{1},{2},{3},{4},{5:F4}",
                _sessionNumber,
                EscapeCsv(_agentType),
                EscapeCsv(personaLabel),
                EscapeCsv(kvp.Key),
                kvp.Value,
                proportion
            );

            StartCoroutine(PostDataToCloud("AttackLog", row));

            // try
            // {
            //     File.AppendAllText(_attackCsvPath, row + "\n", Encoding.UTF8);
            // }
            // catch (Exception ex)
            // {
            //     Debug.LogWarning($"{LogPrefix} Local attack row save skipped/failed. This is expected in WebGL. Error: {ex.Message}");
            // }
        }

        Debug.Log($"{LogPrefix} Attack distribution logged. AttackTypes:{distribution.Count}, Total:{total}");
    }

    private IEnumerator PostDataToCloud(string logType, string dataRow)
{
    if (!enableCloudLogging)
    {
        Debug.LogWarning($"{LogPrefix} Cloud logging disabled. Skipping {logType}.");
        yield break;
    }

    if (string.IsNullOrWhiteSpace(cloudWebhookURL) ||
        cloudWebhookURL == "YOUR_GOOGLE_SCRIPT_URL_HERE")
    {
        Debug.LogWarning($"{LogPrefix} Cloud webhook URL is not configured. Skipping {logType}.");
        yield break;
    }

    WWWForm form = new WWWForm();
    form.AddField("logType", logType);
    form.AddField("dataRow", dataRow);

    using UnityWebRequest request = UnityWebRequest.Post(cloudWebhookURL, form);
    request.downloadHandler = new DownloadHandlerBuffer();

    Debug.Log($"{LogPrefix} Uploading {logType}...");
    Debug.Log($"{LogPrefix} {logType} row: {dataRow}");

    yield return request.SendWebRequest();

    string responseText = request.downloadHandler != null
        ? request.downloadHandler.text
        : "";

    Debug.Log($"{LogPrefix} {logType} HTTP Code: {request.responseCode}");
    Debug.Log($"{LogPrefix} {logType} Cloud Response: {responseText}");

    if (request.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"{LogPrefix} {logType} cloud upload failed. Error: {request.error}");
        yield break;
    }

    if (!responseText.StartsWith("OK"))
    {
        Debug.LogError($"{LogPrefix} {logType} reached Apps Script but was not confirmed. Response: {responseText}");
        yield break;
    }

    Debug.Log($"{LogPrefix} {logType} cloud upload confirmed.");
}

    // private void EnsureCsvFilesExist()
    // {
    //     try
    //     {
    //         if (!File.Exists(_fightCsvPath))
    //         {
    //             File.WriteAllText(_fightCsvPath, FIGHT_CSV_HEADER + "\n", Encoding.UTF8);
    //             _sessionNumber = 0;
    //             Debug.Log($"{LogPrefix} Fight CSV created.");
    //         }
    //         else
    //         {
    //             string[] lines = File.ReadAllLines(_fightCsvPath);
    //             _sessionNumber = Mathf.Max(0, lines.Length - 1);

    //             if (lines.Length > 0 && !lines[0].Contains("end_reason"))
    //             {
    //                 Debug.LogWarning(
    //                     $"{LogPrefix} Existing fight_log.csv uses the old header. " +
    //                     "Delete the old CSV or add the end_reason column manually."
    //                 );
    //             }
    //         }

    //         if (!File.Exists(_attackCsvPath))
    //         {
    //             File.WriteAllText(_attackCsvPath, ATTACK_CSV_HEADER + "\n", Encoding.UTF8);
    //             Debug.Log($"{LogPrefix} Attack distribution CSV created.");
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         Debug.LogWarning($"{LogPrefix} Local file setup skipped/failed. This is expected in WebGL. Error: {ex.Message}");
    //     }
    // }

    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool mustQuote =
            value.Contains(",") ||
            value.Contains("\"") ||
            value.Contains("\n") ||
            value.Contains("\r");

        if (!mustQuote)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public string GetPersonaLabel() => personaLabel;

    public string GetAgentType()
    {
        if (!string.IsNullOrWhiteSpace(_forcedSegmentAgentType))
            return _forcedSegmentAgentType;

        return _agentType;
    }
}