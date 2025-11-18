using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PlayerControl : MonoBehaviour
{
    public Slider healthbar;
    public TMP_Text healthText;
    public int health = 100;
    public int maxHealth = 0;

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
            // 1. Check for I-Frames (Dodge) - (Keep your existing logic here)
            PlayerDodge dodgeScript = GetComponent<PlayerDodge>();
            if (dodgeScript != null && dodgeScript.IsInvincible) return;

            // --- 2. NEW BLOCK CHECK ---
            PlayerBlock blockScript = GetComponent<PlayerBlock>();
            
            // If the block script exists AND we are holding the block button
            if (blockScript != null && blockScript.IsBlocking)
            {
                Debug.Log("Blocked Damage!");
                
                // Disable the enemy's sword so it doesn't hit us the moment we let go of block
                SkeletonAI enemyScript = other.GetComponentInParent<SkeletonAI>();
                if (enemyScript != null) enemyScript.canDealDamage = false;

                return; // STOP HERE. Do not subtract health.
            }
            // ---------------------------

            // 3. Normal Hit Logic
            SkeletonAI skeleton = other.GetComponentInParent<SkeletonAI>();
            if (skeleton != null && skeleton.canDealDamage == true)
            {
                health = health - 10;
                skeleton.canDealDamage = false;
            }
        }
    }
}