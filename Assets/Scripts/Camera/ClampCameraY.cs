using UnityEngine;
using Unity.Cinemachine;

[ExecuteAlways]
public class ClampCameraY : CinemachineExtension
{
    [SerializeField] private float minY = 0f;

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        // Only modify the final output position of the camera
        if (stage == CinemachineCore.Stage.Finalize)
        {
            var pos = state.RawPosition;

            if (pos.y < minY)
            {
                pos.y = minY;
                state.RawPosition = pos;
            }
        }
    }
}
