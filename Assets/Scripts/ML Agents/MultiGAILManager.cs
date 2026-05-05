using UnityEngine;
using System.Collections.Generic;
using Unity.InferenceEngine;

/// <summary>
/// Runs one style critic per persona and combines their rewards using the current style weights.
/// This follows the MultiGAIL pattern of multiple discriminators plus a style-conditioned policy.
/// </summary>
public class MultiGAILManager : MonoBehaviour
{
    [SerializeField] private bool disableInWebGL = true;
    
    [Header("MultiGAIL Critic Models")]
    [Tooltip("One ONNX critic per style/persona.")]
    public List<ModelAsset> criticModelAssets = new List<ModelAsset>();

    [Header("Default Style Weights")]
    [Tooltip("Fallback style weights used when the caller does not provide weights. For stability these are normalized to sum to 1.")]
    public List<float> alphaWeights = new List<float> { 1f, 0f };

    [Header("Critic Observation Contract")]
    [Tooltip("Number of raw skeleton observation floats expected by each critic. This should exclude style-conditioning values.")]
    public int expectedObservationCount = 28;

    [Header("Critic Action Encoding")]
    [Tooltip("Number of continuous action values appended to the critic input.")]
    public int continuousActionCount = 2;
    [Tooltip("Number of discrete action slots encoded as one-hot for the critic input.")]
    public int discreteActionCount = 9;

    private readonly List<Worker> _critics = new List<Worker>();
    private readonly List<float> _resolvedWeights = new List<float>();
    private Model[] _runtimeModels;
    private bool _hasLoggedObservationMismatch;

    public int CriticCount => criticModelAssets != null ? criticModelAssets.Count : 0;
    public int ExpectedCriticInputSize => GetExpectedCriticInputSize(expectedObservationCount);

    void Start()
    {
    #if UNITY_WEBGL && !UNITY_EDITOR
        if (disableInWebGL)
        {
            Debug.LogWarning("[MultiGAILManager] Disabled in WebGL to avoid unsupported inference kernels.");
            enabled = false;
            return;
        }
    #endif

    InitializeCritics();
}

    void InitializeCritics()
    {
        if (criticModelAssets == null || criticModelAssets.Count == 0)
        {
            Debug.LogWarning("MultiGAIL Manager has no critic models assigned.", this);
            return;
        }

        EnsureDefaultWeights();

        _runtimeModels = new Model[criticModelAssets.Count];
        for (int i = 0; i < criticModelAssets.Count; i++)
        {
            if (criticModelAssets[i] == null)
            {
                Debug.LogWarning($"MultiGAIL critic slot {i} is empty.", this);
                continue;
            }

            _runtimeModels[i] = ModelLoader.Load(criticModelAssets[i]);
            Worker worker = new Worker(_runtimeModels[i], BackendType.GPUCompute);
            _critics.Add(worker);
        }

        Debug.Log($"MultiGAIL Manager initialized with {_critics.Count} critics. Expected critic input size: {ExpectedCriticInputSize}.");
    }

    void EnsureDefaultWeights()
    {
        int criticCount = CriticCount;
        if (criticCount <= 0)
        {
            alphaWeights.Clear();
            return;
        }

        if (alphaWeights == null)
        {
            alphaWeights = new List<float>();
        }

        while (alphaWeights.Count < criticCount)
        {
            alphaWeights.Add(0f);
        }

        if (alphaWeights.Count > criticCount)
        {
            alphaWeights.RemoveRange(criticCount, alphaWeights.Count - criticCount);
        }

        NormalizeWeights(alphaWeights);
    }

    void NormalizeWeights(List<float> weights)
    {
        if (weights == null || weights.Count == 0)
        {
            return;
        }

        float sum = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            weights[i] = Mathf.Max(0f, weights[i]);
            sum += weights[i];
        }

        if (sum <= 0f)
        {
            weights[0] = 1f;
            for (int i = 1; i < weights.Count; i++)
            {
                weights[i] = 0f;
            }
            return;
        }

