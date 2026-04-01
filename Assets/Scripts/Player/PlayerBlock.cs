using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBlock : MonoBehaviour
{
    public bool IsBlocking { get; private set; }

    private Animator _animator;
    private PlayerAttack _attackScript;
    private PlayerDodge _dodgeScript;

    private static readonly int BlockingParam = Animator.StringToHash("IsBlocking");

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        _attackScript = GetComponent<PlayerAttack>();
        _dodgeScript = GetComponent<PlayerDodge>();
    }

    public void OnBlock(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            if (_dodgeScript != null && _dodgeScript.IsDodging()) return;
            if (_attackScript != null && _attackScript.IsAttacking()) return;

            IsBlocking = true;
            if (_animator != null) _animator.SetBool(BlockingParam, true);
        }

        if (context.canceled)
        {
            IsBlocking = false;
            if (_animator != null) _animator.SetBool(BlockingParam, false);
        }
    }

    void Update()
    {
        if (IsBlocking && _dodgeScript != null && _dodgeScript.IsDodging())
        {
            ForceDropShield();
        }
    }

    // --- OUR NEW LOGIC: Called by PlayerControl when Stamina is depleted ---
    public void ForceDropShield()
    {
        IsBlocking = false;
        if (_animator != null) _animator.SetBool(BlockingParam, false);
    }

    // --- RESTORED: Needed by PlayerProxyAgent.cs for ML-Agents ---
    public void SetBlocking(bool blocking)
    {
        if (blocking)
        {
            if (_dodgeScript != null && _dodgeScript.IsDodging()) return;
            if (_attackScript != null && _attackScript.IsAttacking()) return;
            
            IsBlocking = true;
            if (_animator != null) _animator.SetBool(BlockingParam, true);
        }
        else
        {
            IsBlocking = false;
            if (_animator != null) _animator.SetBool(BlockingParam, false);
        }
    }
}