using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class LockOn : MonoBehaviour
{
    [Header("Cinemachine (set in Inspector)")]
    [SerializeField] private CinemachineCamera vcamFree;     // your existing free cam (unchanged)
    [SerializeField] private CinemachineCamera vcamLock;     // lock cam (Aim=Composer, no input component)
    [SerializeField] private Transform cameraPivot;          // Follow target used by BOTH VCams (e.g., Player/CameraRoot)

    [SerializeField] private Vector3 cameraOffset;           // Camera Offset behind player

    [Header("Targeting")]
    [SerializeField] private LayerMask enemyLayers;
    [SerializeField] private float maxLockDistance = 25f;
    [SerializeField, Range(0.05f, 0.5f)]
    private float screenRadius = 0.25f;                      // selection circle around screen center


    [Header("Pivot steering while locked")]
    [SerializeField] private float pivotYawLerp   = 10f;     // how fast pivot turns to enemy (yaw)
    private Transform currentEnemy;

    void Awake()
    {
        if (vcamLock == null || vcamFree == null || cameraPivot == null)
            Debug.LogWarning("LockOn: Assign vcamFree, vcamLock, and cameraPivot in the Inspector.");

        // Ensure the lock VCam follows the same pivot (do NOT change free VCam here)
        if (vcamLock != null && vcamLock.Follow == null)
            vcamLock.Follow = cameraPivot;

    }

    void Update()
    {
        // Only steer when locked; the free camera behavior remains untouched.
        if (currentEnemy == null || cameraPivot == null) return;

        // --- YAW: turn the pivot so the camera stays behind the player but faces the enemy ---
        Vector3 playerPos = transform.position; // this script sits on the player
        Vector3 toEnemy   = currentEnemy.position - playerPos;
        Vector3 flat      = new Vector3(toEnemy.x, 0f, toEnemy.z);

    if (flat.sqrMagnitude > 0.0001f)
    {
        Quaternion desiredYaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
        cameraPivot.rotation  = Quaternion.Slerp(cameraPivot.rotation, desiredYaw, pivotYawLerp * Time.deltaTime);

        // move the LOCK camera behind the player, opposite to the enemy direction
        Vector3 oppositeDir = -flat.normalized;                     // opposite of enemy direction
            Vector3 offset = cameraOffset;               // your desired offset
        Vector3 localOffset = Quaternion.LookRotation(oppositeDir) * offset;

        vcamLock.transform.position = playerPos + localOffset;
    }
    }

    // Input System callback (bind your Lock action to call this method)
    public void OnLock(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;

        if (currentEnemy == null)
        {
            var t = AcquireTargetOnScreen();
            if (t == null)
            {
                Debug.Log("⚠️ No enemy on screen — lock-on skipped.");
                return;
            }
            LockOnTarget(t);
        }
        else
        {
            ClearLock();
        }
    }

    private void LockOnTarget(Transform enemy)
    {
        currentEnemy = enemy;
        
        SwapCameraPriorities();
        vcamLock.Target.TrackingTarget = enemy.transform;

        // Blend to lock VCam (free VCam remains otherwise untouched)

        Debug.Log($"🎯 Locked on: {currentEnemy.name}");
    }

    void SwapCameraPriorities()
    {
        int temp = vcamFree.Priority;
        vcamFree.Priority = vcamLock.Priority;
        vcamLock.Priority = temp;
    }
    
    private void ClearLock()
    {
        currentEnemy = null;

        vcamLock.Target.TrackingTarget = null;
        SwapCameraPriorities();
        Debug.Log("🔓 Lock released — free camera active.");
    }

    private Transform AcquireTargetOnScreen()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        var cols = Physics.OverlapSphere(transform.position, maxLockDistance, enemyLayers);
        Transform best = null;
        float bestScore = float.MaxValue;

        foreach (var col in cols)
        {
            Transform root = col.transform.root;

            // Must be visible on screen
            Vector3 vp = cam.WorldToViewportPoint(root.position);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;

            // Prefer near screen center
            Vector2 dc = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            float r = dc.magnitude;
            if (r > screenRadius) continue;

            float dist  = Vector3.Distance(transform.position, root.position);
            float score = r * 100f + dist * 0.2f;

            if (score < bestScore) { bestScore = score; best = root; }
        }
        return best;
    }

    private static float NormalizePitch(float xDeg)
    {
        return (xDeg > 180f) ? xDeg - 360f : xDeg;
    }
}