        for (int i = 0; i < weights.Count; i++)
        {
            weights[i] /= sum;
        }
    }

    List<float> ResolveWeights(IList<float> styleWeights)
    {
        _resolvedWeights.Clear();

        IList<float> source = styleWeights != null && styleWeights.Count > 0
            ? styleWeights
            : alphaWeights;

        if (source == null || source.Count == 0)
        {
            return _resolvedWeights;
        }

        int count = Mathf.Min(source.Count, CriticCount);
        for (int i = 0; i < count; i++)
        {
            _resolvedWeights.Add(Mathf.Max(0f, source[i]));
        }

        NormalizeWeights(_resolvedWeights);
        return _resolvedWeights;
    }

    float EvaluateStyleMatch(float discriminatorOutput)
    {
        // MultiGAIL paper Equation (3), using the LSGAN-style discriminator output.
        return Mathf.Max(0f, 1f - 0.25f * Mathf.Pow(discriminatorOutput - 1f, 2f));
    }

    public int GetExpectedCriticInputSize(int observationCount)
    {
        return observationCount + continuousActionCount + discreteActionCount;
    }

    bool ValidateObservationCount(int observationCount)
    {
        if (expectedObservationCount <= 0 || observationCount == expectedObservationCount)
        {
            return true;
        }

        if (!_hasLoggedObservationMismatch)
        {
            Debug.LogWarning(
                $"MultiGAIL critic observation mismatch. Expected {expectedObservationCount} raw observations, " +
                $"but received {observationCount}. Style reward will be suppressed until the contract matches.",
                this);
            _hasLoggedObservationMismatch = true;
        }

        return false;
    }

    public float CalculateStyleReward(List<float> observations, float strafeAction, float forwardAction, int discreteAction, IList<float> styleWeights = null)
    {
        if (_critics.Count == 0 || observations == null)
        {
            return 0f;
        }

        int obsCount = observations.Count;
        if (!ValidateObservationCount(obsCount))
        {
            return 0f;
        }

        List<float> weights = ResolveWeights(styleWeights);
        if (weights.Count == 0)
        {
            return 0f;
        }

        int inputSize = GetExpectedCriticInputSize(obsCount);
        int clampedDiscreteAction = Mathf.Clamp(discreteAction, 0, Mathf.Max(0, discreteActionCount - 1));

        using (var inputTensor = new Tensor<float>(new TensorShape(1, inputSize)))
        {
            for (int i = 0; i < obsCount; i++)
            {
                inputTensor[i] = observations[i];
            }

            int offset = obsCount;
            if (continuousActionCount > 0) inputTensor[offset] = strafeAction;
            if (continuousActionCount > 1) inputTensor[offset + 1] = forwardAction;
            offset += continuousActionCount;

            for (int i = 0; i < discreteActionCount; i++)
            {
                inputTensor[offset + i] = i == clampedDiscreteAction ? 1f : 0f;
            }

            float totalStyleReward = 0f;
            int criticCount = Mathf.Min(_critics.Count, weights.Count);
            for (int i = 0; i < criticCount; i++)
                {
                    float alpha = weights[i];
                    if (alpha <= 0f) continue;

                    _critics[i].Schedule(inputTensor);
                    
                    // --- THE FIX IS HERE ---
                    var outputTensor = _critics[i].PeekOutput() as Tensor<float>;
                    float discriminatorOutput = 0f;

                    if (outputTensor != null)
                    {
                        // We must 'ReadbackAndClone' to move the data from GPU to CPU
                        // so we can actually read the [0] value.
                        using (var cpuTensor = outputTensor.ReadbackAndClone())
                        {
                            discriminatorOutput = cpuTensor[0];
                        }
                    }

                    totalStyleReward += alpha * EvaluateStyleMatch(discriminatorOutput);
                }


            return totalStyleReward;
        }
    }

    void OnDestroy()
    {
        foreach (var worker in _critics)
        {
            worker?.Dispose();
        }
    }
}
