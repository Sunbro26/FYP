using System.Collections.Generic;
using UnityEngine;
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
    private bool _episodeResolved;
    private bool _subscribed;

    public override void Initialize()
    {
        ResolveReferences();
        SubscribeCombatEvents();
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

    void OnEnable()
    {
        ApplyTrainingResetOverride();
    }

    void OnDisable()
    {
        ReleaseTrainingResetOverride();
        UnsubscribeCombatEvents();
    }

    void OnDestroy()
    {
        ReleaseTrainingResetOverride();
        UnsubscribeCombatEvents();
    }

    void Update()
    {
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
        if (currentStyleWeights != null && currentStyleWeights.Count > 0)
        {
            return currentStyleWeights.Count;
        }

        if (multiGAILManager != null && multiGAILManager.CriticCount > 0)
        {
            return multiGAILManager.CriticCount;
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
        observations.Add(skeletonBody.canDealDamage ? 1f : 0f);

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
        EnsureStyleWeightsInitialized();
        SampleStyleWeightsForEpisode();
        _episodeResolved = false;
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
