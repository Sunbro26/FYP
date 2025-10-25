using UnityEngine;

public class MoveCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The player object for the camera to follow.")]
    public Transform target;

    [Header("Positioning")]
    [Tooltip("The distance the camera should keep from the player.")]
    [SerializeField] private float distance = 6.0f;
    [Tooltip("The height of the camera above the player.")]
    [SerializeField] private float height = 3.0f;

    [Header("Look At Point")]
    [Tooltip("An offset from the player's pivot point for the camera to look at. (e.g., set Y to 1.5 to look at the player's chest instead of their feet).")]
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0, 1.5f, 0);

    [Header("Damping (Smoothing)")]
    [Tooltip("How quickly the camera's position follows the player. Lower values are slower and more 'floaty'.")]
    [SerializeField] private float positionDamping = 0.2f;
    [Tooltip("How quickly the camera rotates to face the player's back. Lower values are slower.")]
    [SerializeField] private float rotationDamping = 0.5f;

    // This is used by SmoothDamp for position
    private Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        // Exit if we don't have a target
        if (target == null)
        {
            Debug.LogWarning("Camera does not have a target to follow.", this);
            return;
        }

        // --- 1. CALCULATE WANTED POSITION ---
        // Determine the desired position behind the player
        Vector3 wantedPosition = target.position - (target.forward * distance) + (Vector3.up * height);

        // --- 2. SMOOTH THE POSITION ---
        // Smoothly move from the current camera position to the wanted position
        transform.position = Vector3.SmoothDamp(transform.position, wantedPosition, ref velocity, positionDamping);

        // --- 3. CALCULATE WANTED ROTATION ---
        // The point we want the camera to look at (player's position + offset)
        Vector3 targetLookAtPoint = target.position + lookAtOffset;
        // The desired rotation to look at that point
        Quaternion wantedRotation = Quaternion.LookRotation(targetLookAtPoint - transform.position, target.up);

        // --- 4. SMOOTH THE ROTATION ---
        // Smoothly rotate from the current rotation to the wanted rotation
        transform.rotation = Quaternion.Slerp(transform.rotation, wantedRotation, Time.deltaTime / rotationDamping);
    }
}