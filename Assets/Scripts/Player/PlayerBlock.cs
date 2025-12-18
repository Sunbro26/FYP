using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBlock : MonoBehaviour
{
    // Public property to check if we are blocking
    public bool IsBlocking { get; private set; }

    private Animator _animator;
    private PlayerAttack _attackScript;
    private PlayerDodge _dodgeScript;

    // We use a Boolean parameter because blocking is a state you hold, not a one-time trigger
    private static readonly int BlockingParam = Animator.StringToHash("IsBlocking");

    void Start()
    {
        _animator = GetComponentInChildren<Animator>();
        _attackScript = GetComponent<PlayerAttack>();
        _dodgeScript = GetComponent<PlayerDodge>();
    }

    public void OnBlock(InputAction.CallbackContext context)
    {
        // 1. Button Pressed (Start Blocking)
        if (context.performed)
        {
            // Check Dodging AND Attacking
            if (_dodgeScript.IsDodging() || _attackScript.IsAttacking()) return;

            IsBlocking = true;
            _animator.SetBool(BlockingParam, true);
        }

        // 2. Button Released (Stop Blocking)
        if (context.canceled)
        {
            IsBlocking = false;
            _animator.SetBool(BlockingParam, false);
        }
    }

    // Safety Update: If we are forced into a dodge or get hit while blocking, 
    // ensure we don't get stuck in the block state logically.
    void Update()
    {
        if (IsBlocking && _dodgeScript.IsDodging())
        {
            IsBlocking = false;
            _animator.SetBool(BlockingParam, false);
        }
    }
    // Inside PlayerBlock.cs
public void SetBlocking(bool blocking)
{
    // Logic from OnBlock performed/canceled
    if (blocking)
    {
        if (_dodgeScript.IsDodging() || _attackScript.IsAttacking()) return;
        IsBlocking = true;
        _animator.SetBool(BlockingParam, true);
    }
    else
    {
        IsBlocking = false;
        _animator.SetBool(BlockingParam, false);
    }
}

}