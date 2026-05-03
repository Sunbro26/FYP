using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveCombatFramework {
    [AddComponentMenu("Framework/Player/Player Block")]
    public class PlayerBlock : MonoBehaviour
    {
        [Header("State Parameters")]
        [Tooltip("The boolean parameter name in the Animator to hold the block pose.")]
        [SerializeField] private string blockingParamName = "IsBlocking";
        
        public bool IsBlocking { get; private set; }

        private Animator _animator;
        private PlayerAttack _attackScript;
        private PlayerDodge _dodgeScript;
        private int _blockingHash;

        void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            _attackScript = GetComponent<PlayerAttack>();
            _dodgeScript = GetComponent<PlayerDodge>();
            _blockingHash = Animator.StringToHash(blockingParamName);
        }

        public void OnBlock(InputAction.CallbackContext context)
        {
            if (context.performed) SetBlocking(true);
            if (context.canceled) SetBlocking(false);
        }

        void Update()
        {
            // Auto-drop shield if we start rolling
            if (IsBlocking && _dodgeScript != null && _dodgeScript.IsDodging())
            {
                ForceDropShield();
            }
        }

        public void ForceDropShield()
        {
            IsBlocking = false;
            if (_animator != null) _animator.SetBool(_blockingHash, false);
        }

        /// <summary>
        /// Logic for starting or stopping a block. Restores agency to the ML Proxy.
        /// </summary>
        public void SetBlocking(bool blocking)
        {
            if (blocking)
            {
                if (_dodgeScript != null && _dodgeScript.IsDodging()) return;
                if (_attackScript != null && _attackScript.IsAttacking()) return;
                
                IsBlocking = true;
                if (_animator != null) _animator.SetBool(_blockingHash, true);
            }
            else
            {
                IsBlocking = false;
                if (_animator != null) _animator.SetBool(_blockingHash, false);
            }
        }
    }
}