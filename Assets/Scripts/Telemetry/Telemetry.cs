using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

/// <summary>
/// Dual-purpose Telemetry system.
/// 1. Logs periodic summary reports to the console for demos.
/// 2. Provides high-frequency, real-time public properties for an ML-Agent to use as observations.
/// </summary>
public class Telemetry : MonoBehaviour
{
    // --- SECTION 1: Original Configuration for Console Logging ---

    [Header("Console Logging Settings")]
    [Tooltip("List of event names (as strings) that should count towards Actions Per Minute (APM).")]
    [SerializeField] private List<string> apmEventNames = new List<string> { "PlayerAttack", "PlayerDodge" };

    [Tooltip("How often (in seconds) to log the summary report to the console.")]
    [SerializeField] private float logInterval = 10f;

    [Header("Object References")]
    [Tooltip("The Transform of the player character.")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("The Transform of the enemy agent to track relative distance.")]
    [SerializeField] private Transform enemyTransform;

    // --- SECTION 2: Public Properties for ML-Agent ---
    // These are updated every frame for the agent's observation vector.

    [Header("Real-time Agent Data (Read-Only)")]
    public float TotalAPM_Agent { get; private set; }
    public float PlayerAttackAPM_Agent { get; private set; }
    public float PlayerDistanceMoved_Agent { get; private set; }
    public float PlayerEnemyDistance_Agent { get; private set; }
    public float PlayerEnemyDistanceChange_Agent { get; private set; } // Positive = getting further, Negative = getting closer

    // --- SECTION 3: Internal State Management ---

    // State for Console Logging (Low Frequency)
    private float _logTimer;
    private Vector3 _lastPlayerPosition_ForLog;
    private float _lastDistanceToEnemy_ForLog;

    // State for Agent Data (High Frequency)
    private Dictionary<string, List<float>> _apmEventTimestamps = new Dictionary<string, List<float>>();
    private Vector3 _lastPlayerPosition_ForAgent;
    private float _lastDistanceToEnemy_ForAgent;

    // --- SECTION 4: Unity Lifecycle Methods ---

    void OnEnable()
    {
        // Subscribe to player action events. Make sure these events exist in your project.
        // Example: public static event Action OnPlayerAttack;
        PlayerAttack.OnPlayerAttack += OnPlayerAttackHandler;
        // If you have a dodge event, uncomment the following line:
        // PlayerDodge.OnPlayerDodge += OnPlayerDodgeHandler;
    }

    void OnDisable()
    {
        PlayerAttack.OnPlayerAttack -= OnPlayerAttackHandler;
        // PlayerDodge.OnPlayerDodge -= OnPlayerDodgeHandler;
    }

    void Start()
    {
        // --- Initialization for both systems ---
        if (playerTransform == null || enemyTransform == null)
        {
            Debug.LogError("Telemetry: Player and/or Enemy Transform not assigned. This script will not function correctly.", this);
            this.enabled = false; // Disable script if references are missing
            return;
        }

        // Initialize logging state
        _lastPlayerPosition_ForLog = playerTransform.position;
        _lastDistanceToEnemy_ForLog = Vector3.Distance(playerTransform.position, enemyTransform.position);
        _logTimer = logInterval;

        // Initialize agent state
        _lastPlayerPosition_ForAgent = playerTransform.position;
        _lastDistanceToEnemy_ForAgent = Vector3.Distance(playerTransform.position, enemyTransform.position);

        // Initialize dictionaries for APM tracking
        foreach (string eventName in apmEventNames)
        {
            if (!_apmEventTimestamps.ContainsKey(eventName))
            {
                _apmEventTimestamps.Add(eventName, new List<float>());
            }
        }
    }

    void Update()
    {
        // --- Part A: Update High-Frequency Agent Data (Every Frame) ---
        UpdateAgentMetrics();

        // --- Part B: Handle Low-Frequency Console Logging ---
        _logTimer -= Time.deltaTime;
        if (_logTimer <= 0f)
        {
            LogSummaryReport(); // Fire the periodic report
            _logTimer = logInterval;
        }
    }

    // --- SECTION 5: Core Logic ---

    private void UpdateAgentMetrics()
    {
        if (playerTransform == null || enemyTransform == null) return;

        // 1. Calculate all APM values
        float oneMinuteAgo = Time.time - 60f;
        int totalActions = 0;

        foreach (var entry in _apmEventTimestamps)
        {
            // Remove old timestamps to keep the list clean
            entry.Value.RemoveAll(t => t < oneMinuteAgo);
            int currentEventAPM = entry.Value.Count;

            // Update specific agent properties
            if (entry.Key == "PlayerAttack")
            {
                PlayerAttackAPM_Agent = currentEventAPM;
            }
            // Add other specific APM metrics here if needed (e.g., PlayerDodge)

            totalActions += currentEventAPM;
        }
        TotalAPM_Agent = totalActions;


        // 2. Calculate positional metrics
        Vector3 currentPlayerPos = playerTransform.position;
        PlayerDistanceMoved_Agent = Vector3.Distance(_lastPlayerPosition_ForAgent, currentPlayerPos);

        float currentDistanceToEnemy = Vector3.Distance(currentPlayerPos, enemyTransform.position);
        PlayerEnemyDistance_Agent = currentDistanceToEnemy;
        PlayerEnemyDistanceChange_Agent = currentDistanceToEnemy - _lastDistanceToEnemy_ForAgent;

        // 3. Update "last known" values for the next frame
        _lastPlayerPosition_ForAgent = currentPlayerPos;
        _lastDistanceToEnemy_ForAgent = currentDistanceToEnemy;
    }


    private void LogSummaryReport()
    {
        Debug.Log($"--- Telemetry Report (Time: {Time.time:F2}s) ---");

        // Log APM from the live agent data
        Debug.Log($"Total Player APM (live): {TotalAPM_Agent}");
        foreach (string eventName in apmEventNames)
        {
            if (_apmEventTimestamps.ContainsKey(eventName))
            {
                 Debug.Log($"- {eventName} APM (live): {_apmEventTimestamps[eventName].Count}");
            }
        }

        // Log positional changes over the log interval
        float playerDistMoved = Vector3.Distance(_lastPlayerPosition_ForLog, playerTransform.position);
        Debug.Log($"Player moved {playerDistMoved:F2} units in the last {logInterval:F2}s.");
        _lastPlayerPosition_ForLog = playerTransform.position;

        float currentDist = Vector3.Distance(playerTransform.position, enemyTransform.position);
        float distChange = currentDist - _lastDistanceToEnemy_ForLog;
        string direction = distChange < 0 ? "closer to" : (distChange > 0 ? "further from" : "same distance from");
        Debug.Log($"Player is now {Mathf.Abs(distChange):F2} units {direction} the enemy.");
        _lastDistanceToEnemy_ForLog = currentDist;

        Debug.Log("------------------------------------");
    }

    // --- SECTION 6: Event Handlers ---

    private void AddApmEventTimestamp(string eventName)
    {
        if (_apmEventTimestamps.ContainsKey(eventName))
        {
            _apmEventTimestamps[eventName].Add(Time.time);
        }
    }

    private void OnPlayerAttackHandler()
    {
        AddApmEventTimestamp("PlayerAttack");
    }

    private void OnPlayerDodgeHandler()
    {
        AddApmEventTimestamp("PlayerDodge");
    }
}