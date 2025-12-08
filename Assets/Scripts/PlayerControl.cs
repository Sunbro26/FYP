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
    private PlayerAttack _attackScript; 
    private Animator _animator;
    private static readonly int HitTrigger = Animator.StringToHash("Hit");

    void Start()
    {
        _stats = GetComponent<CharacterStats>();
        _blockScript = GetComponent<PlayerBlock>();
        _dodgeScript = GetComponent<PlayerDodge>();
        _attackScript = GetComponent<PlayerAttack>();
        _animator = GetComponentInChildren<Animator>();
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
            if (dodgeScript != null && dodgeScript.IsInvincible) return;

            // --- 3. NEW PARRY CHECK ---
            PlayerParry parryScript = GetComponent<PlayerParry>();
            if (parryScript != null && parryScript.IsParryWindowActive)
            {
                // Check Direction (Can usually only parry attacks from the front)
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                if (angle <= 60f) // 60 degree cone for parrying
                {
                    Debug.Log("SUCCESSFUL PARRY!");
                    
                    // Trigger the enemy rebound
                    enemyScript.GetParried();

                    // Spawn a spark effect (reuse the block sparks)
                    if (blockSparksPrefab != null)
                    {
                        Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, Quaternion.identity);
                    }
                    
                    return; // Exit completely
                }
            }

            // --- 4. DIRECTIONAL BLOCK CHECK ---
            bool isBlockingSuccessfully = false;

            if (_blockScript != null && _blockScript.IsBlocking)
            {
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                // Check angle AND if we have enough stamina to block
                if (angle <= blockAngle / 2)
                {
                    // Try to consume stamina for the block
                    if (_stats.UseStamina(staminaCostPerBlock))
                    {
                        isBlockingSuccessfully = true;
                    }
                    else
                    {
                        Debug.Log("Not enough stamina to block!");
                        // Guard break logic could go here
                    }
                }
            }

            // 5. Outcome Logic
            if (isBlockingSuccessfully)
            {
                // Spawn Particles
                if (blockSparksPrefab != null && blockEffectSpawnPoint != null)
                {
                    Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                    Quaternion sparkRotation = Quaternion.LookRotation(directionToEnemy);
                    GameObject sparks = Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, sparkRotation);
                    Destroy(sparks, 1.0f);
                }

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
                
                // --- NEW FLINCH LOGIC ---
                // Only play Flinch animation if we are NOT attacking.
                // (We already know we aren't dodging or blocking from checks above)
                if (_attackScript != null && !_attackScript.IsAttacking())
                {
                    if (_animator != null) _animator.SetTrigger(HitTrigger);
                }
                
                enemyScript.canDealDamage = false;
            }
        }
    }
}