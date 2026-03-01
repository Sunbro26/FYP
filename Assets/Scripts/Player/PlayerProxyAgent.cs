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
            // Fallback padding (Must match final count!)
            for(int i=0; i<20; i++) sensor.AddObservation(0f);
            return;
        }

        // --- GROUP 1: Self State (2 Floats) ---
        sensor.AddObservation(blockScript.IsBlocking ? 1f : 0f); 
        sensor.AddObservation(dodgeScript.IsInvincible ? 1f : 0f); 

        // --- GROUP 2: Spatial Relationship (7 Floats) ---
        float distance = Vector3.Distance(transform.position, enemyTransform.position);
        sensor.AddObservation(distance);
        
        // Critical: We send BOTH forward vectors.
        // This lets the NN calculate angles (e.g., "Am I behind him?")
        sensor.AddObservation(transform.forward); // My Facing (3)
        Vector3 dirToEnemy = (enemyTransform.position - transform.position).normalized;
        sensor.AddObservation(dirToEnemy); // Vector To Enemy (3)

        // --- GROUP 3: Enemy Intent (CRITICAL FOR DODGING) (6 Floats) ---
        // 1. Is he attacking? (Use Telemetry or direct reference if possible)
        // Ideally, read the Enemy's Animator state or a boolean from SkeletonAI
        SkeletonAI enemyAI = enemyTransform.GetComponent<SkeletonAI>();
        bool enemyAttacking = (enemyAI != null && enemyAI.canDealDamage); // Or use IsActionLocked
        sensor.AddObservation(enemyAttacking ? 1f : 0f); 

        // 2. Enemy Facing (So we can dodge to his back/side)
        sensor.AddObservation(enemyTransform.forward); // (3 floats)

        // 3. Dot Product (Are we facing each other?)
        // 1 = Face to Face, -1 = Back turned
        float facingDot = Vector3.Dot(transform.forward, enemyTransform.forward);
        sensor.AddObservation(facingDot); // (1 float)
        
        // 4. Enemy Velocity / Movement (Are they rushing me?)
        // Simple proxy: Is he moving?
        // (1 float)
         Vector3 enemyVelocity = (enemyTransform.position - _lastEnemyPos) / Time.fixedDeltaTime;
        float enemySpeed = enemyVelocity.magnitude;
        
        // Add to observations (Replaces NavMeshAgent logic)
        sensor.AddObservation(enemySpeed);

        // Update for next frame
        _lastEnemyPos = enemyTransform.position;    


        // --- GROUP 4: Telemetry / Performance (5 Floats) ---
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);
        
        // Dynamic feedback
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent); // Closing speed
        sensor.AddObservation(telemetrySystem.RecentDamageDealt_Agent / 100f);  // Reward signal
        sensor.AddObservation(telemetrySystem.RecentDamageReceived_Agent / 100f); // Punishment signal
        
        // RECALCULATE TOTAL:
        // G1: 2
        // G2: 7
        // G3: 1 + 3 + 1 + 1 = 6
        // G4: 5
        // Total = 20. (Perfect match for your current setup!)
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