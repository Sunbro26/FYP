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

            // --- NEW: GET ATTACK DATA ---
            // We fetch the stats of the specific attack being used right now
            var incomingAttack = enemyScript.GetCurrentAttack();
            
            // Set defaults just in case something goes wrong
            int incomingDamage = 10;
            float incomingStaminaCost = 10f;

            if (incomingAttack != null)
            {
                incomingDamage = incomingAttack.damage;
                incomingStaminaCost = incomingAttack.blockStaminaCost;
            }
            // -----------------------------

            // 2. Check for I-Frames (Dodge)
            PlayerDodge dodgeScript = GetComponent<PlayerDodge>();
            if (dodgeScript != null && dodgeScript.IsInvincible) return;

            // 3. PARRY CHECK
            PlayerParry parryScript = GetComponent<PlayerParry>();
            if (parryScript != null && parryScript.IsParryWindowActive)
            {
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                if (angle <= 60f) 
                {
                    Debug.Log("SUCCESSFUL PARRY!");
                    enemyScript.GetParried();
                    if (blockSparksPrefab != null) Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, Quaternion.identity);
                    return; 
                }
            }

            // 4. BLOCK CHECK
            bool isBlockingSuccessfully = false;

            if (_blockScript != null && _blockScript.IsBlocking)
            {
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                if (angle <= blockAngle / 2)
                {
                    // --- CHANGED: Use incomingStaminaCost instead of local variable ---
                    if (_stats.UseStamina(incomingStaminaCost))
                    {
                        isBlockingSuccessfully = true;
                    }
                    else
                    {
                        Debug.Log("Guard Broken! Not enough stamina.");
                        // Optional: Trigger a stagger animation here
                    }
                }
            }

            // 5. Outcome Logic
            if (isBlockingSuccessfully)
            {
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
                // --- CHANGED: Use incomingDamage instead of hardcoded 10 ---
                if (_stats != null)
                {
                    _stats.TakeDamage(incomingDamage);
                }
                
                // FLINCH LOGIC
                if (_attackScript != null && !_attackScript.IsAttacking())
                {
                    Animator anim = GetComponentInChildren<Animator>();
                    if (anim != null) anim.SetTrigger("Hit"); // Make sure "Hit" is in your Player Animator
                }
                
                enemyScript.canDealDamage = false;
            }
        }
    }
}