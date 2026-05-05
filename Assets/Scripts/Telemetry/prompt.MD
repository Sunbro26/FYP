My apologies for the cutoff! Here is the complete, uninterrupted response and the full code for your **`FightLogger.cs`**.

### Step 1: Create the Cloud Database (Google Sheets)
*(If you already did this from the previous message, you can skip to Step 2).*
1. Go to Google Drive and create a new **Google Sheet** (e.g., `ACF_CloudData`).
2. Click **Extensions > Apps Script**.
3. Paste this code:
```javascript
function doPost(e) {
  var sheet = SpreadsheetApp.getActiveSpreadsheet().getActiveSheet();
  var logType = e.parameter.logType;
  var dataRow = e.parameter.dataRow;
  
  // Adds a row: [Date/Time, LogType (Fight/Attack/Survey), The CSV String]
  sheet.appendRow([new Date(), logType, dataRow]);
  return ContentService.createTextOutput("Data Logged Successfully");
}
```
4. Click **Deploy > New deployment**. Set type to **Web app**.
5. Set **Execute as: Me** and **Who has access: Anyone**.
6. Deploy and **copy the Web App URL**.

---

### Step 2: The Complete `FightLogger.cs` Script

Replace your entire `FightLogger.cs` with this. It keeps your local CSV backup (if playing on PC) but adds a `UnityWebRequest` coroutine to silently fire the data to your Google Sheet if you build for WebGL.

