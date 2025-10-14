using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerAttack : MonoBehaviour
{
    private Animator _animator;
    private bool _isAttacking = false;
    
    // A unique ID for our "Attack" trigger for better performance.
    private static readonly int AttackTrigger = Animator.StringToHash("Attack");

    void Start()
    {
        // Find the Animator component on our character.
        _animator = GetComponentInChildren<Animator>();
    }

    // This method is called by the Player Input component when the "Attack" action is triggered.
    public void OnAttack(InputAction.CallbackContext context)
    {
        // We only want to trigger the attack when the button is first pressed down.
        if (context.started)
        {
            // Tell the Animator to fire the "Attack" trigger.
            _animator.SetTrigger(AttackTrigger);
        }
    }

    public bool IsAttacking()
    {
        return _isAttacking;
    }
}