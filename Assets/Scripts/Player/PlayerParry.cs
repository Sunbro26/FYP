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

    public bool IsParryWindowActive { get; private set; }
    public bool IsParryAnimationRunning { get; private set; }

    private Animator _animator;
    private CharacterStats _stats;
    private PlayerBlock _blockScript;
    private PlayerAttack _attackScript;
    private Walk _walkScript;

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
        if (context.started && CanAttemptParry())
        {
            _stats.UseStamina(staminaCost);
            OnParryAttempt?.Invoke();
            StartCoroutine(ParrySequence());
        }
    }

    public float StaminaCost => staminaCost;

    public bool CanAttemptParry()
    {
        return !IsParryAnimationRunning
            && (_blockScript == null || !_blockScript.IsBlocking)
            && (_attackScript == null || !_attackScript.IsAttacking())
            && _stats != null
            && _stats.currentStamina >= staminaCost;
    }

    public void AttemptParry()
    {
        if (CanAttemptParry())
        {
            _stats.UseStamina(staminaCost);
            OnParryAttempt?.Invoke();
            StartCoroutine(ParrySequence());
        }
    }

    private IEnumerator ParrySequence()
    {
        IsParryAnimationRunning = true;

        if (_walkScript != null) _walkScript.IsMovementLocked = true;

        _animator.SetTrigger(ParryTrigger);

        IsParryWindowActive = false;
        yield return new WaitForSeconds(parryWindowStart);

        IsParryWindowActive = true;
        yield return new WaitForSeconds(parryWindowDuration);

        IsParryWindowActive = false;

        float remainingTime = animationDuration - parryWindowStart - parryWindowDuration;
        if (remainingTime > 0) yield return new WaitForSeconds(remainingTime);

        if (_walkScript != null) _walkScript.IsMovementLocked = false;
        IsParryAnimationRunning = false;
    }
}