```csharp
using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Networking; // <--- NEW: For Web Requests

namespace AdaptiveCombatFramework {
    public class FightLogger : MonoBehaviour
    {
        [Header("Cloud Logging (WebGL)")][Tooltip("The Google Apps Script Web App URL to send data to.")]
        public string cloudWebhookURL = "YOUR_GOOGLE_SCRIPT_URL_HERE"; // <--- PASTE URL HERE
        [Tooltip("Toggle to enable/disable sending data to the cloud.")]
        public bool enableCloudLogging = true;

        [Header("CSV File Configuration")]
        [SerializeField] private string fightLogFileName = "fight_log.csv";
        [SerializeField] private string attackDistFileName = "attack_distribution.csv";[Header("Experiment Identity")]
        [SerializeField] private string personaLabel = "Aggressive";[Header("UI & Flow Control")]
        [SerializeField] private float surveyDelay = 6f;
        [SerializeField] private float fallbackRestartTimer = 120f;

        [Header("Required Framework References")][SerializeField] private Telemetry telemetry;
        [SerializeField] private SkeletonAI bossAI;
        [SerializeField] private CharacterStats playerStats;
        [SerializeField] private CharacterStats bossStats;
        [SerializeField] private SurveyUI surveyUI;

        private string _agentType = "Heuristic"; 
        private float _fightStartTime;
        private float _playerSurvivalTime;
        private bool _sessionActive = false;
        private bool _playerDiedThisFight = false;
        private bool _fightEndTriggered = false;
        private int _sessionNumber = 0;

        private float _distanceSampleSum = 0f;
        private int _distanceSampleCount = 0;
        private int _bossRetreatCount = 0;
        private SkeletonAI.AIState _lastBossState = SkeletonAI.AIState.Idle;

        private string _fightCsvPath;
        private string _attackCsvPath;

        private const string FIGHT_CSV_HEADER = "session,agent_type,persona,aggression,fear,fight_duration,player_survived,player_survival_time,player_hp_pct,boss_hp_pct,avg_distance,boss_attack_count,boss_atk_per_sec,boss_retreat_count,boss_retreat_per_sec,player_attacks,player_dodges,player_parries,player_blocks,damage_dealt,damage_received";
        private const string ATTACK_CSV_HEADER = "session,agent_type,persona,attack_name,count,proportion";

        void Awake()
        {
            _fightCsvPath = Path.Combine(Application.persistentDataPath, fightLogFileName);
            _attackCsvPath = Path.Combine(Application.persistentDataPath, attackDistFileName);

            // Local file setup (Wrapped in try-catch so it doesn't crash WebGL builds)
            try 
            {
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
            }
            catch (System.Exception e) 
            { 
                Debug.LogWarning($"[FightLogger] Local file setup skipped (Expected in WebGL): {e.Message}"); 
            }

            Debug.Log($"[FightLogger] Logger initialized. Starting from fight #{_sessionNumber + 1}");
        }

        void Start()
        {
            BeginFight();
        }

        void Update()
        {
            if (!_sessionActive || _fightEndTriggered) return;

            if (playerStats != null && playerStats.IsDead && !_playerDiedThisFight)
            {
                _playerDiedThisFight = true;
                _playerSurvivalTime = Time.time - _fightStartTime;
            }

            if (telemetry != null)
            {
                _distanceSampleSum += telemetry.PlayerEnemyDistance_Agent;
                _distanceSampleCount++;
            }

            if (bossAI != null)
            {
                if (bossAI.currentState == SkeletonAI.AIState.Retreating && _lastBossState != SkeletonAI.AIState.Retreating)
                    _bossRetreatCount++;
                
                _lastBossState = bossAI.currentState;
            }

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

            var behaviourParams = bossAI.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            if (behaviourParams != null && behaviourParams.BehaviorType == Unity.MLAgents.Policies.BehaviorType.HeuristicOnly)
                _agentType = "Heuristic";
            else
                _agentType = "Trained";

            _fightStartTime = Time.time;
            _playerDiedThisFight = false;
            _playerSurvivalTime = 0f;
            _fightEndTriggered = false;
            _sessionActive = true;
            _sessionNumber++;

            _distanceSampleSum = 0f;
            _distanceSampleCount = 0;
            _bossRetreatCount = 0;
            _lastBossState = SkeletonAI.AIState.Idle;

            if (telemetry != null) telemetry.ResetLifetimeCounters();

            Debug.Log($"[FightLogger] Fight {_sessionNumber} started. Mode: {_agentType}");
        }

        private void EndFight()
        {
            _sessionActive = false;

            float fightDuration = Time.time - _fightStartTime;
            bool playerWon = bossStats != null && bossStats.IsDead && (playerStats == null || !playerStats.IsDead);

            if (!_playerDiedThisFight) _playerSurvivalTime = fightDuration;

            float playerHpPct = playerStats != null ? (float)playerStats.currentHealth / playerStats.maxHealth : 0f;
            float bossHpPct = bossStats != null ? (float)bossStats.currentHealth / bossStats.maxHealth : 0f;
            float aggression = bossAI != null ? bossAI.currentPersona.aggression : 0f;
            float fear = bossAI != null ? bossAI.currentPersona.fear : 0f;

            float avgDistance = _distanceSampleCount > 0 ? _distanceSampleSum / _distanceSampleCount : 0f;
            float bossRetreatFreq = fightDuration > 0f ? (float)_bossRetreatCount / fightDuration : 0f;

            // Generate and Write/Send Data
            WriteFightRow(fightDuration, playerWon, playerHpPct, bossHpPct, aggression, fear, avgDistance, bossRetreatFreq);
            WriteAttackDistribution();

            if (surveyUI != null) Invoke(nameof(ShowSurveyDelayed), surveyDelay);
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

            // 1. Send to Cloud
            StartCoroutine(PostDataToCloud("FightLog", row));

            // 2. Try to save locally (will silently fail in WebGL without crashing)
            try { File.AppendAllText(_fightCsvPath, row + "\n", Encoding.UTF8); }
            catch (System.Exception) { /* Ignored for WebGL */ }
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
                
                // 1. Send to Cloud
                StartCoroutine(PostDataToCloud("AttackLog", row));

                // 2. Try to save locally
                try { File.AppendAllText(_attackCsvPath, row + "\n", Encoding.UTF8); }
                catch (System.Exception) { /* Ignored for WebGL */ }
            }
        }

        // --- NEW: CLOUD WEBHOOK COROUTINE ---
        private IEnumerator PostDataToCloud(string logType, string dataRow)
        {
            if (!enableCloudLogging || string.IsNullOrEmpty(cloudWebhookURL) || cloudWebhookURL == "YOUR_GOOGLE_SCRIPT_URL_HERE") 
                yield break;

            WWWForm form = new WWWForm();
            form.AddField("logType", logType);
            form.AddField("dataRow", dataRow);

            using (UnityWebRequest www = UnityWebRequest.Post(cloudWebhookURL, form))
            {
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[CloudLogger] Failed to send {logType}: {www.error}");
                }
                else
                {
                    Debug.Log($"[CloudLogger] {logType} uploaded successfully.");
                }
            }
        }

        public string GetPersonaLabel() => personaLabel;
        public string GetAgentType() => _agentType;
    }
}
```

### How to use this for your Survey
Your groupmate can also use this exact same logic in the `SurveyUI.cs` script! 
If they have a button that submits the survey responses (like "Flow: 5, Challenge: 4"), they can format it as a comma-separated string (`"5,4,..."`) and send it to the same Google Sheet by passing `"SurveyLog"` as the `logType`. The Google Sheet will neatly stack all your fight data, attack distributions, and survey results from anyone who plays the link.

**Don't forget to paste the Web App URL from Step 1 into the `Cloud Webhook URL` slot in the Unity Inspector!**`