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

    // --- 1. OBSERVATIONS (20 Floats) ---
    public override void CollectObservations(VectorSensor sensor)
    {
        if (playerTransform == null || telemetrySystem == null || skeletonBody == null)
        {
            for(int i=0; i<20; i++) sensor.AddObservation(0f);
            return;
        }

        // GROUP 1: Self State (3 Floats)
        sensor.AddObservation(myStats != null ? myStats.currentHealth / myStats.maxHealth : 1f); 
        sensor.AddObservation(skeletonBody.canDealDamage ? 1f : 0f); 
        sensor.AddObservation(skeletonBody.currentState == SkeletonAI.AIState.Stunned ? 1f : 0f); 

        // GROUP 2: Spatial Relationship (7 Floats)
        float distance = Vector3.Distance(transform.position, playerTransform.position);
        sensor.AddObservation(distance);
        sensor.AddObservation(transform.forward); // My Facing (3)
        Vector3 dirToPlayer = (playerTransform.position - transform.position).normalized;
        sensor.AddObservation(dirToPlayer); // Vector To Player (3)

        // GROUP 3: Player Intent (6 Floats)
        // We observe the player to learn when to retreat or attack
        SkeletonAI playerAI = playerTransform.GetComponent<SkeletonAI>(); // If player uses same script
        // Note: If player uses different scripts, reference PlayerAttack/PlayerBlock here
        sensor.AddObservation(0f); // Placeholder for PlayerAttacking
        sensor.AddObservation(0f); // Placeholder for PlayerBlocking
        sensor.AddObservation(playerTransform.forward); // (3 floats)
        
        float facingDot = Vector3.Dot(transform.forward, playerTransform.forward);
        sensor.AddObservation(facingDot); 

        // GROUP 4: Telemetry & Movement (4 Floats)
        Vector3 playerVel = (playerTransform.position - _lastPlayerPos) / Time.fixedDeltaTime;
        sensor.AddObservation(playerVel.magnitude);
        _lastPlayerPos = playerTransform.position;

        sensor.AddObservation(telemetrySystem.PlayerHealthPercentage_Agent);
        sensor.AddObservation(telemetrySystem.RecentDamageDealt_Agent / 100f); 
        sensor.AddObservation(telemetrySystem.RecentDamageReceived_Agent / 100f); 
        
        // Total = 20 Floats
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

        // Simply turn off the Proxy flag so the original SkeletonAI logic runs
        skeletonBody.useExternalAI = false;
        
        // Note: In Heuristic mode, the Agent doesn't "do" anything, 
        // it just lets the original script's Update() function take over.
    }
}