using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;

public class Telemetry : MonoBehaviour
{
    [Header("Telemetry Settings")]
    [Tooltip("List of event names (as strings) that should count towards Actions Per Minute (APM).")]
    [SerializeField] private List<string> apmEventNames = new List<string>();

    [Tooltip("How often (in seconds) to log the current APM and positional data.")]
    [SerializeField] private float logInterval = 10f;

    [Header("Positional Tracking")]
    [Tooltip("The Transform of the player character.")]
    [SerializeField] private Transform playerTransform;
    [Tooltip("The Transform of the enemy agent to track relative distance. Can be null if only tracking player position.")]
    [SerializeField] private Transform enemyTransform;

    // Dictionary to store timestamps for each tracked APM event
    private Dictionary<string, List<float>> _apmEventTimestamps = new Dictionary<string, List<float>>();
    private float _timer;

    // Positional tracking variables
    private Vector3 _lastPlayerPosition;
    private Vector3 _lastEnemyPosition; // To calculate enemy movement as well, though not explicitly asked for here
    private float _lastDistanceToEnemy; // To track change in distance to enemy

    void OnEnable()
    {
        // Subscribe to PlayerAttack event
        PlayerAttack.OnPlayerAttack += OnPlayerAttackHandler;
        // PlayerDodge.OnPlayerDodge += OnPlayerDodgeHandler; // Uncomment if PlayerDodge event exists
    }

    void OnDisable()
    {
        // Unsubscribe from events
        PlayerAttack.OnPlayerAttack -= OnPlayerAttackHandler;
        // PlayerDodge.OnPlayerDodge -= OnPlayerDodgeHandler; // Uncomment if PlayerDodge event exists
    }

    void Start()
    {
        if (playerTransform == null)
        {
            Debug.LogError("Telemetry: Player Transform not assigned. Positional tracking for player will be disabled.", this);
        }
        else
        {
            _lastPlayerPosition = playerTransform.position;
        }

        if (enemyTransform == null)
        {
            Debug.LogWarning("Telemetry: Enemy Transform not assigned. Positional tracking relative to enemy will be disabled.", this);
        }
        else
        {
            _lastEnemyPosition = enemyTransform.position;
            _lastDistanceToEnemy = Vector3.Distance(playerTransform.position, enemyTransform.position);
        }

        if (apmEventNames == null || apmEventNames.Count == 0)
        {
            Debug.LogWarning("Telemetry: No APM event names specified. APM tracking will be limited.", this);
        }

        // Initialize the dictionary for each event listed in apmEventNames
        foreach (string eventName in apmEventNames)
        {
            if (!_apmEventTimestamps.ContainsKey(eventName))
            {
                _apmEventTimestamps.Add(eventName, new List<float>());
            }
        }

        _timer = logInterval; // Start logging immediately on first interval
    }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            LogActionsPerMinute();
            LogPositionalChanges(); // NEW: Log positional changes
            _timer = logInterval;
        }
    }

    private void AddApmEventTimestamp(string eventName)
    {
        if (_apmEventTimestamps.ContainsKey(eventName))
        {
            _apmEventTimestamps[eventName].Add(Time.time);
        }
        else
        {
            Debug.LogWarning($"Telemetry: Event '{eventName}' fired but not initially configured for APM. Adding it to tracking.", this);
            _apmEventTimestamps.Add(eventName, new List<float> { Time.time });
        }
    }

    private void OnPlayerAttackHandler()
    {
        AddApmEventTimestamp("PlayerAttack");
        // Debug.Log("Telemetry: PlayerAttack recorded.");
    }

    // private void OnPlayerDodgeHandler()
    // {
    //     AddApmEventTimestamp("PlayerDodge");
    //     // Debug.Log("Telemetry: PlayerDodge recorded.");
    // }

    private void LogActionsPerMinute()
    {
        float currentTime = Time.time;
        float oneMinuteAgo = currentTime - 60f;
        int totalAPMActions = 0;

        Debug.Log($"--- Telemetry Report ({currentTime:F2}s) ---");

        if (_apmEventTimestamps.Count == 0)
        {
            Debug.Log("No APM events configured or tracked yet.");
        }
        else
        {
            foreach (var entry in _apmEventTimestamps)
            {
                string eventName = entry.Key;
                List<float> timestamps = entry.Value;

                timestamps.RemoveAll(t => t < oneMinuteAgo);
                int apmCount = timestamps.Count;
                totalAPMActions += apmCount;

                Debug.Log($"- {eventName} APM: {apmCount}");
            }
            Debug.Log($"Total Actions Per Minute (APM): {totalAPMActions}");
        }
    }

    // NEW FUNCTION: Log player and relative positional changes
    private void LogPositionalChanges()
    {
        // Player Position Change
        if (playerTransform != null)
        {
            float playerDistanceMoved = Vector3.Distance(_lastPlayerPosition, playerTransform.position);
            Debug.Log($"Player moved {playerDistanceMoved:F2} units in the last {logInterval:F2}s.");
            _lastPlayerPosition = playerTransform.position;
        }

        // Player-to-Enemy Relative Position Change
        if (playerTransform != null && enemyTransform != null)
        {
            float currentDistance = Vector3.Distance(playerTransform.position, enemyTransform.position);
            float distanceChange = currentDistance - _lastDistanceToEnemy;

            // Determine if player got closer or further
            string direction = distanceChange < 0 ? "closer to" : "further from";
            if (Mathf.Approximately(distanceChange, 0)) direction = "same distance from";

            Debug.Log($"Player is {Mathf.Abs(distanceChange):F2} units {direction} enemy.");
            _lastDistanceToEnemy = currentDistance;
        }
        Debug.Log("------------------------------------");
    }
}