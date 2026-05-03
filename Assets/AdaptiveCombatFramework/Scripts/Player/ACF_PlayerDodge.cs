using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using System;

namespace AdaptiveCombatFramework {
    [AddComponentMenu("Framework/Player/Player Dodge")]
    public class PlayerDodge : MonoBehaviour
    {
        [Header("Dodge Physics")]
        [Tooltip("The world distance the dodge covers.")]
        public float dodgeDistance = 5f;
        [Tooltip("The duration of the physical movement in seconds.")]
        public float dodgeDuration = 0.6f;
        [Tooltip("How fast the character rotates to match the dodge direction.")]
        public float dodgeRotationSpeed = 15f;

        [Header("Invincibility (I-Frames)")]
        [Tooltip("Delay (seconds) after starting dodge before invincibility begins.")]
        public float iFrameStartDelay = 0.2f;
        [Tooltip("Duration of the invincibility window.")]
        public float iFrameDuration = 0.3f;

        [Header("Stamina Configuration")]
        [Tooltip("Amount of stamina required to roll.")]
        public float staminaCost = 15f;

        // --- Framework Events (Not visible in Inspector) ---
        public static event Action OnDodgeAttempt;
        public static event Action OnDodgeSuccess;

        // --- Internals ---
        private CharacterController _characterController;
        private Animator _animator;
        private Walk _walkScript;
        private PlayerAttack _attackScript;
        private Transform _cameraTransform;
        private CharacterStats _stats;
        private bool _isDodging = false;
        public bool IsInvincible { get; private set; }

        private static readonly int DodgeTrigger = Animator.StringToHash("Dodge");

        void Start()
        {
            _characterController = GetComponent<CharacterController>();
            _animator = GetComponentInChildren<Animator>();
            _walkScript = GetComponent<Walk>();
            _attackScript = GetComponent<PlayerAttack>();
            _cameraTransform = Camera.main.transform;
            _stats = GetComponent<CharacterStats>();
        }

        public bool IsDodging() => _isDodging;

        public bool CanAttemptDodge()
        {
            if (_isDodging) return false;
            // Prevent dodging during active attack animations
            if (_attackScript != null && _attackScript.IsAttacking()) return false;
            return _stats != null && _stats.CanSpendStamina(staminaCost);
        }

        public void OnDodge(InputAction.CallbackContext context)
        {
            if (context.started && CanAttemptDodge())
            {
                AttemptDodge();
            }
        }

        /// <summary>
        /// External trigger for the dodge sequence. Used by ML-Agents or AI Proxies.
        /// </summary>
        public void AttemptDodge()
        {
            if (_stats != null && !_stats.UseStamina(staminaCost))
            {
                Debug.Log("Not enough stamina to dodge!");
                return;
            }

            if (_walkScript != null) _walkScript.IsMovementLocked = true;
            OnDodgeAttempt?.Invoke();
            StartCoroutine(DodgeSequence());
        }

        public void RegisterPerfectDodge()
        {
            if (IsInvincible)
            {
                Debug.Log("PERFECT DODGE! Framework Event Fired.");
                OnDodgeSuccess?.Invoke();
            }
        }

        private IEnumerator DodgeSequence()
        {
            _isDodging = true;
            if (_attackScript != null) _attackScript.enabled = false;

            _animator.SetTrigger(DodgeTrigger);
            StartCoroutine(HandleIFrames());

            // --- Calculate Movement Vector ---
            Vector2 moveInput = _walkScript.GetMoveInput();
            Vector3 cameraForward = new Vector3(_cameraTransform.forward.x, 0, _cameraTransform.forward.z).normalized;
            Vector3 cameraRight = new Vector3(_cameraTransform.right.x, 0, _cameraTransform.right.z).normalized;
            Vector3 dodgeDirection = (moveInput.magnitude > 0.1f) 
                ? (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized 
                : cameraForward;

            Quaternion targetRotation = Quaternion.LookRotation(dodgeDirection);

            // --- Movement Loop ---
            float timer = 0f;
            while (timer < dodgeDuration)
            {
                float speed = dodgeDistance / dodgeDuration;
                _characterController.Move(dodgeDirection * speed * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, dodgeRotationSpeed * Time.deltaTime);
                timer += Time.deltaTime;
                yield return null;
            }

            transform.rotation = targetRotation;
            IsInvincible = false;

            if (_walkScript != null) _walkScript.IsMovementLocked = false;
            if (_attackScript != null) _attackScript.enabled = true;

            _isDodging = false;
        }

        private IEnumerator HandleIFrames()
        {
            yield return new WaitForSeconds(iFrameStartDelay);
            IsInvincible = true;
            yield return new WaitForSeconds(iFrameDuration);
            IsInvincible = false;
        }
    }
}