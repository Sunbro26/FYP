using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System;

public class PlayerParry : MonoBehaviour
{
    [Header("Parry Stats")]
    [Tooltip("Total stamina cost to attempt a parry.")]
    public float staminaCost = 25f;
    [Tooltip("Total length of the parry animation.")]
    public float animationDuration = 1.0f;

    [Header("The Sweet Spot")]
    [Tooltip("Time delay after pressing button before Parry is active (Hand moving up).")]
    public float parryWindowStart = 0.1f;
    [Tooltip("How long the Parry remains active (Hand waving away).")]
    public float parryWindowDuration = 0.3f;

    // Public state for other scripts
    public bool IsParryWindowActive { get; private set; }
    public bool IsParryAnimationRunning { get; private set; }

    // References
    private Animator _animator;
    private CharacterStats _stats;
    private PlayerBlock _blockScript;
    private PlayerAttack _attackScript;
    private Walk _walkScript; // Or SimpleFreeLookMovement

    public static event Action OnParryAttempt;

    private static readonly int ParryTrigger = Animator.StringToHash("Parry");

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        _stats = GetComponent<CharacterStats>();
        _blockScript = GetComponent<PlayerBlock>();
        _attackScript = GetComponent<PlayerAttack>();
        _walkScript = GetComponent<Walk>();
    }

    public void OnParry(InputAction.CallbackContext context)
    {
        // Can only parry if input started, not already parrying/attacking, and not holding block
        if (context.started && !IsParryAnimationRunning && !_blockScript.IsBlocking && !_attackScript.IsAttacking())
            if (_stats.UseStamina(staminaCost))
            {
                OnParryAttempt?.Invoke();
                StartCoroutine(ParrySequence());
            }
    }

        // Public method for ML-Agents to trigger a parry
    public void AttemptParry()
    {
        // Check conditions: Not already parrying, not blocking, not attacking
        if (!IsParryAnimationRunning && !_blockScript.IsBlocking && !_attackScript.IsAttacking())
        {
            // Check Stamina
            if (_stats.UseStamina(staminaCost))
            {
                // Fire Event & Start Sequence
                OnParryAttempt?.Invoke();
                StartCoroutine(ParrySequence());
            }
        }
    }
    
    private IEnumerator ParrySequence()
    {
        IsParryAnimationRunning = true;
        
        // Lock movement/rotation
        if (_walkScript != null) _walkScript.IsMovementLocked = true;

        // Trigger Animation
        _animator.SetTrigger(ParryTrigger);

        // 1. Wind Up (Vulnerable)
        IsParryWindowActive = false;
        yield return new WaitForSeconds(parryWindowStart);

        // 2. Active Window (The "Parry")
        IsParryWindowActive = true;
        yield return new WaitForSeconds(parryWindowDuration);

        // 3. Recovery (Vulnerable)
        IsParryWindowActive = false;
        
        // Wait for rest of animation
        float remainingTime = animationDuration - parryWindowStart - parryWindowDuration;
        if (remainingTime > 0) yield return new WaitForSeconds(remainingTime);

        // Unlock
        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        IsParryAnimationRunning = false;
    }
}