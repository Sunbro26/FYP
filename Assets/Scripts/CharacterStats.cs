using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterStats : MonoBehaviour
{
    [Header("Identity")]
    public bool isPlayer = false; // Check this for the Player, uncheck for Boss

    [Header("Health Settings")]
    public int maxHealth = 100;
    public int currentHealth;
    public Slider healthSlider;
    public TMP_Text healthText; // Optional for Boss

    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float currentStamina;
    public float staminaRegenRate = 15f;
    public float staminaRegenDelay = 1.0f; // Time before regen starts
    public Slider staminaSlider; // Assign this only for the Player

    private float _lastStaminaUseTime;
    
    // Flag to check if dead (or defeated)
    public bool IsDead => currentHealth <= 0;

    void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }
    void Start()
    {
        UpdateUI();
    }

    void Update()
    {
        // Stamina Regeneration Logic
        if (currentStamina < maxStamina && Time.time > _lastStaminaUseTime + staminaRegenDelay)
        {
            currentStamina += staminaRegenRate * Time.deltaTime;
            if (currentStamina > maxStamina) currentStamina = maxStamina;
            UpdateUI();
        }
    }

    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0) return; // Already at 0

        currentHealth -= damage;
        
        // Clamp to 0 so we don't get negative numbers
        if (currentHealth < 0) currentHealth = 0;

        UpdateUI();

        if (currentHealth == 0)
        {
            Debug.Log(gameObject.name + " has been defeated!");
            // Add death animation logic here later
        }
    }

    public bool UseStamina(float amount)
    {
        // Bosses usually don't use stamina, so we always return true for them
        if (!isPlayer) return true;

        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            _lastStaminaUseTime = Time.time;
            UpdateUI();
            return true; // Stamina used successfully
        }
        
        return false; // Not enough stamina
    }

    private void UpdateUI()
    {
        // Update Health UI
        if (healthSlider != null)
        {
            healthSlider.value = (float)currentHealth / maxHealth;
        }
        if (healthText != null)
        {
            healthText.text = $"{currentHealth} / {maxHealth}";
        }

        // Update Stamina UI (Player only usually)
        if (staminaSlider != null)
        {
            staminaSlider.value = currentStamina / maxStamina;
        }
    }
}