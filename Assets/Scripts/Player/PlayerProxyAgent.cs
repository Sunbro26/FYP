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

    private Vector3 _lastEnemyPos;
    

    // --- 1. OBSERVATIONS (The Inputs) ---
    // Total Size: 20 Floats
public override void CollectObservations(VectorSensor sensor)
    {
        if (enemyTransform == null || telemetrySystem == null)
        {
            // Fallback padding (Must match 22!)
            for(int i=0; i<22; i++) sensor.AddObservation(0f);
            return;
        }

        // --- GROUP 1: Self State (4 Floats) ---
        sensor.AddObservation(blockScript.IsBlocking ? 1f : 0f); 
        sensor.AddObservation(dodgeScript.IsInvincible ? 1f : 0f); 
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);

        // --- GROUP 2: Physical/Spatial (7 Floats) ---
        float distance = Vector3.Distance(transform.position, enemyTransform.position);
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward); // Vector3 (3 floats)
        Vector3 dirToEnemy = (enemyTransform.position - transform.position).normalized;
        sensor.AddObservation(dirToEnemy); // Vector3 (3 floats)

        // --- GROUP 3: Enemy Intent (THE TELL) (8 Floats) ---
        sensor.AddObservation(telemetrySystem.EnemyFSMState_Agent);    // 1
        sensor.AddObservation(telemetrySystem.IsEnemyAttacking_Agent); // 1
        sensor.AddObservation(enemyTransform.forward);                // Vector3 (3)
        sensor.AddObservation(telemetrySystem.RelativeFacing_Agent);  // 1
        
        // --- NEW DATA FOR PARRY/DODGE TIMING ---
        sensor.AddObservation(telemetrySystem.EnemyAttackID_Agent);       // 1
        sensor.AddObservation(telemetrySystem.EnemyAttackProgress_Agent); // 1

        // --- GROUP 4: Performance Feedback (3 Floats) ---
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageDealtByPlayer_Agent / 100f); 
        sensor.AddObservation(telemetrySystem.RecentDamageReceivedByPlayer_Agent / 100f); 

        // RECALCULATE TOTAL: 4 + 7 + 8 + 3 = 22 Floats.
    }

    // --- 2. ACTIONS (The Brain driving the Body) ---
 public override void OnActionReceived(ActionBuffers actions)
    {
        // 1. Force Rotation to Face Enemy
        if (enemyTransform != null)
        {
            Vector3 directionToEnemy = (enemyTransform.position - transform.position).normalized;
            directionToEnemy.y = 0; 
            if (directionToEnemy != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(directionToEnemy), Time.deltaTime * 10f);
            }
        }

        // 2. Movement
        float inputForward = actions.ContinuousActions[1];
        float inputStrafe = actions.ContinuousActions[0];

        if (walkScript != null) 
            walkScript.SetInput(new Vector2(inputStrafe, inputForward));

        // 3. Discrete Actions: Buttons
        // 0=None, 1=Atk, 2=Block, 3=Dodge, 4=Parry
        int button = actions.DiscreteActions[0];

        // Reset persistent states
        if (blockScript != null) blockScript.SetBlocking(false); 

        switch (button)
        {
            case 1: // ATTACK
                if (attackScript) attackScript.AttemptAttack(); 
                break;

            case 2: // BLOCK
                if (blockScript) blockScript.SetBlocking(true); 
                break;

            case 3: // DODGE
                if (dodgeScript) dodgeScript.AttemptDodge(); 
                break;
            
            case 4: // PARRY
                // Ensure PlayerParry has public 'AttemptParry()' method
                if (parryScript) parryScript.AttemptParry(); 
                break;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete = actionsOut.DiscreteActions;

        continuous[0] = 0; continuous[1] = 0; discrete[0] = 0;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) continuous[1] = 1f;
            else if (Keyboard.current.sKey.isPressed) continuous[1] = -1f;

            if (Keyboard.current.dKey.isPressed) continuous[0] = 1f;
            else if (Keyboard.current.aKey.isPressed) continuous[0] = -1f;
            
            if (Keyboard.current.spaceKey.wasPressedThisFrame) discrete[0] = 3; // Dodge
            
            // Example: 'E' or 'F' for Parry if not using Mouse
            if (Keyboard.current.fKey.wasPressedThisFrame) discrete[0] = 4; 
        }

        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.isPressed) discrete[0] = 1; // Attack
            
            // Logic for Block vs Parry on Right Click
            // Simple approach: Right Click = Block. Use a Key for Parry to be distinct for ML.
            // Or: If Pressed This Frame = Parry (4), else if Pressed = Block (2).
            
            if (Mouse.current.rightButton.wasPressedThisFrame) 
                discrete[0] = 4; // Parry (Action 4)
            else if (Mouse.current.rightButton.isPressed) 
                discrete[0] = 2; // Block (Action 2)
        }

        if (continuous[0] != 0 || continuous[1] != 0 || discrete[0] != 0)
        {
            // Debug.Log($"HEURISTIC: Move=[{continuous[0]}, {continuous[1]}] | Action={discrete[0]}");
        }
    }
    
}