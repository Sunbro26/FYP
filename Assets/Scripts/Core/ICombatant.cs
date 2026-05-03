using UnityEngine;

// This is the "Contract". Any enemy (Skeleton, Dragon, Robot) that wants to 
// fight the player MUST include these methods.
public interface ICombatant
{
    bool CanDealDamage { get; set; }
    
    Transform GetTransform(); // Allows the player to find where the enemy is standing
    void TakeHit();
    void GetParried();
    void RegisterHit(); // For your teammate's Telemetry
    
    int GetIncomingDamage();
    float GetIncomingStaminaCost();
    bool IsIncomingAttackParriable();
}