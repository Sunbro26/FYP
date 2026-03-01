using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using UnityEngine.AI; 

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
    public float staminaRegenDelay = 1.0f; 
    public float exhaustionDelay = 2.5f; 
    public Slider staminaSlider; 

    private float _regenStartTime;
    private bool _isDead = false;
    private Animator _animator;
    private NavMeshAgent _agent; 
    private Collider _collider;  
    
    private Vector3 _startPosition;
    private Quaternion _startRotation;

    public bool IsDead => _isDead;
    public event Action<int> OnTakeDamage;

    private static readonly int DeathTrigger = Animator.StringToHash("Death");
    private static readonly int ResetTrigger = Animator.StringToHash("Reset");

    void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
        _startPosition = transform.position;
        _startRotation = transform.rotation;
        
        _animator = GetComponentInChildren<Animator>();
        _agent = GetComponent<NavMeshAgent>();
        _collider = GetComponent<Collider>();
    }

    void Start() { UpdateUI(); }

    void Update()
    {
        if (_isDead) return; 

        if (currentStamina < maxStamina && Time.time > _regenStartTime)
        {
            currentStamina += staminaRegenRate * Time.deltaTime;
            if (currentStamina > maxStamina) currentStamina = maxStamina;
            UpdateUI();
        }
    }

    public void TakeDamage(int damage)
    {
        if (_isDead) return;

        OnTakeDamage?.Invoke(damage);
        currentHealth -= damage;
        
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Die();
        }

        UpdateUI();
    }

    private void Die()
    {
        if (_isDead) return; 
        _isDead = true;
        
        // 1. Play Animation
        if (_animator != null) 
        {
            // Clear any hit triggers so they don't override death
            _animator.ResetTrigger("Hit"); 
            _animator.SetTrigger(DeathTrigger);
        }

        // 2. Disable Physics/Movement
        if (_agent != null) 
        {
            _agent.isStopped = true;
            _agent.enabled = false; 
        }

        if (_collider != null) _collider.enabled = false;
        
        CharacterController cc = GetComponent<CharacterController>();
        if(cc != null) cc.enabled = false;

        // 3. Game Over Screen
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TriggerGameOver(!isPlayer); 
        }
    }

    public void ResetStats()
    {
        _isDead = false;
        currentHealth = maxHealth;
        currentStamina = maxStamina;
        
        // 1. Reset External Scripts (Boss AI / Player Movement)
        SkeletonAI bossAI = GetComponent<SkeletonAI>();
        if (bossAI != null) bossAI.ResetAI();

        Walk playerWalk = GetComponent<Walk>();
        if (playerWalk != null) playerWalk.IsMovementLocked = false;

        // 2. Enable Components & Teleport
        if (_collider != null) _collider.enabled = true;
        
        CharacterController cc = GetComponent<CharacterController>();
        if(cc != null) cc.enabled = true; // Must be enabled to move

        if (_agent != null) 
        {
            _agent.enabled = true;
            _agent.Warp(_startPosition); 
            _agent.isStopped = false;
        }
        else if (cc != null)
        {
            // CC override
            cc.enabled = false; 
            transform.position = _startPosition;
            transform.rotation = _startRotation;
            cc.enabled = true;
        }
        else 
        {
            transform.position = _startPosition;
            transform.rotation = _startRotation;
        }

        // 3. Nuclear Animator Reset
        if (_animator != null)
        {
            // Rebind resets the animator to its start state (Entry)
            _animator.Rebind(); 
            
            // Just in case Rebind didn't clear triggers (Unity version dependent)
            _animator.ResetTrigger(DeathTrigger);
            _animator.ResetTrigger("Hit");
            
            // Force state
            _animator.Play("Locomotion");
        }

        UpdateUI();
    }

    public bool UseStamina(float amount)
    {
        if (!isPlayer) return true; 

        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            if (currentStamina <= 0.1f) 
            {
                currentStamina = 0;
                _regenStartTime = Time.time + exhaustionDelay;
            }
            else
            {
                _regenStartTime = Time.time + staminaRegenDelay;
            }
            UpdateUI();
            return true; 
        }
        return false; 
    }

    public void PauseStaminaRegen(float duration)
    {
        float targetTime = Time.time + duration;
        if (targetTime > _regenStartTime) _regenStartTime = targetTime;
    }

    private void UpdateUI()
    {
        if (healthSlider != null) healthSlider.value = (float)currentHealth / maxHealth;
        if (healthText != null) healthText.text = $"{currentHealth} / {maxHealth}";
        if (staminaSlider != null) staminaSlider.value = currentStamina / maxStamina;
    }
}