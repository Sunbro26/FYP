using UnityEngine;
using UnityEngine.InputSystem;
using System;
using Unity.Cinemachine;

public class LockOn : MonoBehaviour
{
    [SerializeField] CinemachineCamera vcamLock;
    [SerializeField] CinemachineCamera vcamFree;
    void Start()
    {

    }

    public void OnLock(InputAction.CallbackContext context)
    {
        Debug.Log("Camera priority changed");
        SwapCameraPriorities();
    }

    void SwapCameraPriorities()
    {
        int temp = vcamFree.Priority;
        vcamFree.Priority = vcamLock.Priority;
        vcamLock.Priority = temp;
    }
}

