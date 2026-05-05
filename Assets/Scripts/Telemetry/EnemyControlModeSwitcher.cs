using UnityEngine;
using Unity.MLAgents.Policies;

public class EnemyControlModeSwitcher : MonoBehaviour
{
    [Header("Enemy References")]
    [SerializeField] private SkeletonAI skeletonAI;
    [SerializeField] private BehaviorParameters behaviorParameters;

    private void Awake()
    {
        if (skeletonAI == null)
            skeletonAI = GetComponent<SkeletonAI>();

        if (behaviorParameters == null)
            behaviorParameters = GetComponent<BehaviorParameters>();

        Debug.Log(
            $"[EnemyControlModeSwitcher] Awake completed. " +
            $"SkeletonAI found: {skeletonAI != null}, " +
            $"BehaviorParameters found: {behaviorParameters != null}"
        );
    }

    public void SwitchToHeuristicOnly()
    {
        Debug.Log("[EnemyControlModeSwitcher] Switching enemy to Heuristic Only...");

        if (behaviorParameters != null)
        {
            Debug.Log(
                $"[EnemyControlModeSwitcher] Previous BehaviorType: {behaviorParameters.BehaviorType}"
            );

            behaviorParameters.BehaviorType = BehaviorType.HeuristicOnly;

            Debug.Log(
                $"[EnemyControlModeSwitcher] New BehaviorType: {behaviorParameters.BehaviorType}"
            );
        }
        else
        {
            Debug.LogWarning("[EnemyControlModeSwitcher] BehaviorParameters reference is missing.");
        }

        if (skeletonAI != null)
        {
            Debug.Log(
                $"[EnemyControlModeSwitcher] Previous SkeletonAI.useExternalAI: {skeletonAI.useExternalAI}"
            );

            skeletonAI.useExternalAI = false;
            skeletonAI.ResetAI();

            Debug.Log(
                $"[EnemyControlModeSwitcher] New SkeletonAI.useExternalAI: {skeletonAI.useExternalAI}"
            );
        }
        else
        {
            Debug.LogWarning("[EnemyControlModeSwitcher] SkeletonAI reference is missing.");
        }

        Debug.Log("[EnemyControlModeSwitcher] Enemy successfully switched to Heuristic Only.");
    }

    public void SwitchToInferenceOnly()
    {
        Debug.Log("[EnemyControlModeSwitcher] Switching enemy to Inference Only...");

        if (behaviorParameters != null)
        {
            Debug.Log(
                $"[EnemyControlModeSwitcher] Previous BehaviorType: {behaviorParameters.BehaviorType}"
            );

            behaviorParameters.BehaviorType = BehaviorType.InferenceOnly;

            Debug.Log(
                $"[EnemyControlModeSwitcher] New BehaviorType: {behaviorParameters.BehaviorType}"
            );
        }
        else
        {
            Debug.LogWarning("[EnemyControlModeSwitcher] BehaviorParameters reference is missing.");
        }

        if (skeletonAI != null)
        {
            Debug.Log(
                $"[EnemyControlModeSwitcher] Previous SkeletonAI.useExternalAI: {skeletonAI.useExternalAI}"
            );

            skeletonAI.useExternalAI = true;
            skeletonAI.ResetAI();

            Debug.Log(
                $"[EnemyControlModeSwitcher] New SkeletonAI.useExternalAI: {skeletonAI.useExternalAI}"
            );
        }
        else
        {
            Debug.LogWarning("[EnemyControlModeSwitcher] SkeletonAI reference is missing.");
        }

        Debug.Log("[EnemyControlModeSwitcher] Enemy successfully switched to Inference Only.");
    }
}