using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

public class PlayerProxyAgent : Agent
{
    [Header("References")]
    public Walk walkScript;
    public PlayerAttack attackScript;
    public PlayerBlock blockScript;
    public PlayerDodge dodgeScript;
    public PlayerParry parryScript;
    public CharacterStats myStats;

    [Header("Environment")]
    public Transform enemyTransform;
    public Telemetry telemetrySystem;

    [Header("Proxy Constraints")]
    [Range(-1f, 1f)] public float attackFacingDot = 0.1f;
    [Range(-1f, 1f)] public float defenseFacingDot = 0.2f;
    public float attackRangePadding = 0.85f;
    public float blockAttackProgressMin = 0.05f;
    public float parryEarlyTolerance = 0.02f;
    public float parryLateTolerance = 0.08f;

    private SkeletonAI _enemySkeleton;

    public override void Initialize()
    {
        CacheEnemyReferences();
    }

    private void CacheEnemyReferences()
    {
        if (enemyTransform == null)
        {
            _enemySkeleton = null;
            return;
        }

        _enemySkeleton = enemyTransform.GetComponent<SkeletonAI>();
        if (_enemySkeleton == null)
        {
            _enemySkeleton = enemyTransform.GetComponentInParent<SkeletonAI>();
        }
    }

    private bool IsInputSuppressed()
    {
        return myStats != null && myStats.IsDead;
    }

    private void ClearControlledInputs()
    {
        if (walkScript != null)
        {
            walkScript.SetInput(Vector2.zero);
        }

        if (blockScript != null)
        {
            blockScript.SetBlocking(false);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (enemyTransform == null || telemetrySystem == null || IsInputSuppressed())
        {
            for (int i = 0; i < 22; i++) sensor.AddObservation(0f);
            return;
        }

        sensor.AddObservation(blockScript != null && blockScript.IsBlocking ? 1f : 0f);
        sensor.AddObservation(dodgeScript != null && dodgeScript.IsInvincible ? 1f : 0f);
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);

        float distance = Vector3.Distance(transform.position, enemyTransform.position);
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward);
        Vector3 dirToEnemy = (enemyTransform.position - transform.position).normalized;
        sensor.AddObservation(dirToEnemy);

