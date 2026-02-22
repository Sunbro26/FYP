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
        Vector3 dirToEnemy = (enemyTransform.position - transform.position).normalized;
        
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward); // 3 floats
        sensor.AddObservation(dirToEnemy); // 3 floats

          // --- GROUP 3: Tactical Enemy Context (6 Floats) ---
        // ADDITION: This stops random rolling by allowing the AI to see the "Why" (Intent)
        sensor.AddObservation(telemetrySystem.EnemyFSMState_Agent);    // 1 float: Idle/Attack/Maneuver?
        sensor.AddObservation(telemetrySystem.IsEnemyAttacking_Agent); // 1 float: Swing detection

        // Relative Angle (Dot Product): 1 float
        // ADDITION: Provides a "Cheat Sheet" for facing. 1.0 = Facing enemy, -1.0 = Back turned.
        // This is key to fixing the "not looking at skeletons" problem.
        sensor.AddObservation(Vector3.Dot(transform.forward, dirToEnemy)); 
        
        // Enemy Forward Vector: 3 floats
        // ADDITION: Helps AI learn "Backstabbing" by knowing where the enemy is looking.
        sensor.AddObservation(enemyTransform.forward); 

        // --- GROUP 4: Dynamic Performance & Resources (5 Floats) ---
        // UPDATE: Removed "Total Counts" and "Success Rates" (These are too slow for combat).
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent); // Moving closer or further?
        
        // Damage feedback normalized for the neural network
        sensor.AddObservation(telemetrySystem.RecentDamageDealt_Agent / 100f);
        sensor.AddObservation(telemetrySystem.RecentDamageReceived_Agent / 100f);

        // FINAL VALIDATION: 2 + 7 + 6 + 5 = 20 FLOATS TOTAL.
    }

    // --- 2. ACTIONS (The Brain driving the Body) ---
    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- TACTICAL ROTATION: Always face target ---
        if (enemyTransform != null)
        {
            Vector3 lookDir = (enemyTransform.position - transform.position).normalized;
            lookDir.y = 0; 
            if (lookDir != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 15f);
            }
        }

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
            case 1: // ATTACK
                if (enemyTransform != null)
                {
                    // Instant snap for attack precision
                    Vector3 attackDir = (enemyTransform.position - transform.position).normalized;
                    attackDir.y = 0;
                    if (attackDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(attackDir);
                }
                if (attackScript) attackScript.AttemptAttack(); 
                break;

            case 2: // BLOCK
                if (blockScript) blockScript.SetBlocking(true); 
                break;

            case 3: // DODGE
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