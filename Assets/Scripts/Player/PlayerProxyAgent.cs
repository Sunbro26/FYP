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
    public Telemetry telemetrySystem; // Must be assigned!

    // --- 1. OBSERVATIONS (The Inputs) ---
    // Total Size: 20 Floats
    public override void CollectObservations(VectorSensor sensor)
    {
        if (enemyTransform == null || telemetrySystem == null)
        {
            // Fallback padding (20 zeros)
            for(int i=0; i<20; i++) sensor.AddObservation(0f);
            return;
        }

        // Group 1: Internal State (2 floats)
        // Replaces AIState enum with boolean flags converted to floats
        sensor.AddObservation(blockScript.IsBlocking ? 1f : 0f); 
        sensor.AddObservation(dodgeScript.IsInvincible ? 1f : 0f); 

        // Group 2: Physical Spatial State (7 floats)
        float distance = Vector3.Distance(transform.position, enemyTransform.position);
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward); // 3 floats
        sensor.AddObservation((enemyTransform.position - transform.position).normalized); // 3 floats

        // Group 3: Telemetry / Self-Awareness (11 floats)
        // Spatial
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistance_Agent);
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent);
        
        // Resources (Crucial for learning stamina management)
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);
        sensor.AddObservation(telemetrySystem.StaminaUsageRate_Agent);

        // Performance / Skill
        sensor.AddObservation(telemetrySystem.RecentDamageDealt_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageReceived_Agent);
        sensor.AddObservation(telemetrySystem.AttackSuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.ParrySuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.DodgeSuccessRate_Agent);
        
        // Activity Level
        sensor.AddObservation((float)telemetrySystem.TotalAttacks_Agent);
    }

    // --- 2. ACTIONS (The Brain driving the Body) ---
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Continuous: Movement (WASD)
        float moveX = actions.ContinuousActions[0];
        float moveY = actions.ContinuousActions[1];
        
        if (walkScript != null) 
            walkScript.SetInput(new Vector2(moveX, moveY));

        // Discrete: Buttons
        // 0=None, 1=Atk, 2=Block, 3=Dodge
        int button = actions.DiscreteActions[0];

        // Default state (stop blocking unless button is held)
        if (blockScript != null) blockScript.SetBlocking(false); 

        switch (button)
        {
            case 1: 
                if (attackScript) attackScript.AttemptAttack(); 
                break;
            case 2: 
                if (blockScript) blockScript.SetBlocking(true); 
                break;
            case 3: 
                if (dodgeScript) dodgeScript.AttemptDodge(); 
                break;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete = actionsOut.DiscreteActions;

        // Reset
        continuous[0] = 0;
        continuous[1] = 0;
        discrete[0] = 0;

        // 1. Read Keyboard for WASD
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) continuous[1] = 1f;
            else if (Keyboard.current.sKey.isPressed) continuous[1] = -1f;

            if (Keyboard.current.dKey.isPressed) continuous[0] = 1f;
            else if (Keyboard.current.aKey.isPressed) continuous[0] = -1f;
            
            // Buttons
            if (Keyboard.current.spaceKey.wasPressedThisFrame) discrete[0] = 3; // Dodge
        }

        // 2. Read Mouse for Attacks/Block
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.isPressed) discrete[0] = 1; // Attack
            else if (Mouse.current.rightButton.isPressed) discrete[0] = 2; // Block
        }

        // --- DEBUGGING ---
        // Only log if we are actually pressing something
        if (continuous[0] != 0 || continuous[1] != 0 || discrete[0] != 0)
        {
            Debug.Log($"<color=cyan>HEURISTIC:</color> Move=[{continuous[0]}, {continuous[1]}] | Action={discrete[0]}");
        }
    }
    
}