        sensor.AddObservation(telemetrySystem.EnemyFSMState_Agent);
        sensor.AddObservation(telemetrySystem.IsEnemyAttacking_Agent);
        sensor.AddObservation(enemyTransform.forward);
        sensor.AddObservation(telemetrySystem.RelativeFacing_Agent);
        sensor.AddObservation(telemetrySystem.EnemyAttackID_Agent);
        sensor.AddObservation(telemetrySystem.EnemyAttackProgress_Agent);

        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageDealtByPlayer_Agent / 100f);
        sensor.AddObservation(telemetrySystem.RecentDamageReceivedByPlayer_Agent / 100f);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (IsInputSuppressed())
        {
            actionMask.SetActionEnabled(0, 1, false);
            actionMask.SetActionEnabled(0, 2, false);
            actionMask.SetActionEnabled(0, 3, false);
            actionMask.SetActionEnabled(0, 4, false);
            return;
        }

        if (!CanRaiseBlockNow()) actionMask.SetActionEnabled(0, 2, false);
        if (!CanAttemptDodgeNow()) actionMask.SetActionEnabled(0, 3, false);
        if (!CanAttemptParryNow()) actionMask.SetActionEnabled(0, 4, false);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        CacheEnemyReferences();

        if (IsInputSuppressed())
        {
            ClearControlledInputs();
            return;
        }

        if (enemyTransform != null)
        {
            Vector3 directionToEnemy = (enemyTransform.position - transform.position).normalized;
            directionToEnemy.y = 0;
            if (directionToEnemy != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(directionToEnemy),
                    Time.deltaTime * 10f);
            }
        }

        float inputForward = actions.ContinuousActions[1];
        float inputStrafe = actions.ContinuousActions[0];

        if (walkScript != null)
            walkScript.SetInput(new Vector2(inputStrafe, inputForward));

        int button = actions.DiscreteActions[0];

        if (blockScript != null) blockScript.SetBlocking(false);

        switch (button)
        {
            case 1:
                if (attackScript != null) attackScript.AttemptAttack();
                break;

            case 2:
                if (CanRaiseBlockNow() && blockScript != null) blockScript.SetBlocking(true);
                break;

            case 3:
                if (CanAttemptDodgeNow() && dodgeScript != null) dodgeScript.AttemptDodge();
                break;

            case 4:
                if (CanAttemptParryNow() && parryScript != null) parryScript.AttemptParry();
                break;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete = actionsOut.DiscreteActions;

        continuous[0] = 0;
        continuous[1] = 0;
        discrete[0] = 0;

        if (IsInputSuppressed())
        {
            return;
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) continuous[1] = 1f;
            else if (Keyboard.current.sKey.isPressed) continuous[1] = -1f;

            if (Keyboard.current.dKey.isPressed) continuous[0] = 1f;
            else if (Keyboard.current.aKey.isPressed) continuous[0] = -1f;

            if (Keyboard.current.spaceKey.wasPressedThisFrame) discrete[0] = 3;
            if (Keyboard.current.fKey.wasPressedThisFrame) discrete[0] = 4;
        }

        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.isPressed) discrete[0] = 1;

            if (Mouse.current.rightButton.wasPressedThisFrame)
                discrete[0] = 4;
            else if (Mouse.current.rightButton.isPressed)
                discrete[0] = 2;
        }
    }

    private Vector3 FlatDirectionToEnemy()
    {
        if (enemyTransform == null) return transform.forward;

        Vector3 dir = enemyTransform.position - transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
    }

    private bool IsEnemyInFront(float minDot)
    {
        if (enemyTransform == null) return false;
        return Vector3.Dot(transform.forward, FlatDirectionToEnemy()) >= minDot;
    }

    private bool IsEnemyActivelyAttacking()
    {
        if (telemetrySystem == null || enemyTransform == null) return false;
        if (telemetrySystem.IsEnemyAttacking_Agent <= 0.5f) return false;
        if (!IsEnemyInFront(defenseFacingDot)) return false;

        return telemetrySystem.EnemyAttackProgress_Agent >= blockAttackProgressMin;
    }

    private bool CanRaiseBlockNow()
    {
        if (blockScript == null || enemyTransform == null) return false;
        if (!blockScript.CanRaiseBlock()) return false;

        return IsEnemyActivelyAttacking();
    }

    private bool CanAttemptDodgeNow()
    {
        if (dodgeScript == null || enemyTransform == null) return false;
        if (!dodgeScript.CanAttemptDodge()) return false;
        if (!IsEnemyActivelyAttacking()) return false;

        return telemetrySystem != null && telemetrySystem.EnemyAttackProgress_Agent >= 0.15f;
    }

    private bool CanAttemptParryNow()
    {
        if (parryScript == null || _enemySkeleton == null) return false;
        if (!parryScript.CanAttemptParry()) return false;
        if (_enemySkeleton.currentState != SkeletonAI.AIState.Attacking) return false;
        if (!IsEnemyInFront(attackFacingDot)) return false;

        SkeletonAI.EnemyAttack currentAttack = _enemySkeleton.GetCurrentAttack();
        if (currentAttack == null || !currentAttack.isParriable) return false;

        float timeUntilHit = currentAttack.windUpTime - _enemySkeleton.GetAttackElapsedTime();
        float minLead = Mathf.Max(0f, parryScript.parryWindowStart - parryEarlyTolerance);
        float maxLead = parryScript.parryWindowStart + parryScript.parryWindowDuration + parryLateTolerance;

        return timeUntilHit >= minLead && timeUntilHit <= maxLead;
    }
}