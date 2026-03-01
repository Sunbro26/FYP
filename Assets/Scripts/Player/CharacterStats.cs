using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class CharacterStats : MonoBehaviour
{
    [Header("Identity")]
    public bool isPlayer = false; 

    [Header("Health Settings")]
    public int maxHealth = 100;
    public int currentHealth;
    public Slider healthSlider;
    public TMP_Text healthText; 

    [Header("Stamina Settings")]
    public float maxStamina = 100f;
    public float currentStamina;
    public float staminaRegenRate = 15f;
    
    [Tooltip("Standard delay after using stamina before it comes back.")]
    public float staminaRegenDelay = 1.0f; // Renamed from normalRegenDelay to match your old script
    
    [Tooltip("Penalty delay when stamina hits exactly 0.")]
    public float exhaustionDelay = 2.5f; // New feature

    public Slider staminaSlider; 

    // --- Internal State ---
    // We switched from _lastStaminaUseTime to _regenStartTime 
    // because it makes pausing regeneration (Guard Break) much easier to calculate.
    private float _regenStartTime; 
    
    public bool IsDead => currentHealth <= 0;

    // --- TEAMMATE'S TELEMETRY EVENT (PRESERVED) ---
    public event Action<int> OnTakeDamage;

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
        // We only regen if we are below max AND the current time is past the allowed start time
        if (currentStamina < maxStamina && Time.time > _regenStartTime)
        {
            currentStamina += staminaRegenRate * Time.deltaTime;
            if (currentStamina > maxStamina) currentStamina = maxStamina;
            UpdateUI();
        }
    }

    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0) return; 

        // --- FIRE TELEMETRY EVENT ---
        // Using ?.Invoke checks if the Telemetry script is actually listening before firing
        // to prevent crashes if the Telemetry script is disabled.
        OnTakeDamage?.Invoke(damage);

        currentHealth -= damage;
        
        if (currentHealth < 0) currentHealth = 0;

        UpdateUI();

        if (currentHealth == 0)
        {
            Debug.Log(gameObject.name + " has been defeated!");
        }
    }

    public bool UseStamina(float amount)
    {
        if (!isPlayer) return true; 

        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            
            // LOGIC: Did we hit 0?
            if (currentStamina <= 0.1f) 
            {
                currentStamina = 0;
                // Apply the longer "Exhaustion" penalty
                _regenStartTime = Time.time + exhaustionDelay;
            }
            else
            {
                // Apply the standard delay
                _regenStartTime = Time.time + staminaRegenDelay;
            }

            UpdateUI();
            return true; 
        }
        
        return false; // Not enough stamina
    }

    // --- NEW: Helper to force a pause (used by Guard Break in PlayerControl) ---
    public void PauseStaminaRegen(float duration)
    {
        // Push the regen start time into the future
        float targetTime = Time.time + duration;
        
        // Only update if this new pause is longer than the current wait
        if (targetTime > _regenStartTime)
        {
            _regenStartTime = targetTime;
        }
    }

    private void UpdateUI()
    {
        if (healthSlider != null)
        {
            healthSlider.value = (float)currentHealth / maxHealth;
        }
        if (healthText != null)
        {
            healthText.text = $"{currentHealth} / {maxHealth}";
        }

        if (staminaSlider != null)
        {
            staminaSlider.value = currentStamina / maxStamina;
        }
    }
}