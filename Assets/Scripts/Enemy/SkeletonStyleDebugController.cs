using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Receives PlayerInput debug actions and forwards them to the skeleton MultiGAIL agent.
/// Attach this to the same GameObject as the scene's PlayerInput component.
/// </summary>
public class SkeletonStyleDebugController : MonoBehaviour
{
    [Header("References")]
    public SkeletonAiProxyAgent skeletonAgent;

    [Header("Enable")]
    public bool enableDebugActions = true;

    [Header("Blend Presets")]
    [Range(0f, 1f)] public float balancedAggressiveWeight = 0.5f;
    [Range(0f, 1f)] public float sixtyFortyAggressiveWeight = 0.6f;

    [Header("Logging")]
    public bool logActions = true;

    void Awake()
    {
        if (skeletonAgent == null)
        {
            skeletonAgent = FindFirstObjectByType<SkeletonAiProxyAgent>();
        }
    }

    bool CanHandle(InputAction.CallbackContext context)
    {
        return enableDebugActions && skeletonAgent != null && context.performed;
    }

    public void OnToggleDebugOverlay(InputAction.CallbackContext context)
    {
        if (!CanHandle(context)) return;
        skeletonAgent.ToggleDebugOverlay();
        LogAction("Toggled skeleton debug overlay.");
    }

    public void OnDebugAggressive(InputAction.CallbackContext context)
    {
        if (!CanHandle(context)) return;
        skeletonAgent.SetDominantStyle(0);
        LogAction("Forced aggressive style.");
    }

    public void OnDebugDefensive(InputAction.CallbackContext context)
    {
        if (!CanHandle(context)) return;
        skeletonAgent.SetDominantStyle(1);
        LogAction("Forced defensive style.");
    }

    public void OnDebugBalanced(InputAction.CallbackContext context)
    {
        if (!CanHandle(context)) return;
        skeletonAgent.SetTwoStyleBlend(balancedAggressiveWeight);
        LogAction($"Forced balanced blend {balancedAggressiveWeight:F2}/{1f - balancedAggressiveWeight:F2}.");
    }

    public void OnDebugSixtyForty(InputAction.CallbackContext context)
    {
        if (!CanHandle(context)) return;
        skeletonAgent.SetTwoStyleBlend(sixtyFortyAggressiveWeight);
        LogAction($"Forced blend {sixtyFortyAggressiveWeight:F2}/{1f - sixtyFortyAggressiveWeight:F2}.");
    }

    public void OnToggleOpponentModelShift(InputAction.CallbackContext context)
    {
        if (!CanHandle(context)) return;
        skeletonAgent.useOpponentModelStyleShift = !skeletonAgent.useOpponentModelStyleShift;
        LogAction($"Opponent-model style shifting {(skeletonAgent.useOpponentModelStyleShift ? "enabled" : "disabled")}.");
    }

    public void OnToggleSnapStyleShift(InputAction.CallbackContext context)
    {
        if (!CanHandle(context)) return;
        skeletonAgent.snapRuntimeStyleShifts = !skeletonAgent.snapRuntimeStyleShifts;
        LogAction($"Snap runtime style shifts {(skeletonAgent.snapRuntimeStyleShifts ? "enabled" : "disabled")}.");
    }

    void LogAction(string message)
    {
        if (logActions)
        {
            Debug.Log($"[SkeletonStyleDebug] {message}", this);
        }
    }
}
