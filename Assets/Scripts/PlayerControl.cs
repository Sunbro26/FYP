using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PlayerControl : MonoBehaviour
{
    public Slider healthbar;
    public TMP_Text healthText;
    public int health = 100;
    public int maxHealth = 0;

    // New Setting: The total width of the block angle (in degrees)
    public float blockAngle = 60f; 

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
                // Get direction from Player to Enemy
                Vector3 directionToEnemy = (enemyScript.transform.position - transform.position).normalized;
                
                // Calculate the angle between Player's Forward and the Enemy Direction
                float angle = Vector3.Angle(transform.forward, directionToEnemy);

                // We divide the total angle by 2. 
                // Example: For a 60-degree cone, we check if the enemy is within 30 degrees left or right.
                if (angle <= blockAngle / 2)
                {
                    isBlockingSuccessfully = true;
                }
                else
                {
                    Debug.Log("Block failed! Attack came from angle: " + angle);
                }
            }

            // 4. Outcome Logic
            if (isBlockingSuccessfully)
            {
                Debug.Log("Blocked Directional Attack!");
                // Disable damage for this swing so it doesn't hit us immediately after
                enemyScript.canDealDamage = false; 
                return; // STOP HERE. Take no damage.
            }
            else
            {
                // Take Damage (Either wasn't blocking, or blocked in the wrong direction)
                health = health - 10;
                enemyScript.canDealDamage = false;
            }
        }
    }
}