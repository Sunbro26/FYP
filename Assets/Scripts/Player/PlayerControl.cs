using UnityEngine;

public class PlayerControl : MonoBehaviour
{
    [Header("Block Settings")]
    public float blockAngle = 60f;
    public float staminaCostPerBlock = 10f; // New: Blocking drains stamina
    
    [Tooltip("The particle effect prefab to spawn on a successful block.")]
    public GameObject blockSparksPrefab;
    
    [Tooltip("The transform where the sparks will appear.")]
    public Transform blockEffectSpawnPoint;

    // Reference to the new stats script
    private CharacterStats _stats;
    private PlayerBlock _blockScript;
    private PlayerDodge _dodgeScript;

    void Start()
    {
        _stats = GetComponent<CharacterStats>();
        _blockScript = GetComponent<PlayerBlock>();
        _dodgeScript = GetComponent<PlayerDodge>();
    }
private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("sword"))
        {
            // 1. Get Enemy Reference
            SkeletonAI enemyScript = other.GetComponentInParent<SkeletonAI>();
            if (enemyScript == null || enemyScript.canDealDamage == false) return;

            // 2. Check for I-Frames (Dodge)
            PlayerDodge dodgeScript = GetComponent<PlayerDodge>();
            if (dodgeScript != null && dodgeScript.IsInvincible) 
            {
                // --- UPDATE 1: Tell Telemetry this was a successful dodge! ---
                dodgeScript.RegisterPerfectDodge(); 
                return;
            }

            // --- 3. PARRY CHECK ---
            PlayerParry parryScript = GetComponent<PlayerParry>();
            if (parryScript != null && parryScript.IsParryWindowActive)
            {
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                if (angle <= 60f) 
                {
                    Debug.Log("SUCCESSFUL PARRY!");
                    enemyScript.GetParried(); // Telemetry listens to the event inside here (if added)
                    
                    if (blockSparksPrefab != null)
                    {
                        Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, Quaternion.identity);
                    }
                    return; 
                }
            }

            // --- 4. BLOCK CHECK ---
            bool isBlockingSuccessfully = false;
            // ... (Block logic remains the same) ...
            if (_blockScript != null && _blockScript.IsBlocking)
            {
                 // ... logic ...
                 if (_stats.UseStamina(staminaCostPerBlock)) isBlockingSuccessfully = true;
            }

            // 5. Outcome Logic
            if (isBlockingSuccessfully)
            {
                // ... (Sparks logic) ...
                enemyScript.canDealDamage = false; 
                return; 
            }
            else
            {
                // TAKE DAMAGE via CharacterStats
                if (_stats != null)
                {
                    _stats.TakeDamage(10);
                }

                // --- UPDATE 2: THE CRITICAL FIX ---
                // Tell the Enemy AI that its current attack was a success!
                // This fires the OnEnemyAttackSuccess event for the ML Model.
                enemyScript.RegisterHit(); 
                
                enemyScript.canDealDamage = false;
            }
        }
    }
}