using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PlayerControl : MonoBehaviour
{
    [Header("UI")]
    public Slider healthbar;
    public TMP_Text healthText;
    

    [Header("Stats")]
    public int health = 100;
    public int maxHealth = 0;

    [Header("Block Settings")]
    public float blockAngle = 60f;
    
    [Tooltip("The particle effect prefab to spawn on a successful block.")]
    public GameObject blockSparksPrefab;
    
    [Tooltip("The transform where the sparks will appear (e.g., an empty object on the shield).")]
    public Transform blockEffectSpawnPoint;

    void Start()
    {
        maxHealth = health;
    }

    void Update()
    {
        healthText.text = health + " / " + maxHealth;
        healthbar.value = (float)health / (float)maxHealth;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("sword"))
        {
            // 1. Check for I-Frames (Dodge)
            PlayerDodge dodgeScript = GetComponent<PlayerDodge>();
            if (dodgeScript != null && dodgeScript.IsInvincible) return;

            // 2. Get Enemy Reference
            SkeletonAI enemyScript = other.GetComponentInParent<SkeletonAI>();
            if (enemyScript == null || enemyScript.canDealDamage == false) return;

            // --- 3. DIRECTIONAL BLOCK CHECK ---
            PlayerBlock blockScript = GetComponent<PlayerBlock>();
            bool isBlockingSuccessfully = false;

            if (blockScript != null && blockScript.IsBlocking)
            {
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                if (angle <= blockAngle / 2)
                {
                    isBlockingSuccessfully = true;
                }
            }

            // 4. Outcome Logic
            if (isBlockingSuccessfully)
            {
                // --- SPAWN PARTICLES ---
                if (blockSparksPrefab != null && blockEffectSpawnPoint != null)
                {
                    // Instantiate the sparks at the spawn point.
                    // We rotate them to face the enemy so the sparks fly towards the impact.
                    Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                    Quaternion sparkRotation = Quaternion.LookRotation(directionToEnemy);
                    
                    GameObject sparks = Instantiate(blockSparksPrefab, blockEffectSpawnPoint.position, sparkRotation);
                    
                    // Destroy the sparks after 1 second to keep the game clean
                    Destroy(sparks, 1.0f);
                }
                // -----------------------

                enemyScript.canDealDamage = false; 
                return; 
            }
            else
            {
                health = health - 10;
                enemyScript.canDealDamage = false;
            }
        }
    }
}