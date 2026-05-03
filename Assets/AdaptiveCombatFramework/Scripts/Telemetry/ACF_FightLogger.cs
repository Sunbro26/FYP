using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace AdaptiveCombatFramework {
    public class FightLogger : MonoBehaviour
    {
        [Header("CSV File Configuration")]
        [Tooltip("The name of the file where summary data for every fight is stored. Saved in Application.persistentDataPath.")]
        [SerializeField] private string fightLogFileName = "fight_log.csv";
        
        [Tooltip("The name of the file where the frequency of specific attacks is tracked.")]
        [SerializeField] private string attackDistFileName = "attack_distribution.csv";

        [Header("Experiment Identity")]
        [Tooltip("A label used to categorize this session in the CSV. Usually 'Aggressive' or 'Defensive'.")]
        [SerializeField] private string personaLabel = "Aggressive";

        [Header("UI & Flow Control")]
        [Tooltip("Seconds to wait after a death before showing the Player Survey.")]
        [SerializeField] private float surveyDelay = 6f;
        
        [Tooltip("Maximum time (seconds) to wait for a survey response before automatically starting the next fight loop.")]
        [SerializeField] private float fallbackRestartTimer = 120f;

        [Header("Required Framework References")]
        [Tooltip("The Telemetry component gathering real-time data.")]
        [SerializeField] private Telemetry telemetry;
        
        [Tooltip("The AI controller for the boss.")]
        [SerializeField] private SkeletonAI bossAI;
        
        [Tooltip("Health and Stamina stats for the player.")]
        [SerializeField] private CharacterStats playerStats;
        
        [Tooltip("Health stats for the boss.")]
        [SerializeField] private CharacterStats bossStats;
        
        [Tooltip("The UI component that gathers player feedback after a fight.")]
        [SerializeField] private SurveyUI surveyUI;

        // --- Internal State Logic ---
        private string _agentType = "Heuristic"; 
        private float _fightStartTime;
        private float _playerSurvivalTime;
        private bool _sessionActive = false;
        private bool _playerDiedThisFight = false;
        private bool _fightEndTriggered = false;
        private int _sessionNumber = 0;

        // --- Combat Sampling ---
        private float _distanceSampleSum = 0f;
        private int _distanceSampleCount = 0;
        private int _bossRetreatCount = 0;
        private SkeletonAI.AIState _lastBossState = SkeletonAI.AIState.Idle;

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
            // Set up paths based on Inspector settings
            _fightCsvPath = Path.Combine(Application.persistentDataPath, fightLogFileName);
            _attackCsvPath = Path.Combine(Application.persistentDataPath, attackDistFileName);

            // Initialize Fight Log CSV
            if (!File.Exists(_fightCsvPath))
            {
                File.WriteAllText(_fightCsvPath, FIGHT_CSV_HEADER + "\n", Encoding.UTF8);
                _sessionNumber = 0;
            }
            else
            {
                // Count lines to determine current session ID
                string[] lines = File.ReadAllLines(_fightCsvPath);
                _sessionNumber = Mathf.Max(0, lines.Length - 1);
            }

            // Initialize Attack Distribution CSV
            if (!File.Exists(_attackCsvPath))
                File.WriteAllText(_attackCsvPath, ATTACK_CSV_HEADER + "\n", Encoding.UTF8);

            Debug.Log($"[FightLogger] Framework Logger initialized. Starting from fight #{_sessionNumber + 1}");
        }

        void Start()
        {
            BeginFight();
        }

        void Update()
        {
            if (!_sessionActive || _fightEndTriggered) return;

            // 1. Track survival duration for the player
            if (playerStats != null && playerStats.IsDead && !_playerDiedThisFight)
            {
                _playerDiedThisFight = true;
                _playerSurvivalTime = Time.time - _fightStartTime;
            }

            // 2. Sample distance for fight-wide average
            if (telemetry != null)
            {
                _distanceSampleSum += telemetry.PlayerEnemyDistance_Agent;
                _distanceSampleCount++;
            }

            // 3. Track AI state transitions (specifically retreats)
            if (bossAI != null)
            {
                if (bossAI.currentState == SkeletonAI.AIState.Retreating && _lastBossState != SkeletonAI.AIState.Retreating)
                    _bossRetreatCount++;
                
                _lastBossState = bossAI.currentState;
            }

            // 4. Detect Fight Conclusion
            bool playerDead = playerStats != null && playerStats.IsDead;
            bool bossDead = bossStats != null && bossStats.IsDead;

            if (playerDead || bossDead)
            {
                _fightEndTriggered = true;
                EndFight();
            }
        }

        private void BeginFight()
        {
            CancelInvoke();

            // Detect if AI is running on Heuristic (Logic) or Trained (NN)
            var behaviourParams = bossAI.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            if (behaviourParams != null && behaviourParams.BehaviorType == Unity.MLAgents.Policies.BehaviorType.HeuristicOnly)
                _agentType = "Heuristic";
            else
                _agentType = "Trained";

            // Reset Internal Timers
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

            // Clear Telemetry lifetime counters for a clean data slate
            if (telemetry != null) telemetry.ResetLifetimeCounters();

            Debug.Log($"[FightLogger] Fight {_sessionNumber} started. Mode: {_agentType}");
        }

        private void EndFight()
        {
            _sessionActive = false;

            float fightDuration = Time.time - _fightStartTime;
            bool playerWon = bossStats != null && bossStats.IsDead && (playerStats == null || !playerStats.IsDead);

            if (!_playerDiedThisFight)
                _playerSurvivalTime = fightDuration;

            // Final Health Percentages
            float playerHpPct = playerStats != null ? (float)playerStats.currentHealth / playerStats.maxHealth : 0f;
            float bossHpPct = bossStats != null ? (float)bossStats.currentHealth / bossStats.maxHealth : 0f;

            // Final AI Personality Settings
            float aggression = bossAI != null ? bossAI.currentPersona.aggression : 0f;
            float fear = bossAI != null ? bossAI.currentPersona.fear : 0f;

            // Final Spacial Averages
            float avgDistance = _distanceSampleCount > 0 ? _distanceSampleSum / _distanceSampleCount : 0f;
            float bossRetreatFreq = fightDuration > 0f ? (float)_bossRetreatCount / fightDuration : 0f;

            // Log Data to Disk
            WriteFightRow(fightDuration, playerWon, playerHpPct, bossHpPct, aggression, fear, avgDistance, bossRetreatFreq);
            WriteAttackDistribution();

            // Trigger UI Flow
            if (surveyUI != null)
                Invoke(nameof(ShowSurveyDelayed), surveyDelay);

            // Fallback restart in case player walks away from computer
            Invoke(nameof(BeginFight), surveyDelay + fallbackRestartTimer);
        }

        private void ShowSurveyDelayed()
        {
            if (surveyUI != null) surveyUI.ShowSurvey();
        }

        public void OnSurveyComplete()
        {
            CancelInvoke(nameof(BeginFight));
            CancelInvoke(nameof(ShowSurveyDelayed));
            Invoke(nameof(BeginFight), 3.5f);
        }

        private void WriteFightRow(float duration, bool won, float pHP, float bHP, float agg, float fear, float dist, float retreat)
        {
            if (telemetry == null) return;

            int bossAttacks = bossAI != null ? bossAI.totalAttacksThisFight : 0;
            float bossAtkPerSec = duration > 0f ? (float)bossAttacks / duration : 0f;

            string row = string.Format(
                "{0},{1},{2},{3:F3},{4:F3},{5:F2},{6},{7:F2},{8:F3},{9:F3},{10:F2},{11},{12:F3},{13},{14:F3},{15},{16},{17},{18},{19:F2},{20:F2}",
                _sessionNumber, _agentType, personaLabel, agg, fear, duration, won ? 1 : 0, _playerSurvivalTime,
                pHP, bHP, dist, bossAttacks, bossAtkPerSec, _bossRetreatCount, retreat,
                telemetry.LifetimeAttacks, telemetry.LifetimeDodges, telemetry.LifetimeParries, telemetry.LifetimeBlocks,
                telemetry.LifetimeDamageDealt, telemetry.LifetimeDamageReceived
            );

            try { File.AppendAllText(_fightCsvPath, row + "\n", Encoding.UTF8); }
            catch (System.Exception e) { Debug.LogError($"[FightLogger] Write Failed: {e.Message}"); }
        }

        private void WriteAttackDistribution()
        {
            if (telemetry == null) return;

            Dictionary<string, int> dist = telemetry.GetEnemyAttackDistribution();
            if (dist == null || dist.Count == 0) return;

            int total = 0;
            foreach (var kvp in dist) total += kvp.Value;
            if (total == 0) return;

            foreach (var kvp in dist)
            {
                float proportion = (float)kvp.Value / total;
                string row = string.Format("{0},{1},{2},{3},{4},{5:F4}", _sessionNumber, _agentType, personaLabel, kvp.Key, kvp.Value, proportion);
                try { File.AppendAllText(_attackCsvPath, row + "\n", Encoding.UTF8); }
                catch (System.Exception e) { Debug.LogError($"[FightLogger] Attack Log Failed: {e.Message}"); }
            }
        }

        public string GetPersonaLabel() => personaLabel;
        public string GetAgentType() => _agentType;
    }
}