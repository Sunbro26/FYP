using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerDodge : MonoBehaviour
{
    [Header("Dodge Settings")]
    [Tooltip("How far the character will dodge.")]
    public float dodgeDistance = 5f;
    [Tooltip("The duration of the dodge. This should be slightly less than the animation's length.")]
    public float dodgeDuration = 0.6f;

    // References to other components on the player
    private CharacterController _characterController;
    private Animator _animator;
    private Walk _walkScript; // Reference to YOUR Walk.cs script
    private PlayerAttack _attackScript; // We assume you have a PlayerAttack script

    // A flag to prevent starting a new dodge while already dodging
    private bool _isDodging = false;
    
    // Animator trigger hash
    private static readonly int DodgeTrigger = Animator.StringToHash("Dodge");

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponentInChildren<Animator>();
        _walkScript = GetComponent<Walk>();
        _attackScript = GetComponent<PlayerAttack>();
    }

    // This method is called by the Player Input component
    public void OnDodge(InputAction.CallbackContext context)
    {
        // We can only dodge if the button is pressed AND we are not already dodging or attacking.
        if (context.started && !_isDodging && (_attackScript == null || !_attackScript.IsAttacking()))
        {
            StartCoroutine(DodgeSequence());
        }
    }

    private IEnumerator DodgeSequence()
    {
        _isDodging = true;
        
        // Disable player control scripts
        if (_walkScript != null) _walkScript.enabled = false;
        if (_attackScript != null) _attackScript.enabled = false;

        // Trigger the animation in the Animator
        _animator.SetTrigger(DodgeTrigger);

        // --- Calculate Dodge Direction based on player input ---
        Vector2 moveInput = _walkScript.GetMoveInput(); // We will add this helper function to Walk.cs
        Vector3 dodgeDirection;

        // If the player is holding a movement key, dodge in that direction
        if (moveInput.magnitude > 0.1f)
        {
            // We use the player's local forward and right vectors to get the correct direction
            dodgeDirection = (transform.forward * moveInput.y + transform.right * moveInput.x).normalized;
        }
        else // If the player is standing still, dodge backward
        {
            dodgeDirection = -transform.forward;
        }

        // --- Perform the actual dodge movement ---
        float timer = 0f;
        while (timer < dodgeDuration)
        {
            float speed = dodgeDistance / dodgeDuration;
            _characterController.Move(dodgeDirection * speed * Time.deltaTime);
            timer += Time.deltaTime;
            yield return null; // Wait for the next frame before continuing the loop
        }

        // Re-enable player control scripts
        if (_walkScript != null) _walkScript.enabled = true;
        if (_attackScript != null) _attackScript.enabled = true;

        _isDodging = false;
    }
}