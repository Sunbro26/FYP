using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class SkeletonAiProxyAgent : Agent
{
    [Header("References")]
    public SkeletonAI skeletonBody; 
    public CharacterStats myStats;
    
    [Header("Environment")]
    public Transform playerTransform;
    public Telemetry telemetrySystem; 

    private Vector3 _lastPlayerPos;

    public override void Initialize()
    {
        // Automatically tell the body to stop its own logic
        if (skeletonBody != null) skeletonBody.useExternalAI = true;
    }

public override void CollectObservations(VectorSensor sensor)
    {
        if (skeletonBody._target == null || telemetrySystem == null)
        {
            // Total: 20 base + 8 attacks = 28 floats
            for(int i=0; i<28; i++) sensor.AddObservation(0f);
            return;
        }

        // --- GROUP 1: Self State (2 Floats) ---
        sensor.AddObservation((float)skeletonBody.currentState / 5f); // Normalized enum
        sensor.AddObservation(skeletonBody.canDealDamage ? 1f : 0f);

        // --- GROUP 2: Physical/Spatial (7 Floats) ---
        float distance = Vector3.Distance(transform.position, skeletonBody._target.position);
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward); // Facing (3)
        sensor.AddObservation((skeletonBody._target.position - transform.position).normalized); // Dir to player (3)

        // --- GROUP 3: Player Telemetry (Skills & Resources) (8 Floats) ---
        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerStaminaPercentage_Agent);
        sensor.AddObservation(telemetrySystem.PlayerEnemyDistanceChange_Agent); // Is player running away?
        
        sensor.AddObservation(telemetrySystem.ParrySuccessRate_Agent); // Is player a parry god?
        sensor.AddObservation(telemetrySystem.DodgeSuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.BlockSuccessRate_Agent);
        sensor.AddObservation(telemetrySystem.RelativeFacing_Agent); // Is player looking at me?
        
        sensor.AddObservation(telemetrySystem.RecentDamageDealtByPlayer_Agent / 100f); // Pressure check

        // --- GROUP 4: Strategy Success Tracking (3 Floats) ---
        // AI sees how well IT is doing
        sensor.AddObservation(telemetrySystem.EnemyHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageReceivedByPlayer_Agent / 100f); // How much did I hit him?
        sensor.AddObservation(telemetrySystem.TotalAttacks_Agent / 100f); // APM context

        // --- GROUP 5: Per-Attack Success Rates (8 Floats) ---
        // This is key for MultiGAIL to learn which "Modes" work best
        foreach (var attack in skeletonBody.availableAttacks)
        {
            sensor.AddObservation(telemetrySystem.GetEnemyAttackSuccessRate(attack.name));
        }
    }

    // --- 2. ACTIONS ---
    public override void OnActionReceived(ActionBuffers actions)
    {
        if (skeletonBody == null) return;

         if (skeletonBody.useExternalAI == false) return; 

        // Rotation: Always face the player (Auto-Aim)
        if (playerTransform != null)
        {
            Vector3 dir = (playerTransform.position - transform.position).normalized;
            dir.y = 0;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
        }

        // Continuous: Movement (Strafe, Forward)
        float strafe = actions.ContinuousActions[0];
        float forward = actions.ContinuousActions[1];
        skeletonBody.SetMovementInput(strafe, forward);

        // Discrete: Attack Selection
        // 0 = None, 1 = Attack[0], 2 = Attack[1], etc.
        int attackChoice = actions.DiscreteActions[0];
        if (attackChoice > 0)
        {
            skeletonBody.RequestAttack(attackChoice - 1);
        }
    }

    // --- 3. HEURISTIC (Legacy AI) ---
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete = actionsOut.DiscreteActions;

        // 1. Tell the body to use its own internal C# Brain (The Utility System)
        skeletonBody.useExternalAI = false;

        // 2. SCRAPE MOVEMENT: Read what the C# script is currently doing
        continuous[0] = skeletonBody.GetCurrentStrafe();
        continuous[1] = skeletonBody.GetCurrentForward();

        // 3. SCRAPE ATTACKS: 
        // We need to know which attack index the C# script chose this frame.
        // If the AI is in the "Attacking" state, we record that index.
        discrete[0] = 0; // Default: No attack
        if (skeletonBody.currentState == SkeletonAI.AIState.Attacking)
        {
            var currentAtk = skeletonBody.GetCurrentAttack();
            if (currentAtk != null)
            {
                // Find the index of this attack in the library (+1 for 1-based ML index)
                discrete[0] = skeletonBody.availableAttacks.IndexOf(currentAtk) + 1;
            }
        }
        else if (skeletonBody.currentState == SkeletonAI.AIState.Retreating)
        {
            // Record the "Retreat" action (usually last index)
            discrete[0] = skeletonBody.availableAttacks.Count + 1;
        }
    }
}