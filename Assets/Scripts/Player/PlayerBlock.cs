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
            if (!CanRaiseBlock()) return;

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

    public bool CanRaiseBlock()
    {
        return (_dodgeScript == null || !_dodgeScript.IsDodging())
            && (_attackScript == null || !_attackScript.IsAttacking());
    }

    public void ForceDropShield()
    {
        IsBlocking = false;
        if (_animator != null) _animator.SetBool(BlockingParam, false);
    }

    public void SetBlocking(bool blocking)
    {
        if (blocking)
        {
            if (!CanRaiseBlock()) return;

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
