using UnityEngine;

namespace AdaptiveCombatFramework {
    public class RotateTowardsCamera : MonoBehaviour
    {
        private Camera mainCamera;
        void Start()
        {
            mainCamera = Camera.main;
        }

        void Update()
        {
            if (mainCamera != null) {
                transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward, mainCamera.transform.rotation * Vector3.up);
            }
        }
    }
}