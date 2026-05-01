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

    [Header("Stamina Discipline")]
    [Tooltip("Extra stamina to preserve before allowing an attack. This helps keep enough reserve for defense.")]
    public float attackReserveStamina = 15f;
    [Tooltip("If stamina is regen-locked and below this ratio, suppress attacks to stop panic-exhaustion loops.")]
    [Range(0f, 1f)] public float lowStaminaRatioDuringRegenLock = 0.35f;

    const int ObservationSize = 22;

    bool IsInputSuppressed()
    {
        return myStats != null && myStats.IsDead;
    }

    void AddZeroObservations(VectorSensor sensor)
    {
        for (int i = 0; i < ObservationSize; i++)
        {
            sensor.AddObservation(0f);
        }
    }

    void ClearControlledInputs()
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

    bool HasAttackReserve()
    {
        if (myStats == null || attackScript == null)
        {
            return false;
        }

        return myStats.currentStamina >= attackScript.StaminaCost + attackReserveStamina;
    }

    bool IsLowStaminaDuringRegenLock()
    {
        if (myStats == null || myStats.maxStamina <= 0f)
        {
            return false;
        }

        return myStats.IsStaminaRegenLocked && (myStats.currentStamina / myStats.maxStamina) <= lowStaminaRatioDuringRegenLock;
    }

    bool CanAttemptAttackNow()
    {
        if (attackScript == null || !attackScript.CanAttemptAttack())
        {
            return false;
        }

        if (!HasAttackReserve())
        {
            return false;
        }

        if (IsLowStaminaDuringRegenLock())
        {
            return false;
        }

        return true;
    }

    bool CanAttemptDodgeNow()
    {
        return dodgeScript != null && dodgeScript.CanAttemptDodge();
    }

    bool CanAttemptParryNow()
    {
        return parryScript != null && parryScript.CanAttemptParry();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (enemyTransform == null || telemetrySystem == null || IsInputSuppressed())
        {
            AddZeroObservations(sensor);
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
            for (int action = 1; action <= 4; action++)
            {
                actionMask.SetActionEnabled(0, action, false);
            }
            return;
        }

        if (!CanAttemptAttackNow())
        {
            actionMask.SetActionEnabled(0, 1, false);
        }

        if (blockScript == null)
        {
            actionMask.SetActionEnabled(0, 2, false);
        }

        if (!CanAttemptDodgeNow())
        {
            actionMask.SetActionEnabled(0, 3, false);
        }

        if (!CanAttemptParryNow())
        {
            actionMask.SetActionEnabled(0, 4, false);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (IsInputSuppressed())
        {
            ClearControlledInputs();
            return;
        }

        if (enemyTransform != null)
        {
            Vector3 directionToEnemy = (enemyTransform.position - transform.position).normalized;
            directionToEnemy.y = 0f;
            if (directionToEnemy != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(directionToEnemy), Time.deltaTime * 10f);
            }
        }

        float inputForward = actions.ContinuousActions[1];
        float inputStrafe = actions.ContinuousActions[0];

        if (walkScript != null)
        {
            walkScript.SetInput(new Vector2(inputStrafe, inputForward));
        }

        int button = actions.DiscreteActions[0];

        if (blockScript != null)
        {
            blockScript.SetBlocking(false);
        }

        switch (button)
        {
            case 1:
                if (CanAttemptAttackNow()) attackScript.AttemptAttack();
                break;
            case 2:
                if (blockScript != null) blockScript.SetBlocking(true);
                break;
            case 3:
                if (CanAttemptDodgeNow()) dodgeScript.AttemptDodge();
                break;
            case 4:
                if (CanAttemptParryNow()) parryScript.AttemptParry();
                break;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete = actionsOut.DiscreteActions;

        continuous[0] = 0f;
        continuous[1] = 0f;
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

            if (Keyboard.current.spaceKey.wasPressedThisFrame && CanAttemptDodgeNow()) discrete[0] = 3;
            if (Keyboard.current.fKey.wasPressedThisFrame && CanAttemptParryNow()) discrete[0] = 4;
        }

        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.isPressed && CanAttemptAttackNow()) discrete[0] = 1;

            if (Mouse.current.rightButton.wasPressedThisFrame && CanAttemptParryNow())
                discrete[0] = 4;
            else if (Mouse.current.rightButton.isPressed)
                discrete[0] = 2;
        }
    }
}
