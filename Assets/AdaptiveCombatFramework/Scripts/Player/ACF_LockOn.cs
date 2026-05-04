using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

namespace AdaptiveCombatFramework {
    [AddComponentMenu("Framework/Player/Lock-On System")]
    public class LockOn : MonoBehaviour
    {
        [Header("Cinemachine Setup")]
        [Tooltip("The standard third-person free-look camera.")]
        [SerializeField] private CinemachineCamera vcamFree;
        [Tooltip("The camera used when a target is locked.")]
        [SerializeField] private CinemachineCamera vcamLock;
        [Tooltip("The follow target used by both cameras (usually a point above the player's head).")]
        [SerializeField] private Transform cameraPivot;
        [Tooltip("Fixed offset for the lock-on camera relative to the player.")]
        [SerializeField] private Vector3 cameraOffset;

        [Header("Targeting Settings")]
        [Tooltip("Which layers count as valid lock-on targets.")]
        [SerializeField] private LayerMask enemyLayers;
        [Tooltip("Maximum range to acquire a target.")]
        [SerializeField] private float maxLockDistance = 25f;
        [Tooltip("The radius around the center of the screen used to prioritize targets.")]
        [SerializeField, Range(0.05f, 0.5f)] private float screenRadius = 0.25f;

        [Header("Pivot Smoothing")]
        [Tooltip("How smoothly the camera pivot rotates toward the enemy.")]
        [SerializeField] private float pivotYawLerp = 10f;

        [Header("State")]
        [Tooltip("Read-only view of current lock state.")]
        public bool isLockedOn = false;

        private Transform currentEnemy;

        void Awake()
        {
            if (vcamLock == null || vcamFree == null || cameraPivot == null)
                Debug.LogWarning("LockOn: Mandatory references missing in Inspector.");

            if (vcamLock != null && vcamLock.Follow == null)
                vcamLock.Follow = cameraPivot;
        }

        void Start()
        {
            var t = AcquireTargetOnScreen();
            if (t != null) LockOnTarget(t);
        }

        void Update()
        {
            if (currentEnemy == null || cameraPivot == null) return;

            Vector3 playerPos = transform.position;
            Vector3 toEnemy = currentEnemy.position - playerPos;
            Vector3 flat = new Vector3(toEnemy.x, 0f, toEnemy.z);

            if (flat.sqrMagnitude > 0.0001f)
            {
                Quaternion desiredYaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
                cameraPivot.rotation = Quaternion.Slerp(cameraPivot.rotation, desiredYaw, pivotYawLerp * Time.deltaTime);

                Vector3 oppositeDir = -flat.normalized;
                Vector3 localOffset = Quaternion.LookRotation(oppositeDir) * cameraOffset;
                vcamLock.transform.position = playerPos + localOffset;
            }
        }

        public void OnLock(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed) return;

            if (currentEnemy == null)
            {
                var t = AcquireTargetOnScreen();
                if (t != null) LockOnTarget(t);
            }
            else ClearLock();
        }

        private void LockOnTarget(Transform enemy)
        {
            isLockedOn = true;
            currentEnemy = enemy;
            SwapCameraPriorities();
            vcamLock.Target.TrackingTarget = enemy.transform;
            Debug.Log($"🎯 Locked on: {currentEnemy.name}");
        }

        private void ClearLock()
        {
            isLockedOn = false;
            currentEnemy = null;
            vcamLock.Target.TrackingTarget = null;
            SwapCameraPriorities();
            Debug.Log("🔓 Lock released.");
        }

        void SwapCameraPriorities()
        {
            int temp = vcamFree.Priority;
            vcamFree.Priority = vcamLock.Priority;
            vcamLock.Priority = temp;
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
                Vector3 vp = cam.WorldToViewportPoint(root.position);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;

                Vector2 dc = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                float r = dc.magnitude;
                if (r > screenRadius) continue;

                float dist = Vector3.Distance(transform.position, root.position);
                float score = r * 100f + dist * 0.2f;

                if (score < bestScore) { bestScore = score; best = root; }
            }
            return best;
        }
    }
}