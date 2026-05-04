using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System;

namespace AdaptiveCombatFramework {
    [AddComponentMenu("Framework/Player/Player Parry")]
    public class PlayerParry : MonoBehaviour
    {
        [Header("Parry Statistics")]
        [Tooltip("Stamina consumed to attempt a parry.")]
        public float staminaCost = 25f;
        [Tooltip("The total length of the parry animation clip.")]
        public float animationDuration = 1.0f;

        [Header("The Active Window (Sweet Spot)")]
        [Tooltip("Delay after activation before the parry hitbox becomes active.")]
        public float parryWindowStart = 0.1f;
        [Tooltip("How long the parry remains active (in seconds).")]
        public float parryWindowDuration = 0.3f;

        // --- State ---
        public bool IsParryWindowActive { get; private set; }
        public bool IsParryAnimationRunning { get; private set; }

        // --- References ---
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

        public bool CanAttemptParry()
        {
            if (IsParryAnimationRunning) return false;
            if (_blockScript != null && _blockScript.IsBlocking) return false;
            if (_attackScript != null && _attackScript.IsAttacking()) return false;
            return _stats != null && _stats.CanSpendStamina(staminaCost);
        }

        public void OnParry(InputAction.CallbackContext context)
        {
            if (context.started && CanAttemptParry())
            {
                AttemptParry();
            }
        }

        /// <summary>
        /// External trigger for the parry sequence. Used by ML-Agents or AI Proxies.
        /// </summary>
        public void AttemptParry()
        {
            if (_stats != null && _stats.UseStamina(staminaCost))
            {
                OnParryAttempt?.Invoke();
                StartCoroutine(ParrySequence());
            }
        }

        private IEnumerator ParrySequence()
        {
            IsParryAnimationRunning = true;

            if (_walkScript != null) _walkScript.IsMovementLocked = true;
            _animator.SetTrigger(ParryTrigger);

            // Wind-up Phase
            IsParryWindowActive = false;
            yield return new WaitForSeconds(parryWindowStart);

            // Active Phase
            IsParryWindowActive = true;
            yield return new WaitForSeconds(parryWindowDuration);

            // Recovery Phase
            IsParryWindowActive = false;

            float remainingTime = animationDuration - parryWindowStart - parryWindowDuration;
            if (remainingTime > 0) yield return new WaitForSeconds(remainingTime);

            if (_walkScript != null) _walkScript.IsMovementLocked = false;
            IsParryAnimationRunning = false;
        }
    }
}