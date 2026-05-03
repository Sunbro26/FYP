using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class SkeletonAiProxyAgent : Agent
{
    [Header("References")]
    public SkeletonAI skeletonBody;
    public CharacterStats myStats;
    public CharacterStats playerStats;
    public MultiGAILManager multiGAILManager;

    [Header("Environment")]
    public Transform playerTransform;
    public Telemetry telemetrySystem;
    public PlayerInput debugPlayerInput;

    [Header("Training")]
    public bool ownEpisodeResets = true;
    public int defaultMaxStep = 5000;

    [Header("Style Conditioning")]
    [Tooltip("Current style blend observed by the policy. Values are normalized to sum to 1.")]
    public List<float> currentStyleWeights = new List<float> { 1f, 0f };
    [Tooltip("If enabled, a new style blend is sampled each episode during Python-connected training.")]
    public bool sampleStyleWeightsEachEpisode = true;
    [Tooltip("When using exactly two styles, sample only pure endpoints instead of blended mixtures.")]
    public bool sampleOnlyEndpointStyles = false;

    [Header("Runtime Style Shifting")]
    [Tooltip("Allows style weights to be changed while a fight is in progress.")]
    public bool allowRuntimeStyleShifts = true;
    [Tooltip("How quickly runtime style changes are blended into the active policy conditioning.")]
    public float styleShiftLerpSpeed = 6f;
    [Tooltip("If true, runtime style changes are applied immediately instead of blending over time.")]
    public bool snapRuntimeStyleShifts = false;

    [Header("Opponent Modeling")]
    [Tooltip("If enabled, estimate the player's current style from telemetry and drive persona weights during the fight.")]
    public bool useOpponentModelStyleShift = true;
    [Tooltip("How often the opponent model recomputes the player's style estimate.")]
    public float opponentModelUpdateInterval = 0.5f;
    [Tooltip("Combat distance considered close pressure from the player.")]
    public float closePressureDistance = 2.5f;
    [Tooltip("Combat distance considered a clearly defensive / disengaged player.")]
    public float farPressureDistance = 6f;
    [Tooltip("Recent attacks in the telemetry window that count as high pressure.")]
    public float highAttackCount = 6f;
    [Tooltip("Recent player damage in the telemetry window that counts as high pressure.")]
    public float highDamageWindow = 30f;
    [Tooltip("Player stamina usage rate in the telemetry window that counts as high commitment.")]
    public float highStaminaUsageRate = 30f;
    [Tooltip("Distance growth per frame that counts as the player backing off.")]
    public float retreatDistanceDelta = 0.08f;
    [Tooltip("Distance shrink per frame that counts as the player closing in.")]
    public float engageDistanceDelta = 0.08f;
    [Range(-0.5f, 0.5f)] public float opponentAggressionBias = 0f;
    public bool logOpponentModelDecisions = false;

    [Header("Debug Overlay")]
    [Tooltip("Shows a lightweight on-screen overlay with the current opponent-model estimate and style weights.")]
    public bool showOpponentModelDebugOverlay = false;
    public Vector2 debugOverlayPosition = new Vector2(16f, 16f);
    public Vector2 debugOverlaySize = new Vector2(360f, 200f);
    public bool logDebugInputActions = true;

    [Header("Action Gating")]
    [Tooltip("Extra range padding applied when deciding whether an attack is currently viable.")]
    public float attackSelectionRangePadding = 1.25f;
    [Tooltip("Minimum facing required before attack actions are considered valid.")]
    [Range(-1f, 1f)] public float minimumAttackFacing = 0.15f;

    [Header("Reward Scales")]
    public float stepPenalty = -0.0005f;
    public float engagementReward = 0.001f;
    public float disengagePenalty = -0.001f;
    public float inRangeAttackReward = 0.003f;
    public float badAttackChoicePenalty = -0.003f;
    public float damageDealtRewardScale = 0.75f;
    public float damageTakenPenaltyScale = 1.0f;
    public float winReward = 1.5f;
    public float lossPenalty = -1.5f;
    public float styleRewardScale = 0.05f;

    private readonly List<float> _policyObservationBuffer = new List<float>(32);
    private readonly List<float> _criticObservationBuffer = new List<float>(32);
    private readonly List<float> _targetStyleWeights = new List<float>(4);
    private bool _episodeResolved;
    private bool _subscribed;
    private bool _debugInputSubscribed;
    private float _opponentModelTimer;
    private float _estimatedAggressiveWeight = 1f;
    private GUIStyle _debugOverlayStyle;
    private GUIStyle _debugOverlayBoxStyle;

    public override void Initialize()
    {
        ResolveReferences();
        SubscribeCombatEvents();
        SubscribeDebugInput();
        EnsureStyleWeightsInitialized();

        if (skeletonBody != null)
        {
            skeletonBody.useExternalAI = true;
        }

        if (MaxStep <= 0)
        {
            MaxStep = defaultMaxStep;
        }

        ApplyTrainingResetOverride();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ApplyTrainingResetOverride();
        SubscribeDebugInput();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        UnsubscribeDebugInput();
        ReleaseTrainingResetOverride();
        UnsubscribeCombatEvents();
    }

    void OnDestroy()
    {
        UnsubscribeDebugInput();
        ReleaseTrainingResetOverride();
        UnsubscribeCombatEvents();
    }

    void Update()
    {
        UpdateRuntimeStyleShift();
        UpdateOpponentModelStyleShift();

        if (_episodeResolved)
        {
            return;
        }

        if (playerStats != null && playerStats.IsDead)
        {
            HandlePlayerDeath();
        }
        else if (myStats != null && myStats.IsDead)
        {
            HandleSelfDeath();
        }
    }

    void ResolveReferences()
    {
        if (skeletonBody == null) skeletonBody = GetComponent<SkeletonAI>();
        if (myStats == null) myStats = GetComponent<CharacterStats>();
        if (playerTransform == null && skeletonBody != null && skeletonBody._target != null) playerTransform = skeletonBody._target;
        if (playerStats == null && playerTransform != null) playerStats = playerTransform.GetComponent<CharacterStats>();
        if (telemetrySystem == null) telemetrySystem = FindFirstObjectByType<Telemetry>();
        if (debugPlayerInput == null) debugPlayerInput = FindFirstObjectByType<PlayerInput>();
    }

    bool ShouldOwnEpisodeResets()
    {
        return ownEpisodeResets && Academy.Instance != null && Academy.Instance.IsCommunicatorOn;
    }

    void ApplyTrainingResetOverride()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetTrainingResetOverride(ShouldOwnEpisodeResets());
        }
    }

    void ReleaseTrainingResetOverride()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetTrainingResetOverride(false);
        }
    }

    void SubscribeCombatEvents()
    {
        if (_subscribed)
        {
            return;
        }

        ResolveReferences();

        if (myStats != null)
        {
            myStats.OnTakeDamage += HandleSelfDamaged;
            myStats.OnDeath += HandleSelfDeath;
        }

        if (playerStats != null)
        {
            playerStats.OnTakeDamage += HandlePlayerDamaged;
            playerStats.OnDeath += HandlePlayerDeath;
        }

        _subscribed = true;
    }

    void UnsubscribeCombatEvents()
    {
        if (!_subscribed)
        {
            return;
        }

        if (myStats != null)
        {
            myStats.OnTakeDamage -= HandleSelfDamaged;
            myStats.OnDeath -= HandleSelfDeath;
        }

        if (playerStats != null)
        {
            playerStats.OnTakeDamage -= HandlePlayerDamaged;
            playerStats.OnDeath -= HandlePlayerDeath;
        }

        _subscribed = false;
    }

    void SubscribeDebugInput()
    {
        if (_debugInputSubscribed)
        {
            return;
        }
        ResolveReferences();
        if (debugPlayerInput == null || debugPlayerInput.actions == null)
        {
            if (logDebugInputActions)
            {
                Debug.LogWarning("[SkeletonMultiGAIL] No PlayerInput/actions found for debug controls.", this);
            }
            return;
        }
        SubscribeDebugAction("ToggleDebugOverlay", HandleToggleDebugOverlay);
        SubscribeDebugAction("DebugAggressive", HandleDebugAggressive);
        SubscribeDebugAction("DebugDefensive", HandleDebugDefensive);
        SubscribeDebugAction("DebugBalanced", HandleDebugBalanced);
        SubscribeDebugAction("DebugSixtyForty", HandleDebugSixtyForty);
        SubscribeDebugAction("ToggleOpponentModelShift", HandleToggleOpponentModelShift);
        SubscribeDebugAction("ToggleSnapStyleShift", HandleToggleSnapStyleShift);
        _debugInputSubscribed = true;

        if (logDebugInputActions)
        {
            Debug.Log($"[SkeletonMultiGAIL] Subscribed debug input using PlayerInput on '{debugPlayerInput.gameObject.name}'.", this);
        }
    }

    void UnsubscribeDebugInput()
    {
        if (!_debugInputSubscribed || debugPlayerInput == null || debugPlayerInput.actions == null)
        {
            return;
        }
        UnsubscribeDebugAction("ToggleDebugOverlay", HandleToggleDebugOverlay);
        UnsubscribeDebugAction("DebugAggressive", HandleDebugAggressive);
        UnsubscribeDebugAction("DebugDefensive", HandleDebugDefensive);
        UnsubscribeDebugAction("DebugBalanced", HandleDebugBalanced);
        UnsubscribeDebugAction("DebugSixtyForty", HandleDebugSixtyForty);
        UnsubscribeDebugAction("ToggleOpponentModelShift", HandleToggleOpponentModelShift);
        UnsubscribeDebugAction("ToggleSnapStyleShift", HandleToggleSnapStyleShift);
        _debugInputSubscribed = false;
    }

    void SubscribeDebugAction(string actionName, System.Action<InputAction.CallbackContext> callback)
    {
        InputAction action = debugPlayerInput.actions.FindAction(actionName, false);
        if (action == null)
        {
            if (logDebugInputActions)
            {
                Debug.LogWarning($"[SkeletonMultiGAIL] Debug action '{actionName}' was not found in the PlayerInput asset.", this);
            }
            return;
        }

        action.performed -= callback;
        action.performed += callback;
    }

    void UnsubscribeDebugAction(string actionName, System.Action<InputAction.CallbackContext> callback)
    {
        InputAction action = debugPlayerInput.actions.FindAction(actionName, false);
        if (action == null)
        {
            return;
        }

        action.performed -= callback;
    }
    void HandleToggleDebugOverlay(InputAction.CallbackContext _)
    {
        ToggleDebugOverlay();
        LogDebugInputAction($"Overlay {(showOpponentModelDebugOverlay ? "shown" : "hidden")}.");
    }
    void HandleDebugAggressive(InputAction.CallbackContext _)
    {
        SetDominantStyle(0);
        LogDebugInputAction("Forced aggressive style.");
    }
    void HandleDebugDefensive(InputAction.CallbackContext _)
    {
        SetDominantStyle(1);
        LogDebugInputAction("Forced defensive style.");
    }
    void HandleDebugBalanced(InputAction.CallbackContext _)
    {
        SetTwoStyleBlend(0.5f);
        LogDebugInputAction("Forced balanced 0.50/0.50 blend.");
    }
    void HandleDebugSixtyForty(InputAction.CallbackContext _)
    {
        SetTwoStyleBlend(0.6f);
        LogDebugInputAction("Forced 0.60/0.40 blend.");
    }
    void HandleToggleOpponentModelShift(InputAction.CallbackContext _)
    {
        useOpponentModelStyleShift = !useOpponentModelStyleShift;
        LogDebugInputAction($"Opponent-model shifting {(useOpponentModelStyleShift ? "enabled" : "disabled")}.");
    }
    void HandleToggleSnapStyleShift(InputAction.CallbackContext _)
    {
        snapRuntimeStyleShifts = !snapRuntimeStyleShifts;
        LogDebugInputAction($"Snap style shifts {(snapRuntimeStyleShifts ? "enabled" : "disabled")}.");
    }
    void LogDebugInputAction(string message)
    {
        if (logDebugInputActions)
        {
            Debug.Log($"[SkeletonMultiGAIL] {message}", this);
        }
    }

    bool IsInputSuppressed()
    {
        return myStats != null && myStats.IsDead;
    }

    int GetAttackCount()
    {
        return skeletonBody != null && skeletonBody.availableAttacks != null
            ? skeletonBody.availableAttacks.Count
            : 8;
    }

    int GetStyleCount()
    {
        if (multiGAILManager != null && multiGAILManager.CriticCount > 0)
        {
            return multiGAILManager.CriticCount;
        }

        if (currentStyleWeights != null && currentStyleWeights.Count > 0)
        {
            return currentStyleWeights.Count;
        }

        return 2;
    }

    int GetRawObservationSize()
    {
        return 20 + GetAttackCount();
    }

    int GetPolicyObservationSize()
    {
        return GetRawObservationSize() + GetStyleCount();
    }

    void NormalizeStyleWeights()
    {
        if (currentStyleWeights == null || currentStyleWeights.Count == 0)
        {
            currentStyleWeights = new List<float> { 1f, 0f };
        }

        float sum = 0f;
        for (int i = 0; i < currentStyleWeights.Count; i++)
        {
            currentStyleWeights[i] = Mathf.Max(0f, currentStyleWeights[i]);
            sum += currentStyleWeights[i];
        }

        if (sum <= 0f)
        {
            currentStyleWeights[0] = 1f;
            for (int i = 1; i < currentStyleWeights.Count; i++)
            {
                currentStyleWeights[i] = 0f;
            }
            return;
        }

        for (int i = 0; i < currentStyleWeights.Count; i++)
        {
            currentStyleWeights[i] /= sum;
        }
    }

    void NormalizeWeightsInPlace(List<float> weights)
    {
        if (weights == null || weights.Count == 0)
        {
            return;
        }

        float sum = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            weights[i] = Mathf.Max(0f, weights[i]);
            sum += weights[i];
        }

        if (sum <= 0f)
        {
            weights[0] = 1f;
            for (int i = 1; i < weights.Count; i++)
            {
                weights[i] = 0f;
            }
            return;
        }

        for (int i = 0; i < weights.Count; i++)
        {
            weights[i] /= sum;
        }
    }

    void EnsureTargetStyleWeightsInitialized()
    {
        int styleCount = GetStyleCount();
        while (_targetStyleWeights.Count < styleCount)
        {
            _targetStyleWeights.Add(0f);
        }

        if (_targetStyleWeights.Count > styleCount)
        {
            _targetStyleWeights.RemoveRange(styleCount, _targetStyleWeights.Count - styleCount);
        }

        bool hasAnyWeight = false;
        for (int i = 0; i < _targetStyleWeights.Count; i++)
        {
            if (_targetStyleWeights[i] > 0f)
            {
                hasAnyWeight = true;
                break;
            }
        }

        if (!hasAnyWeight)
        {
            for (int i = 0; i < styleCount; i++)
            {
                _targetStyleWeights[i] = i < currentStyleWeights.Count ? currentStyleWeights[i] : 0f;
            }
        }

        NormalizeWeightsInPlace(_targetStyleWeights);
    }

    void EnsureStyleWeightsInitialized()
    {
        int styleCount = GetStyleCount();
        if (currentStyleWeights == null)
        {
            currentStyleWeights = new List<float>(styleCount);
        }

        while (currentStyleWeights.Count < styleCount)
        {
            currentStyleWeights.Add(0f);
        }

        if (currentStyleWeights.Count > styleCount)
        {
            currentStyleWeights.RemoveRange(styleCount, currentStyleWeights.Count - styleCount);
        }

        NormalizeStyleWeights();
        EnsureTargetStyleWeightsInitialized();
    }

    void CopyCurrentWeightsToTarget()
    {
        EnsureStyleWeightsInitialized();
        for (int i = 0; i < currentStyleWeights.Count; i++)
        {
            _targetStyleWeights[i] = currentStyleWeights[i];
        }
    }

    void UpdateRuntimeStyleShift()
    {
        if (!allowRuntimeStyleShifts)
        {
            return;
        }

        EnsureStyleWeightsInitialized();

        if (snapRuntimeStyleShifts)
        {
            for (int i = 0; i < currentStyleWeights.Count; i++)
            {
                currentStyleWeights[i] = _targetStyleWeights[i];
            }
            return;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, styleShiftLerpSpeed) * Time.unscaledDeltaTime);
        for (int i = 0; i < currentStyleWeights.Count; i++)
        {
            currentStyleWeights[i] = Mathf.Lerp(currentStyleWeights[i], _targetStyleWeights[i], t);
        }

        NormalizeStyleWeights();
    }

    void UpdateOpponentModelStyleShift()
    {
        if (!useOpponentModelStyleShift || !allowRuntimeStyleShifts)
        {
            return;
        }

        if (ShouldOwnEpisodeResets())
        {
            // During PPO training, the episode-level sampled style target should stay fixed.
            return;
        }

        if (telemetrySystem == null || playerStats == null || myStats == null)
        {
            return;
        }

        if (_episodeResolved || playerStats.IsDead || myStats.IsDead)
        {
            return;
        }

        _opponentModelTimer -= Time.unscaledDeltaTime;
        if (_opponentModelTimer > 0f)
        {
            return;
        }

        _opponentModelTimer = Mathf.Max(0.05f, opponentModelUpdateInterval);
        float aggressiveWeight = EstimatePlayerAggressionWeightFromTelemetry();
        _estimatedAggressiveWeight = aggressiveWeight;
        SetTwoStyleBlend(aggressiveWeight, false);

        if (logOpponentModelDecisions)
        {
            Debug.Log(
                $"[OpponentModel] Aggressive={aggressiveWeight:F2} Defensive={1f - aggressiveWeight:F2} " +
                $"Attacks={telemetrySystem.TotalAttacks_Agent} Damage={telemetrySystem.RecentDamageDealtByPlayer_Agent:F1} " +
                $"Block={telemetrySystem.BlockSuccessRate_Agent:F2} Dodge={telemetrySystem.DodgeSuccessRate_Agent:F2} " +
                $"Parry={telemetrySystem.ParrySuccessRate_Agent:F2} Dist={telemetrySystem.PlayerEnemyDistance_Agent:F2}",
                this);
        }
    }

    float EstimatePlayerAggressionWeightFromTelemetry()
    {
        if (telemetrySystem == null)
        {
            return 1f;
        }

        float attackPressure = Mathf.Clamp01(telemetrySystem.TotalAttacks_Agent / Mathf.Max(1f, highAttackCount));
        float damagePressure = Mathf.Clamp01(telemetrySystem.RecentDamageDealtByPlayer_Agent / Mathf.Max(1f, highDamageWindow));
        float staminaPressure = Mathf.Clamp01(telemetrySystem.PlayerStaminaUsageRate_Agent / Mathf.Max(1f, highStaminaUsageRate));

        float closePressure = 1f - Mathf.InverseLerp(closePressureDistance, farPressureDistance, telemetrySystem.PlayerEnemyDistance_Agent);
        closePressure = Mathf.Clamp01(closePressure);
        float farSpacing = Mathf.Clamp01(Mathf.InverseLerp(closePressureDistance, farPressureDistance, telemetrySystem.PlayerEnemyDistance_Agent));

        float retreating = Mathf.Clamp01(Mathf.InverseLerp(0f, retreatDistanceDelta, telemetrySystem.PlayerEnemyDistanceChange_Agent));
        float engaging = Mathf.Clamp01(Mathf.InverseLerp(0f, engageDistanceDelta, -telemetrySystem.PlayerEnemyDistanceChange_Agent));

        float defensiveSkill = (
            telemetrySystem.BlockSuccessRate_Agent * 0.45f +
            telemetrySystem.ParrySuccessRate_Agent * 0.25f +
            telemetrySystem.DodgeSuccessRate_Agent * 0.30f
        );

        float aggressiveScore =
            attackPressure * 0.30f +
            damagePressure * 0.25f +
            staminaPressure * 0.15f +
            closePressure * 0.15f +
            engaging * 0.15f;

        float defensiveScore =
            defensiveSkill * 0.45f +
            retreating * 0.20f +
            farSpacing * 0.20f +
            (1f - attackPressure) * 0.15f;

        float aggressiveWeight = aggressiveScore / Mathf.Max(0.001f, aggressiveScore + defensiveScore);
        aggressiveWeight = Mathf.Clamp01(aggressiveWeight + opponentAggressionBias);
        return aggressiveWeight;
    }

    void EnsureDebugOverlayStyles()
    {
        if (_debugOverlayStyle != null && _debugOverlayBoxStyle != null)
        {
            return;
        }

        _debugOverlayStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            richText = true,
            wordWrap = true
        };
        _debugOverlayStyle.normal.textColor = Color.white;

        _debugOverlayBoxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(12, 12, 10, 10)
        };
    }

    string FormatWeights(IReadOnlyList<float> weights)
    {
        if (weights == null || weights.Count == 0)
        {
            return "[]";
        }

        List<string> formatted = new List<string>(weights.Count);
        for (int i = 0; i < weights.Count; i++)
        {
            formatted.Add(weights[i].ToString("F2"));
        }

        return "[" + string.Join(", ", formatted) + "]";
    }

    void OnGUI()
    {
        if (!showOpponentModelDebugOverlay)
        {
            return;
        }

        EnsureDebugOverlayStyles();
        EnsureStyleWeightsInitialized();

        Rect rect = new Rect(debugOverlayPosition.x, debugOverlayPosition.y, debugOverlaySize.x, debugOverlaySize.y);
        GUILayout.BeginArea(rect, GUIContent.none, _debugOverlayBoxStyle);
        GUILayout.Label("<b>Skeleton MultiGAIL Debug</b>", _debugOverlayStyle);
        GUILayout.Label($"Estimated aggressive weight: {_estimatedAggressiveWeight:F2}", _debugOverlayStyle);
        GUILayout.Label($"Current style weights: {FormatWeights(currentStyleWeights)}", _debugOverlayStyle);
        GUILayout.Label($"Target style weights: {FormatWeights(_targetStyleWeights)}", _debugOverlayStyle);
        GUILayout.Label($"Opponent-model shifting: {(useOpponentModelStyleShift ? "ON" : "OFF")}", _debugOverlayStyle);
        GUILayout.Label($"Runtime style shifts: {(allowRuntimeStyleShifts ? "ON" : "OFF")}", _debugOverlayStyle);

        if (telemetrySystem != null)
        {
            GUILayout.Label(
                $"Telemetry: attacks={telemetrySystem.TotalAttacks_Agent} damage={telemetrySystem.RecentDamageDealtByPlayer_Agent:F1} " +
                $"staminaUse={telemetrySystem.PlayerStaminaUsageRate_Agent:F1}",
                _debugOverlayStyle);
            GUILayout.Label(
                $"Spacing: dist={telemetrySystem.PlayerEnemyDistance_Agent:F2} delta={telemetrySystem.PlayerEnemyDistanceChange_Agent:F2} " +
                $"block={telemetrySystem.BlockSuccessRate_Agent:F2} dodge={telemetrySystem.DodgeSuccessRate_Agent:F2} parry={telemetrySystem.ParrySuccessRate_Agent:F2}",
                _debugOverlayStyle);
        }

        GUILayout.Label("Toggle via PlayerInput debug action", _debugOverlayStyle);
        GUILayout.EndArea();
    }

    void SampleStyleWeightsForEpisode()
    {
        EnsureStyleWeightsInitialized();

        if (!sampleStyleWeightsEachEpisode || !ShouldOwnEpisodeResets())
        {
            return;
        }

        int styleCount = currentStyleWeights.Count;
        if (styleCount == 2 && sampleOnlyEndpointStyles)
        {
            currentStyleWeights[0] = Random.value < 0.5f ? 1f : 0f;
            currentStyleWeights[1] = 1f - currentStyleWeights[0];
            CopyCurrentWeightsToTarget();
            return;
        }

        float sum = 0f;
        for (int i = 0; i < styleCount; i++)
        {
            float sample = Random.value + 0.001f;
            currentStyleWeights[i] = sample;
            sum += sample;
        }

        if (sum > 0f)
        {
            for (int i = 0; i < styleCount; i++)
            {
                currentStyleWeights[i] /= sum;
            }
        }

        CopyCurrentWeightsToTarget();
    }

    float GetDistanceToPlayer()
    {
        if (playerTransform == null)
        {
            return float.MaxValue;
        }

        return Vector3.Distance(transform.position, playerTransform.position);
    }

    float GetFacingToPlayer()
    {
        if (playerTransform == null)
        {
            return -1f;
        }

        Vector3 toPlayer = (playerTransform.position - transform.position).normalized;
        return Vector3.Dot(transform.forward, toPlayer);
    }

    bool IsAttackChoiceViable(int attackIndex)
    {
        if (skeletonBody == null || skeletonBody.availableAttacks == null)
        {
            return false;
        }

        if (attackIndex < 0 || attackIndex >= skeletonBody.availableAttacks.Count)
        {
            return false;
        }

        if (IsInputSuppressed() || !skeletonBody.useExternalAI)
        {
            return false;
        }

        SkeletonAI.EnemyAttack attack = skeletonBody.availableAttacks[attackIndex];
        if (Time.time < attack.lastTimeUsed + attack.cooldown)
        {
            return false;
        }

        float distance = GetDistanceToPlayer();
        float rangeWindow = attack.rangeTolerance + attackSelectionRangePadding;
        bool inRange = Mathf.Abs(distance - attack.optimalRange) <= rangeWindow;
        bool facingOkay = GetFacingToPlayer() >= minimumAttackFacing;
        return inRange && facingOkay;
    }

    void PopulateRawObservations(List<float> observations)
    {
        observations.Clear();

        if (skeletonBody == null || telemetrySystem == null || skeletonBody._target == null || IsInputSuppressed())
        {
            for (int i = 0; i < GetRawObservationSize(); i++)
            {
                observations.Add(0f);
            }
            return;
        }

        observations.Add((float)skeletonBody.currentState / 5f);
        observations.Add(skeletonBody.CanDealDamage ? 1f : 0f);

        float distance = Vector3.Distance(transform.position, skeletonBody._target.position);
        observations.Add(distance);
        observations.Add(transform.forward.x);
        observations.Add(transform.forward.y);
        observations.Add(transform.forward.z);

        Vector3 toPlayer = (skeletonBody._target.position - transform.position).normalized;
        observations.Add(toPlayer.x);
        observations.Add(toPlayer.y);
        observations.Add(toPlayer.z);

        observations.Add(telemetrySystem.PlayerHealthPercentage_Agent);
        observations.Add(telemetrySystem.PlayerStaminaPercentage_Agent);
        observations.Add(telemetrySystem.PlayerEnemyDistanceChange_Agent);
        observations.Add(telemetrySystem.ParrySuccessRate_Agent);
        observations.Add(telemetrySystem.DodgeSuccessRate_Agent);
        observations.Add(telemetrySystem.BlockSuccessRate_Agent);
        observations.Add(telemetrySystem.RelativeFacing_Agent);
        observations.Add(telemetrySystem.RecentDamageDealtByPlayer_Agent / 100f);

        observations.Add(telemetrySystem.EnemyHealthPercentage_Agent);
        observations.Add(telemetrySystem.RecentDamageReceivedByPlayer_Agent / 100f);
        observations.Add(telemetrySystem.TotalAttacks_Agent / 100f);

        foreach (var attack in skeletonBody.availableAttacks)
        {
            observations.Add(telemetrySystem.GetEnemyAttackSuccessRate(attack.name));
        }
    }

    void PopulatePolicyObservations(List<float> observations)
    {
        PopulateRawObservations(observations);
        EnsureStyleWeightsInitialized();

        for (int i = 0; i < currentStyleWeights.Count; i++)
        {
            observations.Add(currentStyleWeights[i]);
        }
    }

    void ClearControlledInputs()
    {
        if (skeletonBody == null)
        {
            return;
        }

        skeletonBody.SetMovementInput(0f, 0f);
    }

    float NormalizeDamage(int damage, CharacterStats stats)
    {
        if (stats == null || stats.maxHealth <= 0)
        {
            return 0f;
        }

        return damage / (float)stats.maxHealth;
    }

    void HandlePlayerDamaged(int damage)
    {
        if (_episodeResolved) return;
        AddReward(damageDealtRewardScale * NormalizeDamage(damage, playerStats));
    }

    void HandleSelfDamaged(int damage)
    {
        if (_episodeResolved) return;
        AddReward(-damageTakenPenaltyScale * NormalizeDamage(damage, myStats));
    }

    void HandlePlayerDeath()
    {
        if (_episodeResolved) return;
        _episodeResolved = true;
        AddReward(winReward);
        EndEpisode();
    }

    void HandleSelfDeath()
    {
        if (_episodeResolved) return;
        _episodeResolved = true;
        AddReward(lossPenalty);
        EndEpisode();
    }

    void ApplyShapingReward(float strafeAction, float forwardAction, int discreteAction)
    {
        AddReward(stepPenalty);

        if (skeletonBody == null || playerTransform == null)
        {
            return;
        }

        float preferredRange = skeletonBody.currentPersona != null ? skeletonBody.currentPersona.preferredCombatRange : 2.5f;
        float distance = GetDistanceToPlayer();
        float rangeError = Mathf.Abs(distance - preferredRange);
        float facing = telemetrySystem != null ? telemetrySystem.RelativeFacing_Agent : GetFacingToPlayer();

        if (facing > 0.25f && rangeError < 1.25f)
        {
            AddReward(engagementReward);
        }
        else if (distance > skeletonBody.sensorRadius * 0.75f)
        {
            AddReward(disengagePenalty);
        }

        if (discreteAction > 0)
        {
            if (IsAttackChoiceViable(discreteAction - 1))
            {
                AddReward(inRangeAttackReward);
            }
            else
            {
                AddReward(badAttackChoicePenalty);
            }
        }

        if (multiGAILManager != null)
        {
            PopulateRawObservations(_criticObservationBuffer);
            float styleReward = multiGAILManager.CalculateStyleReward(_criticObservationBuffer, strafeAction, forwardAction, discreteAction, currentStyleWeights);
            AddReward(Mathf.Clamp(styleReward, -1f, 1f) * styleRewardScale);
        }
    }

    public override void OnEpisodeBegin()
    {
        ResolveReferences();
        SubscribeCombatEvents();
        SubscribeDebugInput();
        EnsureStyleWeightsInitialized();
        SampleStyleWeightsForEpisode();
        _episodeResolved = false;
        _opponentModelTimer = 0f;
        Time.timeScale = 1.0f;

        if (skeletonBody != null)
        {
            skeletonBody.useExternalAI = true;
        }

        if (ShouldOwnEpisodeResets())
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ResetSimulationImmediate();
            }
            else
            {
                myStats?.ResetStats();
                playerStats?.ResetStats();
            }
        }

        ClearControlledInputs();
    }

    public void SetStyleWeights(IList<float> newWeights, bool immediate = false)
    {
        if (newWeights == null || newWeights.Count == 0)
        {
            return;
        }

        EnsureStyleWeightsInitialized();
        int styleCount = GetStyleCount();
        int count = Mathf.Min(styleCount, newWeights.Count);

        for (int i = 0; i < styleCount; i++)
        {
            _targetStyleWeights[i] = i < count ? Mathf.Max(0f, newWeights[i]) : 0f;
        }

        NormalizeWeightsInPlace(_targetStyleWeights);

        if (immediate || snapRuntimeStyleShifts)
        {
            for (int i = 0; i < styleCount; i++)
            {
                currentStyleWeights[i] = _targetStyleWeights[i];
            }
            NormalizeStyleWeights();
        }
    }

    public void SetDominantStyle(int styleIndex, bool immediate = false)
    {
        EnsureStyleWeightsInitialized();
        if (styleIndex < 0 || styleIndex >= GetStyleCount())
        {
            Debug.LogWarning($"Invalid style index {styleIndex} requested for {name}.", this);
            return;
        }

        for (int i = 0; i < _targetStyleWeights.Count; i++)
        {
            _targetStyleWeights[i] = i == styleIndex ? 1f : 0f;
        }

        if (immediate || snapRuntimeStyleShifts)
        {
            for (int i = 0; i < currentStyleWeights.Count; i++)
            {
                currentStyleWeights[i] = _targetStyleWeights[i];
            }
            NormalizeStyleWeights();
        }
    }

    public void SetTwoStyleBlend(float firstStyleWeight, bool immediate = false)
    {
        EnsureStyleWeightsInitialized();
        if (GetStyleCount() < 2)
        {
            Debug.LogWarning($"SetTwoStyleBlend was called on {name}, but fewer than two styles are configured.", this);
            return;
        }

        float clamped = Mathf.Clamp01(firstStyleWeight);
        _targetStyleWeights[0] = clamped;
        _targetStyleWeights[1] = 1f - clamped;
        for (int i = 2; i < _targetStyleWeights.Count; i++)
        {
            _targetStyleWeights[i] = 0f;
        }

        if (immediate || snapRuntimeStyleShifts)
        {
            for (int i = 0; i < currentStyleWeights.Count; i++)
            {
                currentStyleWeights[i] = _targetStyleWeights[i];
            }
            NormalizeStyleWeights();
        }
    }

    public IReadOnlyList<float> GetCurrentStyleWeights()
    {
        EnsureStyleWeightsInitialized();
        return currentStyleWeights;
    }

    public IReadOnlyList<float> GetTargetStyleWeights()
    {
        EnsureStyleWeightsInitialized();
        return _targetStyleWeights;
    }

    public float GetEstimatedAggressiveWeight()
    {
        return _estimatedAggressiveWeight;
    }

    public void ToggleDebugOverlay()
    {
        showOpponentModelDebugOverlay = !showOpponentModelDebugOverlay;
    }

    public void SetDebugOverlayVisible(bool isVisible)
    {
        showOpponentModelDebugOverlay = isVisible;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        PopulatePolicyObservations(_policyObservationBuffer);
        for (int i = 0; i < _policyObservationBuffer.Count; i++)
        {
            sensor.AddObservation(_policyObservationBuffer[i]);
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (skeletonBody == null || skeletonBody.availableAttacks == null)
        {
            return;
        }

        for (int actionIndex = 1; actionIndex <= skeletonBody.availableAttacks.Count; actionIndex++)
        {
            if (!IsAttackChoiceViable(actionIndex - 1))
            {
                actionMask.SetActionEnabled(0, actionIndex, false);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (skeletonBody == null || !skeletonBody.useExternalAI)
        {
            return;
        }

        if (IsInputSuppressed())
        {
            ClearControlledInputs();
            return;
        }

        if (playerTransform != null)
        {
            Vector3 dir = (playerTransform.position - transform.position).normalized;
            dir.y = 0f;
            if (dir != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * 10f);
            }
        }

        float strafe = actions.ContinuousActions[0];
        float forward = actions.ContinuousActions[1];
        skeletonBody.SetMovementInput(strafe, forward);

        int attackChoice = actions.DiscreteActions[0];
        if (attackChoice > 0 && IsAttackChoiceViable(attackChoice - 1))
        {
            skeletonBody.RequestAttack(attackChoice - 1);
        }

        ApplyShapingReward(strafe, forward, attackChoice);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete = actionsOut.DiscreteActions;

        continuous[0] = 0f;
        continuous[1] = 0f;
        discrete[0] = 0;

        if (skeletonBody == null)
        {
            return;
        }

        skeletonBody.useExternalAI = false;

        if (IsInputSuppressed())
        {
            return;
        }

        continuous[0] = skeletonBody.GetCurrentStrafe();
        continuous[1] = skeletonBody.GetCurrentForward();

        if (skeletonBody.currentState == SkeletonAI.AIState.Attacking)
        {
            var currentAttack = skeletonBody.GetCurrentAttack();
            if (currentAttack != null)
            {
                int attackIndex = skeletonBody.availableAttacks.IndexOf(currentAttack);
                if (attackIndex >= 0)
                {
                    discrete[0] = attackIndex + 1;
                }
            }
        }
    }
}




