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
            // --- 1. CHECK FOR I-FRAMES ---
            // Get the dodge script reference
            PlayerDodge dodgeScript = GetComponent<PlayerDodge>();
            
            // If the script exists AND IsInvincible is true, ignore the hit completely.
            if (dodgeScript != null && dodgeScript.IsInvincible)
            {
                Debug.Log("Dodged damage thanks to I-Frames!");
                return; // Exit the function immediately
            }
            // -----------------------------

            SkeletonAI enemyScript = other.GetComponentInParent<SkeletonAI>();

            if (enemyScript != null && enemyScript.canDealDamage == true)
            {
                health = health - 10;
                enemyScript.canDealDamage = false;
            }
        }
    }
}