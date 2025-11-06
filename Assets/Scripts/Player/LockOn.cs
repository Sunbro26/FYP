using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class LockOn : MonoBehaviour
{
    [Header("Cinemachine (set in Inspector)")]
    [SerializeField] private CinemachineCamera vcamFree;   // your free camera (has mouse input)
    [SerializeField] private CinemachineCamera vcamLock;   // lock camera (no mouse input)

    [Header("Lock Targeting")]
    [SerializeField] private LayerMask enemyLayers;
    [SerializeField] private float maxLockDistance = 25f;
    [SerializeField, Range(0.05f, 0.5f)]
    private float screenRadius = 0.25f;

    [Header("Player")]

    private CinemachineTargetGroup targetGroup; // created at runtime
    private Transform currentEnemy;

    [Header("Lock on Transforms")]
    [SerializeField] private float playerWeight;
    [SerializeField] private float enemyWeight;

    [SerializeField] private float playerRadius;
    [SerializeField] private float enemyRadius;
    void Awake()
    {
        // Runtime TargetGroup to aim the lock camera at [player + enemy]
        var go = new GameObject("LockOnTargetGroup");
        targetGroup = go.AddComponent<CinemachineTargetGroup>();

        // IMPORTANT: Your VCams should already have Follow set to the player's camera pivot in the Inspector.
        // We do NOT change Follow here; we only set LookAt on the lock camera.
    }

    public void OnLock(InputAction.CallbackContext ctx)
    {
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

    void LockOnTarget(Transform enemy)
    {
        currentEnemy = enemy;

        // Camera position/orbit stays attached to the player (Follow already set on the VCam).
        // We only change AIM: look at a group containing player + enemy.
        targetGroup.AddMember(transform, playerWeight, playerRadius);       // weight, radius
        targetGroup.AddMember(currentEnemy, enemyWeight, enemyRadius); // slight enemy bias

        vcamLock.Target.TrackingTarget = targetGroup.transform; // Aim between player & enemy
        vcamLock.Priority = 20;
        vcamFree.Priority = 10;

        Debug.Log($"🎯 Locked on: {currentEnemy.name}");
    }

    void ClearLock()
    {
        currentEnemy = null;

        vcamLock.Target.TrackingTarget = null;
        vcamLock.Priority = 10;
        vcamFree.Priority = 20;



        Debug.Log("🔓 Lock released — free camera active.");
    }

    Transform AcquireTargetOnScreen()
    {
        var cam = Camera.main;
        if (cam == null) return null;

        var cols = Physics.OverlapSphere(transform.position, maxLockDistance, enemyLayers);
        Transform best = null;
        float bestScore = float.MaxValue;

        foreach (var col in cols)
        {
            var root = col.transform.root;

            // Must be visible on screen
            Vector3 vp = cam.WorldToViewportPoint(root.position);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f)
                continue;

            // Prefer near screen center
            Vector2 dc = new(vp.x - 0.5f, vp.y - 0.5f);
            float r = dc.magnitude;
            if (r > screenRadius) continue;

            // Score: center first, then player distance
            float dist = Vector3.Distance(transform.position, root.position);
            float score = r * 100f + dist * 0.2f;

            if (score < bestScore)
            {
                bestScore = score;
                best = root;
            }
        }
        return best;
    }
}
